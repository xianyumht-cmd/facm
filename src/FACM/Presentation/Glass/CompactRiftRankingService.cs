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
            // The role-specific op.gg host returned HTTP 403 in the Windows probe.
            // The public ranked table at opgg.gg returns all roles with an explicit Lane column.
            return canonical == null ? null : "https://opgg.gg/en/champions-tier/ranked";
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
            // The ranked source has one mixed-lane table. Role is validated from
            // each row's Lane cell, not guessed from the champion or page title.
            if (!Regex.IsMatch(identity, @"\bRanked\b", RegexOptions.IgnoreCase) ||
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
                    // Real site markup: hero name in <th>; rank, lane, tier and
                    // win rate in <td> cells. Global rank is not the role rank.
                    if (cells.Count < 7) continue;
                    var championCell = Regex.Match(match.Groups["r"].Value,
                        @"<th\b[^>]*>(?<v>.*?)</th>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (!championCell.Success) continue;
                    var lane = CompactRiftRoleService.Normalize(Clean(cells[1].Groups["v"].Value));
                    if (lane != role) continue;
                    int globalRank;
                    if (!int.TryParse(Clean(cells[0].Groups["v"].Value),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out globalRank) ||
                        globalRank <= 0 || globalRank > 2000) continue;
                    var hero = Clean(championCell.Groups["v"].Value);
                    if (string.IsNullOrWhiteSpace(hero)) continue;
                    var rateMatch = Regex.Match(Clean(cells[3].Groups["v"].Value),
                        @"(?<v>\d{1,2}(?:\.\d{1,2})?)\s*%");
                    double rate;
                    if (!rateMatch.Success || !double.TryParse(rateMatch.Groups["v"].Value,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out rate)) continue;
                    var candidateTier = Clean(cells[2].Groups["v"].Value);
                    string tier = Regex.IsMatch(candidateTier, @"^(?:S\+|S|A|B|C|D|T[0-3])$",
                        RegexOptions.IgnoreCase) ? candidateTier : null;
                    var index = records.Count + 1;
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
                if (UrlFor(role) != "https://opgg.gg/en/champions-tier/ranked")
                    throw new InvalidOperationException("Ranked mixed-lane source was not qualified.");
                var other = role == "top" ? "jungle" : "top";
                var fixture = "<title>League of Legends champion tier list for Ranked mode – Patch 26.21</title>" +
                    "<table><thead><tr><th># Position</th><th>Lane</th><th>Champion</th>" +
                    "<th>Tier</th><th>Win rate</th></tr></thead><tbody>";
                for (int i = 1; i <= 18; i++)
                {
                    var lane = i == 4 ? other : role;
                    var laneText = lane == "middle" ? "Mid" : lane == "bottom" ? "Bottom" :
                        lane == "support" ? "Support" : lane == "jungle" ? "Jungle" : "Top";
                    fixture += "<tr><td>" + i + "</td><td>" + laneText +
                        "</td><th scope='row'>Hero" + i +
                        "</th><td>S+</td><td>51.20%</td><td>7.25%</td>" +
                        "<td>1.4%</td><td>5000</td><td>-</td></tr>";
                }
                fixture += "</tbody></table>";
                var result = Parse(fixture, role);
                if (!MayhemRankingSourceService.IsExpandedValid(result) ||
                    result.ModeKey != "rift:" + role ||
                    MayhemRankingSourceService.FindRank(result, "Hero18").Rank != 17 ||
                    MayhemRankingSourceService.FindRank(result, "Hero4") != null)
                    throw new InvalidOperationException("Role-filtered Rift ranking fixture rejected.");
                if (MayhemRankingSourceService.IsComplete(Parse(fixture, other)))
                    throw new InvalidOperationException("One row of another lane became a full ranking.");
                if (MayhemRankingSourceService.IsComplete(Parse(
                    fixture.Replace("Ranked mode", "ARAM Mayhem mode"), role)))
                    throw new InvalidOperationException("ARAM page accepted in the Rift source.");
            }
            if (UrlFor("fill") != null ||
                MayhemRankingSourceService.IsComplete(Parse("", "top")))
                throw new InvalidOperationException("Unknown role silently produced a ranking.");
        }
    }
}
