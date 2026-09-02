using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FACM.Core.Mayhem;

namespace FACM.Infrastructure.Mayhem;

internal enum MayhemPublicResourceKind
{
    MayhemAugments,
    MayhemBuild,
    RankingBuild,
    AramLocalizedBuild,
    AramGlobalBuild,
    CommunityDragonItems,
    CommunityDragonAugments,
    CommunityDragonSummonerSpells,
    CommunityDragonChampionSummary,
    CommunityDragonChampionDetail
}

internal sealed record MayhemPublicResourceRequest(
    MayhemPublicResourceKind Kind,
    string ChampionSlug = "",
    int ChampionId = 0);

internal sealed record MayhemPublicDataResponse(
    byte[] Bytes,
    string Route,
    bool FromCache,
    bool IsStale,
    long DurationMilliseconds)
{
    public string ReadUtf8() => Encoding.UTF8.GetString(Bytes);
}

/// <summary>
/// Shared transport for public, unauthenticated Mayhem enrichment data. Callers choose a typed
/// resource; URL construction remains inside Infrastructure. LCU traffic, credentials and writes
/// are deliberately outside this transport.
/// </summary>
internal sealed class MayhemCachedPublicDataTransport : IDisposable
{
    internal const long MaximumBodyBytes = 12L * 1024L * 1024L;
    internal static readonly TimeSpan FreshCacheAge = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan StaleCacheAge = TimeSpan.FromHours(24);

    private const string OpggMayhemBase = "https://op.gg/zh-cn/lol/modes/aram-mayhem";
    private const string RankingBase = "https://arammayhem.com";
    private const string OpggAramLocalizedBase = "https://op.gg/zh-cn/lol/modes/aram";
    private const string OpggAramGlobalBase = "https://op.gg/lol/modes/aram";
    private const string CommunityDragonZhBase =
        "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/zh_cn/v1";

    private readonly string _cacheDirectory;
    private readonly HttpClient _client;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _flights = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public MayhemCachedPublicDataTransport(
        string runtimeCacheDirectory,
        HttpMessageHandler? handler = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(runtimeCacheDirectory))
            throw new ArgumentException("Runtime cache directory is required.", nameof(runtimeCacheDirectory));

        _cacheDirectory = Path.Combine(Path.GetFullPath(runtimeCacheDirectory), "league-public");
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        handler ??= new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
            UseCookies = false
        };
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FACM", "4.0"));
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.7");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
    }

    public async Task<MayhemPublicDataResponse?> GetAsync(
        MayhemPublicResourceRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool allowStale = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        var uri = Resolve(request);
        var cachePath = GetCachePath(uri);

        if (TryReadCache(cachePath, FreshCacheAge, stale: false, out var cached)) return cached;

        var gate = _flights.GetOrAdd(uri.AbsoluteUri, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryReadCache(cachePath, FreshCacheAge, stale: false, out cached)) return cached;

            var started = Stopwatch.StartNew();
            try
            {
                var bytes = await DownloadAsync(uri, timeout, cancellationToken).ConfigureAwait(false);
                TryWriteCache(cachePath, bytes);
                started.Stop();
                return new MayhemPublicDataResponse(bytes, "direct", false, false, started.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                started.Stop();
                if (allowStale && TryReadCache(cachePath, StaleCacheAge, stale: true, out cached)) return cached;
                return null;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<byte[]?> TryGetBytesAsync(
        MayhemPublicResourceRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var response = await GetAsync(request, timeout, cancellationToken, allowStale: true).ConfigureAwait(false);
        return response?.Bytes;
    }

    internal static Uri Resolve(MayhemPublicResourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var slug = NormalizeSlug(request.ChampionSlug);
        return request.Kind switch
        {
            MayhemPublicResourceKind.MayhemAugments when slug.Length > 0 =>
                new Uri(OpggMayhemBase + "/" + Uri.EscapeDataString(slug) + "/augments"),
            MayhemPublicResourceKind.MayhemBuild when slug.Length > 0 =>
                new Uri(OpggMayhemBase + "/" + Uri.EscapeDataString(slug) + "/build"),
            MayhemPublicResourceKind.RankingBuild when slug.Length > 0 =>
                new Uri(RankingBase + "/build/" + Uri.EscapeDataString(slug) + "/"),
            MayhemPublicResourceKind.AramLocalizedBuild when slug.Length > 0 =>
                new Uri(OpggAramLocalizedBase + "/" + Uri.EscapeDataString(slug) + "/build"),
            MayhemPublicResourceKind.AramGlobalBuild when slug.Length > 0 =>
                new Uri(OpggAramGlobalBase + "/" + Uri.EscapeDataString(slug) + "/build"),
            MayhemPublicResourceKind.CommunityDragonItems =>
                new Uri(CommunityDragonZhBase + "/items.json"),
            MayhemPublicResourceKind.CommunityDragonAugments =>
                new Uri(CommunityDragonZhBase + "/cherry-augments.json"),
            MayhemPublicResourceKind.CommunityDragonSummonerSpells =>
                new Uri(CommunityDragonZhBase + "/summoner-spells.json"),
            MayhemPublicResourceKind.CommunityDragonChampionSummary =>
                new Uri(CommunityDragonZhBase + "/champion-summary.json"),
            MayhemPublicResourceKind.CommunityDragonChampionDetail when request.ChampionId > 0 =>
                new Uri(CommunityDragonZhBase + "/champions/" + request.ChampionId.ToString(CultureInfo.InvariantCulture) + ".json"),
            _ => throw new ArgumentException("Mayhem public resource requires a valid typed resource key.", nameof(request))
        };
    }

    private async Task<byte[]> DownloadAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(6) : timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength;
        if (length.HasValue && length.Value > MaximumBodyBytes)
            throw new InvalidDataException("Mayhem public data response exceeded the 12 MB limit.");

        await using var input = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), linked.Token).ConfigureAwait(false);
            if (read <= 0) break;
            total += read;
            if (total > MaximumBodyBytes)
                throw new InvalidDataException("Mayhem public data response exceeded the 12 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private bool TryReadCache(
        string path,
        TimeSpan maxAge,
        bool stale,
        out MayhemPublicDataResponse? response)
    {
        response = null;
        try
        {
            if (!File.Exists(path)) return false;
            var written = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            var age = _utcNow() - written;
            if (age < TimeSpan.Zero || age > maxAge) return false;
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0 || bytes.LongLength > MaximumBodyBytes) return false;
            response = new MayhemPublicDataResponse(
                bytes,
                stale ? "stale-cache" : "fresh-cache",
                true,
                stale,
                0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TryWriteCache(string path, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.LongLength > MaximumBodyBytes) return;
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // Public-data cache is best-effort and must never break a successful network result.
        }
    }

    private string GetCachePath(Uri uri) => Path.Combine(_cacheDirectory, CacheKey(uri.AbsoluteUri) + ".bin");

    internal string GetCachePathForSmoke(MayhemPublicResourceRequest request) => GetCachePath(Resolve(request));

    private static string CacheKey(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeSlug(string? value)
    {
        var slug = MayhemChampionAliases.Slugify(value);
        return Regex.IsMatch(slug, "^[a-z0-9-]{1,80}$", RegexOptions.CultureInvariant) ? slug : string.Empty;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
        foreach (var gate in _flights.Values) gate.Dispose();
        _flights.Clear();
    }
}
