using System;
using System.Linq;
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
            ValidateLobbyRoom();
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

        private static void ValidateLobbyRoom()
        {
            ValidateDynamicRoomCatalog();
            ValidateExtendedRoomActions();
            var api = new RoomFixture();
            var service = new LeagueLobbyRoomService(api, api, () => api.Owner);
            string method, path, body;
            Require(LeagueMatchmakingWriteApiClient.TryBuildRoomRequest(LeagueLobbyRoomAction.Queue, 430,
                out method, out path, out body) && method == "POST" && path == LeagueLiveDataService.LobbyPath &&
                body == "{\"queueId\":430}", "Lobby creation must use the researched POST rather than PUT.");
            Require(!LeagueMatchmakingWriteApiClient.TryBuildRoomRequest(LeagueLobbyRoomAction.Queue, 999999,
                out method, out path, out body), "Invalid queue identifier must not produce a request.");
            Require(LeagueMatchmakingWriteApiClient.TryBuildRoomRequest(LeagueLobbyRoomAction.Kick, 3000000002,
                out method, out path, out body) && path == "/lol-lobby/v2/lobby/members/3000000002/kick" &&
                body == null, "Member action must preserve a 64-bit identity.");
            var snapshot = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(snapshot != null && snapshot.CanQueue && !snapshot.CanLeave, "No-lobby phase must expose creation only.");
            var result = service.ApplyAsync(snapshot, LeagueLobbyRoomAction.Queue, 450, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "confirmed" && result.Snapshot.QueueId == 450 && api.Writes == 1,
                "Creation must confirm the new queue.");
            var lobby = result.Snapshot;
            result = service.ApplyAsync(lobby, LeagueLobbyRoomAction.Kick, 3000000001, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "stale" && api.Writes == 1, "Room action must not target self.");
            result = service.ApplyAsync(lobby, LeagueLobbyRoomAction.Promote, 3000000002, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "confirmed" && !result.Snapshot.IsLeader && api.Writes == 2,
                "Promotion must observe the new leader.");
            result = service.ApplyAsync(lobby, LeagueLobbyRoomAction.Kick, 3000000002, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "stale" && api.Writes == 2, "Former leader snapshot must not kick after promotion.");
            lobby = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            result = service.ApplyAsync(lobby, LeagueLobbyRoomAction.Leave, 0, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "confirmed" && result.Snapshot.Phase == "None" && api.Writes == 3,
                "Leave must verify None rather than merely HTTP success.");
            snapshot = result.Snapshot;
            api.Apply = false;
            result = service.ApplyAsync(snapshot, LeagueLobbyRoomAction.Queue, 440, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "unverified" && api.Writes == 4, "Unchanged queue must not confirm.");
            result = service.ApplyAsync(snapshot, LeagueLobbyRoomAction.Queue, 440, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "unverified" && api.Writes == 4, "Unknown room write must not repeat.");
            result = service.ApplyAsync(snapshot, LeagueLobbyRoomAction.Queue, 430, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "unverified" && api.Writes == 4, "A different action must not overwrite an unresolved room mutation.");
            api.Phase = "Lobby";
            api.Queue = 440;
            api.Leader = true;
            lobby = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            api.StatusCode = 408;
            result = service.ApplyAsync(lobby, LeagueLobbyRoomAction.Leave, 0, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "unverified" && api.Writes == 5, "HTTP 408 is not definitive rejection.");
            result = service.ApplyAsync(lobby, LeagueLobbyRoomAction.Promote, 3000000002, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "unverified" && api.Writes == 5, "A timed-out leave must fence another room mutation.");
            api.Queue = 450;
            lobby = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            api.StatusCode = 403;
            result = service.ApplyAsync(lobby, LeagueLobbyRoomAction.Leave, 0, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "rejected" && api.Writes == 6, "Definitive rejection must remain distinguishable.");
            api.StatusCode = 204;
            api.Apply = true;
            result = service.ApplyAsync(lobby, LeagueLobbyRoomAction.Leave, 0, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "confirmed" && api.Writes == 7, "A new explicit action after rejection may proceed.");
            api.Phase = "Matchmaking";
            Require(service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult() == null,
                "Room management must not act during matchmaking.");
        }

        private static void ValidateExtendedRoomActions()
        {
            var api = new RoomFixture { Phase = "Lobby", Queue = 420, Extended = true };
            var service = new LeagueLobbyRoomService(api, api, () => api.Owner);
            string method, path, body;
            Require(LeagueMatchmakingWriteApiClient.TryBuildRoomRequest(LeagueLobbyRoomAction.Positions, 35,
                out method, out path, out body) && method == "PUT" &&
                path == "/lol-lobby/v1/lobby/members/localMember/position-preferences" &&
                body.Contains("\"firstPreference\":\"MIDDLE\"") && body.Contains("\"secondPreference\":\"UTILITY\""),
                "Role request must use the exact documented body and path.");
            Require(!LeagueMatchmakingWriteApiClient.TryBuildRoomRequest(LeagueLobbyRoomAction.Positions, 33,
                out method, out path, out body), "Duplicate preferred positions must not be sent.");
            Require(LeagueMatchmakingWriteApiClient.TryBuildRoomRequest(LeagueLobbyRoomAction.GrantInvite, 3000000002L,
                out method, out path, out body) && path.EndsWith("/3000000002/grant-invite"), "Grant invite route mismatch.");
            Require(LeagueMatchmakingWriteApiClient.TryBuildRoomRequest(LeagueLobbyRoomAction.RevokeInvite, 3000000002L,
                out method, out path, out body) && path.EndsWith("/3000000002/revoke-invite"), "Revoke invite route mismatch.");
            Require(LeagueMatchmakingWriteApiClient.TryBuildRoomRequest(LeagueLobbyRoomAction.Spectator, 0,
                out method, out path, out body) && path.EndsWith("/team/SPECTATOR"), "Spectator route mismatch.");
            var snapshot = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(snapshot.CanSetPositions && !snapshot.CanSpectate &&
                snapshot.Allows(LeagueLobbyRoomAction.GrantInvite, 3000000002L) &&
                !snapshot.Allows(LeagueLobbyRoomAction.RevokeInvite, 3000000002L), "Initial explicit capabilities mismatch.");
            var result = service.ApplyAsync(snapshot, LeagueLobbyRoomAction.Positions, 35, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "confirmed" && result.Snapshot.FirstRole == 3 && result.Snapshot.SecondRole == 5,
                "Both selected roles must be read back.");
            result = service.ApplyAsync(result.Snapshot, LeagueLobbyRoomAction.GrantInvite, 3000000002L, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "confirmed" && result.Snapshot.Allows(LeagueLobbyRoomAction.RevokeInvite, 3000000002L),
                "Grant must confirm the target permission.");
            result = service.ApplyAsync(result.Snapshot, LeagueLobbyRoomAction.RevokeInvite, 3000000002L, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "confirmed" && api.Writes == 3, "Revoke must confirm the target permission.");
            api.Custom = true;
            snapshot = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(!snapshot.CanSpectate, "NotAllowed policy must prohibit spectator transition.");
            api.Policy = "LobbyAllowed";
            snapshot = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(snapshot.CanSpectate, "Eligible custom lobby must expose spectator transition.");
            result = service.ApplyAsync(snapshot, LeagueLobbyRoomAction.Spectator, 0, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "confirmed" && result.Snapshot.IsSpectator == true && !result.Snapshot.CanSpectate &&
                !result.Snapshot.CanSetPositions && api.Writes == 4, "Spectator transition must verify the local member.");
            api.Extended = false;
            snapshot = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(!snapshot.CanSetPositions && !snapshot.CanSpectate &&
                !snapshot.Allows(LeagueLobbyRoomAction.GrantInvite, 3000000002L),
                "Absent permission and eligibility fields must never authorize new actions.");
        }

        private static void ValidateDynamicRoomCatalog()
        {
            var api = new RoomFixture
            {
                Catalog = "[" + QueueEntry(1090, "Available", true) + "," +
                    QueueEntry(420, "PlatformDisabled", true) + "," + QueueEntry(450, "Available", false) + "]"
            };
            var service = new LeagueLobbyRoomService(api, api, () => api.Owner);
            var snapshot = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(snapshot != null && snapshot.QueueChoices.Count == 1 &&
                snapshot.Allows(LeagueLobbyRoomAction.Queue, 1090) &&
                !snapshot.Allows(LeagueLobbyRoomAction.Queue, 420) &&
                !snapshot.Allows(LeagueLobbyRoomAction.Queue, 450),
                "Only currently visible and available queues may be selected.");
            api.Catalog = "[]";
            var result = service.ApplyAsync(snapshot, LeagueLobbyRoomAction.Queue, 1090, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "stale" && api.Writes == 0 && !result.Snapshot.CanQueue,
                "A removed queue must not be written using a stale UI snapshot.");
            api.Catalog = "[" + QueueEntry(1090, "Available", true) + "]";
            snapshot = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            result = service.ApplyAsync(snapshot, LeagueLobbyRoomAction.Queue, 1090, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "confirmed" && result.Snapshot.QueueId == 1090 && api.Writes == 1,
                "A newly observed non-preset queue must use the same verified creation transaction.");
            api.Catalog = null;
            snapshot = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(snapshot != null && snapshot.QueueChoices == null && !snapshot.CanQueue && snapshot.CanLeave,
                "Unavailable catalog must not disable unrelated lobby actions or enable unverified queue writes.");
            result = service.ApplyAsync(snapshot, LeagueLobbyRoomAction.Leave, 0, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.State == "confirmed" && api.Writes == 2, "Leaving must not depend on catalog availability.");
            Require(LeagueLobbyRoomService.ParseQueueCatalog(Bytes("[" + QueueEntry(1090, "Available", true) +
                "," + QueueEntry(1090, "PlatformDisabled", true) + "]")) == null,
                "Conflicting duplicate queue identities must fail closed.");
        }

        private static string QueueEntry(int id, string availability, bool visible)
        {
            return "{\"id\":" + id + ",\"name\":\"队列 " + id + "\",\"queueAvailability\":\"" +
                availability + "\",\"isVisible\":" + (visible ? "true" : "false") + "}";
        }

        private sealed class RoomFixture : ILeagueClientApi, ILeagueLobbyRoomWriteApi
        {
            internal LeagueClientSession Owner = new LeagueClientSession("LeagueClientUx", 42, 5001, "fixture",
                "https", "smoke", "HN1", "TENCENT");
            internal string Phase = "None";
            internal bool Apply = true;
            internal bool Leader = true;
            internal int Queue;
            internal int Writes;
            internal int StatusCode = 204;
            internal bool Extended, Custom, Spectator, Invite;
            internal int FirstRole = 1, SecondRole = 2;
            internal string Policy = "NotAllowed";
            private static string Flag(bool value) { return value ? "true" : "false"; }
            internal string Catalog = "[" + string.Join(",", new[] { 420, 440, 430, 400, 450, 3270 }
                .Select(id => QueueEntry(id, "Available", true))) + "]";
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (path == LeagueLobbyRoomService.QueueCatalogPath)
                    return Task.FromResult(Bytes(Catalog));
                if (path == LeaguePostGameAutomationController.GameflowPhasePath)
                    return Task.FromResult(Bytes("\"" + Phase + "\""));
                if (path == LeaguePostGameAutomationController.CurrentSummonerPath)
                    return Task.FromResult(Bytes("{\"summonerId\":3000000001}"));
                if (path == LeagueLiveDataService.LobbyPath && Phase == "Lobby" && Extended)
                    return Task.FromResult(Bytes("{\"partyId\":\"fixture-party\",\"gameConfig\":{\"queueId\":" + Queue +
                        ",\"showPositionSelector\":" + Flag(!Custom) + ",\"isCustom\":" + Flag(Custom) +
                        ",\"customSpectatorPolicy\":\"" + Policy + "\"},\"localMember\":{\"summonerId\":3000000001,\"isLeader\":" +
                        Flag(Leader) + ",\"isSpectator\":" + Flag(Spectator) + ",\"allowedToggleInvite\":true,\"firstPositionPreference\":\"" +
                        LeagueLobbyRoomService.RoleName(FirstRole) + "\",\"secondPositionPreference\":\"" +
                        LeagueLobbyRoomService.RoleName(SecondRole) + "\"},\"members\":[{\"summonerId\":3000000001,\"isLeader\":" +
                        Flag(Leader) + "},{\"summonerId\":3000000002,\"isLeader\":" + Flag(!Leader) +
                        ",\"allowedInviteOthers\":" + Flag(Invite) + "}]}"));
                if (path == LeagueLiveDataService.LobbyPath && Phase == "Lobby")
                    return Task.FromResult(Bytes("{\"partyId\":\"fixture-party\",\"gameConfig\":{\"queueId\":" + Queue +
                        "},\"localMember\":{\"summonerId\":3000000001,\"isLeader\":" + (Leader ? "true" : "false") +
                        "},\"members\":[{\"summonerId\":3000000001,\"isLeader\":" + (Leader ? "true" : "false") +
                        "},{\"summonerId\":3000000002,\"isLeader\":" + (Leader ? "false" : "true") + "}]}"));
                return Task.FromResult<byte[]>(null);
            }
            public Task<LeagueClientWriteResponse> TryApplyRoomAsync(LeagueLobbyRoomAction action, long value,
                LeagueClientSession expected, CancellationToken token)
            {
                Require(LeagueEncounterReplayService.SessionMatches(expected, Owner), "Room transport owner changed.");
                Writes++;
                if (Apply)
                {
                    if (action == LeagueLobbyRoomAction.Queue) { Phase = "Lobby"; Queue = (int)value; }
                    if (action == LeagueLobbyRoomAction.Promote) Leader = false;
                    if (action == LeagueLobbyRoomAction.Leave) Phase = "None";
                    if (action == LeagueLobbyRoomAction.Positions) { FirstRole = (int)(value / 10); SecondRole = (int)(value % 10); }
                    if (action == LeagueLobbyRoomAction.GrantInvite) Invite = true;
                    if (action == LeagueLobbyRoomAction.RevokeInvite) Invite = false;
                    if (action == LeagueLobbyRoomAction.Spectator) Spectator = true;
                }
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = StatusCode });
            }
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
