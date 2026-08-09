using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Mayhem
{
    internal sealed class TencentMayhemPatchSnapshot
    {
        public string Patch { get; set; }
        public string SourceUrl { get; set; }
        public Dictionary<string, List<string>> ChampionChanges { get; set; } =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public List<string> FindChampionChanges(params string[] names)
        {
            var targets = (names ?? new string[0])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(ChampionAliases.Normalize)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (targets.Length == 0) return new List<string>();

            foreach (var pair in ChampionChanges)
            {
                var key = ChampionAliases.Normalize(pair.Key);
                if (targets.Any(target => key == target || key.Contains(target) || target.Contains(key)))
                    return new List<string>(pair.Value);
            }
            return new List<string>();
        }
    }

    internal static class TencentMayhemPatchService
    {
        private const string NewsIndexUrl = "https://lol.qq.com/news/index.shtml";
        private const string KnownFallbackArticle = "https://lol.qq.com/gicp/news/410/37092739.html";
        private static readonly HttpClient Client = CreateClient();
        private static readonly object Sync = new object();
        private static DateTime _cacheTime;
        private static TencentMayhemPatchSnapshot _cache;

        public static async Task<TencentMayhemPatchSnapshot> FetchLatestAsync(CancellationToken token)
        {
            lock (Sync)
            {
                if (_cache != null && DateTime.UtcNow - _cacheTime < TimeSpan.FromMinutes(30))
                    return _cache;
            }

            using (var overall = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                overall.CancelAfter(TimeSpan.FromSeconds(4));
                var ct = overall.Token;
                var candidates = new List<string> { KnownFallbackArticle };

                var indexHtml = await ReadSafeAsync(NewsIndexUrl, TimeSpan.FromSeconds(1.8), ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(indexHtml))
                    candidates.InsertRange(0, ExtractArticleUrls(indexHtml).Take(6));
                candidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(7).ToList();

                var tasks = candidates.Select(async url =>
                {
                    var html = await ReadSafeAsync(url, TimeSpan.FromSeconds(2.4), ct).ConfigureAwait(false);
                    return string.IsNullOrWhiteSpace(html) ? null : ParseArticle(html, url);
                }).ToArray();

                TencentMayhemPatchSnapshot[] snapshots;
                try
                {
                    snapshots = await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    snapshots = new TencentMayhemPatchSnapshot[0];
                }

                var latest = snapshots
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Patch))
                    .OrderByDescending(item => ParseVersion(item.Patch))
                    .FirstOrDefault();
                if (latest != null)
                {
                    lock (Sync)
                    {
                        _cache = latest;
                        _cacheTime = DateTime.UtcNow;
                    }
                }
                return latest;
            }
        }

        internal static TencentMayhemPatchSnapshot ParseArticleForSmokeTest(string html)
        {
            return ParseArticle(html, "fixture://tencent-mayhem");
        }

        private static TencentMayhemPatchSnapshot ParseArticle(string html, string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;
            var decoded = WebUtility.HtmlDecode(html);
            var plain = CleanText(decoded);
            var patchMatch = Regex.Match(
                plain,
                "(?:发布|欢迎来到)\\s*(?<v>\\d{1,2}\\.\\d{1,2})\\s*版本",
                RegexOptions.IgnoreCase);
            if (!patchMatch.Success) return null;

            var sectionStart = FindMayhemHeading(decoded);
            if (sectionStart < 0) return null;
            var sectionEnd = decoded.IndexOf("斗魂竞技场", sectionStart + 5, StringComparison.OrdinalIgnoreCase);
            if (sectionEnd < 0) sectionEnd = Math.Min(decoded.Length, sectionStart + 90000);
            var section = decoded.Substring(sectionStart, sectionEnd - sectionStart);
            var lines = ToLines(section);

            var output = new TencentMayhemPatchSnapshot
            {
                Patch = patchMatch.Groups["v"].Value,
                SourceUrl = sourceUrl
            };

            var inHeroes = false;
            string champion = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line == "英雄")
                {
                    inHeroes = true;
                    champion = null;
                    continue;
                }
                if (!inHeroes) continue;
                if (line.Contains("强化符文") || line.Contains("BUG修复") || line.Contains("Bug修复")) break;

                if (line.Contains("⇒") || line.Contains("→"))
                {
                    if (string.IsNullOrWhiteSpace(champion)) continue;
                    var change = NormalizeChange(line);
                    if (change.Length == 0) continue;
                    List<string> changes;
                    if (!output.ChampionChanges.TryGetValue(champion, out changes))
                    {
                        changes = new List<string>();
                        output.ChampionChanges[champion] = changes;
                    }
                    if (!changes.Contains(change)) changes.Add(change);
                    continue;
                }

                if (LooksLikeChampionHeading(line)) champion = CleanHeading(line);
            }

            return output;
        }

        private static int FindMayhemHeading(string html)
        {
            foreach (Match heading in Regex.Matches(
                html ?? string.Empty,
                "<h(?<level>[1-6])\\b[^>]*>(?<body>.*?)</h\\k<level>>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var headingText = CleanText(heading.Groups["body"].Value);
                if (headingText.IndexOf("海克斯大乱斗", StringComparison.OrdinalIgnoreCase) >= 0)
                    return heading.Index;
            }

            // Test fixtures and mirrors may preserve Markdown-style headings instead of HTML.
            var markdown = Regex.Match(html ?? string.Empty, "(?:^|[\\r\\n])\\s*#{1,6}\\s*海克斯大乱斗\\s*(?:[\\r\\n]|$)", RegexOptions.IgnoreCase);
            return markdown.Success ? markdown.Index : -1;
        }

        private static IEnumerable<string> ExtractArticleUrls(string html)
        {
            var urls = new List<Tuple<long, string>>();
            foreach (Match match in Regex.Matches(
                html ?? string.Empty,
                "(?:https?:)?//lol\\.qq\\.com/gicp/news/410/(?<id>\\d+)\\.html|/gicp/news/410/(?<rid>\\d+)\\.html",
                RegexOptions.IgnoreCase))
            {
                var idText = match.Groups["id"].Success ? match.Groups["id"].Value : match.Groups["rid"].Value;
                long id;
                if (!long.TryParse(idText, out id)) continue;
                urls.Add(Tuple.Create(id, "https://lol.qq.com/gicp/news/410/" + idText + ".html"));
            }
            return urls.OrderByDescending(item => item.Item1).Select(item => item.Item2);
        }

        private static async Task<string> ReadSafeAsync(string url, TimeSpan budget, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(budget);
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) return null;
                        return await CancelableHttpContentReader.ReadStringAsync(response.Content, timeout.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    return null;
                }
                catch (Exception exception)
                {
                    AppLog.Info("Tencent Mayhem patch source skipped: " + exception.Message);
                    return null;
                }
            }
        }

        private static string[] ToLines(string html)
        {
            var text = html ?? string.Empty;
            text = Regex.Replace(text, "<(br|hr)\\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "</(li|p|div|h1|h2|h3|h4|h5|h6|blockquote)>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", string.Empty);
            text = WebUtility.HtmlDecode(text)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Replace('\u00a0', ' ');
            return text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => Regex.Replace(value, "\\s+", " ").Trim())
                .Where(value => value.Length > 0)
                .ToArray();
        }

        private static bool LooksLikeChampionHeading(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length > 24) return false;
            if (line.Contains("：") || line.Contains(":") || line.Contains("版本") || line.Contains("海克斯")) return false;
            if (line.StartsWith("[新]", StringComparison.OrdinalIgnoreCase)) return false;
            return line.Any(ch => ch > 127) && !line.Any(char.IsDigit);
        }

        private static string CleanHeading(string line)
        {
            return Regex.Replace(line ?? string.Empty, "^[•●*\\-]+", string.Empty).Trim();
        }

        private static string NormalizeChange(string line)
        {
            var value = Regex.Replace(line ?? string.Empty, "^[•●*\\-]+", string.Empty).Trim();
            value = value.Replace("⇒", "→");
            value = Regex.Replace(value, "\\s+", " ");
            return value.Length > 120 ? value.Substring(0, 120) : value;
        }

        private static string CleanText(string html)
        {
            var text = Regex.Replace(html ?? string.Empty, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ").Trim();
        }

        private static Version ParseVersion(string value)
        {
            Version version;
            return Version.TryParse(value, out version) ? version : new Version(0, 0);
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/151.0 FACM/3.1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9");
            return client;
        }
    }
}
