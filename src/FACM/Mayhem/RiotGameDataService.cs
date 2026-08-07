using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.Mayhem
{
    internal static class RiotGameDataService
    {
        private const string CommunityDragonBase = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/zh_cn/v1/";
        private const string CommunityDragonAssetBase = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/";
        private static readonly HttpClient PublicClient = CreatePublicClient();
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private sealed class LcuSession
        {
            public string BaseUrl { get; set; }
            public string Password { get; set; }
        }

        public static async Task EnrichAsync(MayhemChampionResult result, CancellationToken token)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.ChampionSlug)) return;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var ct = timeout.Token;
                try
                {
                    await RiotDataDragonService.EnrichAsync(result, ct).ConfigureAwait(false);

                    var needChampionFallback = string.IsNullOrWhiteSpace(result.ChampionIconUrl) ||
                                               string.IsNullOrWhiteSpace(result.ChampionSplashUrl) ||
                                               result.SkillIconUrls.Count < 4 ||
                                               result.TopTen.Any(item => string.IsNullOrWhiteSpace(item.IconUrl));
                    var needItemFallback = result.CoreItems.Count > 0 && result.CoreItemIconUrls.Count < result.CoreItems.Count;

                    var championsTask = needChampionFallback ? ReadGameDataAsync("champion-summary.json", ct) : Task.FromResult<object>(null);
                    var itemsTask = needItemFallback ? ReadGameDataAsync("items.json", ct) : Task.FromResult<object>(null);
                    var augmentsTask = result.Augments.Count > 0 ? ReadGameDataAsync("cherry-augments.json", ct) : Task.FromResult<object>(null);
                    await Task.WhenAll(championsTask, itemsTask, augmentsTask).ConfigureAwait(false);

                    var champions = AsArray(championsTask.Result);
                    if (champions != null)
                    {
                        var champion = FindChampion(champions, result.ChampionSlug, result.ChampionName);
                        if (champion != null)
                        {
                            if (string.IsNullOrWhiteSpace(result.ChampionName))
                            {
                                var officialName = ReadString(champion, "name");
                                if (!string.IsNullOrWhiteSpace(officialName)) result.ChampionName = officialName;
                            }
                            if (string.IsNullOrWhiteSpace(result.ChampionIconUrl))
                                result.ChampionIconUrl = AssetReference(ReadString(champion, "squarePortraitPath"));

                            var id = ReadInt(champion, "id");
                            if (id > 0 && (string.IsNullOrWhiteSpace(result.ChampionSplashUrl) || result.SkillIconUrls.Count < 4))
                            {
                                var detail = await ReadGameDataAsync("champions/" + id + ".json", ct).ConfigureAwait(false) as Dictionary<string, object>;
                                EnrichChampionDetailFallback(result, detail);
                            }
                        }
                        EnrichTopTenFallback(result, champions);
                    }

                    EnrichItemsFallback(result, AsArray(itemsTask.Result));
                    EnrichAugments(result, AsArray(augmentsTask.Result));
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    AppLog.Info("Mayhem visual metadata enrichment timed out; card will use available images.");
                }
                catch (Exception exception)
                {
                    AppLog.Info("Mayhem visual metadata enrichment skipped: " + exception.Message);
                }
            }
        }

        public static async Task<byte[]> DownloadImageAsync(string reference, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            if (reference.StartsWith("lcu:", StringComparison.OrdinalIgnoreCase))
            {
                var path = reference.Substring(4);
                var local = await TryReadLcuBytesAsync(path, token).ConfigureAwait(false);
                if (local != null && local.Length > 0) return local;
                reference = ToPublicAssetUrl(path);
            }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, reference))
                using (var response = await PublicClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return null;
                    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
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

        private static async Task<object> ReadGameDataAsync(string relativePath, CancellationToken token)
        {
            var lcuPath = "/lol-game-data/assets/v1/" + relativePath.TrimStart('/');
            var local = await TryReadLcuTextAsync(lcuPath, token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(local))
            {
                try { return Json.DeserializeObject(local); }
                catch { }
            }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, CommunityDragonBase + relativePath))
                using (var response = await PublicClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return null;
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return Json.DeserializeObject(text);
                }
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

        private static async Task<string> TryReadLcuTextAsync(string path, CancellationToken token)
        {
            var bytes = await TryReadLcuBytesAsync(path, token).ConfigureAwait(false);
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }

        private static async Task<byte[]> TryReadLcuBytesAsync(string path, CancellationToken token)
        {
            var session = DiscoverLcuSession();
            if (session == null) return null;
            try
            {
                using (var handler = new HttpClientHandler())
                {
                    handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                    using (var client = new HttpClient(handler) { BaseAddress = new Uri(session.BaseUrl), Timeout = TimeSpan.FromSeconds(2) })
                    {
                        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + session.Password));
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
                        using (var response = await client.GetAsync(path, token).ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode) return null;
                            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested) throw;
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static LcuSession DiscoverLcuSession()
        {
            var names = new[] { "LeagueClientUx", "LeagueClient" };
            foreach (var name in names)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        var executable = process.MainModule == null ? null : process.MainModule.FileName;
                        var directory = string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable);
                        var lockfile = string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, "lockfile");
                        if (string.IsNullOrWhiteSpace(lockfile) || !File.Exists(lockfile)) continue;
                        var parts = File.ReadAllText(lockfile).Trim().Split(':');
                        if (parts.Length < 5) continue;
                        int port;
                        if (!int.TryParse(parts[2], out port) || port <= 0) continue;
                        var protocol = string.IsNullOrWhiteSpace(parts[4]) ? "https" : parts[4];
                        return new LcuSession { BaseUrl = protocol + "://127.0.0.1:" + port + "/", Password = parts[3] };
                    }
                    catch
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            return null;
        }

        private static Dictionary<string, object> FindChampion(object[] champions, string slug, string name)
        {
            if (champions == null) return null;
            var normalizedSlug = Normalize(slug);
            var normalizedName = Normalize(name);
            return champions.OfType<Dictionary<string, object>>().FirstOrDefault(item =>
            {
                var alias = Normalize(ReadString(item, "alias"));
                var itemName = Normalize(ReadString(item, "name"));
                return alias == normalizedSlug || itemName == normalizedName || itemName == normalizedSlug;
            });
        }

        private static void EnrichChampionDetailFallback(MayhemChampionResult result, Dictionary<string, object> detail)
        {
            if (detail == null) return;
            if (string.IsNullOrWhiteSpace(result.ChampionSplashUrl))
            {
                var skins = AsArray(ReadObject(detail, "skins"));
                if (skins != null && skins.Length > 0)
                {
                    var skin = skins[0] as Dictionary<string, object>;
                    result.ChampionSplashUrl = AssetReference(
                        First(ReadString(skin, "uncenteredSplashPath"), ReadString(skin, "splashPath"), ReadString(skin, "loadScreenPath")));
                }
            }

            var spells = AsArray(ReadObject(detail, "spells"));
            if (spells == null) return;
            foreach (var spell in spells.OfType<Dictionary<string, object>>())
            {
                var key = (ReadString(spell, "spellKey") ?? string.Empty).ToUpperInvariant();
                if (key != "Q" && key != "W" && key != "E" && key != "R") continue;
                if (result.SkillIconUrls.ContainsKey(key) && !string.IsNullOrWhiteSpace(result.SkillIconUrls[key])) continue;
                var path = AssetReference(First(ReadString(spell, "abilityIconPath"), ReadString(spell, "iconPath")));
                if (!string.IsNullOrWhiteSpace(path)) result.SkillIconUrls[key] = path;
            }
        }

        private static void EnrichItemsFallback(MayhemChampionResult result, object[] items)
        {
            if (result.CoreItems.Count == 0) return;
            if (result.CoreItemIconUrls.Count >= result.CoreItems.Count) return;
            var output = new List<string>();
            for (var i = 0; i < result.CoreItems.Count; i++)
            {
                if (i < result.CoreItemIconUrls.Count && !string.IsNullOrWhiteSpace(result.CoreItemIconUrls[i]))
                {
                    output.Add(result.CoreItemIconUrls[i]);
                    continue;
                }
                if (items == null)
                {
                    output.Add(null);
                    continue;
                }
                var normalized = Normalize(result.CoreItems[i]);
                var item = items.OfType<Dictionary<string, object>>().FirstOrDefault(candidate =>
                {
                    var candidateName = Normalize(ReadString(candidate, "name"));
                    return candidateName.Length > 0 && (candidateName == normalized || normalized.Contains(candidateName) || candidateName.Contains(normalized));
                });
                output.Add(item == null ? null : AssetReference(ReadString(item, "iconPath")));
            }
            result.CoreItemIconUrls = output;
        }

        private static void EnrichAugments(MayhemChampionResult result, object[] augments)
        {
            result.AugmentIconUrls.Clear();
            if (augments == null) return;
            foreach (var requested in result.Augments)
            {
                var normalized = Normalize(requested);
                var augment = augments.OfType<Dictionary<string, object>>().FirstOrDefault(candidate =>
                {
                    var candidateName = Normalize(ReadString(candidate, "name"));
                    return candidateName.Length > 0 && (candidateName == normalized || normalized.Contains(candidateName) || candidateName.Contains(normalized));
                });
                result.AugmentIconUrls.Add(augment == null ? null : AssetReference(ReadString(augment, "iconPath")));
            }
        }

        private static void EnrichTopTenFallback(MayhemChampionResult result, object[] champions)
        {
            if (champions == null) return;
            foreach (var top in result.TopTen)
            {
                if (!string.IsNullOrWhiteSpace(top.IconUrl)) continue;
                var row = FindChampion(champions, top.Slug, top.Name);
                if (row == null) continue;
                top.IconUrl = AssetReference(ReadString(row, "squarePortraitPath"));
                if (string.IsNullOrWhiteSpace(top.Name))
                {
                    var officialName = ReadString(row, "name");
                    if (!string.IsNullOrWhiteSpace(officialName)) top.Name = officialName;
                }
            }
        }

        private static string AssetReference(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return path;
            if (!path.StartsWith("/", StringComparison.Ordinal)) path = "/lol-game-data/assets/" + path.TrimStart('/');
            return "lcu:" + path;
        }

        private static string ToPublicAssetUrl(string path)
        {
            const string prefix = "/lol-game-data/assets/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return CommunityDragonAssetBase + path.Substring(prefix.Length).TrimStart('/').ToLowerInvariant();
            return CommunityDragonAssetBase + path.TrimStart('/').ToLowerInvariant();
        }

        private static object[] AsArray(object value)
        {
            return value as object[];
        }

        private static object ReadObject(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            var value = ReadObject(source, key);
            return value == null ? null : Convert.ToString(value);
        }

        private static int ReadInt(Dictionary<string, object> source, string key)
        {
            var value = ReadObject(source, key);
            int result;
            return value != null && int.TryParse(Convert.ToString(value), out result) ? result : 0;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new StringBuilder(value.Length);
            foreach (var c in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c > 127) builder.Append(c);
            }
            return builder.ToString();
        }

        private static string First(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private static HttpClient CreatePublicClient()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM/3.1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.6");
            return client;
        }
    }
}
