namespace FACM.Core.Online;

public sealed record UpdateDownloadProgress(
    long ReceivedBytes,
    long? TotalBytes,
    int Percent,
    string Stage);

public sealed record PreparedUpdatePackage(
    string ReceiptId,
    string PackagePath,
    string Version,
    string Sha256,
    long Length)
{
    public string BootstrapManifestUrl { get; init; } = string.Empty;
}

public interface IManifestAwareUpdateReplacementLauncher
{
    Task<bool> StartAsync(
        string validatedPackagePath,
        string expectedSha256,
        string version,
        string bootstrapManifestUrl,
        CancellationToken cancellationToken = default);
}

public sealed record UpdateReplacementResult(bool Started, string Reason);

/// <summary>
/// Product update intent. PrepareAsync owns download + package validation and returns an opaque
/// process-local receipt. StartReplacementAsync accepts only a package prepared by the same service
/// instance and revalidates it before crossing the Windows replacement-launch boundary.
/// </summary>
public interface IPreparedUpdateInstaller
{
    Task<PreparedUpdatePackage> PrepareAsync(
        UpdateManifestSnapshot manifest,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<UpdateReplacementResult> StartReplacementAsync(
        PreparedUpdatePackage package,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Narrow platform edge used only after the package receipt and hash have been revalidated.
/// Implementations choose the trusted updater helper and current executable; callers cannot supply
/// either destination path.
/// </summary>
public interface IUpdateReplacementLauncher
{
    Task<bool> StartAsync(
        string validatedPackagePath,
        string expectedSha256,
        string version,
        CancellationToken cancellationToken = default);
}
