using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeaguePublicDataResponse
    {
        public byte[] Bytes { get; set; }
        public string Route { get; set; }
        public bool FromCache { get; set; }
        public bool IsStale { get; set; }
        public long DurationMs { get; set; }

        public string ReadUtf8()
        {
            return Bytes == null ? null : Encoding.UTF8.GetString(Bytes);
        }
    }

    // Shared transport for public, unauthenticated LOL web data only.
    // Never route localhost/LCU traffic, credentials, cookies or writes through this class.
    internal static class LeaguePublicDataTransport
    {
        private const long MaximumBodyBytes = 12L * 1024L * 1024L;
        private static readonly TimeSpan FreshCacheAge = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan StaleCacheAge = TimeSpan.FromHours(24);
        private static readonly string[] AllowedHostSuffixes =
        {
            "op.gg",
            "communitydragon.org",
            "arammayhem.com",
            "hexdata.com.cn"
        };
        private static readonly HttpClient Client = CreateClient();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Flights =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        public static async Task<LeaguePublicDataResponse> GetAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            bool allowStale = true)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !IsApproved(uri))
                throw new InvalidDataException("LOL 公共数据地址不在允许范围内。");

            RuntimePaths.Initialize();
            var cacheDirectory = Path.Combine(RuntimePaths.CacheDirectory, "league-public");
            Directory.CreateDirectory(cacheDirectory);
            var cachePath = Path.Combine(cacheDirectory, CacheKey(uri.AbsoluteUri) + ".bin");

            LeaguePublicDataResponse cached;
            if (TryReadCache(cachePath, FreshCacheAge, false, out cached)) return cached;

            var gate = Flights.GetOrAdd(uri.AbsoluteUri, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (TryReadCache(cachePath, FreshCacheAge, false, out cached)) return cached;

                var started = Stopwatch.StartNew();
                try
                {
                    var bytes = await DownloadAsync(uri, timeout, cancellationToken).ConfigureAwait(false);
                    WriteCache(cachePath, bytes);
                    started.Stop();
                    AppLog.Info("LOL public data source succeeded: direct; host=" + uri.Host + "; ms=" + started.ElapsedMilliseconds);
                    return new LeaguePublicDataResponse
                    {
                        Bytes = bytes,
                        Route = "direct",
                        FromCache = false,
                        IsStale = false,
                        DurationMs = started.ElapsedMilliseconds
                    };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    started.Stop();
                    if (allowStale && TryReadCache(cachePath, StaleCacheAge, true, out cached))
                    {
                        AppLog.Info("LOL public data fallback: stale-cache; host=" + uri.Host + "; error=" + exception.GetType().Name);
                        return cached;
                    }
                    AppLog.Info("LOL public data source failed: direct; host=" + uri.Host + "; error=" + exception.GetType().Name);
                    throw;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        public static async Task<byte[]> TryGetBytesAsync(
            string url,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = await GetAsync(url, timeout, cancellationToken, true).ConfigureAwait(false);
                return response == null ? null : response.Bytes;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        internal static void ValidateForSmokeTest()
        {
            Require(IsApproved(new Uri("https://op.gg/zh-cn/lol/modes/aram-mayhem/ashe/augments")), "OP.GG page must be allowed.");
            Require(IsApproved(new Uri("https://lol-api-champion.op.gg/api/champions/ranked/1")), "OP.GG API must be allowed.");
            Require(IsApproved(new Uri("https://raw.communitydragon.org/latest/game/assets/test.png")), "CommunityDragon must be allowed.");
            Require(!IsApproved(new Uri("http://op.gg/")), "HTTP must be rejected.");
            Require(!IsApproved(new Uri("https://127.0.0.1:2999/liveclientdata/allgamedata")), "Local client traffic must be rejected.");
            Require(!IsApproved(new Uri("https://localhost:2999/")), "localhost must be rejected.");
            Require(!IsApproved(new Uri("https://example.com/")), "Unknown public hosts must be rejected.");
        }

        private static async Task<byte[]> DownloadAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                linked.CancelAfter(timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(6) : timeout);
                request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.7");
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var length = response.Content.Headers.ContentLength;
                    if (length.HasValue && length.Value > MaximumBodyBytes)
                        throw new InvalidDataException("LOL 公共数据响应过大。");

                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[65536];
                        long total = 0;
                        while (true)
                        {
                            var read = await input.ReadAsync(buffer, 0, buffer.Length, linked.Token).ConfigureAwait(false);
                            if (read <= 0) break;
                            total += read;
                            if (total > MaximumBodyBytes) throw new InvalidDataException("LOL 公共数据响应过大。");
                            output.Write(buffer, 0, read);
                        }
                        return output.ToArray();
                    }
                }
            }
        }

        private static bool IsApproved(Uri uri)
        {
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || uri.IsLoopback) return false;
            var host = (uri.Host ?? string.Empty).TrimEnd('.').ToLowerInvariant();
            if (host.Length == 0 || IPAddress.TryParse(host, out _)) return false;
            return AllowedHostSuffixes.Any(suffix =>
                string.Equals(host, suffix, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryReadCache(
            string path,
            TimeSpan maxAge,
            bool stale,
            out LeaguePublicDataResponse response)
        {
            response = null;
            try
            {
                if (!File.Exists(path)) return false;
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                if (age < TimeSpan.Zero || age > maxAge) return false;
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0 || bytes.LongLength > MaximumBodyBytes) return false;
                response = new LeaguePublicDataResponse
                {
                    Bytes = bytes,
                    Route = stale ? "stale-cache" : "fresh-cache",
                    FromCache = true,
                    IsStale = stale,
                    DurationMs = 0
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteCache(string path, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            try
            {
                var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
            catch { }
        }

        private static string CacheKey(string value)
        {
            using (var algorithm = SHA256.Create())
            {
                var bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = false
            };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FACM/3.5 PublicData");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            return client;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    internal sealed class LeaguePublicOpggBuildApi : IOpggBuildApi
    {
        private const string BaseUrl = "https://lol-api-champion.op.gg";

        public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path)) return Task.FromResult<byte[]>(null);
            var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? path
                : BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
            return LeaguePublicDataTransport.TryGetBytesAsync(url, TimeSpan.FromSeconds(6), cancellationToken);
        }
    }
}
