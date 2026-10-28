using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueJungleHistorySample
    {
        internal LeaguePlayerMatchSummary Match;
        internal LeagueJungleAnalysis Analysis;
    }

    internal sealed class LeagueJungleHistoryAggregate
    {
        internal int Games, PositionGames, FirstCampGames, ObjectiveGames;
        internal int Level3Known, Level3Detected, Level4Known, Level4Detected;
        internal int BlueGames, RedGames, BlueLevel3Known, BlueLevel3Detected, BlueLevel4Known, BlueLevel4Detected;
        internal int RedLevel3Known, RedLevel3Detected, RedLevel4Known, RedLevel4Detected;
        internal int FirstDragonKnown, FirstDragonWon;
        internal int Dragons, SoloDragons, Voidgrubs, Heralds, Barons, MissingKillPositions;
        internal readonly int[] ZoneWeights = new int[3];
        internal readonly int[] Ganks = new int[3];
        internal readonly Dictionary<string, int> Camps = new Dictionary<string, int>(StringComparer.Ordinal);
        internal double? FirstDragonSeconds, FirstVoidgrubSeconds, FirstHeraldSeconds, FirstBaronSeconds;

        internal double? ZoneRate(int index)
        {
            var sum = ZoneWeights.Sum();
            return sum > 0 && index >= 0 && index < 3 ? (double?)ZoneWeights[index] / sum : null;
        }

        internal static LeagueJungleHistoryAggregate Calculate(IEnumerable<LeagueJungleHistorySample> samples)
        {
            var entries = (samples ?? Enumerable.Empty<LeagueJungleHistorySample>())
                .Where(s => s != null && s.Match != null && s.Analysis != null)
                .GroupBy(s => LeaguePlayerDataService.HistoryDetailKey(s.Match), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).Take(LeagueJungleHistoryAnalysis.MaximumSamples).ToArray();
            if (entries.Length == 0) return null;
            var result = new LeagueJungleHistoryAggregate { Games = entries.Length };
            var dragonTimes = new List<long>();
            var grubTimes = new List<long>();
            var heraldTimes = new List<long>();
            var baronTimes = new List<long>();
            foreach (var entry in entries)
            {
                var a = entry.Analysis;
                if (entry.Match.TeamId == 100)
                {
                    result.BlueGames++;
                    if (a.Level3Gank.HasValue)
                    {
                        result.BlueLevel3Known++;
                        if (a.Level3Gank.Value) result.BlueLevel3Detected++;
                    }
                    if (a.Level4Gank.HasValue)
                    {
                        result.BlueLevel4Known++;
                        if (a.Level4Gank.Value) result.BlueLevel4Detected++;
                    }
                }
                else if (entry.Match.TeamId == 200)
                {
                    result.RedGames++;
                    if (a.Level3Gank.HasValue)
                    {
                        result.RedLevel3Known++;
                        if (a.Level3Gank.Value) result.RedLevel3Detected++;
                    }
                    if (a.Level4Gank.HasValue)
                    {
                        result.RedLevel4Known++;
                        if (a.Level4Gank.Value) result.RedLevel4Detected++;
                    }
                }
                for (var i = 0; i < 3; i++)
                {
                    result.ZoneWeights[i] += a.ZoneWeights[i];
                    result.Ganks[i] += a.Ganks[i];
                }
                if (a.ZoneWeights.Sum() > 0) result.PositionGames++;
                result.MissingKillPositions += a.MissingKillPositions;
                if (a.Level3Gank.HasValue)
                {
                    result.Level3Known++;
                    if (a.Level3Gank.Value) result.Level3Detected++;
                }
                if (a.Level4Gank.HasValue)
                {
                    result.Level4Known++;
                    if (a.Level4Gank.Value) result.Level4Detected++;
                }
                if (a.StartCamp != null && a.StartBlue.HasValue &&
                    (entry.Match.TeamId == 100 || entry.Match.TeamId == 200))
                {
                    result.FirstCampGames++;
                    var side = entry.Match.TeamId == 100 ? "blue" : "red";
                    var key = side + "/" + (a.StartBlue.Value == (side == "blue") ? "normal/" : "invade/") + a.StartCamp;
                    int count;
                    result.Camps.TryGetValue(key, out count);
                    result.Camps[key] = count + 1;
                }
                if (!a.ObjectivesAvailable) continue;
                result.ObjectiveGames++;
                result.Dragons += a.Dragons;
                result.SoloDragons += a.SoloDragons;
                result.Voidgrubs += a.Voidgrubs;
                result.Heralds += a.Heralds;
                result.Barons += a.Barons;
                if (a.GotFirstDragon.HasValue)
                {
                    result.FirstDragonKnown++;
                    if (a.GotFirstDragon.Value) result.FirstDragonWon++;
                }
                if (a.FirstDragonTime.HasValue) dragonTimes.Add(a.FirstDragonTime.Value);
                if (a.FirstVoidgrubTime.HasValue) grubTimes.Add(a.FirstVoidgrubTime.Value);
                if (a.FirstHeraldTime.HasValue) heraldTimes.Add(a.FirstHeraldTime.Value);
                if (a.FirstBaronTime.HasValue) baronTimes.Add(a.FirstBaronTime.Value);
            }
            result.FirstDragonSeconds = MeanSeconds(dragonTimes);
            result.FirstVoidgrubSeconds = MeanSeconds(grubTimes);
            result.FirstHeraldSeconds = MeanSeconds(heraldTimes);
            result.FirstBaronSeconds = MeanSeconds(baronTimes);
            return result;
        }

        private static double? MeanSeconds(List<long> times)
        {
            return times.Count == 0 ? (double?)null : times.Average(time => time / 1000d);
        }
    }

    internal static class LeagueJungleHistoryAnalysis
    {
        internal const int BatchSize = 8;
        internal const int MaximumSamples = 40;

        internal static List<LeaguePlayerMatchSummary> PendingBatch(IEnumerable<LeaguePlayerMatchSummary> matches,
            string targetPuuid, ISet<string> attempted, int successful)
        {
            if (successful >= MaximumSamples) return new List<LeaguePlayerMatchSummary>();
            return Candidates(matches, targetPuuid).Where(m => attempted == null ||
                !attempted.Contains(LeaguePlayerDataService.HistoryDetailKey(m)))
                .Take(Math.Min(BatchSize, MaximumSamples - Math.Max(0, successful))).ToList();
        }

        internal static List<LeaguePlayerMatchSummary> Candidates(IEnumerable<LeaguePlayerMatchSummary> matches,
            string targetPuuid)
        {
            return LeaguePlayerHistoryAnalysis.LoadedMatches(matches).Where(m => m.GameId > 0 && m.MapId == 11 &&
                m.ParticipantId >= 1 && m.ParticipantId <= 10 && m.ChampionId > 0 &&
                (m.TeamId == 100 || m.TeamId == 200) &&
                (string.IsNullOrWhiteSpace(m.GameType) || m.GameType == "MATCHED_GAME") &&
                (LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win ||
                 LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Loss) &&
                (string.IsNullOrEmpty(m.PuuId) || m.PuuId == targetPuuid) &&
                (string.Equals(m.Position, "JUNGLE", StringComparison.OrdinalIgnoreCase) || m.HasSmite ||
                 (m.SpellIds != null && m.SpellIds.Contains(11))) &&
                (m.DataSource == LeaguePlayerHistorySource.Lcu && string.IsNullOrEmpty(m.SgpServerId) ||
                 m.DataSource == LeaguePlayerHistorySource.Sgp && !string.IsNullOrEmpty(m.SgpServerId)))
                .GroupBy(LeaguePlayerDataService.HistoryDetailKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).ToList();
        }

        internal static string Report(UiTextCatalog ui, IEnumerable<LeagueJungleHistorySample> entries,
            int eligible, int attempted, int championId = 0)
        {
            var samples = (entries ?? Enumerable.Empty<LeagueJungleHistorySample>())
                .Where(s => s != null && s.Match != null &&
                    (championId == 0 || s.Match.ChampionId == championId)).ToArray();
            var a = LeagueJungleHistoryAggregate.Calculate(samples);
            var lines = new List<string> { ui.Get(UiTextKeys.LeagueJungleHistoryScope),
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleHistoryCoverage),
                    a?.Games ?? 0, eligible, attempted, MaximumSamples) };
            if (a == null)
            {
                lines.Add(ui.Get(UiTextKeys.LeagueJungleHistoryUnavailable));
                return string.Join(Environment.NewLine, lines);
            }
            lines.Add(string.Empty);
            var lanes = new[] { UiTextKeys.LeaguePlayerAnalysisPositionTop,
                UiTextKeys.LeaguePlayerAnalysisPositionMiddle, UiTextKeys.LeaguePlayerAnalysisPositionBottom };
            for (var i = 0; i < 3; i++)
                lines.Add(string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleHistoryLane),
                    ui.Get(lanes[i]), Percent(a.ZoneRate(i)), a.Ganks[i], a.PositionGames));
            lines.Add(string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleHistoryEarly),
                a.Level3Detected, a.Level3Known, a.Level4Detected, a.Level4Known, a.MissingKillPositions));
            lines.Add(string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleHistoryBySide),
                ui.Get(UiTextKeys.LeagueJungleBlueSide), a.BlueGames,
                a.BlueLevel3Detected, a.BlueLevel3Known, a.BlueLevel4Detected, a.BlueLevel4Known));
            lines.Add(string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleHistoryBySide),
                ui.Get(UiTextKeys.LeagueJungleRedSide), a.RedGames,
                a.RedLevel3Detected, a.RedLevel3Known, a.RedLevel4Detected, a.RedLevel4Known));
            lines.Add(string.Empty);
            lines.Add(string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleHistoryCamps),
                a.FirstCampGames, a.Games));
            foreach (var camp in a.Camps.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal))
            {
                var parts = camp.Key.Split('/');
                if (parts.Length != 3) continue;
                var campKey = parts[2] == "blue" ? UiTextKeys.LeagueJungleCampBlue :
                    parts[2] == "wolves" ? UiTextKeys.LeagueJungleCampWolves :
                    parts[2] == "red" ? UiTextKeys.LeagueJungleCampRed : UiTextKeys.LeagueJungleCampRaptors;
                lines.Add(string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleHistoryCampRow),
                    ui.Get(parts[0] == "blue" ? UiTextKeys.LeagueJungleBlueSide : UiTextKeys.LeagueJungleRedSide),
                    ui.Get(parts[1] == "normal" ? UiTextKeys.LeagueJungleHistoryNormal : UiTextKeys.LeagueJungleHistoryInvade),
                    ui.Get(campKey), camp.Value));
            }
            lines.Add(string.Empty);
            lines.Add(string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleHistoryObjectives),
                a.ObjectiveGames, a.Games, a.FirstDragonWon, a.FirstDragonKnown,
                a.Dragons == 0 ? "--" : Percent((double)a.SoloDragons / a.Dragons)));
            var counts = new[] { a.Dragons, a.Voidgrubs, a.Heralds, a.Barons };
            var times = new[] { a.FirstDragonSeconds, a.FirstVoidgrubSeconds,
                a.FirstHeraldSeconds, a.FirstBaronSeconds };
            var names = new[] { UiTextKeys.LeagueJungleDragons, UiTextKeys.LeagueJungleGrubs,
                UiTextKeys.LeagueJungleHeralds, UiTextKeys.LeagueJungleBarons };
            for (var i = 0; i < names.Length; i++)
                lines.Add(string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleHistoryObjectiveRow),
                    ui.Get(names[i]), (a.ObjectiveGames == 0 ? "--" :
                        ((double)counts[i] / a.ObjectiveGames).ToString("0.00", CultureInfo.CurrentCulture)),
                    times[i].HasValue ? LeagueMatchTimelineParser.Time((long)(times[i].Value * 1000d)) : "--"));
            return string.Join(Environment.NewLine, lines);
        }

        private static string Percent(double? rate)
        {
            return rate.HasValue ? (rate.Value * 100d).ToString("0.0", CultureInfo.CurrentCulture) + "%" : "--";
        }
    }
}
