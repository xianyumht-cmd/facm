using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FACM.Core.League;

namespace FACM.Infrastructure.League;

/// <summary>
/// Resolves the small icon set used by the OP.GG guide. Tencent is the domestic first choice;
/// CommunityDragon is a fixed-host fallback. The cache is decorative-only and never gates the
/// existing League session, Workbench refresh, or write paths.
/// </summary>
public sealed class LeagueGuideAssetService : ILeagueGuideAssetService
{
    private const long MaximumBodyBytes = 512L * 1024L;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(30);
    private const string TencentBase = "https://game.gtimg.cn/images/lol/act/img";
    private const string CommunityDragonBase =
        "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/zh_cn/v1";

    private readonly string _cacheDirectory;
    private readonly HttpClient _client;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _flights = new(StringComparer.Ordinal);
    private bool _disposed;

    public LeagueGuideAssetService(string runtimeCacheDirectory, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(runtimeCacheDirectory))
            throw new ArgumentException("Runtime cache directory is required.", nameof(runtimeCacheDirectory));

        _cacheDirectory = Path.Combine(Path.GetFullPath(runtimeCacheDirectory), "league-guide-assets");
        handler ??= new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false,
            UseCookies = false
        };
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GGman", "4.0"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
    }

    public async Task<byte[]?> TryGetBytesAsync(
        string kind,
        int id,
        string? assetPath = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (id <= 0 || string.IsNullOrWhiteSpace(kind)) return null;

        var cachePath = GetCachePath(kind, id, assetPath);
        if (TryReadCache(cachePath, out var cached)) return cached;

        var cacheKey = kind.Trim().ToLowerInvariant() + ":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var gate = _flights.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryReadCache(cachePath, out cached)) return cached;
            foreach (var uri in ResolveUris(kind, id, assetPath))
            {
                var bytes = await TryDownloadAsync(uri, cancellationToken).ConfigureAwait(false);
                if (bytes is null) continue;
                TryWriteCache(cachePath, bytes);
                return bytes;
            }
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    internal static IReadOnlyList<Uri> ResolveUris(string kind, int id, string? assetPath)
    {
        var normalized = kind.Trim().ToLowerInvariant();
        var uris = new List<Uri>(2);
        var tencentFolder = normalized switch
        {
            "champions" => "champion",
            "items" => "item",
            "summoner-spells" => "spell",
            "runes" => "rune",
            _ => string.Empty
        };
        if (tencentFolder.Length > 0)
            uris.Add(new Uri(TencentBase + "/" + tencentFolder + "/" + id + ".png"));

        var communityPath = ResolveCommunityPath(normalized, id, assetPath);
        if (communityPath.Length > 0)
            uris.Add(new Uri(CommunityDragonBase + "/" + communityPath));
        return uris;
    }

    private static string ResolveCommunityPath(string kind, int id, string? assetPath)
    {
        var value = (assetPath ?? string.Empty).Trim().Replace('\\', '/');
        const string marker = "/lol-game-data/assets/";
        var markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var path = value[(markerIndex + marker.Length)..].Trim('/');
            if (path.Length > 0 && !path.Contains("..", StringComparison.Ordinal)) return path;
        }

        return kind switch
        {
            "champions" => "champion-icons/" + id + ".png",
            "items" => "items/icons2d/" + id + ".png",
            "summoner-spells" => "summoner-spells/" + id + ".png",
            "runes" => "perks/" + id + ".png",
            _ => string.Empty
        };
    }

    private async Task<byte[]?> TryDownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2.5));
        try
        {
            using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > MaximumBodyBytes) return null;
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[32 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false);
                if (read <= 0) break;
                output.Write(buffer, 0, read);
                if (output.Length > MaximumBodyBytes) return null;
            }
            return output.Length == 0 ? null : output.ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private string GetCachePath(string kind, int id, string? assetPath)
    {
        var key = kind.Trim().ToLowerInvariant() + "|" + id + "|" + (assetPath ?? string.Empty);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(_cacheDirectory, hash + ".png");
    }

    private static bool TryReadCache(string path, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || DateTime.UtcNow - info.LastWriteTimeUtc >= CacheLifetime || info.Length <= 0 || info.Length > MaximumBodyBytes)
                return false;
            bytes = File.ReadAllBytes(path);
            return bytes.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void TryWriteCache(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
        foreach (var flight in _flights.Values) flight.Dispose();
        _flights.Clear();
    }
}
