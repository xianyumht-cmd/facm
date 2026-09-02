#include "ManifestTrust.h"

#include <windows.h>
#include <bcrypt.h>
#include <wincrypt.h>

#include <cstdint>
#include <cstring>
#include <vector>

namespace facm::bootstrapper {
namespace {

constexpr std::uint32_t kProductionKeyBits = 2048;

struct ProductionKeyMaterial {
    const wchar_t* keyId;
    const wchar_t* algorithm;
    ProductionKeyStatus status;
    const unsigned char* modulus;
    std::size_t modulusSize;
    const unsigned char* exponent;
    std::size_t exponentSize;
};

// This is public release-root material only. The corresponding private key is
// owned by the release process and is never part of this repository or binary.
constexpr unsigned char kProductionModulus[] = {
    0xB2, 0x0F, 0x2B, 0x9C, 0x77, 0x11, 0x9A, 0x4A, 0x9E, 0x27, 0x94, 0xC3, 0x80, 0x52, 0x94, 0xBE,
    0x8C, 0xB1, 0x39, 0xAE, 0x02, 0x23, 0xC2, 0x77, 0xB5, 0x20, 0x00, 0xF4, 0xCB, 0x9F, 0x7A, 0x4F,
    0x7E, 0x63, 0x3C, 0xEF, 0x8F, 0x13, 0x32, 0x8B, 0x46, 0x72, 0x00, 0x04, 0x5E, 0x58, 0xD4, 0xBB,
    0x83, 0x9A, 0xAD, 0x28, 0x6D, 0x26, 0xC8, 0xCF, 0x32, 0x09, 0xF1, 0xCA, 0x94, 0xE8, 0xA5, 0x10,
    0x3F, 0x13, 0x1A, 0x95, 0xC2, 0x18, 0xB3, 0xB7, 0xB1, 0x4C, 0xAE, 0x55, 0x6E, 0x71, 0x67, 0x54,
    0x4F, 0x8D, 0xB7, 0x43, 0x4B, 0xCC, 0xEE, 0x8F, 0x67, 0x5F, 0x95, 0x08, 0x1E, 0x11, 0xBB, 0x36,
    0x31, 0xCE, 0xB4, 0x8C, 0x6D, 0xB6, 0x33, 0x18, 0x8A, 0x95, 0xC5, 0x19, 0x0B, 0x23, 0x53, 0xEB,
    0x36, 0xD5, 0xA2, 0x79, 0xFD, 0x1F, 0x57, 0x5F, 0xD4, 0xD9, 0x54, 0x1A, 0x25, 0xE2, 0x05, 0xCD,
    0x88, 0x22, 0xD9, 0x80, 0x06, 0xFF, 0x2C, 0x2B, 0xBA, 0xE3, 0x39, 0x6B, 0x3D, 0x42, 0x09, 0x83,
    0x72, 0x79, 0x54, 0x8C, 0x7D, 0x20, 0x01, 0xC9, 0x15, 0xFA, 0xEF, 0x44, 0x20, 0x5F, 0x93, 0xCA,
    0x44, 0x74, 0x5F, 0xF0, 0x5C, 0x17, 0xFE, 0xBA, 0x85, 0x5A, 0x62, 0x53, 0xE5, 0x78, 0xA3, 0xF5,
    0x54, 0x54, 0xF5, 0x3C, 0x02, 0x74, 0x4E, 0xA3, 0xCF, 0xBE, 0xE1, 0x23, 0xE9, 0xAC, 0x5F, 0xC1,
    0xF1, 0x56, 0x34, 0xBB, 0x62, 0x4D, 0x80, 0x1C, 0x8D, 0x60, 0xA9, 0x1E, 0x3D, 0xD9, 0x63, 0x07,
    0x42, 0xB4, 0xC3, 0xF1, 0x0D, 0xE8, 0x50, 0x8D, 0xD1, 0x44, 0x78, 0xFF, 0x9D, 0x91, 0xC2, 0xBE,
    0x5F, 0x15, 0x2F, 0xE5, 0x87, 0x6C, 0x49, 0x8D, 0x59, 0x03, 0xFE, 0x3F, 0x33, 0xEB, 0xD3, 0x27,
    0xF2, 0x11, 0x1A, 0xE6, 0x37, 0x3C, 0x91, 0x11, 0xF5, 0x74, 0xC4, 0x7C, 0xEA, 0x9E, 0x67, 0xF1,
};

constexpr unsigned char kProductionExponent[] = { 0x01, 0x00, 0x01 };

// This table is the only production trust source. A future rotation adds a
// reviewed public entry here with Active/Overlap status; it never comes from a
// manifest, config file, environment variable, or remote keyring.
constexpr ProductionKeyMaterial kProductionKeyring[] = {
    { L"facm-production-r1", L"RSA-2048-PKCS1-SHA256", ProductionKeyStatus::Active,
      kProductionModulus, sizeof(kProductionModulus), kProductionExponent, sizeof(kProductionExponent) },
};

bool IsSuccess(NTSTATUS status) {
    return status >= 0;
}

bool DecodeBase64(const std::string& input, std::vector<unsigned char>& output) {
    DWORD size = 0;
    if (!CryptStringToBinaryA(input.c_str(), static_cast<DWORD>(input.size()), CRYPT_STRING_BASE64,
                              nullptr, &size, nullptr, nullptr)) return false;
    output.resize(size);
    return CryptStringToBinaryA(input.c_str(), static_cast<DWORD>(input.size()), CRYPT_STRING_BASE64,
                                output.data(), &size, nullptr, nullptr) != FALSE;
}

const ProductionKeyMaterial* FindProductionKey(const std::wstring& keyId) {
    for (const auto& key : kProductionKeyring) {
        if (keyId == key.keyId) return &key;
    }
    return nullptr;
}

bool IsAcceptedStatus(ProductionKeyStatus status) {
    return status == ProductionKeyStatus::Active || status == ProductionKeyStatus::Overlap;
}

bool BuildPublicKeyBlob(const ProductionKeyMaterial& material, std::vector<unsigned char>& blob) {
    BCRYPT_RSAKEY_BLOB header{};
    header.Magic = BCRYPT_RSAPUBLIC_MAGIC;
    header.BitLength = kProductionKeyBits;
    header.cbPublicExp = static_cast<ULONG>(material.exponentSize);
    header.cbModulus = static_cast<ULONG>(material.modulusSize);
    header.cbPrime1 = 0;
    header.cbPrime2 = 0;
    blob.resize(sizeof(header) + material.exponentSize + material.modulusSize);
    std::memcpy(blob.data(), &header, sizeof(header));
    std::memcpy(blob.data() + sizeof(header), material.exponent, material.exponentSize);
    std::memcpy(blob.data() + sizeof(header) + material.exponentSize, material.modulus, material.modulusSize);
    return true;
}

} // namespace

bool VerifyProductionSignature(
    const std::string& exactBytes,
    const std::wstring& keyId,
    const std::string& base64Signature,
    std::wstring& failure) {
    const auto* material = FindProductionKey(keyId);
    if (!material) {
        failure = L"清单签名的 key ID 不在 bootstrapper 内嵌生产信任根中。";
        return false;
    }
    if (!IsAcceptedStatus(material->status)) {
        failure = L"清单签名的 key ID 处于 planned、retired 或 revoked 生命周期状态。";
        return false;
    }

    std::vector<unsigned char> signature;
    if (!DecodeBase64(base64Signature, signature) || signature.size() != sizeof(kProductionModulus)) {
        failure = L"清单 detached 签名编码或长度无效。";
        return false;
    }

    BCRYPT_ALG_HANDLE hashAlgorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    BCRYPT_ALG_HANDLE rsaAlgorithm = nullptr;
    BCRYPT_KEY_HANDLE key = nullptr;
    std::vector<unsigned char> hashObject;
    unsigned long objectLength = 0;
    unsigned long resultLength = 0;
    std::vector<unsigned char> digest(32);
    std::vector<unsigned char> publicBlob;

    if (!IsSuccess(BCryptOpenAlgorithmProvider(&hashAlgorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0)) ||
        !IsSuccess(BCryptGetProperty(hashAlgorithm, BCRYPT_OBJECT_LENGTH,
                                     reinterpret_cast<PUCHAR>(&objectLength), sizeof(objectLength), &resultLength, 0))) {
        failure = L"SHA-256 校验器初始化失败。";
        goto cleanup;
    }
    hashObject.resize(objectLength);
    if (!IsSuccess(BCryptCreateHash(hashAlgorithm, &hash, hashObject.data(), objectLength, nullptr, 0, 0)) ||
        (!exactBytes.empty() && !IsSuccess(BCryptHashData(hash, reinterpret_cast<PUCHAR>(const_cast<char*>(exactBytes.data())),
                                                          static_cast<ULONG>(exactBytes.size()), 0))) ||
        !IsSuccess(BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0))) {
        failure = L"清单 SHA-256 计算失败。";
        goto cleanup;
    }
    if (!BuildPublicKeyBlob(*material, publicBlob) ||
        !IsSuccess(BCryptOpenAlgorithmProvider(&rsaAlgorithm, BCRYPT_RSA_ALGORITHM, nullptr, 0)) ||
        !IsSuccess(BCryptImportKeyPair(rsaAlgorithm, nullptr, BCRYPT_RSAPUBLIC_BLOB, &key,
                                       publicBlob.data(), static_cast<ULONG>(publicBlob.size()), 0))) {
        failure = L"生产公钥加载失败。";
        goto cleanup;
    }

    {
        BCRYPT_PKCS1_PADDING_INFO padding{};
        padding.pszAlgId = BCRYPT_SHA256_ALGORITHM;
        const auto status = BCryptVerifySignature(key, &padding, digest.data(), static_cast<ULONG>(digest.size()),
                                                   signature.data(), static_cast<ULONG>(signature.size()), BCRYPT_PAD_PKCS1);
        if (!IsSuccess(status)) {
            failure = L"清单签名校验失败；精确签名字节或内容可能已被修改。";
            goto cleanup;
        }
    }

    if (hash) BCryptDestroyHash(hash);
    if (hashAlgorithm) BCryptCloseAlgorithmProvider(hashAlgorithm, 0);
    if (key) BCryptDestroyKey(key);
    if (rsaAlgorithm) BCryptCloseAlgorithmProvider(rsaAlgorithm, 0);
    return true;

cleanup:
    if (hash) BCryptDestroyHash(hash);
    if (hashAlgorithm) BCryptCloseAlgorithmProvider(hashAlgorithm, 0);
    if (key) BCryptDestroyKey(key);
    if (rsaAlgorithm) BCryptCloseAlgorithmProvider(rsaAlgorithm, 0);
    return false;
}

bool IsProductionKeyAccepted(const std::wstring& keyId, ProductionKeyStatus* status) {
    const auto* material = FindProductionKey(keyId);
    if (status) *status = material ? material->status : ProductionKeyStatus::Revoked;
    return material && IsAcceptedStatus(material->status);
}

} // namespace facm::bootstrapper
