using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using FACM.Core.Online;
using FACM.Core.Runtime;

namespace FACM.Infrastructure.Online;

/// <summary>
/// Downloads only a manifest already valid for the fixed FACM GitHub release layout. A successful
/// download is represented by an opaque process-local receipt. Replacement re-checks the receipt,
/// file identity, byte length, SHA-256 and release identity before delegating to the Windows launcher.
/// </summary>
public sealed class HttpPreparedUpdateInstaller : IPreparedUpdateInstaller, IDisposable
{
    public const long MaximumUpdateBytes = 512L * 1024L * 1024L;
    public static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(20);

    private sealed record Receipt(string Path, string Version, string Sha256, long Length);

    private readonly string _updatesDirectory;
    private readonly IUpdateReplacementLauncher _launcher;
    private readonly IUpdatePackageIdentityVerifier _identityVerifier;
    private readonly HttpClient _client;
    private readonly Dictionary<string, Receipt> _receipts = new(StringComparer.Ordinal);
    private readonly object _receiptSync = new();
    private readonly SemaphoreSlim _replacementGate = new(1, 1);
    private bool _disposed;

    public HttpPreparedUpdateInstaller(
        RuntimePathLayout layout,
        IUpdateReplacementLauncher launcher,
        IUpdatePackageIdentityVerifier identityVerifier,
        HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _identityVerifier = identityVerifier ?? throw new ArgumentNullException(nameof(identityVerifier));
        _updatesDirectory = Path.GetFullPath(layout.UpdatesDirectory);
        handler ??= new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
            UseCookies = false
        };
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FACM-Windows-Updater", "4.0"));
    }

    public async Task<PreparedUpdatePackage> PrepareAsync(
        UpdateManifestSnapshot manifest,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateManifest(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_updatesDirectory);
        var version = NormalizeVersion(manifest.Version);
        var destination = Path.Combine(_updatesDirectory, "FACM-" + SanitizeFileName(version) + ".exe");
        var temporary = destination + ".download-" + Guid.NewGuid().ToString("N");
        progress?.Report(new UpdateDownloadProgress(0, null, 0, "connecting"));

        try
        {
            var source = new Uri(manifest.DownloadUrl, UriKind.Absolute);
            using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            headerTimeout.CancelAfter(HeaderTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            HttpResponseMessage? response = null;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Update connection timed out.");
            }

            using (response)
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                if (total.HasValue && (total.Value <= 0 || total.Value > MaximumUpdateBytes))
                    throw new InvalidDataException("Update package size is outside the allowed range.");

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                var buffer = new byte[81920];
                long received = 0;
                while (true)
                {
                    using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    inactivity.CancelAfter(InactivityTimeout);
                    int read;
                    try
                    {
                        read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), inactivity.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("Update transfer produced no data for 20 seconds.");
                    }

                    if (read == 0) break;
                    received += read;
                    if (received > MaximumUpdateBytes)
                        throw new InvalidDataException("Update package exceeded the 512 MiB limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                    var percent = total is > 0
                        ? (int)Math.Min(99, received * 100L / total.Value)
                        : 0;
                    progress?.Report(new UpdateDownloadProgress(received, total, percent, "downloading"));
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (received <= 0) throw new InvalidDataException("Update package was empty.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new UpdateDownloadProgress(new FileInfo(temporary).Length, null, 99, "verifying"));
            var actualHash = ComputeSha256(temporary);
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update package SHA-256 validation failed.");
            _identityVerifier.Validate(temporary, version);

            File.Move(temporary, destination, overwrite: true);
            var fullPath = Path.GetFullPath(destination);
            var length = new FileInfo(fullPath).Length;
            var receiptId = Guid.NewGuid().ToString("N");
            lock (_receiptSync)
            {
                _receipts[receiptId] = new Receipt(fullPath, version, actualHash, length);
            }

            progress?.Report(new UpdateDownloadProgress(length, length, 100, "prepared"));
            return new PreparedUpdatePackage(receiptId, fullPath, version, actualHash, length);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public async Task<UpdateReplacementResult> StartReplacementAsync(
        PreparedUpdatePackage package,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(package);
        await _replacementGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var receiptId = package.ReceiptId;
            if (string.IsNullOrWhiteSpace(receiptId))
                return new UpdateReplacementResult(false, "receipt-missing");

            Receipt receipt;
            lock (_receiptSync)
            {
                if (!_receipts.TryGetValue(receiptId, out receipt!))
                    return new UpdateReplacementResult(false, "receipt-missing");
            }

            var suppliedPath = Path.GetFullPath(package.PackagePath ?? string.Empty);
            if (!string.Equals(suppliedPath, receipt.Path, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(package.Version, receipt.Version, StringComparison.Ordinal) ||
                !string.Equals(package.Sha256, receipt.Sha256, StringComparison.OrdinalIgnoreCase) ||
                package.Length != receipt.Length)
                return new UpdateReplacementResult(false, "receipt-mismatch");

            if (!File.Exists(receipt.Path)) return new UpdateReplacementResult(false, "package-missing");
            var info = new FileInfo(receipt.Path);
            if (info.Length != receipt.Length || info.Length <= 0 || info.Length > MaximumUpdateBytes)
                return new UpdateReplacementResult(false, "package-length-changed");
            var actualHash = ComputeSha256(receipt.Path);
            if (!string.Equals(actualHash, receipt.Sha256, StringComparison.OrdinalIgnoreCase))
                return new UpdateReplacementResult(false, "package-hash-changed");

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _identityVerifier.Validate(receipt.Path, receipt.Version);
            }
            catch (InvalidDataException)
            {
                return new UpdateReplacementResult(false, "package-identity-changed");
            }

            var started = await _launcher.StartAsync(receipt.Path, receipt.Sha256, receipt.Version, cancellationToken)
                .ConfigureAwait(false);
            if (!started) return new UpdateReplacementResult(false, "launcher-not-started");

            lock (_receiptSync) _receipts.Remove(receiptId);
            return new UpdateReplacementResult(true, "replacement-started");
        }
        finally
        {
            _replacementGate.Release();
        }
    }

    private static void ValidateManifest(UpdateManifestSnapshot manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!HttpUpdateManifestSource.IsValidManifest(manifest))
            throw new InvalidDataException("Update manifest is not valid for the fixed FACM GitHub release path.");
        if (!manifest.Enabled) throw new InvalidOperationException("Updates are disabled by the manifest.");
    }

    private static string NormalizeVersion(string value)
    {
        var version = (value ?? string.Empty).Trim();
        if (version.StartsWith('v') || version.StartsWith('V')) version = version[1..];
        if (UpdateDecisionService.ParseVersion(version) is null)
            throw new InvalidDataException("Update version is invalid.");
        return version;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '_');
        return value;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
        _replacementGate.Dispose();
        lock (_receiptSync) _receipts.Clear();
    }
}
