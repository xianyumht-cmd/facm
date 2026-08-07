using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.Mayhem
{
    internal static class OpggMayhemService
    {
        private const string BaseUrl = "https://op.gg/zh-cn/lol/modes/aram-mayhem";
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private sealed class CacheEntry
        {
            public DateTime Time { get; set; }
            public MayhemChampionResult Value { get; set; }
        }

        public static async Task<MayhemChampionResult> QueryAsync(string input, CancellationToken token)
        {
            var query = (input ?? string.Empty).Trim();
            if (query.Length == 0) return new MayhemChampionResult { ErrorMessage = "请输入英雄名称或别名。" };
            lock (Sync)
            {
                CacheEntry entry;
                if (Cache.TryGetValue(query, out entry) && DateTime.UtcNow - entry.Time < TimeSpan.FromMinutes(10)) return entry.Value;
            }

            var result = new MayhemChampionResult { Query = query };
            try
            {
                string slug;
                string mainHtml = null;
                if (!ChampionAliases.TryResolve(query, out slug))
                {
                    mainHtml = await GetAsync(BaseUrl, token).ConfigureAwait(false);
                    slug = ResolveSlug(mainHtml, query);
                }
                if (string.IsNullOrWhiteSpace(slug))
                {
                    result.ErrorMessage = "没有识别到这个英雄，请尝试官方中文名、英文名或常见简称。";
                    return result;
                }

                result.ChampionSlug = slug;
                result.SourceUrl = BaseUrl + "/" + slug + "/build";
                var championTask = GetAsync(result.SourceUrl, token);
                var mainTask = mainHtml == null ? GetAsync(BaseUrl, token) : Task.FromResult(mainHtml);

                string analysis = null;
                string leaderboard = null;
                try
                {
                    using (var mcp = new OpggMcpClient())
                    {
                        var tools = await mcp.ListToolsAsync(token).ConfigureAwait(false);
                        var analysisTool = OpggMcpClient.FindTool(tools, "lol_get_champion_analysis");
                        var leaderboardTool = OpggMcpClient.FindTool(tools, "lol_list_champion_leaderboard");
                        if (analysisTool != null)
                            analysis = await mcp.CallToolAsync("lol_get_champion_analysis", OpggMcpClient.BuildArguments(analysisTool, slug, false), token).ConfigureAwait(false);
                        if (leaderboardTool != null)
                            leaderboard = await mcp.CallToolAsync("lol_list_champion_leaderboard", OpggMcpClient.BuildArguments(leaderboardTool, slug, true), token).ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    Services.AppLog.Info("OP.GG MCP unavailable; page fallback: " + exception.Message);
                }

                var championHtml = await championTask.ConfigureAwait(false);
                mainHtml = await mainTask.ConfigureAwait(false);
                ParseChampion(championHtml, result);
                MergeAnalysis(analysis, result);
                result.TopTen = ParseTopTen(string.IsNullOrWhiteSpace(leaderboard) ? mainHtml : leaderboard);

                var current = result.TopTen.FirstOrDefault(item => string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase));
                if (current != null)
                {
                    if (!result.Rank.HasValue) result.Rank = current.Rank;
                    if (!result.WinRate.HasValue) result.WinRate = current.WinRate;
                    if (string.IsNullOrWhiteSpace(result.Tier)) result.Tier = current.Tier;
                }

                if (string.IsNullOrWhiteSpace(result.ChampionName)) result.ChampionName = Title(slug);
                if (string.IsNullOrWhiteSpace(result.BalanceSummary)) result.BalanceSummary = "OP.GG 当前 Mayhem 页面未公开独立 buff/debuff 数值。";
                if (string.IsNullOrWhiteSpace(result.SkillOrder)) result.SkillOrder = "OP.GG 当前页面未返回技能加点顺序。";
                result.SourceNote = string.IsNullOrWhiteSpace(analysis) ? "数据源：OP.GG ARAM: Mayhem 页面" : "数据源：OP.GG 官方 MCP + ARAM: Mayhem 页面";
                lock (Sync) Cache[query] = new CacheEntry { Time = DateTime.UtcNow, Value = result };
                return result;
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = "查询已取消。";
                return result;
            }
            catch (Exception exception)
            {
                result.ErrorMessage = "读取 OP.GG 数据失败：" + exception.Message;
                return result;
            }
        }

        private static async Task<string> GetAsync(string url, CancellationToken token)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(18) })
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/151.0 FACM/3.1");
                request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.6");
                using (var response = await client.SendAsync(request, token).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode);
                    return body;
                }
            }
        }

        private static string ResolveSlug(string html, string query)
        {
            var target = ChampionAliases.Normalize(query);
            foreach (Match match in Regex.Matches(html ?? string.Empty, "/aram-mayhem/(?<slug>[^/\"'?]+)/build", RegexOptions.IgnoreCase))
            {
                var slug = match.Groups["slug"].Value;
                if (ChampionAliases.Normalize(slug) == target) return slug;
            }
            return null;
        }

        private static void ParseChampion(string html, MayhemChampionResult result)
        {
            var text = CleanText(html);
            result.ChampionName = CleanName(First(Match(html, "<h1[^>]*>(?<v>.*?)</h1>", true), JsonText(html, "champion_name"), JsonText(html, "name")));
            result.Patch = First(Match(text, "(?:版本|Patch)\\s*(?<v>\\d{1,2}\\.\\d{1,2})", false), JsonText(html, "patch"), JsonText(html, "version"));
            result.Tier = First(JsonText(html, "tier"), Match(text, "(?<v>[SABCDF][+0-9]?)\\s*(?:Tier|梯队)", false));
            result.WinRate = Rate(First(Match(text, "(?:胜率|Win\\s*rate)\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%", false), JsonNumber(html, "win_rate")));
            result.PickRate = Rate(First(Match(text, "(?:选取率|选择率|Pick\\s*rate)\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%", false), JsonNumber(html, "pick_rate")));
            result.SkillOrder = First(JsonText(html, "skill_order"), JsonText(html, "skills_order"), SkillSequence(text));
            result.CoreItems = ImageNames(Slice(html, new[] { "核心装备", "核心出装", "Core builds", "Builds Table" }, 22000), 12);
            result.Augments = ImageNames(Slice(html, new[] { "强化符文", "增幅装置", "Augments" }, 16000), 10);
            result.BalanceSummary = Balance(text, html);
        }

        private static void MergeAnalysis(string raw, MayhemChampionResult result)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            result.ChampionName = First(result.ChampionName, JsonText(raw, "champion_name"), JsonText(raw, "name"));
            result.Patch = First(result.Patch, JsonText(raw, "patch"), JsonText(raw, "version"));
            result.Tier = First(result.Tier, JsonText(raw, "tier"));
            if (!result.WinRate.HasValue) result.WinRate = Rate(JsonNumber(raw, "win_rate"));
            if (!result.PickRate.HasValue) result.PickRate = Rate(JsonNumber(raw, "pick_rate"));
            if (!result.Rank.HasValue)
            {
                int rank;
                if (int.TryParse(JsonNumber(raw, "rank"), out rank)) result.Rank = rank;
            }
            result.SkillOrder = First(result.SkillOrder, JsonText(raw, "skill_order"), JsonText(raw, "skills_order"));
            result.BalanceSummary = First(JsonText(raw, "aram_balance"), JsonText(raw, "balance_summary"), result.BalanceSummary);
            if (result.CoreItems.Count == 0) result.CoreItems = NamedValues(raw, new[] { "items", "builds", "core_items" }, 12);
            if (result.Augments.Count == 0) result.Augments = NamedValues(raw, new[] { "augments", "augment" }, 10);
        }

        private static List<MayhemTopChampion> ParseTopTen(string raw)
        {
            var result = new List<MayhemTopChampion>();
            var text = (raw ?? string.Empty).Replace("\\\"", "\"");
            var patterns = new[]
            {
                "\"rank\"\\s*:\\s*(?<r>\\d+).*?\"(?:name|champion_name)\"\\s*:\\s*\"(?<n>[^\"\\\\]+)\".*?\"win_rate\"\\s*:\\s*(?<w>0?\\.\\d+|\\d{1,2}(?:\\.\\d+)?)",
                "\"(?:name|champion_name)\"\\s*:\\s*\"(?<n>[^\"\\\\]+)\".*?\"rank\"\\s*:\\s*(?<r>\\d+).*?\"win_rate\"\\s*:\\s*(?<w>0?\\.\\d+|\\d{1,2}(?:\\.\\d+)?)"
            };
            foreach (var pattern in patterns)
            {
                foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    int rank;
                    if (!int.TryParse(match.Groups["r"].Value, out rank) || rank < 1 || rank > 200) continue;
                    var name = WebUtility.HtmlDecode(match.Groups["n"].Value);
                    result.Add(new MayhemTopChampion
                    {
                        Rank = rank,
                        Name = name,
                        Slug = ChampionAliases.Slugify(name),
                        WinRate = Rate(match.Groups["w"].Value),
                        Tier = null
                    });
                }
                if (result.Count >= 10) break;
            }
            return result.GroupBy(item => item.Rank).Select(group => group.First()).OrderBy(item => item.Rank).Take(10).ToList();
        }

        private static string Balance(string text, string html)
        {
            var fields = new[]
            {
                BalanceField(text, "造成伤害", "Damage dealt"), BalanceField(text, "承受伤害", "Damage taken"),
                BalanceField(text, "攻击速度", "Attack speed"), BalanceField(text, "技能急速", "Cooldown reduction"),
                BalanceField(text, "治疗", "Healing"), BalanceField(text, "护盾", "Shielding")
            }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            return fields.Length > 0 ? string.Join("  ·  ", fields) : First(JsonText(html, "aram_balance"), JsonText(html, "balance_summary"));
        }

        private static string BalanceField(string text, string cn, string en)
        {
            var match = Regex.Match(text ?? string.Empty, "(?:" + Regex.Escape(cn) + "|" + Regex.Escape(en) + ")\\s*(?<v>[+-]?\\d+(?:\\.\\d+)?%|-)", RegexOptions.IgnoreCase);
            return match.Success ? cn + " " + match.Groups["v"].Value : null;
        }

        private static string SkillSequence(string text)
        {
            var match = Regex.Match(text ?? string.Empty, "(?<v>(?:\\b[QWER]\\b[\\s·>/,-]*){8,20})", RegexOptions.IgnoreCase);
            return match.Success ? Regex.Replace(match.Groups["v"].Value.ToUpperInvariant(), "\\s+", " ").Trim() : null;
        }

        private static List<string> ImageNames(string html, int max)
        {
            var list = new List<string>();
            foreach (Match match in Regex.Matches(html ?? string.Empty, "alt\\s*=\\s*[\"'](?<v>[^\"']+)[\"']", RegexOptions.IgnoreCase))
            {
                var value = WebUtility.HtmlDecode(match.Groups["v"].Value).Trim();
                var lower = value.ToLowerInvariant();
                if (value.Length < 2 || lower.Contains("logo") || lower.Contains("advert") || lower.Contains("op.gg") || lower == "image") continue;
                if (!list.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))) list.Add(value);
                if (list.Count >= max) break;
            }
            return list;
        }

        private static List<string> NamedValues(string raw, string[] markers, int max)
        {
            foreach (var marker in markers)
            {
                var index = (raw ?? string.Empty).IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;
                return Regex.Matches(raw.Substring(index, Math.Min(raw.Length - index, 12000)), "\"(?:name|display_name|title)\"\\s*:\\s*\"(?<v>[^\"\\\\]{2,80})\"", RegexOptions.IgnoreCase)
                    .Cast<Match>().Select(match => WebUtility.HtmlDecode(match.Groups["v"].Value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToList();
            }
            return new List<string>();
        }

        private static string Slice(string text, string[] markers, int length)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var start = markers.Select(marker => text.IndexOf(marker, StringComparison.OrdinalIgnoreCase)).Where(index => index >= 0).DefaultIfEmpty(-1).Min();
            return start < 0 ? string.Empty : text.Substring(start, Math.Min(length, text.Length - start));
        }

        private static string Match(string source, string pattern, bool strip)
        {
            var match = Regex.Match(source ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return null;
            return strip ? CleanText(match.Groups["v"].Value) : WebUtility.HtmlDecode(match.Groups["v"].Value).Trim();
        }

        private static string JsonText(string raw, string key)
        {
            var match = Regex.Match(raw ?? string.Empty, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"(?<v>[^\\\"\\\\]+)\\\"", RegexOptions.IgnoreCase);
            return match.Success ? WebUtility.HtmlDecode(match.Groups["v"].Value).Trim() : null;
        }

        private static string JsonNumber(string raw, string key)
        {
            var match = Regex.Match(raw ?? string.Empty, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"?(?<v>-?\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["v"].Value : null;
        }

        private static string CleanText(string html)
        {
            var text = Regex.Replace(html ?? string.Empty, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", " ")), "\\s+", " ").Trim();
        }

        private static string CleanName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? value : Regex.Replace(value, "\\s*(ARAM|极地大乱斗|Build|构建|出装).*$", string.Empty, RegexOptions.IgnoreCase).Trim();
        }

        private static string First(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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
    }
}
