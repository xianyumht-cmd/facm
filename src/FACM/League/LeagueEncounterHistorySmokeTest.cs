using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;

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
                    result.Participants.Single(p => p.IsTarget).DamageToChampions == 18000 &&
                    result.Participants.Single(p => p.IsTarget).ChampionName == "Ashe" &&
                    result.Match.ChampionName == "Ashe" &&
                    result.Participants.Single(p => p.IsTarget).ItemIds.SequenceEqual(new[] { 3006, 3031, 0, 0, 0, 0, 0 }) &&
                    result.Participants.Single(p => p.IsTarget).RoleBoundItemId == 3158 &&
                    result.Match.RoleBoundItemId == 3158 &&
                    result.Participants.Single(p => p.IsTarget).SpellIds.SequenceEqual(new[] { 4, 7 }) &&
                    result.Participants.Single(p => p.IsTarget).PerkIds.SequenceEqual(new[] { 8005, 9111, 9104, 8014, 8345, 8347 }) &&
                    result.Participants.Single(p => p.IsTarget).PrimaryPerkStyleId == 8000 &&
                    result.Participants.Single(p => p.IsTarget).SecondaryPerkStyleId == 8300 &&
                    result.Participants.Single(p => p.IsTarget).AugmentIds[0] == 1234 &&
                    result.Participants.Single(p => p.PuuId == "ally").TeamId == 100 &&
                    result.Participants.Single(p => p.PuuId == "self").TeamId == 200 &&
                    result.BuildCatalog != null && result.BuildCatalog.Items[3006] == "Berserker Greaves" &&
                    result.BuildCatalog.Spells[4] == "Flash" &&
                    result.BuildCatalog.Perks[8005] == "Press the Attack" &&
                    result.BuildCatalog.ItemIcons[3006].StartsWith("lcu:", StringComparison.Ordinal) &&
                    api.Reads == 8 && api.ChampionReads == 1 && api.BuildCatalogReads == 3,
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
                    cachedDetail.BuildCatalog != null && api.Reads == 11 && api.BuildCatalogReads == 3 &&
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
                        "\"name\":\"Press the Attack\"},{\"id\":9111,\"name\":\"Triumph\"}]"));
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
                        "\"totalMinionsKilled\":100,\"neutralMinionsKilled\":3," +
                        "\"totalDamageDealtToChampions\":18000,\"goldEarned\":12000,\"visionScore\":11," +
                        "\"item0\":3006,\"item1\":3031,\"roleBoundItem\":3158," +
                        "\"perk0\":8005,\"perk1\":9111," +
                        "\"perk2\":9104,\"perk3\":8014,\"perk4\":8345,\"perk5\":8347," +
                        "\"perkPrimaryStyle\":8000,\"perkSubStyle\":8300,\"playerAugment1\":1234}}," +
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
