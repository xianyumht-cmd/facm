using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Mayhem
{
    internal static class MayhemRankedAugmentService
    {
        private const string KiwiIconBase = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/assets/ux/kiwi/augments/icons/";
        private const string CherryIconBase = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/assets/ux/cherry/augments/icons/";
        private const string ArenaIconBase = "https://raw.communitydragon.org/latest/game/assets/ux/cherry/augments/icons/";
        private static readonly HttpClient Client = CreateClient();
        private static readonly Dictionary<string, string> FileAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "upgradeinfinityedge", "upgradeie" },
            { "endlessdecimation", "endlessdecimate" },
            { "transmuteprismatic", "transmuteprismatic" },
            { "outlawsgrit", "outlawsgrit" },
            { "finalform", "finalform" },
            { "mysticpunch", "mysticpunch" },
            { "goredrink", "goredrink" },
            { "escapade", "escapade" }
        };

        private sealed class RankedAugment
        {
            public string Name { get; set; }
            public string Slug { get; set; }
            public double? WinRate { get; set; }
            public string IconUrl { get; set; }
        }

        public static async Task EnrichAsync(MayhemChampionResult result, CancellationToken token)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.RankingSourceUrl)) return;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    var html = await ReadAsync(result.RankingSourceUrl, timeout.Token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(html)) return;
                    var picks = ParseRankedPicks(html).Take(5).ToList();
                    if (picks.Count == 0) return;
                    await ProbeMissingIconsAsync(picks, timeout.Token).ConfigureAwait(false);
                    Apply(result, picks);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    AppLog.Info("Ranked augment image queue timed out; keeping existing augment data.");
                }
                catch (Exception exception)
                {
                    AppLog.Info("Ranked augment image queue skipped: " + exception.Message);
                }
            }
        }

        internal static int ApplyFromHtmlForSmokeTest(MayhemChampionResult result, string html)
        {
            var picks = ParseRankedPicks(html).Take(5).ToList();
            Apply(result, picks);
            return picks.Count;
        }

        private static IEnumerable<RankedAugment> ParseRankedPicks(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return Enumerable.Empty<RankedAugment>();
            var normalized = NormalizeEscapedHtml(html);
            var start = normalized.IndexOf("Best Augments for", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return Enumerable.Empty<RankedAugment>();
            var end = normalized.IndexOf("Augment Combos", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0 || end <= start) end = Math.Min(normalized.Length, start + 18000);
            var section = normalized.Substring(start, Math.Min(end - start, 18000));

            var anchors = ParseAnchorPicks(section).ToList();
            if (anchors.Count > 0) return anchors;
            return ParseTextPicks(CleanText(section)).ToList();
        }

        private static void Apply(MayhemChampionResult result, IList<RankedAugment> picks)
        {
            if (result == null || picks == null || picks.Count == 0) return;
            result.Augments.Clear();
            result.AugmentIconUrls.Clear();
            for (var i = 0; i < picks.Count && i < 5; i++)
            {
                var pick = picks[i];
                var label = "#" + (i + 1).ToString(CultureInfo.InvariantCulture);
                if (pick.WinRate.HasValue)
                    label += "  " + pick.WinRate.Value.ToString("0.00", CultureInfo.InvariantCulture) + "%";
                result.Augments.Add(label);
                result.AugmentIconUrls.Add(pick.IconUrl);
            }
        }

        private static async Task ProbeMissingIconsAsync(IList<RankedAugment> picks, CancellationToken token)
        {
            var tasks = picks.Select(pick => ProbePickIconAsync(pick, token)).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async Task ProbePickIconAsync(RankedAugment pick, CancellationToken token)
        {
            if (pick == null) return;
            var normalized = NormalizeName(string.IsNullOrWhiteSpace(pick.Slug) ? pick.Name : pick.Slug);
            string key;
            if (!FileAliases.TryGetValue(normalized, out key)) key = normalized;
            if (string.IsNullOrWhiteSpace(key)) return;

            var candidates = new[]
            {
                KiwiIconBase + key + "_small.png",
                CherryIconBase + key + "_small.png",
                ArenaIconBase + key + "_large.png",
                ArenaIconBase + key + "_small.png"
            };
            foreach (var candidate in candidates)
            {
                if (!await ExistsAsync(candidate, token).ConfigureAwait(false)) continue;
                pick.IconUrl = candidate;
                return;
            }
            AppLog.Info("No ranked augment icon matched: " + pick.Name + "; slug=" + pick.Slug + "; key=" + key);
        }

        private static async Task<bool> ExistsAsync(string url, CancellationToken token)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    return response.IsSuccessStatusCode;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<RankedAugment> ParseAnchorPicks(string section)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(
                section ?? string.Empty,
                "<a\\b[^>]*href\\s*=\\s*[\"'](?<href>[^\"']*/augments/(?<slug>[^/\"']+)/?)[\"'][^>]*>(?<body>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var slug = WebUtility.HtmlDecode(match.Groups["slug"].Value).Trim();
                var body = match.Groups["body"].Value;
                var name = MatchAttribute(body, "alt");
                var text = CleanText(body);
                if (string.IsNullOrWhiteSpace(name))
                {
                    var nameMatch = Regex.Match(text, "^(?<n>.+?)(?<w>\\d{1,2}(?:\\.\\d+)?)%", RegexOptions.Singleline);
                    if (nameMatch.Success) name = nameMatch.Groups["n"].Value.Trim();
                }
                name = WebUtility.HtmlDecode(name ?? string.Empty).Trim();
                if (name.Length < 2 || !seen.Add(name)) continue;

                var rateMatch = Regex.Match(text, "(?<w>\\d{1,2}(?:\\.\\d+)?)%");
                double rate;
                double? winRate = rateMatch.Success && double.TryParse(rateMatch.Groups["w"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out rate)
                    ? rate
                    : (double?)null;
                yield return new RankedAugment { Name = name, Slug = slug, WinRate = winRate };
            }
        }

        private static IEnumerable<RankedAugment> ParseTextPicks(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(text, "(?<n>[A-Za-z][A-Za-z0-9' !:,+.\\-]{1,48}?)(?<w>\\d{1,2}(?:\\.\\d+)?)%"))
            {
                var name = match.Groups["n"].Value.Trim();
                name = Regex.Replace(name, "^Best Augments for\\s+[A-Za-z' .-]+", string.Empty, RegexOptions.IgnoreCase).Trim();
                if (name.Length < 2 || !seen.Add(name)) continue;
                double rate;
                double? winRate = double.TryParse(match.Groups["w"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out rate)
                    ? rate
                    : (double?)null;
                yield return new RankedAugment { Name = name, Slug = Slugify(name), WinRate = winRate };
            }
        }

        private static string Slugify(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var output = Regex.Replace(WebUtility.HtmlDecode(value).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return output;
        }

        private static string MatchAttribute(string html, string attribute)
        {
            var match = Regex.Match(
                html ?? string.Empty,
                "\\b" + Regex.Escape(attribute) + "\\s*=\\s*[\"'](?<v>[^\"']+)[\"']",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? WebUtility.HtmlDecode(match.Groups["v"].Value).Trim() : null;
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new StringBuilder(value.Length);
            foreach (var c in WebUtility.HtmlDecode(value).ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) builder.Append(c);
            }
            return builder.ToString();
        }

        private static async Task<string> ReadAsync(string url, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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

        private static string CleanText(string html)
        {
            var text = Regex.Replace(html ?? string.Empty, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ").Trim();
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM/3.1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.7");
            return client;
        }
    }
}
