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
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(7));
                var requestToken = timeout.Token;

                try
                {
                    Report(progress, "正在识别英雄...");
                    string slug;
                    if (!ChampionAliases.TryResolve(query, out slug))
                    {
                        var indexHtml = await GetSafeAsync(OpggBaseUrl, requestToken).ConfigureAwait(false);
                        slug = ResolveSlug(indexHtml, query);
                    }

                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        result.ErrorMessage = "没有识别到这个英雄，请尝试官方中文名、英文名或常见简称。";
                        return result;
                    }

                    result.ChampionSlug = slug;
                    result.SourceUrl = OpggBaseUrl + "/" + slug + "/build";
                    result.RankingSourceUrl = RankingBaseUrl + "/build/" + slug + "/";

                    Report(progress, "正在并行读取 OP.GG 构建数据与胜率排行...");
                    var opggTask = GetSafeAsync(result.SourceUrl, requestToken);
                    var championRankTask = GetSafeAsync(result.RankingSourceUrl, requestToken);
                    var topTenTask = GetSafeAsync(RankingBaseUrl + "/", requestToken);
                    await Task.WhenAll(opggTask, championRankTask, topTenTask).ConfigureAwait(false);

                    var opggHtml = opggTask.Result;
                    var rankingHtml = championRankTask.Result;
                    var topTenHtml = topTenTask.Result;

                    if (string.IsNullOrWhiteSpace(opggHtml) && string.IsNullOrWhiteSpace(rankingHtml))
                    {
                        result.ErrorMessage = token.IsCancellationRequested
                            ? "查询已取消。"
                            : "数据源在 7 秒内没有返回可用数据，请稍后重试。";
                        return result;
                    }

                    Report(progress, "正在解析英雄、技能、装备、强化和排行...");
                    ParseOpggChampion(opggHtml, result);
                    ParseRankingChampion(rankingHtml, result);
                    result.TopTen = ParseTopTen(topTenHtml);

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
                    if (string.IsNullOrWhiteSpace(result.BalanceSummary))
                        result.BalanceSummary = "当前数据源未公开该英雄的独立 Mayhem buff / debuff 数值。";
                    if (string.IsNullOrWhiteSpace(result.SkillOrder))
                        result.SkillOrder = "OP.GG 当前页面未返回可解析的技能加点顺序。";
                    if (result.CoreItems.Count == 0)
                        result.CoreItems.Add("OP.GG 当前页面未返回可解析的核心出装");
                    if (result.Augments.Count == 0)
                        result.Augments.Add("当前页面未返回可解析的强化符文");

                    result.SourceNote = "构建：OP.GG；胜率/名次/前十：ARAMMayhem.com；并行读取，最长等待 7 秒";
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
                        : "查询超过 7 秒，已自动停止。";
                    return result;
                }
                catch (Exception exception)
                {
                    Services.AppLog.Error("Mayhem query failed", exception);
                    result.ErrorMessage = "读取数据失败：" + exception.Message;
                    return result;
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

        private static async Task<string> GetSafeAsync(string url, CancellationToken token)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Services.AppLog.Info("Mayhem source returned HTTP " + (int)response.StatusCode + ": " + url);
                        return null;
                    }
                    return await CancelableHttpContentReader.ReadStringAsync(response.Content, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception exception)
            {
                Services.AppLog.Info("Mayhem source request failed: " + url + "; " + exception.Message);
                return null;
            }
        }

        private static void ParseOpggChampion(string html, MayhemChampionResult result)
        {
            if (string.IsNullOrWhiteSpace(html)) return;
            var normalized = NormalizeEscapedHtml(html);
            var text = CleanText(normalized);
            var h1 = Match(normalized, "<h1[^>]*>(?<v>.*?)</h1>", true);

            result.ChampionName = First(CleanName(h1), result.ChampionName);
            result.Patch = First(
                Match(text, "(?:在|Patch\\s*)?(?<v>\\d{1,2}\\.\\d{1,2})\\s*(?:版本|Patch)", false),
                Match(text, "(?:版本|Patch)\\s*(?<v>\\d{1,2}\\.\\d{1,2})", false),
                result.Patch);
            result.Tier = First(
                Match(text, "(?<v>[1-5SABCDF](?:\\+)?)\\s*(?:段位|Tier|梯队)", false),
                result.Tier);
            result.SkillOrder = First(ExtractSkillOrder(text), result.SkillOrder);

            result.CoreItems = ExtractAltSection(
                normalized,
                new[] { "核心装备", "核心出装", "Builds Table", "Core builds", "Core Items" },
                new[] { "广告", "增幅装置", "Augments", "召唤师技能", "Summoner" },
                4);
            result.Augments = ExtractAltSection(
                normalized,
                new[] { " 增幅装置", "增幅装置", "强化符文", "Augments" },
                new[] { "召唤师技能", "Summoner", "技能加点", "Skills" },
                8);
        }

        private static void ParseRankingChampion(string html, MayhemChampionResult result)
        {
            if (string.IsNullOrWhiteSpace(html)) return;
            var text = CleanText(NormalizeEscapedHtml(html));

            result.RankingPatch = First(
                Match(text, "Patch\\s*:\\s*(?<v>\\d{1,2}\\.\\d{1,2})", false),
                Match(text, "patch\\s*(?<v>\\d{1,2}\\.\\d{1,2})", false),
                result.RankingPatch);
            result.Tier = First(
                Match(text, "\\b(?<v>S\\+|S|A|B|C|D|F)\\s+Tier\\s+ARAM", false),
                result.Tier);
            result.WinRate = FirstRate(
                Match(text, "(?<v>\\d{1,2}(?:\\.\\d+)?)%\\s*WR", false),
                Match(text, "win rate\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%", false),
                result.WinRate);
            result.PickRate = FirstRate(
                Match(text, "(?<v>\\d{1,2}(?:\\.\\d+)?)%\\s*PR", false),
                Match(text, "pick rate\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%", false),
                result.PickRate);

            int rank;
            var rankText = Match(text, "Rank\\s*:\\s*(?<v>\\d{1,3})", false);
            if (int.TryParse(rankText, NumberStyles.Integer, CultureInfo.InvariantCulture, out rank)) result.Rank = rank;

            result.BalanceSummary = First(ParseBalanceAdjustments(text), result.BalanceSummary);
            if (result.Augments.Count == 0) result.Augments = ParseRankingAugments(text, 8);
        }

        private static List<MayhemTopChampion> ParseTopTen(string html)
        {
            var output = new List<MayhemTopChampion>();
            if (string.IsNullOrWhiteSpace(html)) return output;
            var text = CleanText(NormalizeEscapedHtml(html));
            var marker = text.IndexOf("TOP 10 Highest Win Rate Champions", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) marker = text.IndexOf("TOP 10", StringComparison.OrdinalIgnoreCase);
            var section = marker < 0 ? text : text.Substring(marker, Math.Min(1800, text.Length - marker));

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

        private static string ResolveSlug(string html, string query)
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
            foreach (Match match in Regex.Matches(text ?? string.Empty, "(?<dir>[↑↓])(?<name>[A-Za-z ]{3,28})(?<v>[+-]\\d+(?:\\.\\d+)?%)"))
            {
                var name = match.Groups["name"].Value.Trim();
                var translated = TranslateBalanceName(name);
                var item = translated + " " + match.Groups["v"].Value;
                if (!values.Contains(item)) values.Add(item);
                if (values.Count >= 8) break;
            }
            return values.Count == 0 ? null : string.Join("  ·  ", values);
        }

        private static string TranslateBalanceName(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "attack speed": return "攻击速度";
                case "damage dealt": return "造成伤害";
                case "damage taken": return "承受伤害";
                case "cooldown reduction": return "技能急速";
                case "healing": return "治疗";
                case "shielding": return "护盾";
                case "tenacity": return "韧性";
                default: return value.Trim();
            }
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
