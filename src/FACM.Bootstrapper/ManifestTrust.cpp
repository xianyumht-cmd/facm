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
    0xAD, 0x2B, 0xA3, 0x61, 0xF3, 0xDC, 0x33, 0x66, 0x19, 0xEE, 0x9A, 0x1E, 0x5C, 0xB5, 0x8E, 0xC9,
    0x80, 0x33, 0x97, 0x51, 0x13, 0x7B, 0x74, 0xE7, 0x72, 0x51, 0x48, 0xED, 0x4B, 0x0B, 0xA3, 0xF4,
    0x72, 0xD4, 0x6C, 0x1F, 0x0E, 0x79, 0x2B, 0xF7, 0x7E, 0x0C, 0x04, 0x2A, 0x19, 0x2E, 0xD5, 0x5E,
    0x7F, 0x09, 0xBB, 0xB5, 0xCD, 0x79, 0x75, 0x2C, 0x54, 0x3B, 0x31, 0x32, 0x0E, 0x3B, 0xDF, 0x59,
    0x2F, 0x33, 0x5A, 0xBA, 0xA6, 0xDD, 0x81, 0xC5, 0x32, 0xD6, 0xDC, 0xC6, 0x8F, 0xC3, 0xD6, 0x90,
    0x9B, 0xF1, 0x1E, 0x5E, 0x19, 0x91, 0x12, 0x2A, 0x3D, 0xCB, 0x2F, 0xB4, 0x80, 0xBA, 0x0B, 0xBE,
    0xE3, 0xAA, 0x03, 0xB5, 0x6A, 0xD1, 0xD8, 0x84, 0x9E, 0xEC, 0x4B, 0x27, 0x3E, 0x98, 0x3F, 0xB0,
    0x04, 0x6B, 0x2C, 0xE0, 0xE4, 0x49, 0xD2, 0x67, 0xAE, 0x65, 0x60, 0xC8, 0x12, 0x95, 0x9F, 0x7E,
    0xDD, 0x10, 0x4E, 0xBD, 0x81, 0x8E, 0x0D, 0x7D, 0x2A, 0xC8, 0x28, 0x26, 0x97, 0xB6, 0x17, 0xFC,
    0x11, 0x08, 0xC0, 0x9D, 0x3A, 0x3E, 0x1B, 0x88, 0x45, 0x11, 0x19, 0xD2, 0x64, 0x21, 0x65, 0x77,
    0xC1, 0x12, 0xE3, 0x0B, 0xD0, 0xB5, 0x99, 0x19, 0x7E, 0x8E, 0xA1, 0xDE, 0x4C, 0x42, 0x13, 0x43,
    0x82, 0x4D, 0x56, 0xD9, 0x4B, 0xBA, 0xCE, 0x4E, 0x79, 0x56, 0x15, 0x47, 0x81, 0xCE, 0x9C, 0x0C,
    0x98, 0xA1, 0xE8, 0x77, 0x87, 0x4B, 0xA1, 0x78, 0x70, 0x26, 0x33, 0x4D, 0x49, 0x07, 0x19, 0xA5,
    0x73, 0x3F, 0xC9, 0x20, 0xD0, 0x65, 0xD2, 0x2D, 0x09, 0x61, 0x73, 0x7D, 0xAA, 0x2A, 0xA3, 0x9F,
    0xFB, 0xEB, 0x57, 0x16, 0x18, 0xA0, 0xCD, 0x89, 0x8F, 0xD8, 0x00, 0xDE, 0xDB, 0x53, 0xB1, 0xF8,
    0x8C, 0xA1, 0x6B, 0x61, 0xF6, 0xAE, 0xD5, 0x99, 0xE2, 0x88, 0x5D, 0xF9, 0x18, 0x38, 0x45, 0x45,
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
