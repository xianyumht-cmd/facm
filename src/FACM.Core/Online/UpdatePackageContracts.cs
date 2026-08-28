namespace FACM.Core.Online;

public sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public int Percent => TotalBytes is > 0
        ? (int)Math.Clamp(BytesReceived * 100L / TotalBytes.Value, 0, 99)
        : 0;
}

public sealed record ValidatedUpdatePackage(
    string FilePath,
    string Version,
    string Sha256,
    string DownloadUrl,
    long Length);

public interface IUpdatePackageDownloader
{
    Task<ValidatedUpdatePackage> DownloadAsync(
        UpdateManifestSnapshot manifest,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record UpdateReplacementStartResult(bool Started, string Reason);

public interface IUpdateReplacementLauncher
{
    Task<UpdateReplacementStartResult> StartAsync(
        ValidatedUpdatePackage package,
        CancellationToken cancellationToken = default);
}
