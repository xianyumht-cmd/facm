using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal static class LeagueResearchIntegrationSmokeTest
    {
        public static void Validate()
        {
            var fixture = new FakeClient();
            fixture.Muted.Add(First);
            var service = new LeagueChampSelectMuteService(fixture, fixture);
            var result = service.MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.Requested == 1 && result.Succeeded == 1 && result.AlreadyMuted == 1 &&
                    fixture.Writes == 1 && fixture.Muted.Contains(First) && fixture.Muted.Contains(Second),
                "Mute-all changed an already-muted target or failed to preserve a large identity.");
            result = service.MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.Requested == 0 && result.AlreadyMuted == 2 && fixture.Writes == 1,
                "Repeated mute-all was not idempotent.");

            var unavailable = new FakeClient { MutedOverride = "{}" };
            result = new LeagueChampSelectMuteService(unavailable, unavailable)
                .MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.StateUnavailable && unavailable.Writes == 0,
                "Invalid mute state allowed a blind toggle.");

            var lobby = new FakeClient { Phase = "Lobby" };
            result = new LeagueChampSelectMuteService(lobby, lobby)
                .MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.NotInChampSelect && lobby.Writes == 0,
                "Mute action escaped the champion-select phase.");

            var changed = new FakeClient { ChangeRoster = true };
            result = new LeagueChampSelectMuteService(changed, changed)
                .MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.StateUnavailable && changed.Writes == 0,
                "Mute action continued after roster changed.");

            var uncertain = new FakeClient { UncertainWrite = true };
            result = new LeagueChampSelectMuteService(uncertain, uncertain)
                .MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.StateUnavailable && uncertain.Writes == 1,
                "Uncertain toggle was retried or followed by another write.");

            var cancelled = new FakeClient();
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try
                {
                    new LeagueChampSelectMuteService(cancelled, cancelled)
                        .MuteAllAsync(cancellation.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Canceled mute completed.");
                }
                catch (OperationCanceledException) { }
            }
            Require(cancelled.Writes == 0, "Canceled mute wrote to the client.");

            var penalty = LeagueLeaverBusterService.ParseSearch(Bytes(
                "{\"errors\":[{\"errorType\":\"QUEUE_DODGER\",\"penaltyTimeRemaining\":39233.063}]}"));
            Require(penalty.Available && penalty.HasPenalty && penalty.MaxSecondsRemaining > 39233 &&
                    penalty.Entries[0].Type == "QUEUE_DODGER",
                "Active matchmaking restriction was not recognized.");
            var clean = LeagueLeaverBusterService.ParseSearch(Bytes("{\"errors\":[]}"));
            Require(clean.Available && !clean.HasPenalty, "Empty errors should mean no reported restriction.");
            Require(!LeagueLeaverBusterService.ParseSearch(Bytes("{}")).Available &&
                    !LeagueLeaverBusterService.ParseSearch(Bytes("[]")).Available &&
                    !LeagueLeaverBusterService.ParseSearch(Bytes("{\"errors\":[{}]}")).Available,
                "Missing or malformed matchmaking errors were presented as clean.");

            var rewards = new LeagueRewardsOverviewService(new FakeClient())
                .ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(rewards.Available && rewards.LootAvailable && !rewards.MissionsAvailable &&
                    rewards.LootItemCount == 0, "Partial reward availability collapsed into numeric zero.");

            var lootFailure = new LeagueRewardsOverviewService(new FakeClient { LootFailure = true, MissionBody = "[]" })
                .ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(lootFailure.Available && !lootFailure.LootAvailable && lootFailure.MissionsAvailable &&
                    lootFailure.MissionStatusKnown, "Loot failure discarded healthy missions.");
            var missionFailure = new LeagueRewardsOverviewService(new FakeClient { MissionFailure = true })
                .ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(missionFailure.Available && missionFailure.LootAvailable && !missionFailure.MissionsAvailable,
                "Mission failure discarded healthy loot.");
            var unknownStatus = new LeagueRewardsOverviewService(new FakeClient { MissionBody = "[{}]" })
                .ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(unknownStatus.MissionsAvailable && !unknownStatus.MissionStatusKnown && unknownStatus.MissionItemCount == 1,
                "Unknown mission status was counted as a known zero.");
            var postgame = new LeagueInsightSnapshot
            {
                EndOfGame = new LeagueEndOfGameSummary
                {
                    Available = true,
                    Players = new[] { new LeagueEndOfGamePlayerLine { IsLocalPlayer = true, MinionsKnown = true, MinionsKilled = 0 } }
                }
            };
            var withMinions = LeagueInsightPresentation.Project(postgame, "EndOfGame")["postgame"];
            postgame.EndOfGame.Players[0].MinionsKnown = false;
            Require(withMinions != LeagueInsightPresentation.Project(postgame, "EndOfGame")["postgame"],
                "Known zero CS and missing CS rendered identically.");

            Require(LeagueBlockedPlayersService.ParseCount(Bytes("[]")) == 0 &&
                    LeagueBlockedPlayersService.ParseCount(Bytes("[{\"id\":\"fixture-a\"},{\"id\":\"fixture-a\"}]")) == 1 &&
                    !LeagueBlockedPlayersService.ParseCount(Bytes("[{}]")).HasValue &&
                    !LeagueBlockedPlayersService.ParseCount(Bytes("{}")).HasValue &&
                    !LeagueBlockedPlayersService.ParseCount(null).HasValue,
                "Blocked list count confused absent state with a validated collection.");
            var rank = new LeagueRankedQueueLine { QueueType = "RANKED_SOLO_5x5", Tier = "EMERALD", Division = "II" };
            Require(rank.Display.Contains("翡翠") && !rank.Display.Contains("EMERALD") && rank.Display.Contains("胜点未知"),
                "Rank display exposed raw enum or invented LP.");
            rank.Tier = null;
            Require(!rank.Display.Contains("未定级"), "Absent tier was presented as unranked.");
            var invalidLocal = new LeagueEndOfGameStatsService(new FakeClient
            {
                EogBody = "{\"gameId\":123,\"localPlayer\":{\"summonerId\":0},\"teams\":[{\"players\":[{\"summonerId\":0,\"stats\":{}}]}]}"
            }).ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(invalidLocal.Available && !invalidLocal.Players[0].IsLocalPlayer,
                "Zero summoner identity was attributed to the local player.");

            var reads = new FakeClient { FriendFailure = true, BlockedBody = "[]" };
            var insights = new LeagueInsightService(reads);
            var idle = insights.ReadAsync("InProgress", CancellationToken.None).GetAwaiter().GetResult();
            Require(reads.Reads.Count == 0 && idle.Queue == null, "In-game insight loaded lobby data.");
            var overview = insights.ReadAsync("Lobby", CancellationToken.None).GetAwaiter().GetResult();
            Require(reads.SearchReads == 1 && overview.Leaver.HasPenalty && overview.Queue.Available &&
                    overview.Friends == null && overview.BlockedPlayerCount == 0 && overview.Rewards != null && overview.Rewards.LootAvailable,
                "Insight aggregation repeated search or lost a healthy group after another failed.");
            var rendered = LeagueInsightPresentation.Project(overview, "Lobby");
            Require(rendered.Count == 6 && rendered["penalty"] == overview.Leaver.Summary &&
                    rendered["postgame"] == LeagueInsightPresentation.Project(null, "Lobby")["postgame"],
                "Shared insight projection lost a group or exposed postgame data in lobby.");
            var disconnected = LeagueInsightPresentation.Project(overview, null);
            foreach (var value in disconnected.Values)
                Require(value == disconnected["queue"], "Disconnected insight retained phase data.");
            var estimated = LeagueQueueStatusService.ParseSearch(Bytes("{\"isCurrentlyInQueue\":true}"));
            Require(estimated.Available && estimated.InQueue && !estimated.EstimateKnown && !estimated.ElapsedKnown,
                "Missing queue durations were presented as numeric data.");
        }

        private const string First = "{\"summonerId\":3000000001,\"puuid\":\"fixture-a\",\"obfuscatedSummonerId\":0,\"obfuscatedPuuid\":\"\"}";
        private const string Second = "{\"summonerId\":3000000002,\"puuid\":\"fixture-b\",\"obfuscatedSummonerId\":0,\"obfuscatedPuuid\":\"\"}";
        private static byte[] Bytes(string value) { return value == null ? null : Encoding.UTF8.GetBytes(value); }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeClient : ILeagueClientApi, ILeagueChampSelectMuteWriteApi
        {
            public string Phase = "ChampSelect";
            public string MutedOverride;
            public bool ChangeRoster;
            public bool UncertainWrite;
            public bool FriendFailure;
            public bool LootFailure;
            public bool MissionFailure;
            public string MissionBody;
            public string BlockedBody;
            public string EogBody;
            public int SearchReads;
            public readonly List<string> Reads = new List<string>();
            public int Writes;
            private int _sessionReads;
            public readonly HashSet<string> Muted = new HashSet<string>(StringComparer.Ordinal);

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Reads.Add(path);
                string body = null;
                if (path == LeagueQueueStatusService.SearchPath)
                {
                    SearchReads++;
                    body = "{\"isCurrentlyInQueue\":false,\"errors\":[{\"errorType\":\"QUEUE_DODGER\",\"penaltyTimeRemaining\":39233.063}]}";
                }
                if (path == LeagueBlockedPlayersService.BlockedPlayersPath) body = BlockedBody;
                if (path == LeagueEndOfGameStatsService.StatsPath) body = EogBody;
                if (path == LeagueFriendActivityService.FriendsPath && FriendFailure) throw new InvalidOperationException("Fixture failure");
                if (path == LeagueChampSelectMuteService.PhasePath) body = "\"" + Phase + "\"";
                if (path == LeagueChampSelectMuteService.SessionPath)
                {
                    _sessionReads++;
                    body = "{\"localPlayerCellId\":0,\"gameId\":123,\"myTeam\":[{\"cellId\":0}," +
                           "{\"cellId\":1," + First.Substring(1) +
                           (ChangeRoster && _sessionReads > 1 ? "" : ",{\"cellId\":2," + Second.Substring(1)) +
                           "],\"theirTeam\":[]}";
                }
                if (path == LeagueChampSelectMuteService.MutedPlayersPath)
                    body = MutedOverride ?? ("[" + string.Join(",", Muted) + "]");
                if (path == LeagueRewardsOverviewService.PlayerLootPath)
                {
                    if (LootFailure) throw new InvalidOperationException("Fixture loot failure");
                    body = "[]";
                }
                if (path == LeagueRewardsOverviewService.MissionsPath)
                {
                    if (MissionFailure) throw new InvalidOperationException("Fixture mission failure");
                    body = MissionBody;
                }
                return Task.FromResult(Bytes(body));
            }

            public Task<LeagueClientWriteResponse> TryToggleAsync(string identityJson, LeagueClientSession expected, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Writes++;
                if (UncertainWrite) return Task.FromResult<LeagueClientWriteResponse>(null);
                if (!Muted.Add(identityJson)) Muted.Remove(identityJson);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 204 });
            }
        }
    }
}
