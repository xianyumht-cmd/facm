using System;
using System.Collections.Generic;
using System.Linq;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueObjectiveHistorySmokeTest
    {
        internal static void Validate()
        {
            var player = new LeaguePlayerMatchSummary { GameId = 123, MapId = 11,
                GameMode = "CLASSIC", GameType = "MATCHED_GAME", ParticipantId = 1,
                TeamId = 100, ChampionId = 64, Position = "TOP", PuuId = "target",
                DataSource = LeaguePlayerHistorySource.Lcu,
                ParticipantResolved = true, StatisticsAvailable = true, WinResult = "win" };
            var timeline = new LeagueMatchTimeline { GameId = 123 };
            timeline.Frames.Add(new LeagueMatchTimelineFrame { Timestamp = 0 });
            timeline.Events.Add(Event("DRAGON", 100, 1, null, 100000));
            timeline.Events.Add(Event("HORDE", 100, 2, null, 150000));
            timeline.Events.Add(Event("RIFTHERALD", 200, 7, null, 300000));
            timeline.Events.Add(Event("BARON_NASHOR", 100, 2, null, 1200000));
            var projection = LeagueJungleAnalysis.ObjectivesForAnyRole(timeline, player);
            Require(projection != null && projection.ObjectivesAvailable &&
                projection.GotFirstDragon == true && projection.Dragons == 1 &&
                projection.SoloDragons == 1 && projection.Voidgrubs == 1 &&
                projection.Heralds == 0 && projection.Barons == 1 &&
                projection.FirstDragonTime == 100000,
                "All-role objectives must preserve source team, first dragon and solo kill evidence.");
            Require(LeagueJungleAnalysis.ObjectivesForAnyRole(null, player) == null &&
                LeagueJungleAnalysis.ObjectivesForAnyRole(new LeagueMatchTimeline { GameId = 123 }, player) == null,
                "Absent timelines must not be counted as zero objectives.");
            var candidates = LeagueObjectiveHistoryAnalysis.Candidates(new[] { player, player }, "target");
            Require(candidates.Count == 1 && LeagueObjectiveHistoryAnalysis.PendingBatch(candidates,
                "target", null, 40).Count == 0, "Objective batches must deduplicate and cap reads.");
            var sample = new LeagueObjectiveHistorySample { Match = player, Objectives = projection };
            var report = LeagueObjectiveHistoryAnalysis.Report(UiTextCatalog.Load(),
                new[] { sample, sample }, candidates, "target", null, 0);
            Require(report.Contains("1/1") && report.Contains("100.0%"),
                "Team objectives aggregate once per verified match.");
            var unknown = LeagueObjectiveHistoryAnalysis.Report(UiTextCatalog.Load(),
                null, candidates, "target", null, 0);
            Require(unknown.Contains("0/1") && !unknown.Contains("100.0%"),
                "No timeline is unknown rather than a known zero.");
        }

        private static LeagueMatchTimelineEvent Event(string monster, int team, int killer,
            int[] assists, long time)
        {
            return new LeagueMatchTimelineEvent { Timestamp = time, Type = "ELITE_MONSTER_KILL",
                KillerId = killer, Assists = assists,
                Data = new Dictionary<string, object> {
                    { "monsterType", monster }, { "killerTeamId", team } } };
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
