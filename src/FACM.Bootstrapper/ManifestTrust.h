#pragma once

#include <string>

namespace facm::bootstrapper {

enum class ProductionKeyStatus {
    Active,
    Overlap,
    Planned,
    Retired,
    Revoked,
};

// Verifies a detached RSA-SHA256 signature over the exact bytes supplied by the
// caller. The production keyring is intentionally compiled into the native
// bootstrapper; no configuration or external trust store can add a key.
bool VerifyProductionSignature(
    const std::string& exactBytes,
    const std::wstring& keyId,
    const std::string& base64Signature,
    std::wstring& failure);

// Runtime acceptance is limited to Active and bounded Overlap entries in the
// compiled keyring. Planned, retired, revoked, and unknown identities fail
// closed; callers cannot promote a key through configuration or metadata.
bool IsProductionKeyAccepted(const std::wstring& keyId, ProductionKeyStatus* status = nullptr);

} // namespace facm::bootstrapper
