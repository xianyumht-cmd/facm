#pragma once

#include <string>

namespace facm::bootstrapper {

// Verifies a detached RSA-SHA256 signature over the exact bytes supplied by the
// caller. The production keyring is intentionally compiled into the native
// bootstrapper; no configuration or external trust store can add a key.
bool VerifyProductionSignature(
    const std::string& exactBytes,
    const std::wstring& keyId,
    const std::string& base64Signature,
    std::wstring& failure);

} // namespace facm::bootstrapper
