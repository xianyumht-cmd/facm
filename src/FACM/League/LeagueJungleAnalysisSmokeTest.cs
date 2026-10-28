using System;
using System.Collections.Generic;

namespace FACM.League
{
    internal static class LeagueJungleAnalysisSmokeTest
    {
        internal static void Validate()
        {
            var player = new LeaguePlayerMatchSummary { GameId = 500, MapId = 11,
                ParticipantId = 1, Position = "JUNGLE" };
            var timeline = new LeagueMatchTimeline { GameId = 500 };
            for (var i = 0; i <= 15; i++)
            {
                var frame = new LeagueMatchTimelineFrame { Timestamp = i * 60000 };
                frame.Participants[1] = new Dictionary<string, object> {
                    { "position", Position(i == 2 ? 10000 : 1000, i == 2 ? 1000 : 10000) },
                    { "minionsKilled", 0 }, { "jungleMinionsKilled", 12 }, { "level", 3 }
                };
                timeline.Frames.Add(frame);
            }
            timeline.Frames[1].Participants[1]["position"] = Position(3830, 7880);
            timeline.Events.Add(Kill(180000, 6000, 6000, true));
            timeline.Events.Add(Kill(240000, 10000, 1000, false));
            timeline.Events.Add(Kill(840000, 1000, 10000, true));
            timeline.Events.Add(Kill(840001, 1000, 10000, true));
            var result = LeagueJungleAnalysis.Calculate(timeline, player);
            Require(result != null && result.PositionSamples == 14 && result.AvailableSamples == 14 &&
                result.ZoneWeights[0] == 18 && result.ZoneWeights[1] == 5 && result.ZoneWeights[2] == 6 &&
                result.Ganks[0] == 1 && result.Ganks[1] == 1 && result.Ganks[2] == 1 &&
                result.StartCamp == "blue" && result.StartBlue == true &&
                result.Level3Gank == true && result.Level4Gank == true,
                "Pinned pathing must use frames 1-14, weight kill/assist positions by five and preserve inclusive time boundaries.");
            Require(LeagueJungleAnalysis.MapZone(4000, 7500) == 1 &&
                LeagueJungleAnalysis.GankLane(4000, 8000) == null &&
                LeagueJungleAnalysis.GankLane(3000, 3000) == null &&
                LeagueJungleAnalysis.GankLane(6000, 6000) == 1,
                "Heat-zone and gank-lane geometry have distinct pinned boundary rules.");
            bool blue;
            Require(LeagueJungleAnalysis.NearestCamp(7850, 9420, out blue) == "raptors" && !blue,
                "First-clear inference must distinguish red-side camp coordinates.");

            timeline.Events.Clear();
            var third = timeline.Frames[3].Participants[1];
            var fourth = timeline.Frames[4].Participants[1];
            third["damageStats"] = new Dictionary<string, object> { { "totalDamageDoneToChampions", 100 } };
            third["championStats"] = new Dictionary<string, object>();
            fourth["damageStats"] = new Dictionary<string, object> { { "totalDamageDoneToChampions", 100 } };
            fourth["championStats"] = new Dictionary<string, object>();
            result = LeagueJungleAnalysis.Calculate(timeline, player);
            Require(result.Level3Gank == true && result.Level4Gank == false,
                "Detailed SGP frames must use champion damage and its 3-to-4 frame increase.");
            ((Dictionary<string, object>)fourth["damageStats"])["totalDamageDoneToChampions"] = 101;
            Require(LeagueJungleAnalysis.Calculate(timeline, player).Level4Gank == true,
                "A source-confirmed damage increase must expose the early follow-up signal.");
            third.Remove("jungleMinionsKilled");
            Require(!LeagueJungleAnalysis.Calculate(timeline, player).Level3Gank.HasValue,
                "Missing frame CS must remain unknown, never an inferred zero-clear result.");
            third["jungleMinionsKilled"] = 12;
            third.Remove("championStats"); fourth.Remove("championStats");
            result = LeagueJungleAnalysis.Calculate(timeline, player);
            Require(result.Level3Gank == false && result.Level4Gank == false,
                "A partial detailed-frame marker must not pretend to be SGP champion damage evidence.");
            var unknown = Kill(120000, 6000, 6000, true); unknown.Data.Remove("position");
            timeline.Events.Add(unknown);
            timeline.Frames[1].Participants[1].Remove("position");
            result = LeagueJungleAnalysis.Calculate(timeline, player);
            Require(result.StartCamp == null && !result.StartBlue.HasValue &&
                result.PositionSamples == 13 && result.MissingKillPositions == 1 &&
                !result.Level3Gank.HasValue,
                "Missing event/frame positions cannot be fabricated from another timestamp or participant.");

            player.MapId = 12;
            Require(LeagueJungleAnalysis.Calculate(timeline, player) == null,
                "Non-Rift modes must not expose Rift jungle analysis.");
            player.MapId = 11; player.Position = "TOP";
            Require(LeagueJungleAnalysis.Calculate(timeline, player) == null,
                "A role without smite must not be identified as a jungler.");
            player.SpellIds = new[] { 4, 11 };
            Require(LeagueJungleAnalysis.Calculate(timeline, player) != null,
                "Pinned smite-based jungle detection must remain available when role is absent.");
            ValidateObjectives(timeline, player);
            player.GameId = 501;
            Require(LeagueJungleAnalysis.Calculate(timeline, player) == null,
                "Timeline and selected-match identities must agree.");
        }

        private static void ValidateObjectives(LeagueMatchTimeline timeline, LeaguePlayerMatchSummary player)
        {
            player.TeamId = 100;
            timeline.Events.Clear();
            timeline.Events.Add(Objective(300000, "DRAGON", 6, null, new int[0]));
            timeline.Events.Add(Objective(420000, "HORDE", 1, 100, new int[0]));
            timeline.Events.Add(Objective(421000, "HORDE", 2, 100, new int[0]));
            timeline.Events.Add(Objective(600000, "DRAGON", 1, 100, new int[0]));
            timeline.Events.Add(Objective(800000, "RIFTHERALD", 2, 100, new int[0]));
            timeline.Events.Add(Objective(900000, "DRAGON", 1, 100, new[] { 2 }));
            timeline.Events.Add(Objective(1200000, "BARON_NASHOR", 6, 100, new int[0]));
            var result = LeagueJungleAnalysis.Calculate(timeline, player);
            Require(result.ObjectivesAvailable && result.GotFirstDragon == false && result.Dragons == 2 &&
                result.SoloDragons == 1 && result.Voidgrubs == 2 && result.Heralds == 1 && result.Barons == 1 &&
                result.FirstDragonTime == 600000 && result.FirstVoidgrubTime == 420000 &&
                result.FirstHeraldTime == 800000 && result.FirstBaronTime == 1200000 &&
                result.UnknownObjectiveTeams == 0,
                "Objectives must prefer explicit killer team, retain first-team dragon, distinguish solo kills and include events beyond 14 minutes.");
            timeline.Events.Clear();
            timeline.Events.Add(Objective(300000, "DRAGON", 0, null, new int[0]));
            timeline.Events.Add(Objective(600000, "DRAGON", 1, 100, new int[0]));
            timeline.Events.Add(Objective(700000, "DRAGON", 1, 999, new int[0]));
            result = LeagueJungleAnalysis.Calculate(timeline, player);
            Require(!result.GotFirstDragon.HasValue && result.Dragons == 1 && result.UnknownObjectiveTeams == 2,
                "An unknown first objective team must not be replaced by a later dragon or an invented ordinary team.");
            player.TeamId = 0;
            Require(!LeagueJungleAnalysis.Calculate(timeline, player).ObjectivesAvailable,
                "Missing selected-player team must leave objective data unavailable rather than report measured zero.");
        }

        private static LeagueMatchTimelineEvent Objective(long time, string type, int killer, int? team, int[] assists)
        {
            var data = new Dictionary<string, object> { { "monsterType", type } };
            if (team.HasValue) data["killerTeamId"] = team.Value;
            return new LeagueMatchTimelineEvent { Type = "ELITE_MONSTER_KILL", Timestamp = time,
                KillerId = killer, Assists = assists, Data = data };
        }

        private static Dictionary<string, object> Position(int x, int y)
        { return new Dictionary<string, object> { { "x", x }, { "y", y } }; }

        private static LeagueMatchTimelineEvent Kill(long time, int x, int y, bool killer)
        {
            return new LeagueMatchTimelineEvent { Type = "CHAMPION_KILL", Timestamp = time,
                KillerId = killer ? 1 : 2, Assists = killer ? new int[0] : new[] { 1 },
                Data = new Dictionary<string, object> { { "position", Position(x, y) } } };
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
