using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using FACM.League;
using FACM.Mayhem;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Ordinary ARAM only; never accepts an ARAM: Mayhem or role-ambiguous Ranked page.
    // No local LCU data or player identifiers are sent to the public source.
    internal static class CompactOrdinaryAramRankingService
    {
        private const string Url = "https://opgg.gg/en/champions-tier/aram";
        private static readonly object Sync = new object();
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static MayhemRankingSnapshot _current;
        private static DateTime _fetchedAtUtc;

        internal static async Task<MayhemRankingSnapshot> QueryAsync(string traceId, CancellationToken token)
        {
            lock (Sync)
            {
                if (MayhemRankingSourceService.IsComplete(_current) &&
                    DateTime.UtcNow - _fetchedAtUtc < TimeSpan.FromMinutes(15))
                {
                    CompactTraceLog.Record(traceId, "aram", "cache", "hit", 0);
                    return _current;
                }
            }
            await Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                lock (Sync)
                {
                    if (MayhemRankingSourceService.IsComplete(_current) &&
                        DateTime.UtcNow - _fetchedAtUtc < TimeSpan.FromMinutes(15))
                    {
                        CompactTraceLog.Record(traceId, "aram", "cache", "joined", 0);
                        return _current;
                    }
                }
                var watch = Stopwatch.StartNew();
                try
                {
                    var response = await LeaguePublicDataTransport.GetAsync(
                        Url, TimeSpan.FromSeconds(3.5), token, false).ConfigureAwait(false);
                    var parsed = Parse(response.ReadUtf8());
                    watch.Stop();
                    if (!MayhemRankingSourceService.IsComplete(parsed))
                    {
                        CompactTraceLog.Record(traceId, "aram", "parse", "incomplete", watch.ElapsedMilliseconds);
                        return null;
                    }
                    lock (Sync)
                    {
                        _current = parsed;
                        _fetchedAtUtc = DateTime.UtcNow;
                    }
                    CompactTraceLog.Record(traceId, "aram", "query", response.FromCache ? "transport_cache" : "ok",
                        watch.ElapsedMilliseconds);
                    return parsed;
                }
                catch (OperationCanceledException)
                {
                    CompactTraceLog.Record(traceId, "aram", "query", "cancelled", watch.ElapsedMilliseconds);
                    if (token.IsCancellationRequested) throw;
                    return null;
                }
                catch (Exception exception)
                {
                    CompactTraceLog.Record(traceId, "aram", "query",
                        "failed_" + exception.GetType().Name, watch.ElapsedMilliseconds);
                    return null;
                }
            }
            finally { Gate.Release(); }
        }

        // HTML-only parser. Ambiguous markup is deliberately treated as unavailable.
        internal static MayhemRankingSnapshot Parse(string html)
        {
            var output = new MayhemRankingSnapshot {
                Source = "OPGG-ARAM", ModeKey = "aram",
                Patch = MayhemRankingSourceService.ReadPatch(html),
                TopTen = new List<MayhemTopChampion>()
            };
            if (string.IsNullOrWhiteSpace(html)) return output;
            var title = Regex.Match(html, @"<title\b[^>]*>(?<text>.*?)</title>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var titleText = Clean(title.Groups["text"].Value);
            // Do not accept a navigation link mentioning ARAM on a Mayhem/ranked page.
            if (titleText.IndexOf("ARAM", StringComparison.OrdinalIgnoreCase) < 0 ||
                titleText.IndexOf("MAYHEM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                titleText.IndexOf("Ranked", StringComparison.OrdinalIgnoreCase) >= 0)
                return output;

            foreach (Match table in Regex.Matches(html, @"<table\b[^>]*>(?<content>.*?)</table>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var header = Clean(table.Groups["content"].Value);
                if (header.IndexOf("Champion", StringComparison.OrdinalIgnoreCase) < 0 ||
                    header.IndexOf("Win rate", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var rows = new List<MayhemTopChampion>();
                foreach (Match match in Regex.Matches(table.Groups["content"].Value,
                    @"<tr\b[^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    var cells = Regex.Matches(match.Groups["row"].Value,
                        @"<td\b[^>]*>(?<v>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (cells.Count < 4) continue;
                    var rankText = Clean(cells[0].Groups["v"].Value);
                    int rank;
                    if (!int.TryParse(rankText, NumberStyles.Integer, CultureInfo.InvariantCulture, out rank) ||
                        rank != rows.Count + 1 || rank > 200) continue;
                    var name = Clean(cells[1].Groups["v"].Value);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var tier = Clean(cells[2].Groups["v"].Value);
                    var rateText = Clean(cells[3].Groups["v"].Value);
                    // Some sources put winrate before tier; both validated against known grade tokens.
                    if (!ValidTier(tier)) {
                        var alternative = Clean(cells[3].Groups["v"].Value);
                        if (ValidTier(alternative)) {
                            tier = alternative;
                            rateText = Clean(cells[2].Groups["v"].Value);
                        } else continue;
                    }
                    var rateMatch = Regex.Match(rateText, @"(?<r>\d{1,2}(?:\.\d{1,2})?)\s*%");
                    double rate;
                    if (!rateMatch.Success || !double.TryParse(rateMatch.Groups["r"].Value,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out rate)) continue;
                    rows.Add(new MayhemTopChampion {
                        Rank = rank, Name = name, Slug = ChampionAliases.Slugify(name),
                        WinRate = rate, Tier = tier
                    });
                    if (rows.Count == 200) break;
                }
                if (rows.Count > 10)
                {
                    output.TopTen = rows.Take(10).ToList();
                    output.AllChampions = rows;
                    break;
                }
                if (rows.Count == 10)
                {
                    output.TopTen = rows;
                    break;
                }
            }
            if (!MayhemRankingSourceService.IsComplete(output))
            {
                output.TopTen = new List<MayhemTopChampion>();
                output.AllChampions = null;
            }
            else if (!MayhemRankingSourceService.IsExpandedValid(output))
                output.AllChampions = null;
            return output;
        }

        private static bool ValidTier(string value)
        {
            switch ((value ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "S+": case "S": case "A": case "B": case "C": case "D":
                case "T0": case "T1": case "T2": case "T3": return true;
                default: return false;
            }
        }

        private static string Clean(string text)
        {
            return Regex.Replace(WebUtility.HtmlDecode(
                Regex.Replace(text ?? string.Empty, @"<[^>]*>", " ")), @"\s+", " ").Trim();
        }

        internal static void ValidateForSmokeTest()
        {
            var input = "<title>League of Legends champion tier list for ARAM mode – Patch 26.21</title>" +
                "<h1>ARAM Patch 26.21</h1><table><thead><th>Champion</th><th>Win rate</th></thead><tbody>";
            for (int i = 1; i <= 13; i++)
                input += "<tr><td>" + i + "</td><td>Champion" + i +
                    "</td><td>" + (i <= 5 ? "S+" : "S") + "</td><td>52.15%</td></tr>";
            input += "</tbody></table>";
            var result = Parse(input);
            if (!MayhemRankingSourceService.IsComplete(result) ||
                !MayhemRankingSourceService.IsExpandedValid(result) ||
                result.ModeKey != "aram" || result.TopTen[0].Tier != "S+")
                throw new InvalidOperationException("Ordinary ARAM parser did not identify its isolated fixture.");
            if (MayhemRankingSourceService.FindRank(result, "Champion13").Rank != 13)
                throw new InvalidOperationException("Ordinary ARAM expanded tier unavailable.");
            if (MayhemRankingSourceService.IsComplete(Parse(input.Replace("for ARAM mode",
                    "for ARAM Mayhem mode"))))
                throw new InvalidOperationException("Mayhem page accepted as ordinary ARAM.");
            if (MayhemRankingSourceService.IsComplete(Parse(input.Replace("for ARAM mode",
                    "for Ranked mode"))))
                throw new InvalidOperationException("Ranked page accepted as ordinary ARAM.");
            if (MayhemRankingSourceService.IsComplete(Parse(input.Replace("<td>2</td>",
                    "<td>1</td>"))))
                throw new InvalidOperationException("Duplicated ARAM rank accepted.");
        }
    }
}