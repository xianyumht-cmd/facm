namespace FACM.Core.Online;

/// <summary>
/// Verifies that a downloaded FACM executable has the same release identity as the running product
/// and matches the manifest version. Windows owns signer/trust/file-version details; Core only owns
/// the fail-closed intent used before issuing a receipt and again before replacement.
/// </summary>
public interface IUpdatePackageIdentityVerifier
{
    void Validate(string packagePath, string expectedVersion);
}
