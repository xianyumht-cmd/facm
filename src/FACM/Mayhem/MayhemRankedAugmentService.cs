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
    internal static class MayhemRankedAugmentService
    {
        private const string SiteBaseUrl = "https://arammayhem.com";
        private static readonly HttpClient Client = CreateClient();

        private sealed class RankedAugment
        {
            public string Name { get; set; }
            public double? WinRate { get; set; }
            public string IconUrl { get; set; }
        }

        public static async Task EnrichAsync(MayhemChampionResult result, CancellationToken token)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.RankingSourceUrl)) return;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(2.5));
                try
                {
                    var html = await ReadAsync(result.RankingSourceUrl, timeout.Token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(html)) return;
                    ApplyFromHtml(result, html);
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
            return ApplyFromHtml(result, html);
        }

        private static int ApplyFromHtml(MayhemChampionResult result, string html)
        {
            if (result == null || string.IsNullOrWhiteSpace(html)) return 0;
            var normalized = NormalizeEscapedHtml(html);
            var start = normalized.IndexOf("Best Augments for", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return 0;
            var end = normalized.IndexOf("Augment Combos", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0 || end <= start) end = Math.Min(normalized.Length, start + 18000);
            var section = normalized.Substring(start, Math.Min(end - start, 18000));

            var picks = ParseAnchorPicks(section).Take(5).ToList();
            if (picks.Count == 0) picks = ParseTextPicks(CleanText(section)).Take(5).ToList();
            if (picks.Count == 0) return 0;

            result.Augments.Clear();
            result.AugmentIconUrls.Clear();
            for (var i = 0; i < picks.Count; i++)
            {
                var pick = picks[i];
                var label = "#" + (i + 1).ToString(CultureInfo.InvariantCulture);
                if (pick.WinRate.HasValue)
                    label += "  " + pick.WinRate.Value.ToString("0.00", CultureInfo.InvariantCulture) + "%";
                result.Augments.Add(label);
                result.AugmentIconUrls.Add(pick.IconUrl);
            }
            return picks.Count;
        }

        private static IEnumerable<RankedAugment> ParseAnchorPicks(string section)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(
                section ?? string.Empty,
                "<a\\b[^>]*href\\s*=\\s*[\"'](?<href>[^\"']*/augments/(?<slug>[^/\"']+)/?)[\"'][^>]*>(?<body>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
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
                var source = MatchAttribute(body, "src");
                if (string.IsNullOrWhiteSpace(source)) source = MatchAttribute(body, "data-src");
                var icon = MakeAbsoluteImageUrl(source, name);
                yield return new RankedAugment { Name = name, WinRate = winRate, IconUrl = icon };
            }
        }

        private static IEnumerable<RankedAugment> ParseTextPicks(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(text, "(?<n>[A-Za-z][A-Za-z0-9' !:,+.\\-]{1,48}?)(?<w>\\d{1,2}(?:\\.\\d+)?)%"))
            {
                var name = match.Groups["n"].Value.Trim();
                if (name.StartsWith("Best Augments for", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(name)) continue;
                double rate;
                double? winRate = double.TryParse(match.Groups["w"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out rate)
                    ? rate
                    : (double?)null;
                yield return new RankedAugment
                {
                    Name = name,
                    WinRate = winRate,
                    IconUrl = MakeAbsoluteImageUrl(null, name)
                };
            }
        }

        private static string MatchAttribute(string html, string attribute)
        {
            var match = Regex.Match(
                html ?? string.Empty,
                "\\b" + Regex.Escape(attribute) + "\\s*=\\s*[\"'](?<v>[^\"']+)[\"']",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? WebUtility.HtmlDecode(match.Groups["v"].Value).Trim() : null;
        }

        private static string MakeAbsoluteImageUrl(string source, string name)
        {
            if (!string.IsNullOrWhiteSpace(source))
            {
                if (source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    return source;
                if (source.StartsWith("//", StringComparison.Ordinal)) return "https:" + source;
                if (source.StartsWith("/", StringComparison.Ordinal)) return SiteBaseUrl + source;
                return SiteBaseUrl + "/" + source.TrimStart('/');
            }

            if (string.IsNullOrWhiteSpace(name)) return null;
            var fileName = Uri.EscapeDataString(name.Replace(' ', '_')) + "_mayhem_augment.webp";
            return SiteBaseUrl + "/augments/" + fileName;
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
