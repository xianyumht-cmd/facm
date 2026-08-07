using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.Mayhem
{
    internal static class RiotDataDragonService
    {
        private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";
        private const string CdnBase = "https://ddragon.leagueoflegends.com/cdn/";
        private static readonly HttpClient Client = CreateClient();
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static readonly object Sync = new object();
        private static DateTime _cacheTime;
        private static string _version;
        private static Dictionary<string, object> _champions;
        private static Dictionary<string, object> _items;

        public static async Task EnrichAsync(MayhemChampionResult result, CancellationToken token)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.ChampionSlug)) return;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var ct = timeout.Token;
                try
                {
                    await EnsureMetadataAsync(ct).ConfigureAwait(false);
                    string version;
                    Dictionary<string, object> champions;
                    Dictionary<string, object> items;
                    lock (Sync)
                    {
                        version = _version;
                        champions = _champions;
                        items = _items;
                    }
                    if (string.IsNullOrWhiteSpace(version) || champions == null) return;

                    var championPair = FindChampion(champions, result.ChampionSlug, result.ChampionName);
                    if (!string.IsNullOrWhiteSpace(championPair.Key) && championPair.Value != null)
                    {
                        var row = championPair.Value;
                        var officialName = ReadString(row, "name");
                        if (!string.IsNullOrWhiteSpace(officialName)) result.ChampionName = officialName;
                        result.ChampionIconUrl = CdnBase + version + "/img/champion/" + Uri.EscapeDataString(championPair.Key) + ".png";
                        result.ChampionSplashUrl = CdnBase + "img/champion/splash/" + Uri.EscapeDataString(championPair.Key) + "_0.jpg";
                        await EnrichSkillsAsync(result, version, championPair.Key, ct).ConfigureAwait(false);
                    }

                    EnrichItems(result, version, items);
                    EnrichTopTen(result, version, champions);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                }
                catch (Exception exception)
                {
                    Services.AppLog.Info("Riot Data Dragon enrichment skipped: " + exception.Message);
                }
            }
        }

        private static async Task EnsureMetadataAsync(CancellationToken token)
        {
            lock (Sync)
            {
                if (!string.IsNullOrWhiteSpace(_version) && _champions != null && _items != null && DateTime.UtcNow - _cacheTime < TimeSpan.FromMinutes(30))
                    return;
            }

            var versionsJson = await GetStringAsync(VersionsUrl, token).ConfigureAwait(false);
            var versions = Json.Deserialize<string[]>(versionsJson);
            var version = versions == null ? null : versions.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(version)) return;

            var championTask = GetStringAsync(CdnBase + version + "/data/zh_CN/champion.json", token);
            var itemTask = GetStringAsync(CdnBase + version + "/data/zh_CN/item.json", token);
            await Task.WhenAll(championTask, itemTask).ConfigureAwait(false);

            var championRoot = Json.DeserializeObject(championTask.Result) as Dictionary<string, object>;
            var itemRoot = Json.DeserializeObject(itemTask.Result) as Dictionary<string, object>;
            var champions = ReadDictionary(championRoot, "data");
            var items = ReadDictionary(itemRoot, "data");
            if (champions == null || items == null) return;

            lock (Sync)
            {
                _version = version;
                _champions = champions;
                _items = items;
                _cacheTime = DateTime.UtcNow;
            }
        }

        private static async Task EnrichSkillsAsync(MayhemChampionResult result, string version, string championId, CancellationToken token)
        {
            var json = await GetStringAsync(CdnBase + version + "/data/zh_CN/champion/" + Uri.EscapeDataString(championId) + ".json", token).ConfigureAwait(false);
            var root = Json.DeserializeObject(json) as Dictionary<string, object>;
            var data = ReadDictionary(root, "data");
            if (data == null) return;
            object detailObject;
            if (!data.TryGetValue(championId, out detailObject)) detailObject = data.Values.FirstOrDefault();
            var detail = detailObject as Dictionary<string, object>;
            if (detail == null) return;
            var spells = ReadArray(detail, "spells");
            if (spells == null) return;

            var keys = new[] { "Q", "W", "E", "R" };
            for (var i = 0; i < keys.Length && i < spells.Length; i++)
            {
                var spell = spells[i] as Dictionary<string, object>;
                var image = ReadDictionary(spell, "image");
                var filename = ReadString(image, "full");
                if (!string.IsNullOrWhiteSpace(filename))
                    result.SkillIconUrls[keys[i]] = CdnBase + version + "/img/spell/" + Uri.EscapeDataString(filename);
            }
        }

        private static void EnrichItems(MayhemChampionResult result, string version, Dictionary<string, object> items)
        {
            if (items == null) return;
            result.CoreItemIconUrls.Clear();
            foreach (var requested in result.CoreItems)
            {
                var normalized = Normalize(requested);
                Dictionary<string, object> match = null;
                foreach (var itemObject in items.Values)
                {
                    var item = itemObject as Dictionary<string, object>;
                    var name = Normalize(ReadString(item, "name"));
                    if (name.Length == 0) continue;
                    if (name == normalized || normalized.Contains(name) || name.Contains(normalized))
                    {
                        match = item;
                        break;
                    }
                }
                var image = ReadDictionary(match, "image");
                var filename = ReadString(image, "full");
                result.CoreItemIconUrls.Add(string.IsNullOrWhiteSpace(filename) ? null : CdnBase + version + "/img/item/" + Uri.EscapeDataString(filename));
            }
        }

        private static void EnrichTopTen(MayhemChampionResult result, string version, Dictionary<string, object> champions)
        {
            if (champions == null) return;
            foreach (var top in result.TopTen)
            {
                var pair = FindChampion(champions, top.Slug, top.Name);
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
                top.IconUrl = CdnBase + version + "/img/champion/" + Uri.EscapeDataString(pair.Key) + ".png";
                var officialName = ReadString(pair.Value, "name");
                if (!string.IsNullOrWhiteSpace(officialName)) top.Name = officialName;
            }
        }

        private static KeyValuePair<string, Dictionary<string, object>> FindChampion(Dictionary<string, object> champions, string slug, string name)
        {
            if (champions == null) return default(KeyValuePair<string, Dictionary<string, object>>);
            var normalizedSlug = Normalize(slug);
            var normalizedName = Normalize(name);
            foreach (var pair in champions)
            {
                var row = pair.Value as Dictionary<string, object>;
                if (row == null) continue;
                var id = Normalize(ReadString(row, "id"));
                var officialName = Normalize(ReadString(row, "name"));
                var key = Normalize(pair.Key);
                if (id == normalizedSlug || key == normalizedSlug || officialName == normalizedName || officialName == normalizedSlug)
                    return new KeyValuePair<string, Dictionary<string, object>>(pair.Key, row);
            }
            return default(KeyValuePair<string, Dictionary<string, object>>);
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            if (source == null) return null;
            object value;
            return source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static object[] ReadArray(Dictionary<string, object> source, string key)
        {
            if (source == null) return null;
            object value;
            return source.TryGetValue(key, out value) ? value as object[] : null;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            if (source == null) return null;
            object value;
            return source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
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

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM/3.1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.5");
            return client;
        }
    }
}
