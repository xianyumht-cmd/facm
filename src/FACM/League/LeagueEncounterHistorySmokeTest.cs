using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Performance;
using FACM.Theming;

namespace FACM.League
{
    internal static class LeagueEncounterHistorySmokeTest
    {
        internal static void Validate()
        {
            Require(FACM.AppHost.Modules.LeagueEfficiencyModule.IsSafeEncounterAssetReference(
                    "lcu:/lol-game-data/assets/v1/items/3006.png") &&
                !FACM.AppHost.Modules.LeagueEfficiencyModule.IsSafeEncounterAssetReference(
                    "https://example.invalid/icon.png") &&
                !FACM.AppHost.Modules.LeagueEfficiencyModule.IsSafeEncounterAssetReference(
                    "lcu:/lol-game-data/assets/../../client-config") &&
                !FACM.AppHost.Modules.LeagueEfficiencyModule.IsSafeEncounterAssetReference(
                    "lcu:/lol-game-data/assets/%2e%2e/secret"),
                "Encountered-match icon loading must be restricted to safe local game-data paths.");
            ValidateMatchDetailAsync().GetAwaiter().GetResult();
            ValidateCardPresentation();
            ValidateReplayAsync().GetAwaiter().GetResult();
            var session = new LeagueClientSession("LeagueClientUx", 55, 2999, "test", "https", "fixture", "HN1", "HN1");
            var game = new LeagueLiveSnapshot();
            new LeagueLiveDataService(new StubClient(), new PerformanceBudgetProvider()).ApplyCurrentGame(game,
                Encoding.UTF8.GetBytes("{\"phase\":\"InProgress\",\"gameData\":{\"gameId\":12345," +
                    "\"queue\":{\"id\":450,\"type\":\"ARAM\"},\"teamOne\":[{\"puuid\":\"self\"},{\"puuid\":\"ally\"}]," +
                    "\"teamTwo\":[{\"puuid\":\"enemy\"}]}}"));
            var capture = LeagueEncounterCapture.Project(session, game, new LeaguePlayerProfile { PuuId = "self" });
            Require(capture != null && capture.GameId == 12345 && capture.Participants.Length == 3 &&
                capture.QueueType == "ARAM", "Encountered-game queue type must match the authenticated gameflow session.");
            game.QueueType = null;
            Require(LeagueEncounterCapture.Project(session, game, new LeaguePlayerProfile { PuuId = "self" }).QueueType == "queue-450",
                "Older gameflow payloads without queue.type should preserve the bounded fallback.");
            game.QueueType = "ARAM";
            Require(LeagueEncounterCapture.Project(session, game, new LeaguePlayerProfile { PuuId = "stranger" }) == null,
                "Encounter projection must not record a game without the signed-in player.");
            game.Phase = "EndOfGame";
            Require(LeagueEncounterCapture.Project(session, game, new LeaguePlayerProfile { PuuId = "self" }) == null,
                "A stale end-game roster must not be mistaken for an in-progress match capture.");
            game.Phase = "InProgress";
            game.Players.Add(new LeagueLivePlayerRow { PuuId = "enemy" });
            Require(LeagueEncounterCapture.Project(session, game, new LeaguePlayerProfile { PuuId = "self" }) == null,
                "Duplicated roster identities must not be recorded.");
            game.Players.RemoveAt(game.Players.Count - 1);

            var directory = Path.Combine(Path.GetTempPath(), "ggman-encounters-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LeagueEncounterHistoryStore(directory);
                Require(store.ReadPlayer("self", "enemy", "HN1", "HN1").Count == 0,
                    "Fresh encounter storage should be empty.");
                Require(store.SaveGame(capture.GameId, capture.SelfPuuid, capture.Region,
                    capture.PlatformId, capture.QueueType, capture.Participants) == 2,
                    "The first game must record each other player exactly once.");
                Require(store.SaveGame(capture.GameId, capture.SelfPuuid, capture.Region,
                    capture.PlatformId, capture.QueueType, capture.Participants) == 0,
                    "Replayed EndOfGame events must not duplicate a game.");
                var reloaded = new LeagueEncounterHistoryStore(directory);
                Require(reloaded.ReadPlayer("self", "enemy", "HN1", "HN1").Single().GameId == 12345 &&
                    reloaded.ReadPlayer("self", "enemy", "HN2", "HN1").Count == 0 &&
                    reloaded.ReadPlayer("different-self", "enemy", "HN1", "HN1").Count == 0 &&
                    reloaded.ReadPlayer("self", "enemy", "HN1", "HN2").Count == 0,
                    "Historical encounters must persist and remain scoped to the owning player and platform.");
                Require(reloaded.DeleteGame(12345, "self", "enemy", "HN1", "HN1") &&
                    reloaded.ReadPlayer("self", "enemy", "HN1", "HN1").Count == 0 &&
                    reloaded.ReadPlayer("self", "ally", "HN1", "HN1").Count == 1,
                    "Deleting one player's encounter must not remove another player's record.");
                Require(reloaded.SaveGame(12346, "self", "HN1", "HN1", "CLASSIC", new[] { "self", "enemy" }) == 1 &&
                    reloaded.SaveGame(12347, "self", "HN1", "HN1", "ARAM", new[] { "self", "enemy" }) == 1 &&
                    reloaded.SaveGame(12348, "self", "HN1", "HN1", "ARAM", new[] { "self", "enemy" }) == 1,
                    "Encounter query fixture could not write all games.");
                var page1 = reloaded.QueryPlayer("self", "enemy", "HN1", "HN1", 1, 2);
                var page2 = reloaded.QueryPlayer("self", "enemy", "HN1", "HN1", 2, 2);
                Require(page1.Total == 3 && page1.Records.Select(p => p.GameId).SequenceEqual(new long[] { 12348, 12347 }) &&
                    page2.Total == 3 && page2.Records.Single().GameId == 12346,
                    "Encountered-game paging or newest-first ordering differs from the source query.");
                var filtered = reloaded.QueryPlayer("self", "enemy", "HN1", "HN1", 1, 20, "ARAM", false);
                Require(filtered.Total == 2 && filtered.Records.Select(p => p.GameId).SequenceEqual(new long[] { 12347, 12348 }) &&
                    reloaded.QueryPlayer("self", "enemy", "HN1", "HN1", 1, 20, "AR").Total == 0 &&
                    reloaded.QueryPlayer("other", "enemy", "HN1", "HN1").Total == 0,
                    "Encountered-game filters must be exact and remain scoped to the signed-in account.");
                Require(!reloaded.DeleteGame(12348, "other", "enemy", "HN1", "HN1") &&
                    !reloaded.DeleteGame(12348, "self", "enemy", "HN2", "HN1") &&
                    !reloaded.DeleteGame(12348, "self", "enemy", "HN1", "HN2") &&
                    reloaded.QueryPlayer("self", "enemy", "HN1", "HN1").Total == 3,
                    "History deletion from an offline saved-player view must remain exact-account and exact-region scoped.");
                var tagUpdated = DateTime.UtcNow;
                var overview = LeagueSavedPlayerOverview.Build(new[]
                {
                    new LeaguePlayerTagRecord { SelfPuuid = "self", Puuid = "enemy", Region = "hn1",
                        PlatformId = "hn1", Tag = "known", UpdatedUtc = tagUpdated },
                    new LeaguePlayerTagRecord { SelfPuuid = "other", Puuid = "enemy", Region = "HN1",
                        PlatformId = "HN1", Tag = "different owner", UpdatedUtc = DateTime.UtcNow },
                    new LeaguePlayerTagRecord { SelfPuuid = "self", Puuid = "enemy", Region = "HN2",
                        PlatformId = "HN2", Tag = "different region", UpdatedUtc = DateTime.UtcNow },
                    new LeaguePlayerTagRecord { SelfPuuid = "self", Puuid = "tag-only", Region = "HN1",
                        PlatformId = "HN1", Tag = "no encounters", UpdatedUtc = DateTime.UtcNow }
                }, reloaded.ReadAll());
                Require(overview.Count == 5 && overview.Single(p => p.Puuid == "enemy" && p.SelfPuuid == "self" && p.Region == "HN1")
                    .EncounterCount == 3 && overview.Single(p => p.Puuid == "enemy" && p.SelfPuuid == "self" && p.Region == "HN1")
                    .Tag == "known" && overview.Single(p => p.Puuid == "enemy" && p.SelfPuuid == "self" && p.Region == "HN1")
                    .TagUpdatedUtc == tagUpdated && overview.Single(p => p.Puuid == "enemy" && p.SelfPuuid == "self" && p.Region == "HN2")
                    .EncounterCount == 0 && overview.Single(p => p.Puuid == "enemy" && p.SelfPuuid == "other")
                    .EncounterCount == 0 && overview.Single(p => p.Puuid == "ally").EncounterCount == 1 &&
                    overview.Single(p => p.Puuid == "tag-only").LastMetUtc == null,
                    "Saved-player index must include untagged encounters and isolated tag-only records.");
                var copied = reloaded.ReadAll().First();
                copied.Puuid = "modified";
                Require(reloaded.ReadAll().All(p => p.Puuid != "modified"),
                    "Saved-player overview must not expose mutable local-store state.");
                bool rejected = false;
                try { reloaded.SaveGame(99, "self", "HN1", "HN1", "queue-450", new[] { "ally", "enemy" }); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected, "An unrelated roster cannot be credited to the signed-in player.");
                var path = Path.Combine(directory, "league-encounters.json");
                File.WriteAllText(path, "{invalid encounter file", Encoding.UTF8);
                rejected = false;
                try { reloaded.SaveGame(888, "self", "HN1", "HN1", "queue-450", new[] { "self", "enemy" }); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected && File.ReadAllText(path, Encoding.UTF8).Contains("invalid encounter file"),
                    "Invalid encounter storage must fail closed without overwriting the damaged file.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void ValidateCardPresentation()
        {
            var runner = new LeaguePlayerMatchParticipant { ParticipantId = 1, TeamId = 100,
                PlayerSubteamId = 2, SubteamPlacement = 2, IsTarget = true };
            var winner = new LeaguePlayerMatchParticipant { ParticipantId = 2, TeamId = 100,
                PlayerSubteamId = 1, SubteamPlacement = 1 };
            var teammate = new LeaguePlayerMatchParticipant { ParticipantId = 3, TeamId = 100,
                PlayerSubteamId = 2, SubteamPlacement = 2 };
            var unassigned = new LeaguePlayerMatchParticipant { ParticipantId = 4, TeamId = 100 };
            var otherSide = new LeaguePlayerMatchParticipant { ParticipantId = 5, TeamId = 200 };
            var groups = new[] {
                new LeagueMatchTeamScore { TeamId = 2, IsCherry = true, SubteamPlacement = 2 },
                new LeagueMatchTeamScore { TeamId = 1, IsCherry = true, SubteamPlacement = 1 }
            };
            var participants = new[] { runner, unassigned, teammate, winner };
            Require(LeagueEncounterMatchScoreboardDialog.OrderedScoreboardTeams(groups, true)
                    .Select(team => team.TeamId).SequenceEqual(new[] { 1, 2 }) &&
                LeagueEncounterMatchScoreboardDialog.OrderScoreboardParticipants(participants, groups, true)
                    .Select(player => player.ParticipantId).SequenceEqual(new[] { 2, 1, 3, 4 }) &&
                LeagueEncounterMatchScoreboardDialog.OrderScoreboardParticipants(
                    new[] { runner, winner, otherSide }, null, true)
                    .Select(player => player.ParticipantId).SequenceEqual(new[] { 1, 2, 5 }),
                "Cherry cards and rows must order verified placements together, retain teammate identity " +
                "and leave missing subteam identity unassigned without dropping any player.");
            var ambiguousGroups = new[] {
                new LeagueMatchTeamScore { TeamId = 2, IsCherry = true, SubteamPlacement = null },
                new LeagueMatchTeamScore { TeamId = 1, IsCherry = true, SubteamPlacement = 1 }
            };
            Require(LeagueEncounterMatchScoreboardDialog.OrderScoreboardParticipants(participants,
                    ambiguousGroups, true).Select(player => player.ParticipantId)
                        .SequenceEqual(new[] { 2, 1, 3, 4 }) &&
                LeagueEncounterMatchScoreboardDialog.OrderScoreboardParticipants(
                    new[] { otherSide, winner, runner }, new[] {
                        new LeagueMatchTeamScore { TeamId = 200 },
                        new LeagueMatchTeamScore { TeamId = 100 }
                    }, false).Select(player => player.ParticipantId).SequenceEqual(new[] { 2, 1, 5 }),
                "Unknown Cherry placements must sort last, while ordinary teams retain red/blue membership.");
            Func<string, string> t = key => LeagueEfficiencyText.Get(null, key);
            var ratesPlayer = new LeaguePlayerMatchParticipant {
                TeamId = 100, PlayerSubteamId = 2, Kills = 4, Deaths = 0, Assists = 6,
                CreepScore = 120, CreepScoreAvailable = true,
                DamageToChampions = 5000, DamageAvailable = true,
                GoldEarned = 10000, GoldAvailable = true
            };
            var ratesTeams = new[] {
                new LeagueMatchTeamScore { TeamId = 100, Kills = 20, DamageToChampions = 40000 },
                new LeagueMatchTeamScore { TeamId = 2, Kills = 10, DamageToChampions = 20000 }
            };
            Require(LeagueEncounterMatchScoreboardDialog.FormatParticipantRates(
                    ratesPlayer, ratesTeams, true, 1200, t) ==
                        "KDA 10.00 · 参团率 100% · 伤害占比 25%\r\n补刀/分 6.0 · 金币/分 500.0" &&
                LeagueEncounterMatchScoreboardDialog.FormatParticipantRates(
                    ratesPlayer, ratesTeams, false, 1200, t).Contains("参团率 50% · 伤害占比 13%"),
                "MatchCard rates must use Akari's deaths=0 fallback and Cherry subteam, not red/blue team totals.");
            var sparsePlayer = new LeaguePlayerMatchParticipant { TeamId = 100, Kills = 1, Assists = 1 };
            var sparseRates = LeagueEncounterMatchScoreboardDialog.FormatParticipantRates(
                sparsePlayer, null, false, 0, t);
            Require(sparseRates.Contains("KDA 2.00 · 参团率 — · 伤害占比 —") &&
                sparseRates.Contains("补刀/分 — · 金币/分 —") &&
                LeagueEncounterMatchScoreboardDialog.FormatParticipantRates(
                    ratesPlayer, ratesTeams, true, 0, t).Contains("补刀/分 — · 金币/分 —"),
                "Unavailable roster, stats and duration must not appear as measured zeroes.");
            var zeroPlayer = new LeaguePlayerMatchParticipant { TeamId = 100, CreepScoreAvailable = true,
                GoldAvailable = true, DamageAvailable = true };
            Require(LeagueEncounterMatchScoreboardDialog.FormatParticipantRates(zeroPlayer,
                    new[] { new LeagueMatchTeamScore { TeamId = 100, Kills = 0, DamageToChampions = 0 } },
                    false, 600, t).Contains("参团率 0% · 伤害占比 0%") &&
                LeagueEncounterMatchScoreboardDialog.FormatParticipantRates(zeroPlayer,
                    new[] { new LeagueMatchTeamScore { TeamId = 100, Kills = 0, DamageToChampions = 0 } },
                    false, 600, t).Contains("补刀/分 0.0 · 金币/分 0.0"),
                "Explicitly verified zero values must remain distinguishable from unknown fields.");
            var streaks = new LeaguePlayerMatchParticipant { DoubleKills = 6, TripleKills = 3,
                QuadraKills = 2, PentaKills = 1 };
            Require(LeagueEncounterMatchScoreboardDialog.FormatMultikillTags(streaks, t) ==
                    "连杀表现：五杀 · 四杀 · 三杀 · 双杀 ×3\r\n" &&
                LeagueEncounterMatchScoreboardDialog.FormatMultikillTags(
                    new LeaguePlayerMatchParticipant { DoubleKills = 2, TripleKills = 0,
                        QuadraKills = 0, PentaKills = 0 }, t) == "连杀表现：双杀 ×2\r\n" &&
                LeagueEncounterMatchScoreboardDialog.FormatMultikillTags(
                    new LeaguePlayerMatchParticipant(), t) == string.Empty &&
                LeagueEncounterMatchScoreboardDialog.FormatMultikillTags(null, t) == string.Empty,
                "Source-cumulative multikills must be de-duplicated and absent tags omitted.");
            streaks.PentaKills = null;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMultikillTags(streaks, t) ==
                    "连杀表现：三杀 · 双杀 ×3\r\n",
                "A missing higher streak count must not be interpreted as a known zero.");
            streaks.PentaKills = 1;
            streaks.DoubleKills = 1;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMultikillTags(streaks, t) ==
                    "连杀表现：五杀 · 四杀 · 三杀\r\n",
                "Contradictory cumulative counts must not fabricate a negative or extra lower streak tag.");
            streaks.DoubleKills = 200;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMultikillTags(streaks, t) ==
                    "连杀表现：五杀 · 双杀 ×197 · 四杀 · 三杀\r\n",
                "Large source streak counts must retain Akari's dynamic tag priority rather than fixed order.");
            var leader = new LeaguePlayerMatchParticipant { ParticipantId = 1, TeamId = 100,
                DamageAvailable = true, DamageToChampions = 1000, TotalDamageTaken = 500,
                TotalHeal = 200, GoldAvailable = true, GoldEarned = 1200 };
            var ally = new LeaguePlayerMatchParticipant { ParticipantId = 2, TeamId = 100,
                DamageAvailable = true, DamageToChampions = 500, TotalDamageTaken = 700,
                TotalHeal = 100, GoldAvailable = true, GoldEarned = 1000 };
            var enemy = new LeaguePlayerMatchParticipant { ParticipantId = 3, TeamId = 200,
                DamageAvailable = true, DamageToChampions = 900, TotalDamageTaken = 600,
                TotalHeal = 150, GoldAvailable = true, GoldEarned = 1100 };
            var enemyAlly = new LeaguePlayerMatchParticipant { ParticipantId = 4, TeamId = 200,
                DamageAvailable = true, DamageToChampions = 400, TotalDamageTaken = 300,
                TotalHeal = 50, GoldAvailable = true, GoldEarned = 700 };
            var leaderboard = new[] { leader, ally, enemy, enemyAlly };
            Require(LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, false, t) ==
                    "对局表现：全场输出第一（1,000 / 66.67%） · 全场伤害经济效率第一（83.33%） · " +
                    "全场治疗第一（200 / 66.67%） · " +
                    "全场经济第一（1,200 / 54.55%）\r\n" &&
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    ally, leaderboard, false, t) ==
                    "对局表现：全场承伤第一（700 / 58.33%）\r\n" &&
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, false, t).Contains("队内输出第一（900 / 69.23%）") &&
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, false, t).Contains("队内治疗第一（150 / 75.00%）") &&
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, false, t).Contains("队内经济第一（1,100 / 61.11%）"),
                "MatchCard leader tags must use pinned match/team precedence, metric-specific totals and priority.");
            var efficiencyTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, false, t);
            Require(efficiencyTags.Contains("全场伤害经济效率第一（83.33%）") &&
                efficiencyTags.IndexOf("全场输出第一", StringComparison.Ordinal) <
                    efficiencyTags.IndexOf("全场伤害经济效率第一", StringComparison.Ordinal) &&
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, false, t).Contains("队内伤害经济效率第一（81.82%）"),
                "Damage/gold efficiency must follow pinned match/team priorities and percentage scaling.");
            enemyAlly.GoldAvailable = false;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, false, t).Contains("队内伤害经济效率第一（83.33%）") &&
                !LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemyAlly, leaderboard, false, t).Contains("伤害经济效率第一"),
                "Missing gold must suppress a match-wide efficiency claim and unavailable self metrics.");
            enemyAlly.GoldAvailable = true;
            enemy.DamageToChampions = 1000;
            enemy.GoldEarned = 1200;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, false, t).Contains("全场伤害经济效率第一（83.33%）"),
                "Tied damage/gold ratios must retain all-match leaders.");
            enemy.DamageToChampions = 900;
            enemy.GoldEarned = 1100;
            enemyAlly.TotalHeal = null;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, false, t).Contains("队内治疗第一（200 / 66.67%）") &&
                !LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, false, t).Contains("治疗第一"),
                "Partial enemy healing must not imply a match-wide record or a complete team comparison.");
            enemyAlly.TotalHeal = 50;
            enemy.DamageToChampions = 1000;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, false, t).Contains("全场输出第一（1,000 / 71.43%）"),
                "Source-equal positive match maxima must retain tied leaders.");
            enemy.DamageToChampions = 900;
            var unknownGroup = new LeaguePlayerMatchParticipant { ParticipantId = 5,
                DamageAvailable = true, DamageToChampions = 2000 };
            Require(LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard.Concat(new[] { unknownGroup }), false, t) == string.Empty &&
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    null, leaderboard, false, t) == string.Empty &&
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, null, false, t) == string.Empty,
                "Unknown team membership or missing selection must not fabricate leader claims.");
            leader.PlayerSubteamId = 2;
            ally.PlayerSubteamId = 2;
            enemy.PlayerSubteamId = 1;
            enemyAlly.PlayerSubteamId = 1;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, true, t).Contains("全场输出第一（1,000 / 66.67%）"),
                "Cherry leader shares must use verified subteams, not the ordinary team ID.");
            leader.DamageAvailable = false;
            Require(!LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, true, t).Contains("输出第一"),
                "Unavailable own damage cannot qualify for a leader tag.");

            leader.DamageAvailable = true;
            leader.TotalDamageToTowers = 1200;
            ally.TotalDamageToTowers = 600;
            enemy.TotalDamageToTowers = 900;
            enemyAlly.TotalDamageToTowers = 300;
            leader.TotalDamageShieldedOnTeammates = 300;
            ally.TotalDamageShieldedOnTeammates = 100;
            enemy.TotalDamageShieldedOnTeammates = 500;
            enemyAlly.TotalDamageShieldedOnTeammates = 100;
            leader.SoloKills = 2;
            leader.CreepScore = 210;
            leader.CreepScoreAvailable = true;
            ally.CreepScore = 140;
            ally.CreepScoreAvailable = true;
            enemy.CreepScore = 180;
            enemy.CreepScoreAvailable = true;
            enemyAlly.CreepScore = 90;
            enemyAlly.CreepScoreAvailable = true;
            var expandedTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, true, t);
            Require(expandedTags.Contains("单杀 ×2") &&
                expandedTags.Contains("全场推塔伤害第一（1,200 / 66.67%）") &&
                expandedTags.Contains("队内护盾第一（300 / 75.00%）") &&
                expandedTags.Contains("全场补刀第一（210）") &&
                expandedTags.IndexOf("单杀 ×2", StringComparison.Ordinal) <
                    expandedTags.IndexOf("全场治疗第一", StringComparison.Ordinal) &&
                expandedTags.IndexOf("全场推塔伤害第一", StringComparison.Ordinal) <
                    expandedTags.IndexOf("全场经济第一", StringComparison.Ordinal),
                "Pinned tower, shield, solo and CS tags must retain metric shares and priority.");
            enemy.TotalDamageToTowers = 1200;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                enemy, leaderboard, true, t).Contains("全场推塔伤害第一（1,200 / 80.00%）"),
                "Tied tower damage must retain all-match leaders with Cherry subgroup shares.");
            enemy.TotalDamageToTowers = 900;
            enemyAlly.TotalDamageShieldedOnTeammates = null;
            enemy.CreepScoreAvailable = false;
            leader.SoloKills = 1;
            expandedTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, true, t);
            Require(!expandedTags.Contains("全场护盾第一") &&
                expandedTags.Contains("队内护盾第一（300 / 75.00%）") &&
                !expandedTags.Contains("全场补刀第一") && !expandedTags.Contains("单杀"),
                "Sparse cross-team evidence must not imply match leaders or one-solo tags.");
            leader.TotalDamageToTowers = null;
            Require(!LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, true, t).Contains("推塔伤害第一"),
                "Missing tower damage must not be promoted to a verified zero or leader.");

            leader.Kills = 7;
            leader.Assists = 3;
            ally.Kills = 3;
            ally.Assists = 2;
            enemy.Kills = 6;
            enemy.Assists = 0;
            enemyAlly.Kills = 2;
            enemyAlly.Assists = 4;
            var killTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, true, t);
            Require(killTags.Contains("全场击杀第一（7）") &&
                killTags.Contains("全场参团率第一（100.00%）") &&
                killTags.IndexOf("全场击杀第一", StringComparison.Ordinal) <
                    killTags.IndexOf("全场参团率第一", StringComparison.Ordinal) &&
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, true, t).Contains("队内击杀第一（6）") &&
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemyAlly, leaderboard, true, t).Contains("队内参团率第一（75.00%）"),
                "Pinned kill/KP match and Cherry-team leader tags must use exact counts, rates and priority.");
            enemy.Kills = 7;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                enemy, leaderboard, true, t).Contains("全场击杀第一（7）"),
                "Tied positive kills must retain both match leaders.");
            enemy.Kills = -1;
            Require(!LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemyAlly, leaderboard, true, t).Contains("参团率第一") &&
                !LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, true, t).Contains("击杀第一"),
                "Invalid teammate kill evidence must suppress unverifiable team KP and kill claims.");
            enemy.Kills = 6;
            leader.Kills = 0;
            leader.Assists = 0;
            ally.Kills = 0;
            ally.Assists = 0;
            Require(!LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, true, t).Contains("参团率第一"),
                "Zero team kills must not fabricate a positive participation leader.");
            leader.TimeCCingOthers = 85.5;
            ally.TimeCCingOthers = 50;
            enemy.TimeCCingOthers = 70;
            enemyAlly.TimeCCingOthers = 30;
            var ccTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, true, t);
            Require(ccTags.Contains("全场控制第一（85.5）") &&
                ccTags.IndexOf("全场输出第一", StringComparison.Ordinal) <
                    ccTags.IndexOf("全场控制第一", StringComparison.Ordinal) &&
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, true, t).Contains("队内控制第一（70）"),
                "Pinned CC-time leaders must preserve fractional seconds, source priority and Cherry grouping.");
            enemy.TimeCCingOthers = 85.5;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, true, t).Contains("全场控制第一（85.5）"),
                "Tied measured control time must retain all-match leaders.");
            enemy.TimeCCingOthers = null;
            Require(!LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, true, t).Contains("全场控制第一") &&
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, true, t).Contains("队内控制第一（85.5）") &&
                !LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, true, t).Contains("控制第一"),
                "Sparse cross-team CC evidence must not fabricate match-wide or missing-self leaders.");
            enemy.TimeCCingOthers = -1;
            Require(!LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    enemy, leaderboard, true, t).Contains("控制第一"),
                "Invalid negative control time must not qualify for a leader tag.");
            leader.MaxCsAdvantageOnLaneOpponent = 40.5;
            leader.KnockEnemyIntoTeamAndKill = 6;
            var challengeTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, true, t);
            Require(challengeTags.Contains("补刀压制 ×41") &&
                challengeTags.Contains("击飞配合击杀 ×6") &&
                challengeTags.IndexOf("补刀压制 ×41", StringComparison.Ordinal) <
                    challengeTags.IndexOf("全场经济第一", StringComparison.Ordinal) &&
                challengeTags.IndexOf("全场经济第一", StringComparison.Ordinal) <
                    challengeTags.IndexOf("击飞配合击杀 ×6", StringComparison.Ordinal),
                "SGP CS advantage and knock-up tags must preserve source thresholds, rounding and priorities.");
            leader.MaxCsAdvantageOnLaneOpponent = 200;
            Require(LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, true, t).IndexOf("补刀压制 ×200", StringComparison.Ordinal) <
                LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, true, t).IndexOf("全场输出第一", StringComparison.Ordinal),
                "High CS advantage must use the pinned dynamic priority.");
            leader.MaxCsAdvantageOnLaneOpponent = 39.9;
            leader.KnockEnemyIntoTeamAndKill = 5;
            challengeTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, true, t);
            Require(!challengeTags.Contains("补刀压制") && !challengeTags.Contains("击飞配合击杀"),
                "Below-threshold SGP challenge counts must not create tags.");
            leader.MaxCsAdvantageOnLaneOpponent = double.NaN;
            leader.KnockEnemyIntoTeamAndKill = -1;
            challengeTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, true, t);
            Require(!challengeTags.Contains("补刀压制") && !challengeTags.Contains("击飞配合击杀"),
                "Invalid SGP challenge metrics must not create tags.");
            leader.KillsNearEnemyTurret = 6;
            leader.KillsUnderOwnTurret = 5;
            var towerTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, false, t, 11, 1800);
            Require(towerTags.Contains("越塔 ×6") && towerTags.Contains("塔之子 ×5") &&
                towerTags.IndexOf("越塔 ×6", StringComparison.Ordinal) <
                    towerTags.IndexOf("塔之子 ×5", StringComparison.Ordinal),
                "Rift tower-area kill tags must honor source duration ratios and priorities.");
            leader.KillsNearEnemyTurret = 5;
            leader.KillsUnderOwnTurret = 4;
            towerTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, false, t, 11, 1800);
            Require(!towerTags.Contains("越塔") && !towerTags.Contains("塔之子"),
                "Rift tower-area thresholds must not round up near-miss counts.");
            leader.KillsNearEnemyTurret = 10;
            leader.KillsUnderOwnTurret = 10;
            towerTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, false, t, 12, 900);
            Require(towerTags.Contains("越塔 ×10") && towerTags.Contains("塔之子 ×10") &&
                !LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, false, t, 21, 900).Contains("越塔") &&
                !LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                    leader, leaderboard, false, t, 12, 0).Contains("塔之子"),
                "ARAM uses 1.5-minute thresholds; unsupported maps or missing duration emit no tags.");
            leader.KillsNearEnemyTurret = 1;
            leader.KillsUnderOwnTurret = 1;
            towerTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, false, t, 11, 60);
            Require(towerTags.Contains("越塔 · 塔之子") &&
                !towerTags.Contains("越塔 ×") && !towerTags.Contains("塔之子 ×"),
                "A single tower-area kill must keep the pinned uncounted label.");
            leader.KillsNearEnemyTurret = null;
            leader.KillsUnderOwnTurret = -1;
            towerTags = LeagueEncounterMatchScoreboardDialog.FormatMatchPerformanceTags(
                leader, leaderboard, false, t, 11, 1800);
            Require(!towerTags.Contains("越塔") && !towerTags.Contains("塔之子"),
                "Missing or invalid SGP tower-area evidence must not create tags.");
            leader.KillsNearEnemyTurret = null;
            leader.KillsUnderOwnTurret = null;
            leader.MaxCsAdvantageOnLaneOpponent = null;
            leader.KnockEnemyIntoTeamAndKill = null;
            leader.PentaKills = 1;
            leader.QuadraKills = 1;
            leader.TripleKills = 1;
            leader.DoubleKills = 171;
            leader.KillsNearEnemyTurret = 1;
            leader.MaxCsAdvantageOnLaneOpponent = 91;
            var combinedTags = LeagueEncounterMatchScoreboardDialog.FormatCombinedMatchTags(
                leader, leaderboard, false, t, 11, 60);
            var doubleIndex = combinedTags.IndexOf("双杀 ×170", StringComparison.Ordinal);
            var damageIndex = combinedTags.IndexOf("全场输出第一", StringComparison.Ordinal);
            var efficiencyIndex = combinedTags.IndexOf("全场伤害经济效率第一", StringComparison.Ordinal);
            var towerIndex = combinedTags.IndexOf("越塔", StringComparison.Ordinal);
            var csAdvantageIndex = combinedTags.IndexOf("补刀压制", StringComparison.Ordinal);
            Require(combinedTags.StartsWith("对局表现：五杀 · 双杀 ×170", StringComparison.Ordinal) &&
                doubleIndex > 0 && damageIndex > doubleIndex &&
                efficiencyIndex > damageIndex && towerIndex > efficiencyIndex &&
                csAdvantageIndex > towerIndex && !combinedTags.Contains("连杀表现：") &&
                combinedTags.EndsWith("\r\n", StringComparison.Ordinal) &&
                combinedTags.IndexOf("\r\n", StringComparison.Ordinal) ==
                    combinedTags.Length - 2,
                "MatchCard must globally sort multikills and performance by pinned priority, " +
                "retain source order on ties and render one accessible tag group.");
            leader.DoubleKills = 181;
            combinedTags = LeagueEncounterMatchScoreboardDialog.FormatCombinedMatchTags(
                leader, leaderboard, false, t, 11, 60);
            Require(combinedTags.IndexOf("双杀 ×180", StringComparison.Ordinal) <
                    combinedTags.IndexOf("全场输出第一", StringComparison.Ordinal) &&
                LeagueEncounterMatchScoreboardDialog.FormatCombinedMatchTags(
                    leader, new[] { leader, new LeaguePlayerMatchParticipant() }, false, t)
                    .Contains("双杀 ×180") &&
                LeagueEncounterMatchScoreboardDialog.FormatCombinedMatchTags(
                    null, leaderboard, false, t) == string.Empty,
                "Dynamic multikill priorities must cross leader thresholds; incomplete roster " +
                "evidence must not erase valid streak tags or invent performance leaders.");
            leader.PentaKills = null;
            leader.QuadraKills = null;
            leader.TripleKills = null;
            leader.DoubleKills = null;
            leader.KillsNearEnemyTurret = null;
            leader.MaxCsAdvantageOnLaneOpponent = null;
            var visualTags = LeagueEncounterMatchScoreboardDialog.BuildCombinedMatchTagEntries(
                leader, leaderboard, false, t);
            Require(visualTags.Select(tag => tag.Value).SequenceEqual(
                    visualTags.OrderByDescending(tag => tag.Key).Select(tag => tag.Value)) &&
                LeagueMatchTagStrip.ChipLabel("全场输出第一（1,000 / 66.67%）") == "全场输出第一" &&
                LeagueMatchTagStrip.ChipLabel("五杀") == "五杀" &&
                LeagueMatchTagStrip.ChipLabel("Damage leader (1,000 / 66.67%)") == "Damage leader" &&
                LeagueMatchTagStrip.VisibleCount(170, new[] { 50, 50, 50 }) == 3 &&
                LeagueMatchTagStrip.VisibleCount(155, new[] { 50, 50, 50 }) == 2 &&
                LeagueMatchTagStrip.VisibleCount(140, new[] { 50, 50, 50 }) == 1 &&
                LeagueMatchTagStrip.VisibleCount(155, new[] { 50, 50, 50 }, 62) == 1 &&
                LeagueMatchTagStrip.VisibleCount(170, new[] { 50, 50, 50 }, 62) == 3 &&
                LeagueMatchTagStrip.VisibleCount(0, new[] { 50, 50, 50 }) == 0 &&
                LeagueMatchTagStrip.OverflowButtonWidth(29, 44) == 29 &&
                LeagueMatchTagStrip.OverflowButtonWidth(155, 62) == 62 &&
                LeagueMatchTagStrip.OverflowButtonWidth(-1, 44) == 0 &&
                LeagueMatchTagStrip.TagColor(
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderDamageMatch), t) ==
                    Color.FromArgb(153, 27, 27) &&
                LeagueMatchTagStrip.TagColor(
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillPenta), t) ==
                    Color.FromArgb(190, 18, 60) &&
                LeagueMatchTagStrip.TagColor(
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderHealTeam), t) ==
                    Color.FromArgb(5, 150, 105) &&
                LeagueMatchTagStrip.TagColor(
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderHealTeam), t, true) ==
                    Color.FromArgb(4, 120, 87) &&
                LeagueMatchTagStrip.TagColor(
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerUnder), t, true) ==
                    Color.FromArgb(87, 83, 78) &&
                LeagueMatchTagStrip.TagColor(
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderEfficiencyMatch), t, true) ==
                    Color.FromArgb(63, 98, 18),
                "Native ManyTags colors, details and reserved +N overflow must retain source order.");
            using (var tagStrip = new LeagueMatchTagStrip(t) { Size = new Size(900, 30) })
            {
                var tagDetails = new[] { "五杀", "全场输出第一（1,000 / 66.67%）", "队内治疗第一（250）" };
                tagStrip.SetTags(tagDetails);
                var focusableChips = tagStrip.Controls.OfType<Button>().ToArray();
                Require(focusableChips.Length == tagDetails.Length &&
                    focusableChips.Select(chip => chip.AccessibleDescription).SequenceEqual(tagDetails) &&
                    focusableChips.Select(chip => chip.TabIndex).SequenceEqual(new[] { 0, 1, 2 }) &&
                    focusableChips.All(chip => chip.TabStop && chip.Width > 0 &&
                        chip.Height == 20 && chip.Top == 5 && chip.Region != null &&
                        !chip.Region.IsVisible(0, 0) &&
                        chip.Region.IsVisible(chip.Width / 2, chip.Height / 2) &&
                        chip.ForeColor == Color.White &&
                        chip.FlatAppearance.MouseOverBackColor == chip.BackColor &&
                        chip.FlatAppearance.MouseDownBackColor == chip.BackColor &&
                        chip.BackColor == LeagueMatchTagStrip.TagColor(
                            chip.Text, t, !FacmThemeRuntime.Current.IsLight)) &&
                    !tagStrip.Controls.OfType<Label>().Any() &&
                    focusableChips.All(chip => chip.AutoEllipsis),
                    "Visible MatchCard chips must expose their full metric detail in keyboard tab order.");
                tagStrip.Width = 100;
                var compactChips = tagStrip.Controls.OfType<Button>().ToArray();
                Require(compactChips.Length == 1 && compactChips[0].TabStop &&
                    compactChips[0].AccessibleDescription.Contains(tagDetails[0]) &&
                    compactChips[0].Right <= tagStrip.ClientSize.Width &&
                    compactChips[0].Width > 0 && compactChips[0].Height == 20 &&
                    compactChips[0].Region != null &&
                    !compactChips[0].Region.IsVisible(0, 0) &&
                    compactChips[0].Region.IsVisible(compactChips[0].Width / 2, 10),
                    "Narrow MatchCard strips must keep a keyboard-accessible overflow control.");
                var hiddenItems = compactChips[0].ContextMenuStrip?.Items.OfType<ToolStripMenuItem>().ToArray();
                Require(hiddenItems != null && hiddenItems.Length == tagDetails.Length &&
                    hiddenItems.Select(item => item.AccessibleDescription).SequenceEqual(tagDetails) &&
                    hiddenItems.All(item => item.AutoToolTip && item.ToolTipText == item.AccessibleDescription &&
                        item.AccessibleName == LeagueMatchTagStrip.ChipLabel(item.AccessibleDescription) &&
                        item.ForeColor == Color.White),
                    "Hidden MatchCard tags must retain selectable full metric details, not just shortened labels.");
                var longMetric = "Long translated metric " + new string('X', 200) + " (1,000 / 66.67%)";
                var compactMenuText = LeagueMatchTagStrip.BoundedMenuLabel(
                    LeagueMatchTagStrip.ChipLabel(longMetric), tagStrip.Font, 280);
                Require(compactMenuText.EndsWith("…", StringComparison.Ordinal) &&
                    TextRenderer.MeasureText(compactMenuText, tagStrip.Font,
                        new Size(int.MaxValue, 25),
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width <= 280 &&
                    LeagueMatchTagStrip.BoundedMenuLabel("五杀", tagStrip.Font, 280) == "五杀" &&
                    LeagueMatchTagStrip.BoundedMenuLabel("🙂🙂🙂" + new string('X', 200),
                        tagStrip.Font, 280).EndsWith("…", StringComparison.Ordinal),
                    "Overflow menu captions must be bounded without splitting localized text elements.");
                tagStrip.SetTags(new[] { longMetric, "五杀", "队内治疗第一（250）" });
                var boundedItem = tagStrip.Controls.OfType<Button>().Single()
                    .ContextMenuStrip.Items.OfType<ToolStripMenuItem>().First();
                Require(boundedItem.Text == compactMenuText &&
                    boundedItem.AccessibleName == LeagueMatchTagStrip.ChipLabel(longMetric) &&
                    boundedItem.AccessibleDescription == longMetric &&
                    boundedItem.ToolTipText == longMetric,
                    "Clipped overflow captions must preserve the original accessible label and full metric detail.");
                tagStrip.Width = 900;
                tagStrip.SetTags(new[] { longMetric });
                var truncatedChip = tagStrip.Controls.OfType<Button>().Single();
                Require(truncatedChip.Width == 320 && truncatedChip.AutoEllipsis &&
                    truncatedChip.Text == LeagueMatchTagStrip.ChipLabel(longMetric) &&
                    truncatedChip.AccessibleDescription == longMetric,
                    "Long localized MatchCard labels must elide visually without losing full accessible metric details.");
            }
            var damagePlayer = new LeaguePlayerMatchParticipant { DamageAvailable = true,
                DamageToChampions = 800, PhysicalDamageToChampions = 500,
                MagicDamageToChampions = 200, TrueDamageToChampions = 100,
                TotalDamageTaken = 700, PhysicalDamageTaken = 400,
                MagicDamageTaken = 200, TrueDamageTaken = 100 };
            Require(LeagueEncounterMatchScoreboardDialog.FormatParticipantDamageDetails(damagePlayer, t) ==
                    "英雄输出 800（物理 500 / 魔法 200 / 真实 100）\r\n" +
                    "承受伤害 700（物理 400 / 魔法 200 / 真实 100）" &&
                LeagueEncounterMatchScoreboardDialog.FormatParticipantDamageDetails(null, t).Contains(
                    "承受伤害 —（物理 — / 魔法 — / 真实 —）"),
                "MatchCard damage details must retain distinct dealt/taken and physical/magic/true counters.");
            Require(LeagueEncounterMatchScoreboardDialog.FormatDamageTooltip(damagePlayer, false, 1000, t)
                    .Contains("英雄输出 800 · 全场峰值占比 80.00%") &&
                LeagueEncounterMatchScoreboardDialog.FormatDamageTooltip(damagePlayer, false, 1000, t)
                    .Contains("魔法 200（25%）") &&
                LeagueEncounterMatchScoreboardDialog.FormatDamageTooltip(damagePlayer, true, 1400, t)
                    .Contains("承受伤害 700 · 全场峰值占比 50.00%") &&
                LeagueEncounterMatchScoreboardDialog.FormatDamageTooltip(damagePlayer, true, 1400, t)
                    .Contains("物理 400（57%）"),
                "MatchCard damage popovers must use source separate outgoing/taken maxima and type shares.");
            Require(LeagueEncounterMatchScoreboardDialog.DamageBarWidths(100, 1000, 500, 200, 100)
                    .SequenceEqual(new[] { 50, 20, 10 }) &&
                LeagueEncounterMatchScoreboardDialog.DamageBarWidths(100, 1000, 900, 300, 100)
                    .SequenceEqual(new[] { 90, 10, 0 }) &&
                LeagueEncounterMatchScoreboardDialog.DamageBarWidths(100, 1000, null, 200, 0)
                    .SequenceEqual(new[] { 0, 20, 0 }) &&
                LeagueEncounterMatchScoreboardDialog.DamageBarWidths(100, null, 500, 200, 100)
                    .SequenceEqual(new[] { 0, 0, 0 }) &&
                LeagueEncounterMatchScoreboardDialog.DamageBarWidths(100, 0, 0, 0, 0)
                    .SequenceEqual(new[] { 0, 0, 0 }),
                "Damage bars must clip contradictory totals and keep missing baselines or parts unmeasured.");
            using (var damageBitmap = new Bitmap(220, 42))
            using (var damageGraphics = Graphics.FromImage(damageBitmap))
            {
                LeagueEncounterMatchScoreboardDialog.PaintDamageBar(damageGraphics,
                    new Rectangle(0, 0, 220, 42), damagePlayer, false, 1000, t);
                Require(damageBitmap.GetPixel(9, 32).ToArgb() == Color.FromArgb(224, 120, 86).ToArgb() &&
                    damageBitmap.GetPixel(115, 32).ToArgb() == Color.FromArgb(91, 159, 215).ToArgb() &&
                    damageBitmap.GetPixel(158, 32).ToArgb() == Color.FromArgb(168, 168, 168).ToArgb(),
                    "The native MatchCard painter must render the source physical/magic/true colors in descending widths.");
            }
            damagePlayer.DamageAvailable = false;
            damagePlayer.MagicDamageTaken = 0;
            damagePlayer.TrueDamageTaken = null;
            Require(LeagueEncounterMatchScoreboardDialog.FormatParticipantDamageDetails(damagePlayer, t)
                    .Contains("英雄输出 —（物理 500 / 魔法 200 / 真实 100）") &&
                LeagueEncounterMatchScoreboardDialog.FormatParticipantDamageDetails(damagePlayer, t)
                    .Contains("承受伤害 700（物理 400 / 魔法 0 / 真实 —）"),
                "Partial damage records must expose available components without fabricating unknown totals.");
            Require(LeagueEncounterMatchScoreboardDialog.FormatDamageTooltip(damagePlayer, false, 1000, t)
                    .Contains("英雄输出 — · 全场峰值占比 —") &&
                LeagueEncounterMatchScoreboardDialog.FormatDamageTooltip(damagePlayer, false, 1000, t)
                    .Contains("物理 500（—）") &&
                LeagueEncounterMatchScoreboardDialog.FormatDamageTooltip(damagePlayer, true, 0, t)
                    .Contains("魔法 0（0%）"),
                "Absent totals must not become shares, while explicit source zero remains measurable.");
            var radarSelf = new LeaguePlayerMatchParticipant { ParticipantId = 1, TeamId = 100,
                DamageAvailable = true, DamageToChampions = 100, TotalDamageTaken = 120,
                GoldAvailable = true, GoldEarned = 1000, CreepScoreAvailable = true, CreepScore = 50,
                Kills = 4, Deaths = 2, Assists = 6, TotalHeal = 80 };
            var radarAlly = new LeaguePlayerMatchParticipant { ParticipantId = 2, TeamId = 100,
                DamageAvailable = true, DamageToChampions = 300, TotalDamageTaken = 180,
                GoldAvailable = true, GoldEarned = 3000, CreepScoreAvailable = true, CreepScore = 30,
                Kills = 5, Deaths = 1, Assists = 5, TotalHeal = 0 };
            var radarEnemy = new LeaguePlayerMatchParticipant { ParticipantId = 3, TeamId = 200,
                DamageAvailable = true, DamageToChampions = 400, TotalDamageTaken = 400,
                GoldAvailable = true, GoldEarned = 4000, CreepScoreAvailable = true, CreepScore = 100,
                Kills = 1, Deaths = 3, Assists = 1, TotalHeal = 160 };
            var radarPlayers = new[] { radarSelf, radarAlly, radarEnemy };
            var radarTeams = new[] { new LeagueMatchTeamScore { TeamId = 100, Kills = 20 },
                new LeagueMatchTeamScore { TeamId = 200, Kills = 10 } };
            var radar = LeagueMatchRadarPanel.Project(radarPlayers, radarTeams, false, radarSelf);
            Require(radar != null && radar.PlayerRatio[0] == 0.25 && radar.TeamAverageRatio[0] == 0.5 &&
                radar.PlayerRatio[4] == 0.5 && radar.TeamAverageRatio[4] == 0.75 &&
                radar.PlayerRatio[5] == 1 && radar.TeamAverageRatio[5] == 1 &&
                radar.PlayerRatio[6] == 0.5 && radar.TeamAverageRatio[6] == 0.25 &&
                LeagueMatchRadarPanel.FormatDetails(radar, t).Contains("输出：100（25.0%） / 200（50.0%）") &&
                LeagueMatchRadarPanel.FormatDetails(null, t).Contains("选择一名玩家"),
                "Akari's seven-axis radar must normalize player and complete team averages to each game maximum.");
            using (var radarBitmap = new Bitmap(420, 300))
            using (var radarGraphics = Graphics.FromImage(radarBitmap))
                LeagueMatchRadarPanel.PaintRadar(radarGraphics, new Rectangle(0, 0, 420, 300), radar, t);
            var radarBounds = new Rectangle(0, 0, 420, 300);
            Require(LeagueMatchRadarPanel.HitTest(radarBounds, radar, new Point(210, 133), t) ==
                    "输出 · 玩家：25.0%" &&
                LeagueMatchRadarPanel.HitTest(radarBounds, radar, new Point(210, 106), t) ==
                    "输出 · 同队平均：50.0%" &&
                LeagueMatchRadarPanel.HitTest(radarBounds, radar, new Point(5, 5), t) == null &&
                LeagueMatchRadarPanel.HitTest(new Rectangle(0, 0, 120, 80), radar,
                    new Point(60, 40), t) == null &&
                LeagueMatchRadarPanel.HitTest(radarBounds, null, new Point(210, 133), t) == null,
                "Radar hover must report the pinned one-decimal normalized series, not empty chart space.");
            radarAlly.TotalHeal = null;
            radar = LeagueMatchRadarPanel.Project(radarPlayers, radarTeams, false, radarSelf);
            Require(radar.PlayerRatio[6] == 0.5 && !radar.TeamAverageRatio[6].HasValue &&
                LeagueMatchRadarPanel.FormatDetails(radar, t).Contains("治疗：80（50.0%） / —（—）"),
                "Incomplete team healing must not be plotted as a known zero or misleading team mean.");
            var healPoint = new Point(210 + (int)Math.Round(Math.Sin(6 * 2 * Math.PI / 7) * 53),
                159 - (int)Math.Round(Math.Cos(6 * 2 * Math.PI / 7) * 53));
            Require(LeagueMatchRadarPanel.HitTest(radarBounds, radar, healPoint, t) ==
                    "治疗 · 玩家：50.0%",
                "Absent teammate healing must never generate a hover tooltip for the team series.");
            radarSelf.PlayerSubteamId = 2;
            radarAlly.PlayerSubteamId = 1;
            radarEnemy.PlayerSubteamId = 3;
            radar = LeagueMatchRadarPanel.Project(radarPlayers, new[] {
                new LeagueMatchTeamScore { TeamId = 1, Kills = 10 },
                new LeagueMatchTeamScore { TeamId = 2, Kills = 10 },
                new LeagueMatchTeamScore { TeamId = 3, Kills = 10 }
            }, true, radarSelf);
            Require(radar.TeamAverageRatio[0] == 0.25 && radar.PlayerRatio[5] == 1 &&
                radar.TeamAverageRatio[6] == 0.5,
                "Cherry radar must compare only verified subteam members, never an ordinary shared teamId.");
            radarSelf.PlayerSubteamId = null;
            radar = LeagueMatchRadarPanel.Project(radarPlayers, radarTeams, true, radarSelf);
            Require(!radar.TeamAverageRatio[0].HasValue && !radar.PlayerRatio[5].HasValue &&
                radar.PlayerRatio[0] == 0.25,
                "An unknown Cherry subteam must not inherit ordinary-team averages or kill participation.");
            damagePlayer.TotalDamageTaken = null;
            damagePlayer.MagicDamageTaken = -1;
            Require(LeagueEncounterMatchScoreboardDialog.FormatParticipantDamageDetails(damagePlayer, t)
                    .Contains("承受伤害 —（物理 400 / 魔法 — / 真实 —）"),
                "Missing totals and malformed negative damage components must stay unknown.");
            var catalog = new LeagueBuildAdvisorCatalog();
            LeagueBuildAdvisorDataService.ParseLocalPerkStyles(Encoding.UTF8.GetBytes(
                "{\"styles\":[{\"id\":8000,\"name\":\"Precision\"},{\"id\":8300,\"name\":\"Inspiration\"}]}"), catalog);
            LeagueBuildAdvisorDataService.ParseLocalAugments(Encoding.UTF8.GetBytes("[" +
                "{\"id\":1234,\"nameTRA\":\"Prismatic Fixture\",\"rarity\":\"kPrismatic\"," +
                "\"augmentSmallIconPath\":\"/lol-game-data/assets/v1/augment/1234.png\"}," +
                "{\"id\":5678,\"nameTRA\":\"Unsafe Fixture\",\"rarity\":\"kGold\"," +
                "\"augmentSmallIconPath\":\"/lol-game-data/assets/../client-config\"}]"), catalog);
            var clone = LeagueBuildAdvisorDataService.CloneLocalCatalog(catalog);
            Require(clone.PerkStyles[8000] == "Precision" && clone.Augments[1234] == "Prismatic Fixture" &&
                clone.AugmentIcons[1234] == "lcu:/lol-game-data/assets/v1/augment/1234.png" &&
                !clone.AugmentIcons.ContainsKey(5678) && clone.AugmentRarities[5678] == "kGold" &&
                LeagueEncounterMatchScoreboardDialog.FormatAugments(new[] { 1234, 5678, 9999, 0 }, clone) ==
                    "棱彩 · Prismatic Fixture / 黄金 · Unsafe Fixture / #9999" &&
                LeagueEncounterMatchScoreboardDialog.FormatAugments(new[] { 0 }, clone) == "—" &&
                LeagueEncounterMatchScoreboardDialog.FormatAugments(null, clone) == "—" &&
                LeagueEncounterMatchScoreboardDialog.FormatPerkGroupHeading("主系符文", 8000, clone.PerkStyles) ==
                    "主系符文 · Precision" &&
                LeagueEncounterMatchScoreboardDialog.FormatPerkGroupHeading("副系符文", 9999, clone.PerkStyles) ==
                    "副系符文 · #9999",
                "Match card must render named/ranked augments and distinct rune trees without unsafe icon paths.");
            var runes = LeagueBuildAdvisorDataService.ParseLocalCatalog(null, null, null,
                Encoding.UTF8.GetBytes("[{\"id\":8005,\"name\":\"Press the Attack\"," +
                    "\"endOfGameStatDescs\":[\"<b>Damage</b> &amp; hits: @eogvar1@\"," +
                    "\"Difference: @eogvar2@, count: @eogvar3@\"]}," +
                    "{\"id\":9111,\"name\":\"Triumph\",\"endOfGameStatDescs\":[\"Recovered @eogvar1@\"]}]"));
            LeagueBuildAdvisorDataService.ParseLocalPerkStyles(Encoding.UTF8.GetBytes(
                "{\"styles\":[{\"id\":8000,\"name\":\"Precision\"},{\"id\":8300,\"name\":\"Inspiration\"}]}"), runes);
            var withValues = new LeaguePlayerMatchParticipant {
                PlayerName = "Fixture Player", PrimaryPerkStyleId = 8000, SecondaryPerkStyleId = 8300,
                PerkIds = new[] { 8005, 9111, 0, 0, 0, 0 },
                PerkEndOfGameValues = new[] {
                    new double?[] { 777, -2.5, 0 }, new double?[] { null, null, null },
                    new double?[3], new double?[3], new double?[3], new double?[3]
                },
                StatPerkIds = new int?[] { 5008, null, 0 }
            };
            var descriptions = LeagueEncounterMatchScoreboardDialog.FormatParticipantRuneDetails(
                withValues, runes, key => LeagueEfficiencyText.Get(null, key));
            var runeCopy = LeagueBuildAdvisorDataService.CloneLocalCatalog(runes);
            runeCopy.PerkEndOfGameDescriptions[8005][0] = "mutated";
            Require(runes.PerkEndOfGameDescriptions[8005][0].Contains("<b>") &&
                descriptions.Contains("Damage & hits: 777") &&
                descriptions.Contains("Difference: -2.5, count: 0") &&
                descriptions.Contains("Recovered —") && descriptions.Contains("属性碎片：#5008 / — / —") &&
                !descriptions.Contains("<b>") && !descriptions.Contains("@eogvar") &&
                descriptions.Contains("主系符文 · Precision") &&
                LeagueEncounterMatchScoreboardDialog.FormatParticipantRuneDetails(
                    null, runes, key => LeagueEfficiencyText.Get(null, key)).Contains("选择一名玩家"),
                "Rune card must preserve signed/zero variables, unknown placeholders and detached perk descriptions.");
            var regional = LeaguePlayerDataService.NormalizeSgpGame(new Dictionary<string, object> {
                { "json", new Dictionary<string, object> {
                    { "gameId", 44L }, { "participants", new object[] {
                        new Dictionary<string, object> {
                            { "participantId", 1 }, { "puuid", "fixture" },
                            { "doubleKills", 3 }, { "tripleKills", 1 },
                            { "quadraKills", 0 }, { "pentaKills", 0 },
                            { "magicalDamageTaken", 321 }, { "magicDamageTaken", 999 },
                            { "challenges", new Dictionary<string, object> {
                                { "maxCsAdvantageOnLaneOpponent", 40.5 },
                                { "knockEnemyIntoTeamAndKill", 6 },
                                { "killsNearEnemyTurret", 6 },
                                { "killsUnderOwnTurret", 5 } } },
                            { "perks", new Dictionary<string, object> {
                                { "statPerks", new Dictionary<string, object> {
                                    { "offense", 5008 }, { "flex", 5002 } } },
                                { "styles", new object[] { new Dictionary<string, object> {
                                    { "description", "primaryStyle" }, { "style", 8000 },
                                    { "selections", new object[] { new Dictionary<string, object> {
                                    { "perk", 8005 }, { "var1", 777 }, { "var2", -2.5 }, { "var3", 0 }
                                    } } }
                                } } }
                            } }
                        }
                    } }
                } }
            });
            var participant = ((List<Dictionary<string, object>>)regional["participants"])[0];
            var stats = (Dictionary<string, object>)participant["stats"];
            Require(stats["perk0Var1"].ToString() == "777" &&
                stats["perk0Var2"].ToString() == "-2.5" && stats["perk0Var3"].ToString() == "0" &&
                stats["magicDamageTaken"].ToString() == "321" &&
                stats["doubleKills"].ToString() == "3" &&
                stats["tripleKills"].ToString() == "1" &&
                ((Dictionary<string, object>)stats["challenges"])["maxCsAdvantageOnLaneOpponent"]
                    .ToString() == "40.5" &&
                ((Dictionary<string, object>)stats["challenges"])["knockEnemyIntoTeamAndKill"]
                    .ToString() == "6" &&
                ((Dictionary<string, object>)stats["challenges"])["killsNearEnemyTurret"]
                    .ToString() == "6" &&
                ((Dictionary<string, object>)stats["challenges"])["killsUnderOwnTurret"]
                    .ToString() == "5" &&
                stats["perkStatOffense"].ToString() == "5008" &&
                stats["perkStatFlex"].ToString() == "5002" && !stats.ContainsKey("perkStatDefense"),
                "SGP normalization must retain raw rune selection variables and nullable stat shards.");
        }

        private static async Task ValidateMatchDetailAsync()
        {
            var session = new LeagueClientSession("LeagueClientUx", 55, 2999, "test", "https", "fixture", "HN1", "HN1");
            LeagueClientSession active = session;
            var api = new MatchDetailClient();
            var reader = new LeagueEncounterMatchDetailService(api, new PerformanceBudgetProvider(), () => active);
            var record = new LeagueEncounterRecord { GameId = 123, SelfPuuid = "self", Puuid = "enemy",
                Region = "HN1", PlatformId = "HN1", QueueType = "ARAM" };
            using (var token = new CancellationTokenSource())
            {
                active = null;
                Require((await reader.ReadAsync(record, token.Token)).Status == LeagueEncounterMatchDetailStatus.NoClient &&
                    api.Reads == 0, "Offline history details must not read LCU without a client.");
                active = session;
                record.Region = "HN2";
                Require((await reader.ReadAsync(record, token.Token)).Status == LeagueEncounterMatchDetailStatus.WrongAccount &&
                    api.Reads == 0, "A different region must be rejected before any LCU request.");
                record.Region = "HN1";
                api.SelfPuuid = "other";
                Require((await reader.ReadAsync(record, token.Token)).Status == LeagueEncounterMatchDetailStatus.WrongAccount &&
                    api.Reads == 1, "A different signed-in account must not fetch historical details.");
                api.SelfPuuid = "self";
                var result = await reader.ReadAsync(record, token.Token);
                Require(result.Status == LeagueEncounterMatchDetailStatus.Ready && result.Match != null &&
                    result.Match.GameId == 123 && result.Match.ParticipantResolved && result.Match.Win &&
                    result.Match.Kills == 5 && result.Match.Deaths == 2 && result.Match.Assists == 9 &&
                    result.Match.CreepScore == 103 && result.Participants != null &&
                    result.Participants.Count == 3 && result.Participants.Single(p => p.IsTarget).PlayerName == "Target#HN1" &&
                    result.Participants.Single(p => p.IsTarget).CreepScoreAvailable &&
                    !result.Participants.Single(p => p.PuuId == "ally").CreepScoreAvailable &&
                    !result.Participants.Single(p => p.PuuId == "ally").DamageAvailable &&
                    !result.Participants.Single(p => p.PuuId == "ally").GoldAvailable &&
                    result.Participants.Single(p => p.IsTarget).DamageToChampions == 18000 &&
                    result.Participants.Single(p => p.IsTarget).PhysicalDamageToChampions == 9000 &&
                    result.Participants.Single(p => p.IsTarget).MagicDamageToChampions == 7000 &&
                    result.Participants.Single(p => p.IsTarget).TrueDamageToChampions == 2000 &&
                    result.Participants.Single(p => p.IsTarget).TotalDamageTaken == 5000 &&
                    result.Participants.Single(p => p.IsTarget).PhysicalDamageTaken == 3000 &&
                    result.Participants.Single(p => p.IsTarget).MagicDamageTaken == 1500 &&
                    result.Participants.Single(p => p.IsTarget).TrueDamageTaken == 500 &&
                    result.Participants.Single(p => p.IsTarget).TotalHeal == 2400 &&
                    result.Participants.Single(p => p.IsTarget).DoubleKills == 6 &&
                    result.Participants.Single(p => p.IsTarget).TripleKills == 3 &&
                    result.Participants.Single(p => p.IsTarget).QuadraKills == 2 &&
                    result.Participants.Single(p => p.IsTarget).PentaKills == 1 &&
                    !result.Participants.Single(p => p.PuuId == "ally").DoubleKills.HasValue &&
                    !result.Participants.Single(p => p.PuuId == "ally").TotalHeal.HasValue &&
                    result.Participants.Single(p => p.IsTarget).ChampionName == "Ashe" &&
                    result.Match.ChampionName == "Ashe" &&
                    result.Participants.Single(p => p.IsTarget).ItemIds.SequenceEqual(new[] { 3006, 3031, 0, 0, 0, 0, 0 }) &&
                    result.Participants.Single(p => p.IsTarget).RoleBoundItemId == 3158 &&
                    result.Match.RoleBoundItemId == 3158 &&
                    result.Participants.Single(p => p.IsTarget).SpellIds.SequenceEqual(new[] { 4, 7 }) &&
                    result.Participants.Single(p => p.IsTarget).PerkIds.SequenceEqual(new[] { 8005, 9111, 9104, 8014, 8345, 8347 }) &&
                    result.Participants.Single(p => p.IsTarget).PerkEndOfGameValues[0][0] == 777 &&
                    result.Participants.Single(p => p.IsTarget).PerkEndOfGameValues[0][1] == -2.5 &&
                    result.Participants.Single(p => p.IsTarget).PerkEndOfGameValues[0][2] == 0 &&
                    !result.Participants.Single(p => p.IsTarget).PerkEndOfGameValues[1][0].HasValue &&
                    result.Participants.Single(p => p.IsTarget).StatPerkIds == null &&
                    result.Participants.Single(p => p.IsTarget).PrimaryPerkStyleId == 8000 &&
                    result.Participants.Single(p => p.IsTarget).SecondaryPerkStyleId == 8300 &&
                    result.Participants.Single(p => p.IsTarget).AugmentIds[0] == 1234 &&
                    result.Participants.Single(p => p.PuuId == "ally").TeamId == 100 &&
                    result.Participants.Single(p => p.PuuId == "self").TeamId == 200 &&
                    result.BuildCatalog != null && result.BuildCatalog.Items[3006] == "Berserker Greaves" &&
                    result.BuildCatalog.Spells[4] == "Flash" &&
                    result.BuildCatalog.Perks[8005] == "Press the Attack" &&
                    result.BuildCatalog.ItemIcons[3006].StartsWith("lcu:", StringComparison.Ordinal) &&
                    result.BuildCatalog.Augments[1234] == "Fixture Augment" &&
                    result.BuildCatalog.AugmentRarities[1234] == "kPrismatic" &&
                    result.BuildCatalog.AugmentIcons[1234].StartsWith("lcu:", StringComparison.Ordinal) &&
                    result.BuildCatalog.PerkStyles[8000] == "Precision" &&
                    api.Reads == 10 && api.ChampionReads == 1 && api.BuildCatalogReads == 5,
                    "Explicit LCU detail must resolve the recorded target participant, not the signed-in player.");
                Require(LeagueEncounterMatchScoreboardDialog.FormatBoundItem(3158,
                            new Dictionary<int, string> { { 3158, "Special Item" } }) == "Special Item" &&
                        LeagueEncounterMatchScoreboardDialog.FormatBoundItem(3158, null) == "#3158" &&
                        LeagueEncounterMatchScoreboardDialog.FormatBoundItem(0, null) == "—" &&
                        LeagueEncounterMatchScoreboardDialog.FormatBoundItem(null, null) == "—",
                    "Role-bound items must have a named/ID fallback and distinguish missing or empty equipment.");
                Require(result.IsCurrentSession != null && result.IsCurrentSession(),
                    "Selected-event actions must retain the detail request's originating session.");
                active = new LeagueClientSession("LeagueClientUx", 56, 3000, "other", "https", "fixture", "HN1", "HN1");
                Require(!result.IsCurrentSession(), "A replacement client must invalidate deferred event assets and actions.");
                active = session;
                var cachedDetail = await reader.ReadAsync(record, token.Token);
                Require(cachedDetail.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    cachedDetail.BuildCatalog != null && api.Reads == 13 && api.BuildCatalogReads == 5 &&
                    api.ChampionReads == 1,
                    "Repeated explicit matches must reuse the cached client game-data catalog, not refetch every icon catalog.");
                Require(result.Teams != null && result.Teams.Count == 2 &&
                    result.Teams[0].TeamId == 100 && result.Teams[0].PlayerCount == 2 &&
                    result.Teams[0].Win == true && result.Teams[0].Kills == 8 &&
                    result.Teams[0].Deaths == 6 && result.Teams[0].Assists == 15 &&
                    result.Teams[0].DamageToChampions == null && result.Teams[0].GoldEarned == null &&
                    result.Teams[0].BaronKills == 1 && result.Teams[0].DragonKills == 2 &&
                    result.Teams[0].TowerKills == 6 && result.Teams[0].HordeKills == 3 &&
                    result.Teams[0].RiftHeraldKills == 0 && result.Teams[0].InhibitorKills == 2 &&
                    result.Teams[1].TeamId == 200 && result.Teams[1].PlayerCount == 1 &&
                    result.Teams[1].Win == false && result.Teams[1].BaronKills == 0 &&
                    result.Teams[1].HordeKills == null,
                    "Team summaries must aggregate verified participants while preserving objective zero versus absent.");
                Require(result.Teams[0].FirstBaron == true && result.Teams[0].FirstDragon == true &&
                    result.Teams[0].FirstTower == true && result.Teams[0].FirstInhibitor == false &&
                    result.Teams[0].Bans != null && result.Teams[0].Bans.Count == 2 &&
                    result.Teams[0].Bans[0].ChampionId == 22 && result.Teams[0].Bans[0].PickTurn == 1 &&
                    result.Teams[0].Bans[1].ChampionId == 99 && result.Teams[0].Bans[1].PickTurn == 3 &&
                    result.ChampionNames != null && result.ChampionNames[22] == "Ashe" &&
                    result.Teams[1].FirstDragon == false && result.Teams[1].Bans != null &&
                    result.Teams[1].Bans.Count == 0,
                    "Team bans and first-objective flags must use the same verified game and cached hero names.");
                Func<string, string> t = key => LeagueEfficiencyText.Get(null, key);
                var teamDetails = LeagueEncounterMatchScoreboardDialog.FormatTeamDetails(
                    result.Teams[0], result.ChampionNames, t);
                Require(teamDetails.Contains("Ashe (#22)") && teamDetails.Contains("顺位 1") &&
                    teamDetails.Contains("首大龙 是") && teamDetails.Contains("首水晶 否") &&
                    LeagueEncounterMatchScoreboardDialog.FormatTeamDetails(
                        result.Teams[0], null, t).Contains("#22") &&
                    LeagueEncounterMatchScoreboardDialog.FormatTeamDetails(
                        result.Teams[1], null, t).Contains("禁用英雄：无"),
                    "Team detail formatting must show localized bans and first-objective ownership.");
                api.ReturnZeroTeamMetrics = true;
                var completeMetrics = await reader.ReadAsync(record, token.Token);
                Require(completeMetrics.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    completeMetrics.Teams[0].DamageToChampions == 18000 &&
                    completeMetrics.Teams[0].GoldEarned == 12000,
                    "A reported zero for a member must allow a complete measured team total.");
                api.ReturnZeroTeamMetrics = false;
                api.ReturnInvalidTeamMetrics = true;
                var invalidMetrics = await reader.ReadAsync(record, token.Token);
                Require(invalidMetrics.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    invalidMetrics.Teams[0].DamageToChampions == null &&
                    invalidMetrics.Teams[0].GoldEarned == null,
                    "Null or invalid member metrics must not masquerade as measured zeroes.");
                api.ReturnInvalidTeamMetrics = false;
                api.ReturnMissingTeams = true;
                var missingTeams = await reader.ReadAsync(record, token.Token);
                Require(missingTeams.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    missingTeams.Teams.Count == 2 && missingTeams.Teams[0].Kills == 8 &&
                    missingTeams.Teams[0].BaronKills == null && missingTeams.Teams[1].DragonKills == null &&
                    missingTeams.Teams[0].Bans == null && missingTeams.Teams[0].FirstBaron == null &&
                    LeagueEncounterMatchScoreboardDialog.FormatTeamDetails(
                        missingTeams.Teams[0], null, t).Contains("禁用英雄：未知"),
                    "Missing team metadata must not discard real participant scores or invent objective zeroes.");
                api.ReturnMissingTeams = false;
                api.ReturnDuplicateTeams = true;
                var duplicateTeams = await reader.ReadAsync(record, token.Token);
                Require(duplicateTeams.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    duplicateTeams.Teams[0].Kills == 8 && duplicateTeams.Teams[0].BaronKills == null &&
                    duplicateTeams.Teams[0].FirstBaron == null && duplicateTeams.Teams[0].Bans == null &&
                    duplicateTeams.Teams[1].DragonKills == 1,
                    "Ambiguous team metadata must not attach objectives to the wrong team.");
                api.ReturnDuplicateTeams = false;
                api.ReturnMalformedFirst = true;
                var malformedFirst = await reader.ReadAsync(record, token.Token);
                Require(malformedFirst.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    malformedFirst.Teams[0].FirstBaron == null && malformedFirst.Teams[0].FirstDragon == true,
                    "An invalid first-objective flag must not be converted to a false claim.");
                api.ReturnMalformedFirst = false;
                api.ReturnMalformedBans = true;
                var malformedBans = await reader.ReadAsync(record, token.Token);
                Require(malformedBans.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    malformedBans.Teams[1].Bans == null &&
                    LeagueEncounterMatchScoreboardDialog.FormatTeamDetails(
                        malformedBans.Teams[1], null, t).Contains("禁用英雄：未知"),
                    "Malformed ban lists must stay unknown rather than falsely reporting no bans.");
                api.ReturnMalformedBans = false;
                api.ReturnConflictingWin = true;
                var conflictingWin = await reader.ReadAsync(record, token.Token);
                Require(conflictingWin.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    conflictingWin.Teams[0].Win == null && conflictingWin.Teams[0].Kills == 8,
                    "Conflicting participant results must show unknown team outcome, not a false win.");
                api.ReturnConflictingWin = false;
                api.ReturnCherry = true;
                var cherry = await reader.ReadAsync(record, token.Token);
                Require(cherry.Status == LeagueEncounterMatchDetailStatus.Ready && cherry.Teams.Count == 2 &&
                    cherry.Match.GameMode == "CHERRY" && cherry.Teams[0].IsCherry &&
                    cherry.Teams[0].TeamId == 1 && cherry.Teams[0].SubteamPlacement == 1 &&
                    cherry.Teams[0].PlayerCount == 1 && cherry.Teams[0].Kills == 3 &&
                    cherry.Teams[1].TeamId == 2 && cherry.Teams[1].SubteamPlacement == 2 &&
                    cherry.Teams[1].PlayerCount == 2 && cherry.Teams[1].Kills == 7 &&
                    cherry.Teams.All(team => team.BaronKills == null && team.Bans == null) &&
                    cherry.Participants.Single(player => player.IsTarget).PlayerSubteamId == 2 &&
                    cherry.Match.PlayerSubteamId == 2 && cherry.Match.SubteamPlacement == 2,
                    "Cherry participants must group by the source subteam ID, not conventional red/blue team IDs.");
                Require(LeagueEncounterMatchScoreboardDialog.FormatTeamDetails(cherry.Teams[0], null, t)
                        .Contains("斗魂队伍 1 · 第1名") &&
                    !LeagueEncounterMatchScoreboardDialog.FormatTeamDetails(cherry.Teams[0], null, t)
                        .Contains("大龙") &&
                    LeagueCherryTeams.GroupId(cherry.Participants.Single(player => player.IsTarget), true) == 2 &&
                    LeagueCherryTeams.GroupId(cherry.Participants.Single(player => player.IsTarget), false) == 100,
                    "Cherry scoreboard and timeline team comparisons must use the same verified group keys.");
                api.ReturnCherryConflict = true;
                var ambiguousCherry = await reader.ReadAsync(record, token.Token);
                Require(ambiguousCherry.Teams.Count == 2 && ambiguousCherry.Teams[1].SubteamPlacement == null &&
                    LeagueEncounterMatchScoreboardDialog.FormatTeamDetails(ambiguousCherry.Teams[1], null, t)
                        .Contains("未知"),
                    "Conflicting subgroup placements must not display one participant's rank as the team's rank.");
                api.ReturnCherryConflict = false;
                api.ReturnCherryUnknown = true;
                var unknownCherry = await reader.ReadAsync(record, token.Token);
                Require(unknownCherry.Teams.Count == 1 && unknownCherry.Teams[0].TeamId == 1 &&
                    unknownCherry.Participants.Single(player => player.IsTarget).PlayerSubteamId == null &&
                    LeagueCherryTeams.GroupId(unknownCherry.Participants.Single(player => player.IsTarget), true) == 0 &&
                    LeagueEncounterMatchScoreboardDialog.FormatCherryPlacement(null, t) == "未知",
                    "Missing/invalid subteam identity must not be converted to red/blue ownership or first place.");
                api.ReturnCherryUnknown = false;
                api.ReturnCherry = false;
                var sgpCherry = LeaguePlayerDataService.NormalizeSgpGame(new Dictionary<string, object>
                {
                    { "json", new Dictionary<string, object> {
                        { "gameId", 123L }, { "gameMode", "CHERRY" },
                        { "participants", new object[] {
                            new Dictionary<string, object> { { "participantId", 1 }, { "puuid", "s1" },
                                { "playerSubteamId", 3 }, { "subteamPlacement", 1 } },
                            new Dictionary<string, object> { { "participantId", 2 }, { "puuid", "s2" },
                                { "playerSubteamId", 4 }, { "subteamPlacement", 2 } }
                        } }
                    } }
                });
                Require(sgpCherry != null &&
                    ((Dictionary<string, object>)((List<Dictionary<string, object>>)sgpCherry["participants"])[0]["stats"])
                        ["playerSubteamId"].ToString() == "3" &&
                    ((Dictionary<string, object>)((List<Dictionary<string, object>>)sgpCherry["participants"])[1]["stats"])
                        ["subteamPlacement"].ToString() == "2",
                    "Normalized regional SGP rows must preserve Cherry subgroup and placement fields.");
                var busyBudget = new PerformanceBudgetProvider();
                busyBudget.UpdateLeagueActivity(LeagueActivityLevel.InGame);
                var busyApi = new MatchDetailClient();
                var busyReader = new LeagueEncounterMatchDetailService(busyApi, busyBudget, () => session);
                var busyResult = await busyReader.ReadAsync(record, token.Token);
                Require(busyResult.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    busyResult.Participants.Single(p => p.IsTarget).ChampionId == 22 &&
                    busyApi.ChampionReads == 0 && busyApi.BuildCatalogReads == 0,
                    "In-game budgets must not request extra game-data metadata for historical breakdowns.");
                var upgradeApi = new MatchDetailClient { ReturnNoAugments = true };
                var upgradeReader = new LeagueEncounterMatchDetailService(upgradeApi,
                    new PerformanceBudgetProvider(), () => session);
                var noAugments = await upgradeReader.ReadAsync(record, token.Token);
                Require(noAugments.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    upgradeApi.BuildCatalogReads == 4 && noAugments.BuildCatalog.Augments.Count == 0,
                    "Ordinary matches must not load Cherry augmentation metadata when no player has augments.");
                upgradeApi.ReturnNoAugments = false;
                var withAugments = await upgradeReader.ReadAsync(record, token.Token);
                Require(withAugments.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    upgradeApi.BuildCatalogReads == 5 && withAugments.BuildCatalog.Augments[1234] == "Fixture Augment" &&
                    noAugments.BuildCatalog.Augments.Count == 0,
                    "An augmented match must upgrade the cached catalog with one optional LCU request without mutating old cards.");
                await upgradeReader.ReadAsync(record, token.Token);
                Require(upgradeApi.BuildCatalogReads == 5,
                    "Repeated augmented matches must reuse enriched game-data metadata.");
                var switchedApi = new MatchDetailClient { AfterChampionRead = () => { active = new LeagueClientSession(
                    "LeagueClientUx", 55, 3001, "replacement", "https", "fixture", "HN1", "HN1"); } };
                var switchedReader = new LeagueEncounterMatchDetailService(switchedApi,
                    new PerformanceBudgetProvider(), () => active);
                Require((await switchedReader.ReadAsync(record, token.Token)).Status == LeagueEncounterMatchDetailStatus.NoClient,
                    "A client replacement while loading optional hero names must discard the full scoreboard.");
                active = session;
                api.ReturnMalformedRoster = true;
                Require((await reader.ReadAsync(record, token.Token)).Status == LeagueEncounterMatchDetailStatus.Unavailable,
                    "Duplicate participant IDs must not produce a partial or misleading scoreboard.");
                api.ReturnMalformedRoster = false;
                api.AfterMatchRead = () => api.SelfPuuid = "other";
                Require((await reader.ReadAsync(record, token.Token)).Status == LeagueEncounterMatchDetailStatus.WrongAccount,
                    "A same-session account switch after the game read must discard the old owner's match detail.");
                api.AfterMatchRead = null;
                api.SelfPuuid = "self";
                api.ReturnMissingParticipant = true;
                Require((await reader.ReadAsync(record, token.Token)).Status == LeagueEncounterMatchDetailStatus.Unavailable,
                    "Incomplete match participants must not be displayed as zero statistics.");
                api.ReturnMissingParticipant = false;
                api.AfterProfileRead = () => active = new LeagueClientSession("LeagueClientUx", 55, 3000,
                    "replacement", "https", "fixture", "HN1", "HN1");
                Require((await reader.ReadAsync(record, token.Token)).Status == LeagueEncounterMatchDetailStatus.NoClient,
                    "Changed client sessions must invalidate in-flight detail requests.");
            }
        }

        private sealed class MatchDetailClient : ILeagueClientApi
        {
            internal int Reads;
            internal int ChampionReads;
            internal int BuildCatalogReads;
            internal string SelfPuuid = "self";
            internal bool ReturnMissingParticipant;
            internal bool ReturnMalformedRoster;
            internal bool ReturnMissingTeams;
            internal bool ReturnDuplicateTeams;
            internal bool ReturnConflictingWin;
            internal bool ReturnZeroTeamMetrics;
            internal bool ReturnInvalidTeamMetrics;
            internal bool ReturnMalformedFirst;
            internal bool ReturnMalformedBans;
            internal bool ReturnCherry;
            internal bool ReturnCherryUnknown;
            internal bool ReturnCherryConflict;
            internal bool ReturnNoAugments;
            internal Action AfterProfileRead;
            internal Action AfterMatchRead;
            internal Action AfterChampionRead;

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Reads++;
                if (path == LeagueDashboardDetailsService.SummonerPath)
                {
                    AfterProfileRead?.Invoke();
                    return Task.FromResult(Encoding.UTF8.GetBytes("{\"puuid\":\"" + SelfPuuid + "\"}"));
                }
                if (path == LeaguePlayerDataService.ChampionSummaryPath)
                {
                    ChampionReads++;
                    AfterChampionRead?.Invoke();
                    return Task.FromResult(Encoding.UTF8.GetBytes("[{\"id\":22,\"name\":\"Ashe\"}," +
                        "{\"id\":99,\"name\":\"Lux\"},{\"id\":12,\"name\":\"Alistar\"}]"));
                }
                if (path == LeagueBuildAdvisorDataService.ItemsPath)
                {
                    BuildCatalogReads++;
                    return Task.FromResult(Encoding.UTF8.GetBytes("[{\"id\":3006,\"name\":\"Berserker Greaves\"," +
                        "\"iconPath\":\"/lol-game-data/assets/v1/items/3006.png\"}," +
                        "{\"id\":3031,\"name\":\"Infinity Edge\"}]"));
                }
                if (path == LeagueBuildAdvisorDataService.SummonerSpellsPath)
                {
                    BuildCatalogReads++;
                    return Task.FromResult(Encoding.UTF8.GetBytes("[{\"id\":4,\"name\":\"Flash\"}," +
                        "{\"id\":7,\"name\":\"Heal\"}]"));
                }
                if (path == LeagueBuildAdvisorDataService.PerksPath)
                {
                    BuildCatalogReads++;
                    return Task.FromResult(Encoding.UTF8.GetBytes("[{\"id\":8005," +
                        "\"name\":\"Press the Attack\",\"endOfGameStatDescs\":[\"Damage @eogvar1@\"]}," +
                        "{\"id\":9111,\"name\":\"Triumph\"}]"));
                }
                if (path == LeagueBuildAdvisorDataService.PerkStylesPath)
                {
                    BuildCatalogReads++;
                    return Task.FromResult(Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"styles\":[" +
                        "{\"id\":8000,\"name\":\"Precision\"},{\"id\":8300,\"name\":\"Inspiration\"}]}"));
                }
                if (path == LeagueBuildAdvisorDataService.CherryAugmentsPath)
                {
                    BuildCatalogReads++;
                    return Task.FromResult(Encoding.UTF8.GetBytes("[{\"id\":1234," +
                        "\"nameTRA\":\"Fixture Augment\",\"rarity\":\"kPrismatic\"," +
                        "\"augmentSmallIconPath\":\"/lol-game-data/assets/v1/augment/1234.png\"}]"));
                }
                if (path == "/lol-match-history/v1/games/123")
                {
                    AfterMatchRead?.Invoke();
                    var target = ReturnMissingParticipant ? "different-player" : "enemy";
                    var otherParticipantId = ReturnMalformedRoster ? 1 : 2;
                    var teams = ReturnMissingTeams ? "" : ",\"teams\":[{\"teamId\":100," +
                        "\"baronKills\":1,\"dragonKills\":2,\"towerKills\":6," +
                        "\"hordeKills\":3,\"riftHeraldKills\":0,\"inhibitorKills\":2," +
                        "\"firstBaron\":true,\"firstDargon\":true,\"firstTower\":true," +
                        "\"firstInhibitor\":false,\"bans\":[{\"championId\":22,\"pickTurn\":1}," +
                        "{\"championId\":99,\"pickTurn\":3},{\"championId\":-1,\"pickTurn\":5}]}," +
                        "{\"teamId\":200,\"baronKills\":0,\"dragonKills\":1,\"towerKills\":2," +
                        "\"firstBaron\":false,\"firstDragon\":false,\"bans\":[]}]";
                    if (ReturnDuplicateTeams)
                        teams = teams.Replace("{\"teamId\":200", "{\"teamId\":100,\"baronKills\":9},{\"teamId\":200");
                    if (ReturnMalformedFirst)
                        teams = teams.Replace("\"firstBaron\":true", "\"firstBaron\":\"invalid\"");
                    if (ReturnMalformedBans)
                        teams = teams.Replace("\"bans\":[]", "\"bans\":\"invalid\"");
                    var allyMetrics = ReturnZeroTeamMetrics
                        ? ",\"totalDamageDealtToChampions\":0,\"goldEarned\":0"
                        : ReturnInvalidTeamMetrics ? ",\"totalDamageDealtToChampions\":null,\"goldEarned\":\"invalid\"" : "";
                    var cherryMode = ReturnCherry ? "CHERRY" : "ARAM";
                    var cherryFirst = ReturnCherry && !ReturnCherryUnknown
                        ? "\"playerSubteamId\":2,\"subteamPlacement\":2," : "";
                    var cherrySecond = ReturnCherry ? "\"playerSubteamId\":1,\"subteamPlacement\":1," : "";
                    var cherryThird = ReturnCherry && !ReturnCherryUnknown
                        ? "\"playerSubteamId\":2,\"subteamPlacement\":" +
                            (ReturnCherryConflict ? "3" : "2") + "," : "";
                    return Task.FromResult(Encoding.UTF8.GetBytes("{\"gameId\":123,\"gameMode\":\"" + cherryMode + "\"," +
                        "\"gameDuration\":1200,\"participantIdentities\":[{\"participantId\":1," +
                        "\"player\":{\"puuid\":\"" + target + "\",\"gameName\":\"Target\",\"tagLine\":\"HN1\"}}," +
                        "{\"participantId\":2,\"player\":{\"puuid\":\"ally\"}}," +
                        "{\"participantId\":3,\"player\":{\"puuid\":\"self\"}}],\"participants\":[{" +
                        "\"participantId\":1,\"teamId\":100,\"championId\":22," +
                        "\"spell1Id\":4,\"spell2Id\":7,\"stats\":{" +
                        cherryFirst + "\"win\":true,\"kills\":5,\"deaths\":2,\"assists\":9," +
                        "\"doubleKills\":6,\"tripleKills\":3,\"quadraKills\":2,\"pentaKills\":1," +
                        "\"totalMinionsKilled\":100,\"neutralMinionsKilled\":3," +
                        "\"totalDamageDealtToChampions\":18000,\"goldEarned\":12000,\"visionScore\":11," +
                        "\"physicalDamageDealtToChampions\":9000,\"magicDamageDealtToChampions\":7000," +
                        "\"trueDamageDealtToChampions\":2000,\"totalDamageTaken\":5000," +
                        "\"physicalDamageTaken\":3000,\"magicDamageTaken\":1500,\"trueDamageTaken\":500," +
                        "\"totalHeal\":2400," +
                        "\"item0\":3006,\"item1\":3031,\"roleBoundItem\":3158," +
                        "\"perk0\":8005,\"perk0Var1\":777,\"perk0Var2\":-2.5,\"perk0Var3\":0,\"perk1\":9111," +
                        "\"perk2\":9104,\"perk3\":8014,\"perk4\":8345,\"perk5\":8347," +
                        "\"perkPrimaryStyle\":8000,\"perkSubStyle\":8300,\"playerAugment1\":" +
                            (ReturnNoAugments ? "0" : "1234") + "}}," +
                        "{\"participantId\":" + otherParticipantId + ",\"teamId\":100,\"championId\":99," +
                        "\"stats\":{" + cherrySecond + "\"win\":" + (ReturnConflictingWin ? "false" : "true") +
                        ",\"kills\":3,\"deaths\":4,\"assists\":6" + allyMetrics + "}}," +
                        "{\"participantId\":3,\"teamId\":200,\"championId\":12," +
                        "\"stats\":{" + cherryThird + "\"win\":false,\"kills\":2,\"deaths\":6,\"assists\":3}}]" +
                        teams + "}"));
                }
                return Task.FromResult<byte[]>(null);
            }
        }

        private static async Task ValidateReplayAsync()
        {
            Require(LeagueReplayWriteApiClient.IsAllowedPathForSmokeTest(
                    "/lol-replays/v2/metadata/123/create") &&
                LeagueReplayWriteApiClient.IsAllowedPathForSmokeTest(
                    "/lol-replays/v1/rofls/123/download") &&
                LeagueReplayWriteApiClient.IsAllowedPathForSmokeTest(
                    "/lol-replays/v1/rofls/123/watch") &&
                !LeagueReplayWriteApiClient.IsAllowedPathForSmokeTest(
                    "/lol-replays/v1/rofls/../watch") &&
                !LeagueReplayWriteApiClient.IsAllowedPathForSmokeTest(
                    "/lol-replays/v1/rofls/123/delete"),
                "Replay writer must only accept positive game IDs and exact replay POST routes.");
            var session = new LeagueClientSession("LeagueClientUx", 55, 2999, "test", "https", "fixture", "HN1", "HN1");
            var active = session;
            var record = new LeagueEncounterRecord { GameId = 123, SelfPuuid = "self", Puuid = "enemy",
                PlatformId = "HN1", Region = "HN1" };
            var match = new LeaguePlayerMatchSummary { GameId = 123, QueueId = 450,
                GameType = "MATCHED_GAME", GameCreationEpochMs = 1720000000000,
                GameDurationSeconds = 1234 };
            var read = new ReplayReadClient();
            var write = new ReplayWriteClient();
            var service = new LeagueEncounterReplayService(read, write, () => active);

            var result = await service.ExecuteAsync(record, match, CancellationToken.None);
            Require(result.Status == LeagueEncounterReplayStatus.DownloadRequested &&
                write.Created == 1 && write.Downloads == 1 && write.Watches == 0 &&
                write.CreatedMatch == match && write.CreatedVersion == "15.21.1" &&
                read.ProfileReads == 2,
                "Downloading a replay must check the owner twice, create metadata and POST once.");
            read.State = "watch";
            result = await service.ExecuteAsync(record, match, CancellationToken.None);
            Require(result.Status == LeagueEncounterReplayStatus.WatchRequested && write.Watches == 1 &&
                write.Downloads == 1, "A watch-ready replay should only send the watch action.");
            read.State = "downloading";
            read.Progress = 0.42;
            result = await service.ExecuteAsync(record, match, CancellationToken.None);
            Require(result.Status == LeagueEncounterReplayStatus.Downloading && result.Progress == 42 &&
                write.Watches == 1 && write.Downloads == 1,
                "Download progress must be informative without resending replay commands.");
            read.State = "checking";
            Require((await service.ExecuteAsync(record, match, CancellationToken.None)).Status ==
                LeagueEncounterReplayStatus.Checking, "Checking must not trigger a replay POST.");
            read.State = "incompatible";
            Require((await service.ExecuteAsync(record, match, CancellationToken.None)).Status ==
                LeagueEncounterReplayStatus.Incompatible, "Incompatible replays must not be played.");
            read.State = "download";
            read.Enabled = false;
            var writesBefore = write.Downloads;
            Require((await service.ExecuteAsync(record, match, CancellationToken.None)).Status ==
                LeagueEncounterReplayStatus.Unavailable && write.Downloads == writesBefore,
                "Replay configuration opt-outs must be honored.");
            read.Enabled = true;
            read.SelfPuuid = "another-player";
            Require((await service.ExecuteAsync(record, match, CancellationToken.None)).Status ==
                LeagueEncounterReplayStatus.WrongAccount && write.Downloads == writesBefore,
                "A changed signed-in owner must not act on saved-player history.");
            read.SelfPuuid = "self";
            read.MetadataGameId = 999;
            Require((await service.ExecuteAsync(record, match, CancellationToken.None)).Status ==
                LeagueEncounterReplayStatus.Unavailable && write.Downloads == writesBefore,
                "Mismatched replay metadata must not trigger an action.");
            read.MetadataGameId = 123;
            read.AfterMetadata = () => active = new LeagueClientSession("LeagueClientUx", 55, 3000,
                "other", "https", "fixture", "HN1", "HN1");
            Require((await service.ExecuteAsync(record, match, CancellationToken.None)).Status ==
                LeagueEncounterReplayStatus.NoClient && write.Downloads == writesBefore,
                "Switching LCU sessions while reading metadata must stop the replay action.");
            active = session;
            read.AfterMetadata = null;
            var wrongRegion = new LeagueEncounterRecord { GameId = 123, SelfPuuid = "self",
                Puuid = "enemy", PlatformId = "HN2", Region = "HN2" };
            Require((await service.ExecuteAsync(wrongRegion, match, CancellationToken.None)).Status ==
                LeagueEncounterReplayStatus.WrongAccount && write.Downloads == writesBefore,
                "A foreign platform or region must never receive replay commands.");
            match.GameCreationEpochMs = 0;
            Require((await service.ExecuteAsync(record, match, CancellationToken.None)).Status ==
                LeagueEncounterReplayStatus.Unavailable && write.Downloads == writesBefore,
                "Incomplete creation timestamps must prevent invalid metadata POSTs.");
            var historyMatch = new LeaguePlayerMatchSummary { GameId = 123, QueueId = 450,
                GameType = "MATCHED_GAME", GameCreationEpochMs = 1720000000000, GameDurationSeconds = 1234 };
            read.AfterConfiguration = () => historyMatch.GameId = 999;
            result = await service.ExecuteForHistoryAsync(session, "self", "enemy", historyMatch,
                CancellationToken.None);
            Require(result.Status == LeagueEncounterReplayStatus.DownloadRequested &&
                write.Downloads == writesBefore + 1 && write.CreatedMatch.GameId == 123 &&
                historyMatch.GameId == 999,
                "History actions must freeze the selected match before any asynchronous LCU read.");
            read.AfterConfiguration = null;
            writesBefore = write.Downloads;
            var movedSession = new LeagueClientSession("LeagueClientUx", 55, 2999, "test",
                "https", "fixture", "HN2", "HN2");
            Require(LeagueEncounterReplayService.CanOfferHistoryReplay(session, active, "self", historyMatch) &&
                !LeagueEncounterReplayService.CanOfferHistoryReplay(movedSession, active, "self", historyMatch) &&
                !LeagueEncounterReplayService.CanOfferHistoryReplay(session, active, null, historyMatch) &&
                !LeagueEncounterReplayService.CanOfferHistoryReplay(session, active, "self", null),
                "History replay controls must require a verified account, selected match and unchanged page session.");
            Require((await service.ExecuteForHistoryAsync(movedSession, "self", "enemy", match,
                    CancellationToken.None)).Status == LeagueEncounterReplayStatus.NoClient &&
                (await service.ExecuteForHistoryAsync(null, "self", "enemy", match,
                    CancellationToken.None)).Status == LeagueEncounterReplayStatus.NoClient &&
                write.Downloads == writesBefore,
                "A remote or cached history page must retain its original authenticated region and session.");
            Require((await service.ExecuteForHistoryAsync(session, "self", "enemy", null,
                    CancellationToken.None)).Status == LeagueEncounterReplayStatus.Unavailable &&
                write.Downloads == writesBefore,
                "Unselected history rows must not trigger replay writes.");
            Require(LeagueEncounterMatchScoreboardDialog.ReplayStatusText(
                new LeagueEncounterReplayResult { Status = LeagueEncounterReplayStatus.Downloading, Progress = 42 },
                key => key == LeagueEfficiencyUiTextKeys.DynamicEncounterReplayDownloading ? "{0}%" : key) == "42%",
                "Replay state must be reflected in the native scoreboard label.");
        }

        private sealed class ReplayReadClient : ILeagueClientApi
        {
            internal string SelfPuuid = "self";
            internal string State = "download";
            internal bool Enabled = true;
            internal double Progress;
            internal long MetadataGameId = 123;
            internal Action AfterMetadata;
            internal Action AfterConfiguration;
            internal int ProfileReads;

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                string json = null;
                if (path == LeagueDashboardDetailsService.SummonerPath)
                {
                    ProfileReads++;
                    json = "{\"puuid\":\"" + SelfPuuid + "\"}";
                }
                else if (path == LeagueEncounterReplayService.ConfigurationPath)
                {
                    AfterConfiguration?.Invoke();
                    json = "{\"gameVersion\":\"15.21.1\",\"isReplaysEnabled\":" +
                        (Enabled ? "true" : "false") + ",\"isReplaysForMatchHistoryEnabled\":true}";
                }
                else if (path == "/lol-replays/v1/metadata/123")
                {
                    AfterMetadata?.Invoke();
                    json = "{\"gameId\":" + MetadataGameId + ",\"state\":\"" + State +
                        "\",\"downloadProgress\":" + Progress.ToString(
                            System.Globalization.CultureInfo.InvariantCulture) + "}";
                }
                return Task.FromResult(json == null ? null : Encoding.UTF8.GetBytes(json));
            }
        }

        private sealed class ReplayWriteClient : ILeagueReplayWriteApi
        {
            internal int Created;
            internal int Downloads;
            internal int Watches;
            internal LeaguePlayerMatchSummary CreatedMatch;
            internal string CreatedVersion;

            public Task<LeagueClientWriteResponse> TryCreateMetadataAsync(LeagueClientSession session,
                LeaguePlayerMatchSummary match, string gameVersion, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Created++;
                CreatedMatch = match;
                CreatedVersion = gameVersion;
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 200 });
            }

            public Task<LeagueClientWriteResponse> TryReplayAsync(LeagueClientSession session,
                long gameId, bool watch, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (watch) Watches++;
                else Downloads++;
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 200 });
            }
        }

        private sealed class StubClient : ILeagueClientApi
        {
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                return Task.FromResult<byte[]>(null);
            }
        }
    }
}
