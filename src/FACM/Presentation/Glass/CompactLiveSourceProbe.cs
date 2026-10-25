using System;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FACM.Mayhem;

namespace FACM.Presentation.Glass
{
    // Public-source smoke runs without a game client, account, CloudBase or credentials.
    // This probe is informative, not a release gate: an unreachable CDN is reported
    // separately from malformed markup, and all results remain non-authoritative.
    internal static class CompactLiveSourceProbe
    {
        internal static int Run(string outputDirectory)
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
                {
                    var trace = FACM.Services.CompactTraceLog.NewId();
                    var aramTask = CompactOrdinaryAramRankingService.QueryAsync(trace, cancellation.Token);
                    var riftTask = CompactRiftRankingService.QueryAsync("top", trace, cancellation.Token);
                    Task.WhenAll(aramTask, riftTask).GetAwaiter().GetResult();
                    var aram = aramTask.Result;
                    var rift = riftTask.Result;
                    var content = new StringBuilder();
                    content.AppendLine("GGman compact public-source probe (unauthenticated)");
                    content.AppendLine("UTC=" + DateTime.UtcNow.ToString("O"));
                    content.AppendLine("trace=" + trace);
                    Append(content, "ordinary-aram", aram);
                    Append(content, "rift-top", rift);
                    AppendHttp(content, "ordinary-aram", "https://opgg.gg/en/champions-tier/aram");
                    AppendHttp(content, "rift-role", "https://op.gg/lol/champions?position=top");
                    AppendHttp(content, "rift-global", "https://opgg.gg/en/champions-tier/ranked");
                    content.AppendLine("CAVEAT: This is a CI network result, not mainland no-VPN performance.");
                    File.WriteAllText(Path.Combine(outputDirectory, "public-source-probe.txt"),
                        content.ToString(), new UTF8Encoding(false));
                }
                return 0;
            }
            catch (Exception exception)
            {
                try
                {
                    File.WriteAllText(Path.Combine(outputDirectory, "public-source-probe.txt"),
                        "probe_exception=" + exception.GetType().Name);
                }
                catch { }
                return 4;
            }
        }

        private static void AppendHttp(StringBuilder output, string label, string url)
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                using (var response = client.GetAsync(url).GetAwaiter().GetResult())
                {
                    output.Append(label).Append(" http=").Append((int)response.StatusCode);
                    if (!response.IsSuccessStatusCode)
                    {
                        output.AppendLine(" html_skipped=true");
                        return;
                    }
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    output.Append(" bytes=").Append(Encoding.UTF8.GetByteCount(body));
                    output.Append(" tables=").Append(Regex.Matches(body,
                        @"<table\b", RegexOptions.IgnoreCase).Count);
                    output.Append(" rows=").Append(Regex.Matches(body,
                        @"<tr\b", RegexOptions.IgnoreCase).Count);
                    output.Append(" has_patch=").Append(body.IndexOf("Patch",
                        StringComparison.OrdinalIgnoreCase) >= 0);
                    output.Append(" has_champion=").Append(body.IndexOf("Champion",
                        StringComparison.OrdinalIgnoreCase) >= 0);
                    output.AppendLine();
                    AppendStructure(output, label, body);
                }
            }
            catch (Exception exception)
            {
                output.Append(label).Append(" http_error=").AppendLine(exception.GetType().Name);
            }
        }

        private static void AppendStructure(StringBuilder output, string label, string html)
        {
            var title = Regex.Match(html, @"<title\b[^>]*>(?<v>.*?)</title>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            output.Append(label).Append(" title=").AppendLine(CleanPublic(title.Groups["v"].Value));
            var tables = Regex.Matches(html, @"<table\b[^>]*>(?<v>.*?)</table>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (tables.Count == 0) return;
            var table = tables[0].Groups["v"].Value;
            var headers = Regex.Matches(table, @"<th\b[^>]*>(?<v>.*?)</th>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            output.Append(label).Append(" headers=");
            foreach (System.Text.RegularExpressions.Match header in headers)
                output.Append("[").Append(CleanPublic(header.Groups["v"].Value)).Append("]");
            output.AppendLine();
            var rows = Regex.Matches(table, @"<tr\b[^>]*>(?<v>.*?)</tr>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            for (var i = 0; i < Math.Min(3, rows.Count); i++)
            {
                output.Append(label).Append(" row=").Append(i).Append(" ");
                var cells = Regex.Matches(rows[i].Groups["v"].Value,
                    @"<td\b[^>]*>(?<v>.*?)</td>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                output.Append("td=").Append(cells.Count).Append(" ");
                for (var j = 0; j < Math.Min(9, cells.Count); j++)
                {
                    var inner = cells[j].Groups["v"].Value;
                    var alt = Regex.Match(inner, "alt=[\\\"'](?<v>[^\\\"']{0,48})[\\\"']",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    output.Append(j).Append(":")
                        .Append(CleanPublic(inner))
                        .Append(alt.Success ? "{alt=" + CleanPublic(alt.Groups["v"].Value) + "}" : "")
                        .Append(" | ");
                }
                output.AppendLine();
            }
        }

        private static string CleanPublic(string value)
        {
            var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(
                value ?? string.Empty, @"<[^>]*>", " "));
            text = Regex.Replace(text, @"\s+", " ").Trim();
            // The source contains public champion statistics only. Limit diagnostics
            // to short text snippets and do not include raw HTML or URLs.
            return text.Length <= 68 ? text : text.Substring(0, 68);
        }

        private static void Append(StringBuilder output, string name, MayhemRankingSnapshot snapshot)
        {
            output.Append(name);
            output.Append(" valid=").Append(MayhemRankingSourceService.IsComplete(snapshot));
            output.Append(" expanded=").Append(MayhemRankingSourceService.IsExpandedValid(snapshot));
            output.Append(" source=").Append(snapshot == null ? "none" : snapshot.Source);
            output.Append(" patch=").Append(snapshot == null ? "none" : snapshot.Patch);
            output.Append(" rows=").Append(snapshot == null || snapshot.TopTen == null ?
                0 : snapshot.TopTen.Count);
            output.AppendLine();
        }
    }
}
