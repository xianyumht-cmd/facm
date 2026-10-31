using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueObjectiveHistorySample
    {
        internal LeaguePlayerMatchSummary Match;
        internal LeagueJungleAnalysis Objectives;
    }

    // Akari 1.5.1 single/objectives.ts applies to every role, not only junglers.
    internal static class LeagueObjectiveHistoryAnalysis
    {
        internal const int BatchSize = 8, MaximumSamples = 40;

        internal static List<LeaguePlayerMatchSummary> Candidates(
            IEnumerable<LeaguePlayerMatchSummary> matches, string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return new List<LeaguePlayerMatchSummary>();
            return LeaguePlayerHistoryAnalysis.LoadedMatches(matches).Where(m =>
                m.GameId > 0 && m.MapId == 11 && m.ParticipantId >= 1 && m.ParticipantId <= 10 &&
                (m.TeamId == 100 || m.TeamId == 200) &&
                string.Equals(m.GameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase) &&
                (LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win ||
                 LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Loss) &&
                string.Equals(m.PuuId, target, StringComparison.Ordinal) &&
                (m.DataSource == LeaguePlayerHistorySource.Lcu && string.IsNullOrEmpty(m.SgpServerId) ||
                 m.DataSource == LeaguePlayerHistorySource.Sgp && !string.IsNullOrEmpty(m.SgpServerId)))
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

        internal static string Report(UiTextCatalog ui, IEnumerable<LeagueObjectiveHistorySample> samples,
            IEnumerable<LeaguePlayerMatchSummary> matches, string target,
            ISet<string> attempted, int championId)
        {
            var eligible = Candidates(matches, target)
                .Where(m => championId == 0 || m.ChampionId == championId).ToList();
            var keys = new HashSet<string>(eligible.Select(LeaguePlayerDataService.HistoryDetailKey),
                StringComparer.OrdinalIgnoreCase);
            var known = (samples ?? Enumerable.Empty<LeagueObjectiveHistorySample>())
                .Where(s => s != null && s.Match != null && s.Objectives != null &&
                    s.Objectives.ObjectivesAvailable &&
                    string.Equals(s.Match.PuuId, target, StringComparison.Ordinal) &&
                    keys.Contains(LeaguePlayerDataService.HistoryDetailKey(s.Match)))
                .GroupBy(s => LeaguePlayerDataService.HistoryDetailKey(s.Match),
                    StringComparer.OrdinalIgnoreCase).Select(g => g.First().Objectives).ToList();
            var attempts = attempted == null ? 0 : keys.Count(attempted.Contains);
            var header = string.Format(CultureInfo.CurrentCulture,
                ui.Get(UiTextKeys.LeaguePlayerAnalysisObjectivesSummary),
                known.Count, eligible.Count, attempts);
            if (known.Count == 0) return header;
            var firstKnown = known.Where(s => s.GotFirstDragon.HasValue).ToList();
            var lines = new List<string> { header,
                string.Format(CultureInfo.CurrentCulture,
                    ui.Get(UiTextKeys.LeaguePlayerAnalysisObjectivesFirstDragon),
                    firstKnown.Count == 0 ? "--" :
                        (100d * firstKnown.Count(s => s.GotFirstDragon.Value) / firstKnown.Count)
                        .ToString("0.0", CultureInfo.CurrentCulture) + "%",
                    firstKnown.Count, known.Count) };
            var names = new[] { UiTextKeys.LeagueJungleDragons, UiTextKeys.LeagueJungleGrubs,
                UiTextKeys.LeagueJungleHeralds, UiTextKeys.LeagueJungleBarons };
            Func<LeagueJungleAnalysis, int>[] counts = { s => s.Dragons, s => s.Voidgrubs,
                s => s.Heralds, s => s.Barons };
            Func<LeagueJungleAnalysis, long?>[] times = { s => s.FirstDragonTime,
                s => s.FirstVoidgrubTime, s => s.FirstHeraldTime, s => s.FirstBaronTime };
            for (var i = 0; i < names.Length; i++)
            {
                var validTimes = known.Select(times[i]).Where(v => v.HasValue).Select(v => v.Value).ToList();
                lines.Add(string.Format(CultureInfo.CurrentCulture,
                    ui.Get(UiTextKeys.LeaguePlayerAnalysisObjectivesRow),
                    ui.Get(names[i]), known.Average(counts[i]).ToString("0.00", CultureInfo.CurrentCulture),
                    validTimes.Count == 0 ? "--" :
                        LeagueMatchTimelineParser.Time((long)validTimes.Average()),
                    validTimes.Count, known.Count));
            }
            lines.Add(string.Format(CultureInfo.CurrentCulture,
                ui.Get(UiTextKeys.LeaguePlayerAnalysisObjectivesSoloDragon),
                known.Sum(s => s.SoloDragons), known.Sum(s => s.Dragons)));
            return string.Join(Environment.NewLine, lines);
        }
    }
}
