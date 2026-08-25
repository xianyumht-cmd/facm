using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.League;
using FACM.Services;

namespace FACM.Mayhem
{
    internal static class MayhemDecisionLocalizationService
    {
        private const string ZhGameDataBase = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/zh_cn/v1/";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(20);
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient Client = CreateClient();

        private sealed class CacheEntry
        {
            public DateTime Time { get; set; }
            public object Value { get; set; }
        }

        public static async Task EnrichAsync(MayhemChampionResult result, ILeagueClientApi leagueClient, CancellationToken token)
        {
            if (result == null) return;

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromMilliseconds(1650));
                var budget = timeout.Token;

                var itemsTask = ReadJsonBestEffortAsync("items.json", leagueClient, budget, token);
                var augmentsTask = ReadJsonBestEffortAsync("cherry-augments.json", leagueClient, budget, token);
                var summonersTask = ReadJsonBestEffortAsync("summoner-spells.json", leagueClient, budget, token);
                var championsTask = ReadJsonBestEffortAsync("champion-summary.json", leagueClient, budget, token);

                await Task.WhenAll(itemsTask, augmentsTask, summonersTask, championsTask).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                ApplyItems(result, AsArray(itemsTask.Result));
                ApplyAugments(result, AsArray(augmentsTask.Result));
                ApplySummoners(result, AsArray(summonersTask.Result));

                var champion = FindChampion(AsArray(championsTask.Result), result.ChampionSlug, result.ChampionName);
                if (champion != null)
                {
                    var championId = ReadInt(champion, "id");
                    var portrait = FirstText(champion, "squarePortraitPath", "iconPath");
                    if (!string.IsNullOrWhiteSpace(portrait)) result.ChampionIconUrl = AssetReference(portrait);
                    else if (championId > 0) result.ChampionIconUrl = "lcu:/lol-game-data/assets/v1/champion-icons/" + championId + ".png";

                    if (championId > 0 && !budget.IsCancellationRequested)
                    {
                        var detail = await ReadJsonBestEffortAsync("champions/" + championId + ".json", leagueClient, budget, token).ConfigureAwait(false) as Dictionary<string, object>;
                        ApplyChampionSkills(result, detail);
                    }
                }

                ReprojectLegacyLists(result);
            }
        }

        internal static void ApplyFixtureForSmokeTest(
            MayhemChampionResult result,
            string itemsJson,
            string augmentsJson,
            string summonersJson,
            string championSummaryJson,
            string championDetailJson)
        {
            if (result == null) return;
            ApplyItems(result, ParseArray(itemsJson));
            ApplyAugments(result, ParseArray(augmentsJson));
            ApplySummoners(result, ParseArray(summonersJson));
            var champion = FindChampion(ParseArray(championSummaryJson), result.ChampionSlug, result.ChampionName);
            if (champion != null)
            {
                var portrait = FirstText(champion, "squarePortraitPath", "iconPath");
                if (!string.IsNullOrWhiteSpace(portrait)) result.ChampionIconUrl = AssetReference(portrait);
            }
            ApplyChampionSkills(result, ParseDictionary(championDetailJson));
            ReprojectLegacyLists(result);
        }

        private static async Task<object> ReadJsonBestEffortAsync(
            string relativePath,
            ILeagueClientApi leagueClient,
            CancellationToken budgetToken,
            CancellationToken userToken)
        {
            object cached;
            if (TryGetCache(relativePath, out cached)) return cached;

            try
            {
                if (leagueClient != null)
                {
                    var localPath = "/lol-game-data/assets/v1/" + relativePath.TrimStart('/');
                    var bytes = await leagueClient.TryGetBytesAsync(localPath, budgetToken).ConfigureAwait(false);
                    var local = TryDeserialize(bytes == null ? null : Encoding.UTF8.GetString(bytes));
                    if (local != null)
                    {
                        PutCache(relativePath, local);
                        return local;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (userToken.IsCancellationRequested) throw;
                return null;
            }
            catch
            {
            }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, ZhGameDataBase + relativePath.TrimStart('/')))
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budgetToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return null;
                    var text = await CancelableHttpContentReader.ReadStringAsync(response.Content, budgetToken).ConfigureAwait(false);
                    var value = TryDeserialize(text);
                    if (value != null) PutCache(relativePath, value);
                    return value;
                }
            }
            catch (OperationCanceledException)
            {
                if (userToken.IsCancellationRequested) throw;
                return null;
            }
            catch (Exception exception)
            {
                AppLog.Info("Mayhem localized game-data skipped: " + relativePath + "; " + exception.GetType().Name);
                return null;
            }
        }

        private static void ApplyItems(MayhemChampionResult result, object[] catalog)
        {
            if (result == null || catalog == null) return;
            foreach (var build in result.CoreBuilds ?? new List<MayhemBuildPath>()) LocalizeItemList(build == null ? null : build.Items, catalog);
            LocalizeItemList(result.StarterItems, catalog);
            LocalizeItemList(result.BootItems, catalog);
        }

        private static void LocalizeItemList(IList<MayhemBuildItem> values, object[] catalog)
        {
            if (values == null) return;
            foreach (var item in values)
            {
                if (item == null) continue;
                var row = FindItem(catalog, item);
                if (row == null) continue;
                var name = FirstText(row, "nameTRA", "name");
                var icon = FirstText(row, "iconPath", "icon");
                if (!string.IsNullOrWhiteSpace(name) && ContainsCjk(name)) item.Name = name.Trim();
                if (!string.IsNullOrWhiteSpace(icon)) item.IconUrl = AssetReference(icon);
                if (string.IsNullOrWhiteSpace(item.Id)) item.Id = ReadString(row, "id");
            }
        }

        private static Dictionary<string, object> FindItem(object[] catalog, MayhemBuildItem item)
        {
            if (catalog == null || item == null) return null;
            var id = FirstNonEmpty(item.Id, ExtractNumericId(item.IconUrl));
            if (!string.IsNullOrWhiteSpace(id))
            {
                var byId = catalog.OfType<Dictionary<string, object>>().FirstOrDefault(row => string.Equals(ReadString(row, "id"), id, StringComparison.OrdinalIgnoreCase));
                if (byId != null) return byId;
            }
            var key = NormalizeKey(item.Name);
            return catalog.OfType<Dictionary<string, object>>().FirstOrDefault(row =>
                (NormalizeKey(ReadString(row, "name")) == key || NormalizeKey(ReadString(row, "nameTRA")) == key) && key.Length > 0);
        }

        private static void ApplyAugments(MayhemChampionResult result, object[] catalog)
        {
            if (result == null || catalog == null || result.AugmentRows == null) return;
            var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in result.AugmentRows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Name)) continue;
                var oldName = row.Name;
                var localized = FindAugment(catalog, row);
                if (localized == null) continue;

                var name = FirstText(localized, "nameTRA", "name");
                var icon = FirstText(
                    localized,
                    "augmentSmallIconPath",
                    "augmentIconPath",
                    "augmentLargeIconPath",
                    "iconSmall",
                    "iconLarge",
                    "smallIconPath",
                    "iconPath",
                    "icon");
                var description = CleanDescription(FirstText(
                    localized,
                    "descTRA",
                    "descriptionTRA",
                    "description",
                    "desc",
                    "tooltip"));
                if (!string.IsNullOrWhiteSpace(name) && ContainsCjk(name)) row.Name = name.Trim();
                if (!string.IsNullOrWhiteSpace(icon)) row.IconUrl = AssetReference(icon);
                if (!string.IsNullOrWhiteSpace(description)) row.Description = description;
                if (string.IsNullOrWhiteSpace(row.Id)) row.Id = FirstText(localized, "id", "augmentId", "apiName");
                if (!string.Equals(oldName, row.Name, StringComparison.OrdinalIgnoreCase)) renamed[oldName] = row.Name;
            }

            if (result.AugmentRoutes != null)
            {
                foreach (var route in result.AugmentRoutes)
                {
                    if (route == null || string.IsNullOrWhiteSpace(route.AugmentName)) continue;
                    string localized;
                    if (renamed.TryGetValue(route.AugmentName, out localized)) route.AugmentName = localized;
                }
            }
        }

        private static Dictionary<string, object> FindAugment(object[] catalog, MayhemAugmentRow row)
        {
            if (catalog == null || row == null) return null;
            if (!string.IsNullOrWhiteSpace(row.Id))
            {
                var byId = catalog.OfType<Dictionary<string, object>>().FirstOrDefault(candidate =>
                    string.Equals(FirstText(candidate, "id", "augmentId"), row.Id, StringComparison.OrdinalIgnoreCase));
                if (byId != null) return byId;
            }

            var sourceKeys = new[]
            {
                NormalizeKey(row.Slug),
                NormalizeKey(row.Name)
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
            if (sourceKeys.Length == 0) return null;

            return catalog.OfType<Dictionary<string, object>>().FirstOrDefault(candidate =>
            {
                var candidateKeys = new[]
                {
                    NormalizeKey(FirstText(candidate, "apiName", "internalName", "slug")),
                    NormalizeKey(ReadString(candidate, "name")),
                    NormalizeKey(ReadString(candidate, "nameTRA")),
                    NormalizeKey(FileToken(FirstText(
                        candidate,
                        "augmentSmallIconPath",
                        "augmentIconPath",
                        "augmentLargeIconPath",
                        "iconSmall",
                        "iconLarge",
                        "smallIconPath",
                        "iconPath",
                        "icon")))
                }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

                foreach (var sourceKey in sourceKeys)
                {
                    foreach (var candidateKey in candidateKeys)
                    {
                        if (candidateKey == sourceKey) return true;
                        if (sourceKey.Length < 5 || candidateKey.Length < 5) continue;
                        if (candidateKey.EndsWith(sourceKey, StringComparison.OrdinalIgnoreCase) ||
                            candidateKey.IndexOf(sourceKey, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            sourceKey.EndsWith(candidateKey, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                return false;
            });
        }

        private static void ApplySummoners(MayhemChampionResult result, object[] catalog)
        {
            if (result == null || catalog == null || result.SummonerSpells == null) return;
            foreach (var spell in result.SummonerSpells)
            {
                if (spell == null) continue;
                var fileKey = NormalizeKey(FileToken(spell.IconUrl));
                var sourceName = NormalizeKey(spell.Name);
                var localized = catalog.OfType<Dictionary<string, object>>().FirstOrDefault(candidate =>
                {
                    var iconKey = NormalizeKey(FileToken(FirstText(candidate, "iconPath", "icon")));
                    var apiKey = NormalizeKey(FirstText(candidate, "apiName", "alias", "name"));
                    return (fileKey.Length > 0 && iconKey == fileKey) || (sourceName.Length > 0 && apiKey == sourceName);
                });
                if (localized == null) continue;
                var name = FirstText(localized, "nameTRA", "name");
                var icon = FirstText(localized, "iconPath", "icon");
                if (!string.IsNullOrWhiteSpace(name) && ContainsCjk(name)) spell.Name = name.Trim();
                if (!string.IsNullOrWhiteSpace(icon)) spell.IconUrl = AssetReference(icon);
            }
        }

        private static void ApplyChampionSkills(MayhemChampionResult result, Dictionary<string, object> detail)
        {
            if (result == null || detail == null) return;
            var spells = AsArray(ReadObject(detail, "spells"));
            if (spells == null) return;
            foreach (var spell in spells.OfType<Dictionary<string, object>>())
            {
                var key = (ReadString(spell, "spellKey") ?? string.Empty).Trim().ToUpperInvariant();
                if (key != "Q" && key != "W" && key != "E" && key != "R") continue;
                var icon = FirstText(spell, "abilityIconPath", "iconPath");
                var name = FirstText(spell, "nameTRA", "name");
                if (!string.IsNullOrWhiteSpace(icon)) result.SkillIconUrls[key] = AssetReference(icon);
                if (result.SkillPriority == null) continue;
                foreach (var priority in result.SkillPriority.Where(value => value != null && string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!string.IsNullOrWhiteSpace(icon)) priority.IconUrl = AssetReference(icon);
                    if (!string.IsNullOrWhiteSpace(name) && ContainsCjk(name)) priority.Name = name.Trim();
                }
            }
        }

        private static Dictionary<string, object> FindChampion(object[] champions, string slug, string name)
        {
            if (champions == null) return null;
            var slugKey = NormalizeKey(slug);
            var nameKey = NormalizeKey(name);
            return champions.OfType<Dictionary<string, object>>().FirstOrDefault(row =>
            {
                var alias = NormalizeKey(ReadString(row, "alias"));
                var display = NormalizeKey(FirstText(row, "nameTRA", "name"));
                return (slugKey.Length > 0 && alias == slugKey) || (nameKey.Length > 0 && display == nameKey);
            });
        }

        private static void ReprojectLegacyLists(MayhemChampionResult result)
        {
            if (result == null) return;
            if (result.CoreBuilds != null && result.CoreBuilds.Count > 0 && result.CoreBuilds[0] != null)
            {
                var first = result.CoreBuilds[0].Items.Take(5).ToList();
                result.CoreItems = first.Select(item => item == null ? null : item.Name).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
                result.CoreItemIconUrls = first.Select(item => item == null ? null : item.IconUrl).ToList();
            }
            if (result.AugmentRows != null && result.AugmentRows.Count > 0)
            {
                result.Augments = result.AugmentRows.Take(5).Select(row => row == null ? null : row.Name).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
                result.AugmentIconUrls = result.AugmentRows.Take(5).Select(row => row == null ? null : row.IconUrl).ToList();
            }
        }

        private static object TryDeserialize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text); }
            catch { return null; }
        }

        private static object[] ParseArray(string json) { return AsArray(TryDeserialize(json)); }
        private static Dictionary<string, object> ParseDictionary(string json) { return TryDeserialize(json) as Dictionary<string, object>; }
        private static object[] AsArray(object value)
        {
            var array = value as object[];
            if (array != null) return array;
            var dictionary = value as Dictionary<string, object>;
            return dictionary == null ? null : dictionary.Values.ToArray();
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
            int value;
            return int.TryParse(ReadString(source, key), out value) ? value : 0;
        }

        private static string FirstText(Dictionary<string, object> source, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = ReadString(source, key);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return null;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static string ExtractNumericId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var match = Regex.Match(value, "(?:item[/_-]?|/)(?<id>\\d{3,6})(?:\\.png|\\?|/|$)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["id"].Value : null;
        }

        private static string FileToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var clean = value.Split('?')[0].TrimEnd('/');
            var index = clean.LastIndexOf('/');
            if (index >= 0) clean = clean.Substring(index + 1);
            var dot = clean.LastIndexOf('.');
            return dot > 0 ? clean.Substring(0, dot) : clean;
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new StringBuilder(value.Length);
            foreach (var c in value.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) builder.Append(c);
            return builder.ToString();
        }

        private static bool ContainsCjk(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Any(c => c >= 0x3400 && c <= 0x9fff);
        }

        private static string CleanDescription(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = Regex.Replace(value, "<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, "\\s+", " ").Trim();
            return text;
        }

        private static string AssetReference(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var value = path.Trim().Replace('\\', '/');
            if (value.StartsWith("lcu:", StringComparison.OrdinalIgnoreCase)) return value;

            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var marker = "/game/assets/";
                var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    marker = "/global/default/";
                    index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                }
                if (index < 0) return value;
                value = value.Substring(index + marker.Length);
            }

            value = value.TrimStart('/');
            if (value.StartsWith("lol-game-data/assets/", StringComparison.OrdinalIgnoreCase))
                return "lcu:/" + value;
            if (value.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("assets/".Length);
            return "lcu:/lol-game-data/assets/" + value;
        }

        private static bool TryGetCache(string key, out object value)
        {
            lock (Sync)
            {
                CacheEntry entry;
                if (Cache.TryGetValue(key, out entry) && DateTime.UtcNow - entry.Time < CacheLifetime)
                {
                    value = entry.Value;
                    return value != null;
                }
            }
            value = null;
            return false;
        }

        private static void PutCache(string key, object value)
        {
            if (value == null) return;
            lock (Sync)
            {
                Cache[key] = new CacheEntry { Time = DateTime.UtcNow, Value = value };
                if (Cache.Count <= 20) return;
                var expired = Cache.Where(pair => DateTime.UtcNow - pair.Value.Time >= CacheLifetime).Select(pair => pair.Key).ToList();
                foreach (var item in expired) Cache.Remove(item);
            }
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM/3.5 MayhemDecisionCard");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9");
            return client;
        }
    }
}
