using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Mayhem
{
    internal static class OpggMayhemService
    {
        private const string HexdataHeroesUrl = "https://hexdata.com.cn/heroes";
        private const string OpggBaseUrl = "https://op.gg/zh-cn/lol/modes/aram-mayhem";
        private const string RankingBaseUrl = "https://arammayhem.com";
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient Client = CreateClient();

        private sealed class CacheEntry
        {
            public DateTime Time { get; set; }
            public MayhemChampionResult Value { get; set; }
        }

        private sealed class HexdataChampionRow
        {
            public int Rank { get; set; }
            public string Name { get; set; }
            public string Slug { get; set; }
            public double? WinRate { get; set; }
        }

        public static Task<MayhemChampionResult> QueryAsync(string input, CancellationToken token)
        {
            return QueryAsync(input, null, token);
        }

        public static async Task<MayhemChampionResult> QueryAsync(string input, IProgress<string> progress, CancellationToken token)
        {
            var query = (input ?? string.Empty).Trim();
            if (query.Length == 0) return new MayhemChampionResult { ErrorMessage = "请输入英雄名称或别名。" };

            lock (Sync)
            {
                CacheEntry cached;
                if (Cache.TryGetValue(query, out cached) && DateTime.UtcNow - cached.Time < TimeSpan.FromMinutes(10))
                {
                    Report(progress, "已命中 10 分钟本地缓存");
                    return cached.Value;
                }
            }

            var result = new MayhemChampionResult { Query = query };
            using (var overall = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                overall.CancelAfter(TimeSpan.FromSeconds(5.5));
                var requestToken = overall.Token;
                try
                {
                    Report(progress, "正在读取国内排行与国服版本...");
                    var hexdataTask = GetSafeAsync(HexdataHeroesUrl, TimeSpan.FromSeconds(2.8), requestToken);
                    var officialTask = TencentMayhemPatchService.FetchLatestAsync(requestToken);

                    string slug;
                    string hexdataHtml = null;
                    if (!ChampionAliases.TryResolve(query, out slug))
                    {
                        hexdataHtml = await hexdataTask.ConfigureAwait(false);
                        slug = ResolveSlugFromHexdata(hexdataHtml, query);
                        if (string.IsNullOrWhiteSpace(slug))
                        {
                            var opggIndex = await GetSafeAsync(OpggBaseUrl, TimeSpan.FromSeconds(1.5), requestToken).ConfigureAwait(false);
                            slug = ResolveSlugFromOpgg(opggIndex, query);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        result.ErrorMessage = "没有识别到这个英雄，请尝试官方中文名、英文名或常见简称。";
                        return result;
                    }

                    result.ChampionSlug = slug;
                    result.SourceUrl = OpggBaseUrl + "/" + slug + "/build";
                    result.RankingSourceUrl = RankingBaseUrl + "/build/" + slug + "/";

                    Report(progress, "正在并行读取排行、平衡与攻略补充...");
                    var rankingTask = GetSafeAsync(result.RankingSourceUrl, TimeSpan.FromSeconds(3.8), requestToken);
                    var rankingTopTask = GetSafeAsync(RankingBaseUrl + "/", TimeSpan.FromSeconds(3.4), requestToken);
                    var opggTask = GetSafeAsync(result.SourceUrl, TimeSpan.FromSeconds(2.2), requestToken);

                    if (hexdataHtml == null) hexdataHtml = await hexdataTask.ConfigureAwait(false);
                    await Task.WhenAll(rankingTask, rankingTopTask, opggTask).ConfigureAwait(false);
                    var rankingHtml = rankingTask.Result;
                    var rankingTopHtml = rankingTopTask.Result;
                    var opggHtml = opggTask.Result;

                    Report(progress, "正在合并国内排行、当前平衡和攻略字段...");
                    var hexRows = ParseHexdataRows(hexdataHtml);
                    var hexTargetFound = ApplyHexdata(hexRows, slug, query, result);

                    ParseRankingChampion(rankingHtml, result);
                    if (result.TopTen.Count < 10)
                    {
                        var fallbackTop = ParseTopTen(rankingTopHtml);
                        if (fallbackTop.Count > result.TopTen.Count) result.TopTen = fallbackTop;
                    }
                    ParseOpggChampion(opggHtml, result);

                    var current = result.TopTen.FirstOrDefault(item =>
                        string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ChampionAliases.Normalize(item.Name), ChampionAliases.Normalize(result.ChampionName), StringComparison.OrdinalIgnoreCase));
                    if (current != null)
                    {
                        if (!result.Rank.HasValue) result.Rank = current.Rank;
                        if (!result.WinRate.HasValue) result.WinRate = current.WinRate;
                        if (string.IsNullOrWhiteSpace(result.Tier)) result.Tier = current.Tier;
                    }

                    if (string.IsNullOrWhiteSpace(result.ChampionName)) result.ChampionName = Title(slug);

                    TencentMayhemPatchSnapshot official = null;
                    try { official = await officialTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { if (token.IsCancellationRequested) throw; }
                    ApplyOfficialPatch(result, official, !string.IsNullOrWhiteSpace(rankingHtml), query);

                    var anyPrimary = hexTargetFound || !string.IsNullOrWhiteSpace(rankingHtml) || !string.IsNullOrWhiteSpace(opggHtml);
                    if (!anyPrimary && string.IsNullOrWhiteSpace(result.BalanceSummary))
                    {
                        result.ErrorMessage = token.IsCancellationRequested
                            ? "查询已取消。"
                            : "暂时没有读取到可用排行，请稍后重试。";
                        return result;
                    }

                    if (string.IsNullOrWhiteSpace(result.Tier) && result.Rank.HasValue)
                        result.Tier = InferTier(result.Rank.Value);

                    result.SourceNote = BuildSourceNote(
                        hexTargetFound,
                        !string.IsNullOrWhiteSpace(rankingHtml),
                        !string.IsNullOrWhiteSpace(opggHtml),
                        official != null);

                    lock (Sync)
                    {
                        Cache[query] = new CacheEntry { Time = DateTime.UtcNow, Value = result };
                    }
                    Report(progress, "查询完成");
                    return result;
                }
                catch (OperationCanceledException)
                {
                    result.ErrorMessage = token.IsCancellationRequested
                        ? "查询已取消。"
                        : "查询超过 5.5 秒，已返回前仍未得到可用结果。";
                    return result;
                }
                catch (Exception exception)
                {
                    AppLog.Error("Mayhem resilient query failed", exception);
                    result.ErrorMessage = "读取数据失败：" + exception.Message;
                    return result;
                }
            }
        }

        private static bool ApplyHexdata(IList<HexdataChampionRow> rows, string slug, string query, MayhemChampionResult result)
        {
            if (rows == null || rows.Count == 0) return false;

            result.TopTen = rows.Take(10).Select(row => new MayhemTopChampion
            {
                Rank = row.Rank,
                Name = row.Name,
                Slug = row.Slug,
                WinRate = row.WinRate,
                Tier = row.Rank <= 10 ? "S+" : InferTier(row.Rank)
            }).ToList();

            var normalizedQuery = ChampionAliases.Normalize(query);
            var target = rows.FirstOrDefault(row => string.Equals(row.Slug, slug, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                target = rows.FirstOrDefault(row =>
                {
                    var name = ChampionAliases.Normalize(row.Name);
                    return normalizedQuery.Length > 0 && (name.Contains(normalizedQuery) || normalizedQuery.Contains(name));
                });
            }
            if (target == null) return false;

            result.ChampionName = target.Name;
            result.Rank = target.Rank;
            result.WinRate = target.WinRate;
            result.Tier = InferTier(target.Rank);
            return true;
        }

        private static List<HexdataChampionRow> ParseHexdataRows(string html)
        {
            var output = new List<HexdataChampionRow>();
            if (string.IsNullOrWhiteSpace(html)) return output;
            var normalized = NormalizeEscapedHtml(html);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var anchors = Regex.Matches(
                normalized,
                "<a\\b[^>]*href\\s*=\\s*[\"'](?<href>[^\"']*/hero/(?<id>\\d+)-(?<slug>[a-z0-9-]+))[^\"']*[\"'][^>]*>(?<body>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match anchor in anchors)
            {
                var slug = WebUtility.HtmlDecode(anchor.Groups["slug"].Value).Trim();
                var name = ExtractPreferredChampionName(CleanText(anchor.Groups["body"].Value));
                if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(name) ||
                    string.Equals(name, "查看详情", StringComparison.OrdinalIgnoreCase) || seen.Contains(slug)) continue;

                var windowLength = Math.Min(700, normalized.Length - anchor.Index);
                var window = CleanText(normalized.Substring(anchor.Index, windowLength));
                var winText = First(
                    Match(window, "胜率\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%", false),
                    Match(window, "(?<v>\\d{1,2}(?:\\.\\d+)?)%\\s*[·•]?\\s*样本", false));
                var winRate = Rate(winText);
                if (!winRate.HasValue) continue;

                seen.Add(slug);
                output.Add(new HexdataChampionRow
                {
                    Rank = output.Count + 1,
                    Name = name,
                    Slug = slug,
                    WinRate = winRate
                });
            }
            return output;
        }

        private static string ResolveSlugFromHexdata(string html, string query)
        {
            var target = ChampionAliases.Normalize(query);
            if (target.Length == 0) return null;
            foreach (var row in ParseHexdataRows(html))
            {
                var name = ChampionAliases.Normalize(row.Name);
                if (name == target || name.Contains(target) || target.Contains(name)) return row.Slug;
            }
            return null;
        }

        private static void ApplyOfficialPatch(
            MayhemChampionResult result,
            TencentMayhemPatchSnapshot official,
            bool fullStateFetched,
            string query)
        {
            if (official == null)
            {
                if (fullStateFetched && string.IsNullOrWhiteSpace(result.BalanceSummary))
                    result.BalanceSummary = "当前版本未发现英雄专属平衡修正。";
                return;
            }

            result.Patch = official.Patch;
            var changes = official.FindChampionChanges(result.ChampionName, query);
            var rankingPatch = result.RankingPatch;
            var fullStateCurrent = fullStateFetched &&
                                   !string.IsNullOrWhiteSpace(rankingPatch) &&
                                   PatchesMatch(rankingPatch, official.Patch);

            if (fullStateFetched && !string.IsNullOrWhiteSpace(rankingPatch) && !fullStateCurrent)
            {
                result.RankingPatch = null;
                result.BalanceSummary = changes.Count > 0
                    ? "国服 " + official.Patch + " 本版本官方改动（完整当前状态同步中）：" + string.Join(" · ", changes)
                    : "国服 " + official.Patch + " 平衡状态正在同步，暂不展示 " + rankingPatch + " 的旧数值。";
                return;
            }

            if (fullStateCurrent)
            {
                if (string.IsNullOrWhiteSpace(result.BalanceSummary))
                    result.BalanceSummary = "当前版本：无英雄专属修正。";
                return;
            }

            if (string.IsNullOrWhiteSpace(result.BalanceSummary) && changes.Count > 0)
            {
                result.BalanceSummary = "国服 " + official.Patch + " 本版本官方改动（非完整当前状态）：" + string.Join(" · ", changes);
            }
        }

        private static bool PatchesMatch(string first, string second)
        {
            Version a;
            Version b;
            return Version.TryParse(first, out a) && Version.TryParse(second, out b) && a.Equals(b);
        }

        private static string BuildSourceNote(bool hexdata, bool ranking, bool opgg, bool official)
        {
            var parts = new List<string>();
            parts.Add(hexdata ? "排行：Hexdata 国内优先" : (ranking ? "排行：ARAMMayhem 备用" : "排行：部分降级"));
            parts.Add(opgg ? "攻略：OP.GG 已补充" : "攻略：OP.GG 未连接也可查询");
            parts.Add(ranking ? "平衡：ARAMMayhem 完整状态" : "平衡：完整状态未连接");
            parts.Add(official ? "国服版本：腾讯官网已校验" : "国服版本：本次未校验");
            return string.Join("；", parts);
        }

        private static async Task<string> GetSafeAsync(string url, TimeSpan budget, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(budget);
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            AppLog.Info("Mayhem source returned HTTP " + (int)response.StatusCode + ": " + url);
                            return null;
                        }
                        return await CancelableHttpContentReader.ReadStringAsync(response.Content, timeout.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    AppLog.Info("Mayhem source budget expired: " + url);
                    return null;
                }
                catch (Exception exception)
                {
                    AppLog.Info("Mayhem source request failed: " + url + "; " + exception.Message);
                    return null;
                }
            }
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/151.0 FACM/3.1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.7");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.7");
            return client;
        }

        private static void ParseOpggChampion(string html, MayhemChampionResult result)
        {
            if (string.IsNullOrWhiteSpace(html)) return;
            var normalized = NormalizeEscapedHtml(html);
            var text = CleanText(normalized);
            var h1 = Match(normalized, "<h1[^>]*>(?<v>.*?)</h1>", true);

            if (string.IsNullOrWhiteSpace(result.ChampionName))
                result.ChampionName = First(CleanName(h1), result.ChampionName);
            if (string.IsNullOrWhiteSpace(result.Patch))
                result.Patch = First(
                    Match(text, "(?:在|Patch\\s*)?(?<v>\\d{1,2}\\.\\d{1,2})\\s*(?:版本|Patch)", false),
                    Match(text, "(?:版本|Patch)\\s*(?<v>\\d{1,2}\\.\\d{1,2})", false),
                    result.Patch);
            if (string.IsNullOrWhiteSpace(result.SkillOrder)) result.SkillOrder = ExtractSkillOrder(text);

            var items = ExtractAltSection(
                normalized,
                new[] { "核心装备", "核心出装", "Builds Table", "Core builds", "Core Items" },
                new[] { "广告", "增幅装置", "Augments", "召唤师技能", "Summoner" },
                4);
            if (items.Count > 0) result.CoreItems = items;

            var augments = ExtractAltSection(
                normalized,
                new[] { " 增幅装置", "增幅装置", "强化符文", "Augments" },
                new[] { "召唤师技能", "Summoner", "技能加点", "Skills" },
                8);
            if (augments.Count > 0) result.Augments = augments;
        }

        private static void ParseRankingChampion(string html, MayhemChampionResult result)
        {
            if (string.IsNullOrWhiteSpace(html)) return;
            var text = CleanText(NormalizeEscapedHtml(html));

            result.RankingPatch = First(
                Match(text, "Patch\\s*:\\s*(?<v>\\d{1,2}\\.\\d{1,2})", false),
                Match(text, "patch\\s*(?<v>\\d{1,2}\\.\\d{1,2})", false),
                result.RankingPatch);
            if (string.IsNullOrWhiteSpace(result.Tier))
                result.Tier = Match(text, "\\b(?<v>S\\+|S|A|B|C|D|F)\\s+Tier\\s+ARAM", false);
            if (!result.WinRate.HasValue)
                result.WinRate = FirstRate(
                    Match(text, "(?<v>\\d{1,2}(?:\\.\\d+)?)%\\s*WR", false),
                    Match(text, "win rate\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%", false),
                    result.WinRate);
            result.PickRate = FirstRate(
                Match(text, "(?<v>\\d{1,2}(?:\\.\\d+)?)%\\s*PR", false),
                Match(text, "pick rate\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%", false),
                result.PickRate);

            if (!result.Rank.HasValue)
            {
                int rank;
                var rankText = Match(text, "Rank\\s*:\\s*(?<v>\\d{1,3})", false);
                if (int.TryParse(rankText, NumberStyles.Integer, CultureInfo.InvariantCulture, out rank)) result.Rank = rank;
            }

            result.BalanceSummary = ParseBalanceAdjustments(text);
            if (result.Augments.Count == 0) result.Augments = ParseRankingAugments(text, 8);
        }

        private static List<MayhemTopChampion> ParseTopTen(string html)
        {
            var output = new List<MayhemTopChampion>();
            if (string.IsNullOrWhiteSpace(html)) return output;
            var text = CleanText(NormalizeEscapedHtml(html));
            var marker = text.IndexOf("TOP 10 Highest Win Rate Champions", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) marker = text.IndexOf("TOP 10", StringComparison.OrdinalIgnoreCase);
            var section = marker < 0 ? text : text.Substring(marker, Math.Min(2200, text.Length - marker));

            foreach (Match match in Regex.Matches(
                section,
                "(?<!\\d)(?<r>10|[1-9])\\s+(?<n>[A-Za-z][A-Za-z0-9' .-]{1,30}?)\\s+(?<w>\\d{1,2}\\.\\d{1,2})%",
                RegexOptions.IgnoreCase))
            {
                int rank;
                if (!int.TryParse(match.Groups["r"].Value, out rank)) continue;
                var name = match.Groups["n"].Value.Trim();
                var win = Rate(match.Groups["w"].Value);
                if (!win.HasValue || output.Any(item => item.Rank == rank)) continue;
                output.Add(new MayhemTopChampion
                {
                    Rank = rank,
                    Name = name,
                    Slug = ChampionAliases.Slugify(name),
                    WinRate = win,
                    Tier = rank <= 7 ? "S+" : "S"
                });
            }
            return output.OrderBy(item => item.Rank).Take(10).ToList();
        }

        private static string ResolveSlugFromOpgg(string html, string query)
        {
            var target = ChampionAliases.Normalize(query);
            var normalized = NormalizeEscapedHtml(html ?? string.Empty);
            foreach (Match match in Regex.Matches(normalized, "/aram-mayhem/(?<slug>[^/\"'?]+)/build", RegexOptions.IgnoreCase))
            {
                var slug = match.Groups["slug"].Value;
                var start = Math.Max(0, match.Index - 300);
                var length = Math.Min(normalized.Length - start, 700);
                var window = normalized.Substring(start, length);
                if (ChampionAliases.Normalize(slug) == target || ChampionAliases.Normalize(CleanText(window)).Contains(target)) return slug;
            }
            return null;
        }

        private static string ExtractSkillOrder(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var start = IndexOfAny(text, new[] { "技能加点", "Skill order", "SkillOrder Table" }, 0);
            var section = start < 0 ? text : text.Substring(start, Math.Min(700, text.Length - start));
            var match = Regex.Match(section, "(?<v>(?:\\b[QWER]\\b[\\s>→·,/|-]*){10,18})", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            var letters = Regex.Matches(match.Groups["v"].Value.ToUpperInvariant(), "[QWER]")
                .Cast<Match>()
                .Select(item => item.Value)
                .Take(18)
                .ToArray();
            return letters.Length < 8 ? null : string.Join(" → ", letters);
        }

        private static List<string> ExtractAltSection(string html, string[] startMarkers, string[] endMarkers, int max)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(html)) return result;
            var start = IndexOfAny(html, startMarkers, 0);
            if (start < 0) return result;
            var end = IndexOfAny(html, endMarkers, start + 2);
            if (end < 0 || end <= start) end = Math.Min(html.Length, start + 18000);
            var section = html.Substring(start, Math.Min(end - start, 18000));

            foreach (Match match in Regex.Matches(
                section,
                "(?:alt\\s*=\\s*[\"']|\"alt\"\\s*:\\s*\")(?<v>[^\"']{1,100})[\"']",
                RegexOptions.IgnoreCase))
            {
                var value = WebUtility.HtmlDecode(match.Groups["v"].Value).Trim();
                if (!IsUsefulName(value)) continue;
                if (result.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(value);
                if (result.Count >= max) break;
            }
            return result;
        }

        private static bool IsUsefulName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 2) return false;
            var lower = value.ToLowerInvariant();
            if (lower.Contains("logo") || lower.Contains("advert") || lower.Contains("op.gg") || lower == "image") return false;
            if (Regex.IsMatch(value, "^(技能|装备|出装|强化|增幅|表格|table|闪现|标记)$", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(value, "^[QWER]$", RegexOptions.IgnoreCase)) return false;
            return true;
        }

        private static List<string> ParseRankingAugments(string text, int max)
        {
            var output = new List<string>();
            var start = text.IndexOf("Best Augments for", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return output;
            var end = text.IndexOf("Augment Combos", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = Math.Min(text.Length, start + 1200);
            var section = text.Substring(start, Math.Min(end - start, 1200));
            foreach (Match match in Regex.Matches(section, "(?<n>[A-Za-z][A-Za-z' :+-]{2,44}?)(?:\\d{1,2}(?:\\.\\d+)?)%"))
            {
                var name = Regex.Replace(match.Groups["n"].Value, "^Best Augments for\\s+[A-Za-z' .-]+", string.Empty, RegexOptions.IgnoreCase).Trim();
                if (name.Length < 2 || output.Contains(name)) continue;
                output.Add(name);
                if (output.Count >= max) break;
            }
            return output;
        }

        private static string ParseBalanceAdjustments(string text)
        {
            var values = new List<string>();
            var pattern = "(?<name>Damage\\s+Dealt|Damage\\s+Taken|Attack\\s+Speed|Ability\\s+Haste|Cooldown\\s+Reduction|Healing|Shielding|Tenacity|Minion\\s+Damage)\\s*(?<v>[+-]?\\d+(?:\\.\\d+)?%?)";
            foreach (Match match in Regex.Matches(text ?? string.Empty, pattern, RegexOptions.IgnoreCase))
            {
                var translated = TranslateBalanceName(match.Groups["name"].Value);
                var item = translated + " " + match.Groups["v"].Value;
                if (!values.Any(value => string.Equals(value, item, StringComparison.OrdinalIgnoreCase))) values.Add(item);
                if (values.Count >= 10) break;
            }
            return values.Count == 0 ? null : string.Join("  ·  ", values);
        }

        private static string TranslateBalanceName(string value)
        {
            switch (Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), "\\s+", " "))
            {
                case "attack speed": return "攻击速度";
                case "damage dealt": return "造成伤害";
                case "damage taken": return "承受伤害";
                case "ability haste":
                case "cooldown reduction": return "技能急速";
                case "healing": return "治疗";
                case "shielding": return "护盾";
                case "tenacity": return "韧性";
                case "minion damage": return "对小兵伤害";
                default: return value.Trim();
            }
        }

        private static string ExtractPreferredChampionName(string value)
        {
            var text = (value ?? string.Empty).Trim();
            var match = Regex.Match(text, "[（(](?<v>[^）)]+)[）)]");
            return match.Success ? match.Groups["v"].Value.Trim() : text;
        }

        private static string InferTier(int rank)
        {
            if (rank <= 10) return "S+";
            if (rank <= 30) return "S";
            if (rank <= 60) return "A";
            if (rank <= 100) return "B";
            return "C";
        }

        private static string NormalizeEscapedHtml(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\u003c", "<")
                .Replace("\\u003e", ">")
                .Replace("\\u0026", "&")
                .Replace("\\\"", "\"")
                .Replace("\\/", "/");
        }

        private static int IndexOfAny(string text, IEnumerable<string> markers, int startIndex)
        {
            var indexes = markers
                .Where(marker => !string.IsNullOrWhiteSpace(marker))
                .Select(marker => text.IndexOf(marker, Math.Max(0, startIndex), StringComparison.OrdinalIgnoreCase))
                .Where(index => index >= 0)
                .ToArray();
            return indexes.Length == 0 ? -1 : indexes.Min();
        }

        private static string Match(string source, string pattern, bool strip)
        {
            var match = Regex.Match(source ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return null;
            return strip ? CleanText(match.Groups["v"].Value) : WebUtility.HtmlDecode(match.Groups["v"].Value).Trim();
        }

        private static string CleanText(string html)
        {
            var text = Regex.Replace(html ?? string.Empty, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ").Trim();
        }

        private static string CleanName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var name = Regex.Replace(value, "\\s*(极地大乱斗|ARAM|Build|构建|出装).*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            return Regex.Replace(name, "^(Image:|图片:)\\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
        }

        private static string First(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private static double? FirstRate(string first, string second, double? fallback)
        {
            return Rate(first) ?? Rate(second) ?? fallback;
        }

        private static double? Rate(string value)
        {
            double number;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return null;
            if (number > 0 && number <= 1) number *= 100D;
            return number >= 0 && number <= 100 ? (double?)number : null;
        }

        private static string Title(string slug)
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase((slug ?? string.Empty).Replace("-", " "));
        }

        private static void Report(IProgress<string> progress, string message)
        {
            if (progress != null) progress.Report(message);
        }
    }
}
