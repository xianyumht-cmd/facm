using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using FACM.Core.Online;

namespace FACM.Infrastructure.Online;

public sealed class HttpUpdateManifestSource : IUpdateManifestSource, IDisposable
{
    public static readonly Uri ProductionManifestUri = new(
        "https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/version.json",
        UriKind.Absolute);
    public static readonly Uri ModularProductionManifestUri = new(
        "https://gitee.com/xymhtcmd/facm/raw/main/online/facm4-version.json",
        UriKind.Absolute);

    public const int DefaultMaxMetadataBytes = 128 * 1024;

    private readonly HttpClient _client;
    private readonly Uri _manifestUri;
    private readonly TimeSpan _timeout;
    private readonly int _maxMetadataBytes;

    public HttpUpdateManifestSource(
        HttpMessageHandler? handler = null,
        Uri? manifestUri = null,
        TimeSpan? timeout = null,
        int maxMetadataBytes = DefaultMaxMetadataBytes)
    {
        if (maxMetadataBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxMetadataBytes));
        _manifestUri = manifestUri ?? ProductionManifestUri;
        if (!_manifestUri.IsAbsoluteUri || _manifestUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Update manifest URI must be absolute HTTPS.", nameof(manifestUri));
        _timeout = timeout ?? TimeSpan.FromSeconds(7);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _maxMetadataBytes = maxMetadataBytes;

        handler ??= new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM-Windows/4.0");
        _client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
    }

    public async Task<UpdateManifestSnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, _manifestUri);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var length = response.Content.Headers.ContentLength;
        if (length.HasValue && length.Value > _maxMetadataBytes)
            throw new InvalidDataException("FACM update metadata response is too large.");

        using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(chunk.AsMemory(0, chunk.Length), timeout.Token).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > _maxMetadataBytes)
                throw new InvalidDataException("FACM update metadata response is too large.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
        }

        var dto = JsonSerializer.Deserialize<ManifestDto>(buffer.ToArray())
            ?? throw new InvalidDataException("FACM update metadata is empty.");
        var snapshot = new UpdateManifestSnapshot(
            dto.Enabled,
            dto.Version ?? string.Empty,
            dto.MinimumVersion ?? string.Empty,
            dto.ForceUpdate,
            dto.DownloadUrl ?? string.Empty,
            dto.Sha256 ?? string.Empty,
            dto.ReleaseNotes ?? string.Empty,
            dto.PublishedAt ?? string.Empty,
            dto.BootstrapManifestUrl ?? string.Empty);
        if (!IsValidManifest(snapshot)) throw new InvalidDataException("FACM update metadata failed validation.");
        return snapshot;
    }

    public static bool IsValidManifest(UpdateManifestSnapshot? manifest)
    {
        if (manifest is null) return false;
        var version = UpdateDecisionService.ParseVersion(manifest.Version);
        if (version is null) return false;
        if (!string.IsNullOrWhiteSpace(manifest.MinimumVersion) && UpdateDecisionService.ParseVersion(manifest.MinimumVersion) is null) return false;

        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var download) ||
            !IsApprovedReleaseUrl(download, version)) return false;

        if (!string.IsNullOrWhiteSpace(manifest.BootstrapManifestUrl))
        {
            if (!Uri.TryCreate(manifest.BootstrapManifestUrl, UriKind.Absolute, out var bootstrapManifest) ||
                !IsApprovedReleaseManifestUrl(bootstrapManifest, version)) return false;
        }

        if (manifest.Sha256.Length != 64) return false;
        foreach (var character in manifest.Sha256)
        {
            var valid = character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!valid) return false;
        }
        return true;
    }

    private static bool IsApprovedReleaseUrl(Uri uri, Version version)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrWhiteSpace(uri.Query) ||
            !string.IsNullOrWhiteSpace(uri.Fragment)) return false;

        var normalizedVersion = version.ToString();
        var githubPrefix = "/xianyumht-cmd/facm/releases/download/v" + normalizedVersion + "/";
        if (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return HasSingleAssetPath(uri.AbsolutePath, githubPrefix);

        var giteePrefix = "/xymhtcmd/facm/releases/download/v" + normalizedVersion + "/";
        return string.Equals(uri.Host, "gitee.com", StringComparison.OrdinalIgnoreCase) &&
               HasSingleAssetPath(uri.AbsolutePath, giteePrefix);
    }

    private static bool IsApprovedReleaseManifestUrl(Uri uri, Version version) =>
        IsApprovedReleaseUrl(uri, version) &&
        uri.AbsolutePath.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase);

    private static bool HasSingleAssetPath(string path, string prefix)
    {
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               path.Length > prefix.Length && !path[prefix.Length..].Contains('/');
    }

    public void Dispose() => _client.Dispose();

    private sealed class ManifestDto
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; }
        [JsonPropertyName("version")]
        public string? Version { get; init; }
        [JsonPropertyName("minimum_version")]
        public string? MinimumVersion { get; init; }
        [JsonPropertyName("force_update")]
        public bool ForceUpdate { get; init; }
        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; init; }
        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }
        [JsonPropertyName("release_notes")]
        public string? ReleaseNotes { get; init; }
        [JsonPropertyName("published_at")]
        public string? PublishedAt { get; init; }
        [JsonPropertyName("manifest_url")]
        public string? BootstrapManifestUrl { get; init; }
    }
}
