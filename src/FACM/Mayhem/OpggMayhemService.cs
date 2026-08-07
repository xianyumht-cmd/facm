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
                timeout.CancelAfter(TimeSpan.FromSeconds(9));
                var requestToken = timeout.Token;

                try
                {
                    Report(progress, "正在识别英雄...");
                    string slug;
                    string mainHtml = null;
                    if (!ChampionAliases.TryResolve(query, out slug))
                    {
                        mainHtml = await GetSafeAsync(BaseUrl, requestToken).ConfigureAwait(false);
                        slug = ResolveSlug(mainHtml, query);
                    }

                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        result.ErrorMessage = "没有识别到这个英雄，请尝试官方中文名、英文名或常见简称。";
                        return result;
                    }

                    result.ChampionSlug = slug;
                    result.SourceUrl = BaseUrl + "/" + slug + "/build";
                    Report(progress, "正在并行读取出装、技能、强化与排行榜...");

                    var buildTask = GetSafeAsync(BaseUrl + "/" + slug + "/build", requestToken);
                    var skillsTask = GetSafeAsync(BaseUrl + "/" + slug + "/skills", requestToken);
                    var augmentsTask = GetSafeAsync(BaseUrl + "/" + slug + "/augments", requestToken);
                    var itemsTask = GetSafeAsync(BaseUrl + "/" + slug + "/items", requestToken);
                    var leaderboardTask = mainHtml == null
                        ? GetSafeAsync(BaseUrl, requestToken)
                        : Task.FromResult(mainHtml);

                    await Task.WhenAll(buildTask, skillsTask, augmentsTask, itemsTask, leaderboardTask).ConfigureAwait(false);

                    var buildHtml = buildTask.Result;
                    var skillsHtml = skillsTask.Result;
                    var augmentsHtml = augmentsTask.Result;
                    var itemsHtml = itemsTask.Result;
                    mainHtml = leaderboardTask.Result;

                    if (string.IsNullOrWhiteSpace(buildHtml) &&
                        string.IsNullOrWhiteSpace(skillsHtml) &&
                        string.IsNullOrWhiteSpace(itemsHtml))
                    {
                        result.ErrorMessage = token.IsCancellationRequested
                            ? "查询已取消。"
                            : "OP.GG 在 9 秒内没有返回可用数据，请稍后重试。";
                        return result;
                    }

                    Report(progress, "正在解析 OP.GG 返回内容...");
                    ParseChampion(buildHtml, skillsHtml, augmentsHtml, itemsHtml, result);
                    result.TopTen = ParseTopTen(mainHtml);

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
                        result.BalanceSummary = "OP.GG 当前 Mayhem 页面未公开该英雄的独立 buff / debuff 数值。";
                    if (string.IsNullOrWhiteSpace(result.SkillOrder))
                        result.SkillOrder = "OP.GG 当前页面未返回可解析的技能加点顺序。";

                    result.SourceNote = "数据源：OP.GG ARAM: Mayhem；并行请求，最长等待 9 秒";
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
                        : "OP.GG 查询超过 9 秒，已自动停止。";
                    return result;
                }
                catch (Exception exception)
                {
                    Services.AppLog.Error("OP.GG Mayhem query failed", exception);
                    result.ErrorMessage = "读取 OP.GG 数据失败：" + exception.Message;
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
            var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/151.0 FACM/3.1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.6");
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
                        Services.AppLog.Info("OP.GG page returned HTTP " + (int)response.StatusCode + ": " + url);
                        return null;
                    }
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception exception)
            {
                Services.AppLog.Info("OP.GG page request failed: " + url + "; " + exception.Message);
                return null;
            }
        }

        private static string ResolveSlug(string html, string query)
        {
            var target = ChampionAliases.Normalize(query);
            foreach (Match match in Regex.Matches(html ?? string.Empty, "/aram-mayhem/(?<slug>[^/\"'?]+)/build", RegexOptions.IgnoreCase))
            {
                var slug = match.Groups["slug"].Value;
                var windowStart = Math.Max(0, match.Index - 260);
                var windowLength = Math.Min((html ?? string.Empty).Length - windowStart, 620);
                var window = string.IsNullOrEmpty(html) ? string.Empty : html.Substring(windowStart, windowLength);
                if (ChampionAliases.Normalize(slug) == target || ChampionAliases.Normalize(CleanText(window)).Contains(target)) return slug;
            }
            return null;
        }

        private static void ParseChampion(string buildHtml, string skillsHtml, string augmentsHtml, string itemsHtml, MayhemChampionResult result)
        {
            var combined = string.Join(Environment.NewLine, new[] { buildHtml, skillsHtml, augmentsHtml, itemsHtml }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var buildText = CleanText(buildHtml);
            var skillsText = CleanText(skillsHtml);

            result.ChampionName = CleanName(First(
                Match(buildHtml, "<h1[^>]*>(?<v>.*?)</h1>", true),
                JsonText(combined, "champion_name"),
                JsonText(combined, "name")));
            result.Patch = First(
                Match(buildText, "(?:版本|Patch)\\s*(?<v>\\d{1,2}\\.\\d{1,2})", false),
                JsonText(combined, "patch"),
                JsonText(combined, "version"));
            result.Tier = First(
                JsonText(combined, "tier"),
                Match(buildText, "(?<v>[1-5SABCDF][+0-9]?)\\s*(?:段位|Tier|梯队)", false));
            result.WinRate = Rate(First(
                Match(buildText, "(?:胜率|Win\\s*rate)\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%", false),
                JsonNumber(combined, "win_rate")));
            result.PickRate = Rate(First(
                Match(buildText, "(?:选用率|选取率|选择率|Pick\\s*rate)\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%", false),
                JsonNumber(combined, "pick_rate")));

            result.SkillOrder = First(
                SkillSequence(skillsText),
                JsonText(combined, "skill_order"),
                JsonText(combined, "skills_order"));
            result.CoreItems = ExtractSectionNames(
                string.IsNullOrWhiteSpace(itemsHtml) ? buildHtml : itemsHtml,
                new[] { "核心装备", "核心出装", "Core builds", "Core Items" },
                12);
            result.Augments = ExtractSectionNames(
                string.IsNullOrWhiteSpace(augmentsHtml) ? buildHtml : augmentsHtml,
                new[] { "增幅装置", "强化符文", "Augments" },
                12);
            result.BalanceSummary = Balance(CleanText(combined), combined);
        }

        private static List<MayhemTopChampion> ParseTopTen(string raw)
        {
            var output = new List<MayhemTopChampion>();
            if (string.IsNullOrWhiteSpace(raw)) return output;

            var normalized = raw.Replace("\\\"", "\"");
            foreach (Match rankMatch in Regex.Matches(normalized, "\"rank\"\\s*:\\s*(?<r>\\d{1,3})", RegexOptions.IgnoreCase))
            {
                int rank;
                if (!int.TryParse(rankMatch.Groups["r"].Value, out rank) || rank < 1 || rank > 200) continue;

                var start = Math.Max(0, rankMatch.Index - 1100);
                var length = Math.Min(normalized.Length - start, 2500);
                var window = normalized.Substring(start, length);
                var name = First(
                    LastJsonText(window, "champion_name", rankMatch.Index - start),
                    LastJsonText(window, "name", rankMatch.Index - start),
                    JsonText(window, "champion_name"),
                    JsonText(window, "name"));
                var slug = First(
                    LastJsonText(window, "key", rankMatch.Index - start),
                    LastJsonText(window, "slug", rankMatch.Index - start),
                    JsonText(window, "key"),
                    JsonText(window, "slug"));
                var winRate = Rate(First(
                    LastJsonNumber(window, "win_rate", rankMatch.Index - start),
                    JsonNumber(window, "win_rate")));
                var tier = First(
                    LastJsonText(window, "tier", rankMatch.Index - start),
                    JsonText(window, "tier"));

                if (string.IsNullOrWhiteSpace(name) || !winRate.HasValue) continue;
                if (string.IsNullOrWhiteSpace(slug)) slug = ChampionAliases.Slugify(name);
                if (output.Any(item => item.Rank == rank || string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase))) continue;

                output.Add(new MayhemTopChampion
                {
                    Rank = rank,
                    Name = WebUtility.HtmlDecode(name),
                    Slug = slug,
                    WinRate = winRate,
                    Tier = tier
                });
            }

            if (output.Count < 10)
            {
                var objectPattern = "\\{[^{}]{0,1800}?\"(?:champion_name|name)\"\\s*:\\s*\"(?<n>[^\"\\\\]+)\"[^{}]{0,1800}?\"win_rate\"\\s*:\\s*\"?(?<w>0?\\.\\d+|\\d{1,2}(?:\\.\\d+)?)\"?[^{}]{0,1800}?\"rank\"\\s*:\\s*(?<r>\\d{1,3})[^{}]{0,1800}?\\}";
                foreach (Match match in Regex.Matches(normalized, objectPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    int rank;
                    if (!int.TryParse(match.Groups["r"].Value, out rank) || rank < 1 || rank > 200) continue;
                    var winRate = Rate(match.Groups["w"].Value);
                    if (!winRate.HasValue || output.Any(item => item.Rank == rank)) continue;
                    var name = WebUtility.HtmlDecode(match.Groups["n"].Value);
                    output.Add(new MayhemTopChampion
                    {
                        Rank = rank,
                        Name = name,
                        Slug = ChampionAliases.Slugify(name),
                        WinRate = winRate,
                        Tier = null
                    });
                }
            }

            return output.OrderBy(item => item.Rank).Take(10).ToList();
        }

        private static string Balance(string text, string raw)
        {
            var fields = new[]
            {
                BalanceField(text, "造成伤害", "Damage dealt"),
                BalanceField(text, "承受伤害", "Damage taken"),
                BalanceField(text, "攻击速度", "Attack speed"),
                BalanceField(text, "技能急速", "Cooldown reduction"),
                BalanceField(text, "治疗", "Healing"),
                BalanceField(text, "护盾", "Shielding")
            }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            return fields.Length > 0
                ? string.Join("  ·  ", fields)
                : First(JsonText(raw, "aram_balance"), JsonText(raw, "balance_summary"));
        }

        private static string BalanceField(string text, string cn, string en)
        {
            var match = Regex.Match(text ?? string.Empty, "(?:" + Regex.Escape(cn) + "|" + Regex.Escape(en) + ")\\s*(?<v>[+-]?\\d+(?:\\.\\d+)?%|-)", RegexOptions.IgnoreCase);
            return match.Success ? cn + " " + match.Groups["v"].Value : null;
        }

        private static string SkillSequence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = Regex.Match(text, "(?<v>(?:\\d{1,2}\\s*[QWER]\\s*){8,18})", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            var letters = Regex.Matches(match.Groups["v"].Value.ToUpperInvariant(), "[QWER]")
                .Cast<Match>()
                .Select(item => item.Value)
                .ToArray();
            return letters.Length == 0 ? null : string.Join(" → ", letters);
        }

        private static List<string> ExtractSectionNames(string html, string[] markers, int max)
        {
            var section = Slice(html, markers, 22000);
            var list = new List<string>();
            foreach (Match match in Regex.Matches(section ?? string.Empty, "alt\\s*=\\s*[\"'](?<v>[^\"']+)[\"']", RegexOptions.IgnoreCase))
            {
                var value = WebUtility.HtmlDecode(match.Groups["v"].Value).Trim();
                var lower = value.ToLowerInvariant();
                if (value.Length < 2 ||
                    lower.Contains("logo") ||
                    lower.Contains("advert") ||
                    lower.Contains("op.gg") ||
                    lower == "image" ||
                    Regex.IsMatch(value, "^(技能|装备|出装|强化|增幅|表格|table)$", RegexOptions.IgnoreCase)) continue;
                if (!list.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))) list.Add(value);
                if (list.Count >= max) break;
            }
            return list;
        }

        private static string Slice(string text, string[] markers, int length)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var start = markers
                .Select(marker => text.IndexOf(marker, StringComparison.OrdinalIgnoreCase))
                .Where(index => index >= 0)
                .DefaultIfEmpty(-1)
                .Min();
            return start < 0 ? string.Empty : text.Substring(start, Math.Min(length, text.Length - start));
        }

        private static string Match(string source, string pattern, bool strip)
        {
            var match = Regex.Match(source ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return null;
            return strip
                ? CleanText(match.Groups["v"].Value)
                : WebUtility.HtmlDecode(match.Groups["v"].Value).Trim();
        }

        private static string JsonText(string raw, string key)
        {
            var match = Regex.Match(raw ?? string.Empty, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"(?<v>[^\"\\\\]+)\"", RegexOptions.IgnoreCase);
            return match.Success ? WebUtility.HtmlDecode(match.Groups["v"].Value).Trim() : null;
        }

        private static string LastJsonText(string raw, string key, int beforeIndex)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string result = null;
            foreach (Match match in Regex.Matches(raw, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"(?<v>[^\"\\\\]+)\"", RegexOptions.IgnoreCase))
            {
                if (match.Index > beforeIndex) break;
                result = WebUtility.HtmlDecode(match.Groups["v"].Value).Trim();
            }
            return result;
        }

        private static string JsonNumber(string raw, string key)
        {
            var match = Regex.Match(raw ?? string.Empty, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"?(?<v>-?\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["v"].Value : null;
        }

        private static string LastJsonNumber(string raw, string key, int beforeIndex)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string result = null;
            foreach (Match match in Regex.Matches(raw, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"?(?<v>-?\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase))
            {
                if (match.Index > beforeIndex) break;
                result = match.Groups["v"].Value;
            }
            return result;
        }

        private static string CleanText(string html)
        {
            var text = Regex.Replace(html ?? string.Empty, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", " ")), "\\s+", " ").Trim();
        }

        private static string CleanName(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? value
                : Regex.Replace(value, "\\s*(ARAM|极地大乱斗|Build|构建|出装).*$", string.Empty, RegexOptions.IgnoreCase).Trim();
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

        private static void Report(IProgress<string> progress, string message)
        {
            if (progress != null) progress.Report(message);
        }
    }
}
