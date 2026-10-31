using System;
using System.Collections.Generic;
using System.Linq;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueEarlyJungleDeathSmokeTest
    {
        internal static void Validate()
        {
            var match = Match();
            var timeline = new LeagueMatchTimeline { GameId = match.GameId };
            timeline.Frames.Add(new LeagueMatchTimelineFrame { Timestamp = 0 });
            timeline.Events.Add(Event(120000, 1, 7, null));
            timeline.Events.Add(Event(900000, 1, 6, new[] { 7 }));
            timeline.Events.Add(Event(900001, 1, 7, null));
            timeline.Events.Add(Event(100000, 1, 6, null));
            timeline.Events.Add(Event(200000, 2, 7, null));
            Require(LeagueEarlyJungleDeathAnalysis.Calculate(timeline, match) == 2,
                "Only deaths involving an enemy jungler within the inclusive 15-minute window count.");
            Require(LeagueEarlyJungleDeathAnalysis.Calculate(null, match) == null &&
                LeagueEarlyJungleDeathAnalysis.Calculate(new LeagueMatchTimeline { GameId = 100 }, match) == null &&
                LeagueEarlyJungleDeathAnalysis.Calculate(new LeagueMatchTimeline { GameId = 999 }, match) == null,
                "Missing, empty and wrong-game timelines are unknown.");
            timeline.Events.Clear();
            Require(LeagueEarlyJungleDeathAnalysis.Calculate(timeline, match) == 0,
                "A complete empty timeline yields a measured zero.");
            match.ParticipantRoster[6].Position = null;
            match.ParticipantRoster[6].SpellIds = new[] { 11, 4 };
            Require(LeagueEarlyJungleDeathAnalysis.EnemyJunglers(match).SequenceEqual(new[] { 7 }),
                "Enemy Smite fallback works without position.");
            match.ParticipantRoster[6].SpellIds = null;
            Require(LeagueEarlyJungleDeathAnalysis.Calculate(timeline, match) == null,
                "No identifiable enemy jungler is unknown.");
            match.ParticipantRoster[6].Position = "JUNGLE";
            var duplicate = Match(); duplicate.ParticipantRoster[8].ParticipantId = 7;
            Require(LeagueEarlyJungleDeathAnalysis.EnemyJunglers(duplicate).Length == 0,
                "Duplicate roster slots are rejected.");
            var jungler = Match(); jungler.HasSmite = true;
            Require(LeagueEarlyJungleDeathAnalysis.EnemyJunglers(jungler).Length == 0,
                "Self junglers are excluded.");
            var wrongMode = Match(); wrongMode.GameMode = "ARAM";
            Require(LeagueEarlyJungleDeathAnalysis.Calculate(timeline, wrongMode) == null,
                "Only Classic Rift is eligible.");
            var regional = Match(); regional.DataSource = LeaguePlayerHistorySource.Sgp;
            regional.SgpServerId = "TENCENT_HN10"; regional.GameId = 200;
            var candidates = LeagueEarlyJungleDeathAnalysis.Candidates(new[] {
                match, match, regional, wrongMode, jungler, duplicate
            }, "target");
            Require(candidates.Count == 2 && candidates.Any(m => m.GameId == 200),
                "Distinct valid target/source/region games only.");
            var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                LeaguePlayerDataService.HistoryDetailKey(match)
            };
            Require(LeagueEarlyJungleDeathAnalysis.PendingBatch(candidates, "target", attempted, 0)
                .Single().GameId == 200 &&
                LeagueEarlyJungleDeathAnalysis.PendingBatch(candidates, "target", attempted, 40).Count == 0,
                "Batch bounds and attempted keys hold.");
            var sample = new LeagueEarlyJungleDeathSample { Match = match, Deaths = 0 };
            var ui = UiTextCatalog.Load();
            var report = LeagueEarlyJungleDeathAnalysis.Report(ui, new[] { sample, sample },
                candidates, "target", attempted, 0);
            Require(report.Contains("0.00") && report.Contains("1/2"),
                "Known zero, duplicates and coverage are distinct.");
            Require(LeagueEarlyJungleDeathAnalysis.Report(ui, null, candidates, "target",
                attempted, 0).Contains("--"), "Unloaded samples are unknown.");
        }

        private static LeagueMatchTimelineEvent Event(long time, int victim, int killer, int[] assists)
        {
            return new LeagueMatchTimelineEvent { Timestamp = time, Type = "CHAMPION_KILL",
                VictimId = victim, KillerId = killer, Assists = assists };
        }

        private static LeaguePlayerMatchSummary Match()
        {
            var roster = Enumerable.Range(1, 10).Select(i => new LeaguePlayerMatchSummary {
                ParticipantId = i, TeamId = i <= 5 ? 100 : 200,
                PuuId = i == 1 ? "target" : "player-" + i,
                Position = i == 7 ? "JUNGLE" : "TOP"
            }).ToArray();
            return new LeaguePlayerMatchSummary { GameId = 100, MapId = 11,
                GameMode = "CLASSIC", GameType = "MATCHED_GAME", PuuId = "target",
                ParticipantId = 1, TeamId = 100, ChampionId = 64, Position = "TOP",
                DataSource = LeaguePlayerHistorySource.Lcu, ParticipantRoster = roster,
                ParticipantResolved = true, StatisticsAvailable = true, WinResult = "win" };
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
