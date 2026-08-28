namespace FACM.Core.Online;

public sealed record ValidatedUpdatePackage(
    string FilePath,
    string Version,
    string Sha256,
    string DownloadUrl,
    long Length);

/// <summary>
/// Lower-level verified package download boundary retained for deterministic package validation.
/// Progress uses the canonical UpdateDownloadProgress contract owned by UpdateInstallationContracts.
/// Product replacement is owned by IPreparedUpdateInstaller so there is only one launcher contract.
/// </summary>
public interface IUpdatePackageDownloader
{
    Task<ValidatedUpdatePackage> DownloadAsync(
        UpdateManifestSnapshot manifest,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record UpdateReplacementStartResult(bool Started, string Reason);
