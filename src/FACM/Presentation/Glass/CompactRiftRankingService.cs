using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FACM.League;
using FACM.Mayhem;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Read-only, strictly role-qualified Rift standings. Never infer role/tier from
    // player champion choice or combine entries from different ranked lanes.
    internal static class CompactRiftRankingService
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, MayhemRankingSnapshot> Cache =
            new Dictionary<string, MayhemRankingSnapshot>(StringComparer.Ordinal);
        private static readonly Dictionary<string, DateTime> CacheSaved =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        private static string UrlFor(string role)
        {
            var canonical = CompactRiftRoleService.Normalize(role);
            return canonical == null ? null :
                "https://op.gg/lol/champions?position=" +
                (canonical == "support" ? "support" : canonical);
        }

        internal static async Task<MayhemRankingSnapshot> QueryAsync(string role,
            string traceId, CancellationToken cancellationToken)
        {
            role = CompactRiftRoleService.Normalize(role);
            if (role == null)
            {
                CompactTraceLog.Record(traceId, "rift", "role", "unavailable", 0);
                return null;
            }
            lock (Sync)
            {
                MayhemRankingSnapshot existing;
                DateTime when;
                if (Cache.TryGetValue(role, out existing) &&
                    CacheSaved.TryGetValue(role, out when) &&
                    DateTime.UtcNow - when < TimeSpan.FromMinutes(15))
                {
                    CompactTraceLog.Record(traceId, "rift", "cache", "hit", 0);
                    return existing;
                }
            }

            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (Sync)
                {
                    MayhemRankingSnapshot existing;
                    DateTime when;
                    if (Cache.TryGetValue(role, out existing) &&
                        CacheSaved.TryGetValue(role, out when) &&
                        DateTime.UtcNow - when < TimeSpan.FromMinutes(15))
                    {
                        CompactTraceLog.Record(traceId, "rift", "cache", "joined", 0);
                        return existing;
                    }
                }
                var elapsed = Stopwatch.StartNew();
                try
                {
                    var response = await LeaguePublicDataTransport.GetAsync(
                        UrlFor(role), TimeSpan.FromSeconds(3.2),
                        cancellationToken, false).ConfigureAwait(false);
                    var ranking = Parse(response.ReadUtf8(), role);
                    if (!MayhemRankingSourceService.IsComplete(ranking))
                    {
                        CompactTraceLog.Record(traceId, "rift", "parse", "incomplete",
                            elapsed.ElapsedMilliseconds);
                        return null;
                    }
                    lock (Sync)
                    {
                        Cache[role] = ranking;
                        CacheSaved[role] = DateTime.UtcNow;
                    }
                    CompactTraceLog.Record(traceId, "rift", "query", "ok",
                        elapsed.ElapsedMilliseconds);
                    return ranking;
                }
                catch (OperationCanceledException)
                {
                    CompactTraceLog.Record(traceId, "rift", "query", "cancelled",
                        elapsed.ElapsedMilliseconds);
                    if (cancellationToken.IsCancellationRequested) throw;
                    return null;
                }
                catch (Exception exception)
                {
                    CompactTraceLog.Record(traceId, "rift", "query",
                        "failed_" + exception.GetType().Name, elapsed.ElapsedMilliseconds);
                    return null;
                }
            }
            finally { Gate.Release(); }
        }

        internal static MayhemRankingSnapshot Parse(string html, string role)
        {
            role = CompactRiftRoleService.Normalize(role);
            var ranking = new MayhemRankingSnapshot
            {
                ModeKey = role == null ? null : "rift:" + role,
                Source = "OPGG-RIFT-" + (role ?? "unknown"),
                TopTen = new List<MayhemTopChampion>()
            };
            if (role == null || string.IsNullOrWhiteSpace(html)) return ranking;
            var title = Regex.Match(html, @"<title\b[^>]*>(?<t>.*?)</title>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var h1 = Regex.Match(html, @"<h1\b[^>]*>(?<t>.*?)</h1>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var identity = Clean(title.Groups["t"].Value + " " + h1.Groups["t"].Value);
            var expected = role == "middle" ? new[] { "Middle", "Mid" } :
                role == "bottom" ? new[] { "Bottom", "ADC" } :
                role == "support" ? new[] { "Support", "Utility" } :
                role == "jungle" ? new[] { "Jungle" } : new[] { "Top" };
            if (!expected.Any(word => Regex.IsMatch(identity, @"\b" + word + @"\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) ||
                Regex.IsMatch(identity, @"\b(ARAM|Mayhem)\b", RegexOptions.IgnoreCase))
                return ranking;
            ranking.Patch = MayhemRankingSourceService.ReadPatch(identity);
            if (string.IsNullOrWhiteSpace(ranking.Patch)) return ranking;

            foreach (Match table in Regex.Matches(html,
                @"<table\b[^>]*>(?<t>.*?)</table>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var header = Clean(table.Groups["t"].Value);
                if (header.IndexOf("Win rate", StringComparison.OrdinalIgnoreCase) < 0 &&
                    header.IndexOf("Winrate", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var records = new List<MayhemTopChampion>();
                foreach (Match match in Regex.Matches(table.Groups["t"].Value,
                    @"<tr\b[^>]*>(?<r>.*?)</tr>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    var cells = Regex.Matches(match.Groups["r"].Value,
                        @"<td\b[^>]*>(?<v>.*?)</td>",
                        RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (cells.Count < 3) continue;
                    int index;
                    if (!int.TryParse(Clean(cells[0].Groups["v"].Value),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out index) ||
                        index != records.Count + 1 || index > 200) continue;
                    var hero = Clean(cells[1].Groups["v"].Value);
                    if (string.IsNullOrWhiteSpace(hero)) continue;
                    var winRate = Regex.Match(Clean(match.Groups["r"].Value),
                        @"(?<v>\d{1,2}(?:\.\d{1,2})?)\s*%");
                    double rate;
                    if (!winRate.Success || !double.TryParse(winRate.Groups["v"].Value,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out rate))
                        continue;
                    // Unless a tier is explicit and valid, show truthful #rank instead.
                    string tier = null;
                    foreach (Match cell in cells.Cast<Match>().Skip(2))
                    {
                        var t = Clean(cell.Groups["v"].Value);
                        if (Regex.IsMatch(t, @"^(?:S\+|S|A|B|C|D|T[0-3])$",
                            RegexOptions.IgnoreCase)) { tier = t; break; }
                    }
                    records.Add(new MayhemTopChampion
                    {
                        Rank = index, Name = hero, Slug = ChampionAliases.Slugify(hero),
                        Tier = tier, WinRate = rate
                    });
                }
                if (records.Count >= 10)
                {
                    ranking.TopTen = records.Take(10).ToList();
                    if (records.Count > 10) ranking.AllChampions = records;
                    break;
                }
            }
            if (!MayhemRankingSourceService.IsComplete(ranking))
                ranking.TopTen = new List<MayhemTopChampion>();
            if (!MayhemRankingSourceService.IsExpandedValid(ranking))
                ranking.AllChampions = null;
            return ranking;
        }

        private static string Clean(string html)
        {
            return Regex.Replace(WebUtility.HtmlDecode(
                Regex.Replace(html ?? string.Empty, @"<[^>]*>", " ")),
                @"\s+", " ").Trim();
        }

        internal static void ValidateForSmokeTest()
        {
            CompactRiftRoleService.ValidateForSmokeTest();
            var roles = new[] { "top", "jungle", "middle", "bottom", "support" };
            foreach (var role in roles)
            {
                if (UrlFor(role) == null || UrlFor(role).IndexOf("position=" + role,
                    StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Role-specific URL was not qualified.");
                var title = role == "middle" ? "Mid" : role == "bottom" ? "Bottom" :
                    role == "support" ? "Support" : role == "jungle" ? "Jungle" : "Top";
                var fixture = "<title>Champion Tier List for " + title +
                    ", Emerald +, Patch 16.21</title><table>" +
                    "<th>Rank</th><th>Champion</th><th>Win rate</th>";
                for (int i = 1; i <= 13; i++)
                    fixture += "<tr><td>" + i + "</td><td>Hero" + i +
                        "</td><td>51.20%</td><td>A</td></tr>";
                fixture += "</table>";
                var result = Parse(fixture, role);
                if (!MayhemRankingSourceService.IsExpandedValid(result) ||
                    result.ModeKey != "rift:" + role ||
                    MayhemRankingSourceService.FindRank(result, "Hero13").Rank != 13)
                    throw new InvalidOperationException("Role-specific Rift ranking fixture rejected.");
                var other = role == "top" ? "jungle" : "top";
                if (MayhemRankingSourceService.IsComplete(Parse(fixture, other)))
                    throw new InvalidOperationException("Cross-lane Rift ranking was accepted.");
                if (MayhemRankingSourceService.IsComplete(Parse(
                    fixture.Replace("Tier List for", "Tier List ARAM for"), role)))
                    throw new InvalidOperationException("ARAM page accepted in the Rift source.");
            }
            if (UrlFor("fill") != null ||
                MayhemRankingSourceService.IsComplete(Parse("", "top")))
                throw new InvalidOperationException("Unknown role silently produced a ranking.");
        }
    }
}
