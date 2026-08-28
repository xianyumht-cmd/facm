using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using FACM.Core.Online;
using FACM.Core.Runtime;

namespace FACM.Infrastructure.Online;

public sealed class HttpUpdatePackageDownloader : IUpdatePackageDownloader, IDisposable
{
    public const long MaximumUpdateBytes = 512L * 1024L * 1024L;
    public static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ReadInactivityTimeout = TimeSpan.FromSeconds(20);

    private readonly string _updatesDirectory;
    private readonly HttpClient _client;
    private bool _disposed;

    public HttpUpdatePackageDownloader(RuntimePathLayout layout, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _updatesDirectory = Path.GetFullPath(layout.UpdatesDirectory);
        handler ??= new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
            UseCookies = false
        };
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FACM-Windows-Updater", "4.0"));
        _client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
    }

    public async Task<ValidatedUpdatePackage> DownloadAsync(
        UpdateManifestSnapshot manifest,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HttpUpdateManifestSource.IsValidManifest(manifest))
            throw new InvalidDataException("FACM update manifest is not eligible for package download.");

        var downloadUri = new Uri(manifest.DownloadUrl, UriKind.Absolute);
        Directory.CreateDirectory(_updatesDirectory);
        var version = NormalizeVersion(manifest.Version);
        var destination = Path.Combine(_updatesDirectory, "FACM-" + SanitizeFileName(version) + ".exe");
        var temporary = destination + ".download";
        TryDelete(temporary);
        progress?.Report(new UpdateDownloadProgress(0, null));

        try
        {
            using var response = await GetHeadersAsync(downloadUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            if (total is < 0 or > MaximumUpdateBytes)
                throw new InvalidDataException("FACM update package size is outside the allowed range.");

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long received = 0;
                while (true)
                {
                    var read = await ReadWithInactivityTimeoutAsync(input, buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    received += read;
                    if (received > MaximumUpdateBytes)
                        throw new InvalidDataException("FACM update package exceeded the 512 MiB limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report(new UpdateDownloadProgress(received, total));
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var length = new FileInfo(temporary).Length;
            if (length <= 0 || length > MaximumUpdateBytes)
                throw new InvalidDataException("FACM update package is empty or too large.");
            if (total.HasValue && total.Value != length)
                throw new InvalidDataException("FACM update package length did not match the response metadata.");

            // Re-open the completed file and compute its identity from bytes at rest. The validated receipt
            // is only minted after this independent post-download pass succeeds.
            var actualSha = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualSha, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("FACM update package SHA-256 verification failed.");
            ValidatePortableExecutable(temporary);

            File.Move(temporary, destination, overwrite: true);
            progress?.Report(new UpdateDownloadProgress(length, length));
            return new ValidatedUpdatePackage(
                destination,
                version,
                actualSha.ToUpperInvariant(),
                downloadUri.AbsoluteUri,
                length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private async Task<HttpResponseMessage> GetHeadersAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HeaderTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("FACM update source did not return headers within 10 seconds.");
        }
    }

    private static async Task<int> ReadWithInactivityTimeoutAsync(
        Stream input,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadInactivityTimeout);
        try
        {
            return await input.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("FACM update source delivered no data for 20 seconds.");
        }
    }

    internal static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var algorithm = SHA256.Create();
        var hash = await algorithm.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    internal static void ValidatePortableExecutable(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1024 || stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("FACM update package is not a valid PE executable.");
    }

    internal static string NormalizeVersion(string value)
    {
        var parsed = UpdateDecisionService.ParseVersion(value)
            ?? throw new InvalidDataException("FACM update version is invalid.");
        return parsed.ToString();
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '_');
        return value;
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
    }
}
