using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueEarlyJungleDeathSample
    {
        internal LeaguePlayerMatchSummary Match;
        internal int Deaths;
    }

    // Source: pinned Akari 1.5.1 single/early-deaths.ts and aggregate/details.ts.
    internal static class LeagueEarlyJungleDeathAnalysis
    {
        internal const int BatchSize = 8, MaximumSamples = 40;
        internal const long EarlyLimitMs = 15L * 60 * 1000;

        private static bool Jungler(LeaguePlayerMatchSummary p)
        {
            return string.Equals(p.Position, "JUNGLE", StringComparison.OrdinalIgnoreCase) ||
                p.HasSmite || (p.SpellIds != null && p.SpellIds.Contains(11));
        }

        internal static int[] EnemyJunglers(LeaguePlayerMatchSummary m)
        {
            if (m == null || m.ParticipantRoster == null || m.ParticipantRoster.Length != 10 ||
                m.ParticipantId < 1 || m.ParticipantId > 10 ||
                (m.TeamId != 100 && m.TeamId != 200) || Jungler(m)) return new int[0];
            var roster = m.ParticipantRoster;
            if (roster.Any(p => p == null || p.ParticipantId < 1 || p.ParticipantId > 10 ||
                (p.TeamId != 100 && p.TeamId != 200)) ||
                roster.Select(p => p.ParticipantId).Distinct().Count() != 10 ||
                roster.Count(p => p.TeamId == 100) != 5 ||
                roster.Count(p => p.TeamId == 200) != 5) return new int[0];
            var self = roster.SingleOrDefault(p => p.ParticipantId == m.ParticipantId);
            if (self == null || self.TeamId != m.TeamId || Jungler(self)) return new int[0];
            var enemies = roster.Where(p => p.TeamId != m.TeamId).ToArray();
            var positioned = enemies.Where(p => string.Equals(p.Position, "JUNGLE",
                StringComparison.OrdinalIgnoreCase)).Select(p => p.ParticipantId).ToArray();
            return positioned.Length > 0 ? positioned :
                enemies.Where(Jungler).Select(p => p.ParticipantId).ToArray();
        }

        internal static List<LeaguePlayerMatchSummary> Candidates(
            IEnumerable<LeaguePlayerMatchSummary> matches, string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return new List<LeaguePlayerMatchSummary>();
            return LeaguePlayerHistoryAnalysis.LoadedMatches(matches).Where(m =>
                m.GameId > 0 && m.MapId == 11 &&
                string.Equals(m.GameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase) &&
                (LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win ||
                 LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Loss) &&
                string.Equals(m.PuuId, target, StringComparison.Ordinal) &&
                (m.DataSource == LeaguePlayerHistorySource.Lcu && string.IsNullOrEmpty(m.SgpServerId) ||
                 m.DataSource == LeaguePlayerHistorySource.Sgp && !string.IsNullOrEmpty(m.SgpServerId)) &&
                EnemyJunglers(m).Length > 0)
                .GroupBy(LeaguePlayerDataService.HistoryDetailKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).ToList();
        }

        internal static List<LeaguePlayerMatchSummary> PendingBatch(
            IEnumerable<LeaguePlayerMatchSummary> matches, string target,
            ISet<string> attempted, int successful)
        {
            if (successful >= MaximumSamples) return new List<LeaguePlayerMatchSummary>();
            return Candidates(matches, target).Where(m => attempted == null ||
                !attempted.Contains(LeaguePlayerDataService.HistoryDetailKey(m)))
                .Take(Math.Min(BatchSize, MaximumSamples - Math.Max(0, successful))).ToList();
        }

        internal static int? Calculate(LeagueMatchTimeline timeline, LeaguePlayerMatchSummary match)
        {
            if (timeline == null || match == null || timeline.GameId != match.GameId ||
                timeline.Frames.Count == 0 || match.MapId != 11 ||
                !string.Equals(match.GameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(match.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase))
                return null;
            var enemies = EnemyJunglers(match);
            if (enemies.Length == 0) return null;
            var enemySet = new HashSet<int>(enemies);
            return timeline.Events.Count(e => e.Type == "CHAMPION_KILL" &&
                e.Timestamp >= 0 && e.Timestamp <= EarlyLimitMs &&
                e.VictimId == match.ParticipantId &&
                (enemySet.Contains(e.KillerId) ||
                 (e.Assists != null && e.Assists.Any(enemySet.Contains))));
        }

        internal static string Report(UiTextCatalog ui, IEnumerable<LeagueEarlyJungleDeathSample> samples,
            IEnumerable<LeaguePlayerMatchSummary> matches, string target,
            ISet<string> attempted, int championId)
        {
            var eligible = Candidates(matches, target)
                .Where(m => championId == 0 || m.ChampionId == championId).ToList();
            var keys = new HashSet<string>(eligible.Select(LeaguePlayerDataService.HistoryDetailKey),
                StringComparer.OrdinalIgnoreCase);
            var known = (samples ?? Enumerable.Empty<LeagueEarlyJungleDeathSample>())
                .Where(s => s != null && s.Match != null &&
                    keys.Contains(LeaguePlayerDataService.HistoryDetailKey(s.Match)))
                .GroupBy(s => LeaguePlayerDataService.HistoryDetailKey(s.Match),
                    StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            var attemptedCount = attempted == null ? 0 : keys.Count(attempted.Contains);
            return string.Format(CultureInfo.CurrentCulture,
                ui.Get(UiTextKeys.LeaguePlayerAnalysisEarlyJungleResult),
                known.Count == 0 ? "--" : known.Average(s => s.Deaths).ToString("0.00", CultureInfo.CurrentCulture),
                known.Count, eligible.Count, attemptedCount,
                known.Count == 0 ? "--" : known.Sum(s => s.Deaths).ToString(CultureInfo.CurrentCulture));
        }
    }
}
