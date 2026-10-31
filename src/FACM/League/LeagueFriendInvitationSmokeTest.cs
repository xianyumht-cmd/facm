using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal static class LeagueFriendInvitationSmokeTest
    {
        private const string FriendPuuid = "friend-puuid-123456789";
        private const string OtherPuuid = "other-puuid-987654321";

        internal static void Validate()
        {
            ValidateWriteShapeAndFence();
            ValidateIdleAndScheduledOnlineTransition();
            ValidatePermissionAndAlreadyJoined();
            ValidateLobbyReplacementAndExit();
            ValidateFriendRemovedAndInvalidData();
            ValidateCancelBeforeWrite();
            ValidateOptionalCustomGameNotice();
            ValidateNoticeGuards();
        }

        private static void ValidateWriteShapeAndFence()
        {
            Require(LeagueMatchmakingWriteApiClient.BuildOutgoingInvitationJson(12345) ==
                "[{\"toSummonerId\":12345}]", "Pinned Akari 1.5.1 outgoing invitation payload changed.");
            Require(LeagueMatchmakingWriteApiClient.IsAllowedOutgoingInviteForSmokeTest(12345) &&
                !LeagueMatchmakingWriteApiClient.IsAllowedOutgoingInviteForSmokeTest(0) &&
                !LeagueMatchmakingWriteApiClient.IsAllowedOutgoingInviteForSmokeTest(-1),
                "Outbound invitation must require a positive numeric summoner ID.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest(
                "POST", LeagueMatchmakingWriteApiClient.OutgoingInvitationsPath),
                "The bodyless generic write route must not allow outgoing invitation POSTs.");
            Require(LeagueFriendInvitationController.IsSafePuuid(FriendPuuid) &&
                !LeagueFriendInvitationController.IsSafePuuid("../../danger") &&
                !LeagueFriendInvitationController.IsSafePuuid("short"),
                "Friend identifiers must be validated before temporary scheduling.");
            Require(LeagueMatchmakingWriteApiClient.IsSafeConversationId("room-12@group") &&
                !LeagueMatchmakingWriteApiClient.IsSafeConversationId("../room/12") &&
                !LeagueMatchmakingWriteApiClient.IsSafeConversationId(""),
                "Room notice path segments must be restricted to safe conversation IDs.");
            var body = LeagueMatchmakingWriteApiClient.BuildRoomNoticeJson("room-12@group", "已邀请 Test 加入房间");
            var notice = new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>;
            Require(notice != null && Equals(notice["type"], "celebration") && Equals(notice["isHistorical"], false) &&
                Equals(notice["body"], "已邀请 Test 加入房间"),
                "Room notification must use the pinned Akari celebration payload.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest(
                "POST", LeagueMatchmakingWriteApiClient.ConversationsPrefix + "room-12@group/messages"),
                "General matchmaking writes must never gain arbitrary chat access.");
        }

        private static void ValidateOptionalCustomGameNotice()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-notice", true));
            read.Set(LeagueFriendInvitationController.FriendsPath, Friends("chat", "offline"));
            read.Set(LeagueFriendInvitationController.ConversationsPath,
                "[{\"id\":\"room-12@group\",\"type\":\"customGame\"}]");
            using (var controller = new LeagueFriendInvitationController(read, write, write))
            {
                controller.Observe(Phase("Lobby"));
                var catalog = Load(controller);
                Require(!controller.AnnounceEnabled && controller.SetAnnounce(catalog.LobbyKey, true),
                    "Chat notice must require an explicit per-lobby choice.");
                Require(controller.SetScheduled(catalog.LobbyKey, FriendPuuid, true),
                    "Could not schedule friend for chat notice.");
                Run(controller);
                Run(controller);
                Require(write.Calls.Count == 1 && write.Notices.Count == 1 &&
                    write.Notices[0] == "room-12@group|已邀请 Example #CN1 加入房间",
                    "Successful invited friend should receive exactly one optional custom-game notice.");
                controller.Observe(Phase("ChampSelect"));
                Require(!controller.AnnounceEnabled,
                    "Room notice choice must reset on gameflow exit.");
            }

            var noNoticeRead = new Read();
            var noNoticeWrite = new Write();
            noNoticeRead.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-no-notice", true));
            noNoticeRead.Set(LeagueFriendInvitationController.FriendsPath, Friends("chat", "offline"));
            using (var controller = new LeagueFriendInvitationController(noNoticeRead, noNoticeWrite, noNoticeWrite))
            {
                controller.Observe(Phase("Lobby"));
                var catalog = Load(controller);
                controller.SetScheduled(catalog.LobbyKey, FriendPuuid, true);
                Run(controller);
                Require(noNoticeWrite.Calls.Count == 1 && noNoticeWrite.Notices.Count == 0 &&
                    noNoticeRead.GetCount(LeagueFriendInvitationController.ConversationsPath) == 0,
                    "Default-off notice must perform no chat reads or writes.");
            }
        }

        private static void ValidateNoticeGuards()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-stale-notice", true));
            read.Set(LeagueFriendInvitationController.FriendsPath, Friends("chat", "offline"));
            read.Set(LeagueFriendInvitationController.ConversationsPath,
                "[{\"id\":\"room-12@group\",\"type\":\"customGame\"}]");
            using (var controller = new LeagueFriendInvitationController(read, write, write))
            {
                controller.Observe(Phase("Lobby"));
                var catalog = Load(controller);
                controller.SetAnnounce(catalog.LobbyKey, true);
                controller.SetScheduled(catalog.LobbyKey, FriendPuuid, true);
                read.BeforeRead = (path, count) =>
                {
                    if (path == LeagueFriendInvitationController.ConversationsPath)
                        read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-replaced", true));
                };
                Run(controller);
                Require(write.Calls.Count == 1 && write.Notices.Count == 0,
                    "Conversation notice must not target a room after lobby replacement.");
            }

            read = new Read();
            write = new Write { InviteStatus = 500 };
            read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-failed-invite", true));
            read.Set(LeagueFriendInvitationController.FriendsPath, Friends("chat", "offline"));
            using (var controller = new LeagueFriendInvitationController(read, write, write))
            {
                controller.Observe(Phase("Lobby"));
                var catalog = Load(controller);
                controller.SetAnnounce(catalog.LobbyKey, true);
                controller.SetScheduled(catalog.LobbyKey, FriendPuuid, true);
                Run(controller);
                Require(write.Notices.Count == 0 && read.GetCount(LeagueFriendInvitationController.ConversationsPath) == 0,
                    "Failed invitation must never announce success.");
            }
        }

        private static void ValidateIdleAndScheduledOnlineTransition()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-1", true));
            read.Set(LeagueFriendInvitationController.FriendsPath, Friends("offline", "offline"));
            using (var controller = new LeagueFriendInvitationController(read, write))
            {
                controller.Observe(Phase("Lobby"));
                Require(read.Count == 0, "No scheduled friend must mean no background friend reads.");
                var catalog = Load(controller);
                Require(catalog != null && catalog.Friends.Count == 2 &&
                    catalog.Friends.All(friend => !friend.Selected), "On-demand friend list was not loaded.");
                Require(controller.SetScheduled(catalog.LobbyKey, FriendPuuid, true), "Scheduling an offline friend failed.");
                Run(controller);
                Require(write.Calls.Count == 0 && controller.PendingCount == 1,
                    "Offline friend must stay scheduled without an invite.");

                read.Set(LeagueFriendInvitationController.FriendsPath, Friends("chat", "offline"));
                Run(controller);
                Run(controller);
                Require(write.Calls.SequenceEqual(new long[] { 12345 }) && controller.PendingCount == 0,
                    "Online friend should get exactly one invitation and leave the queue.");
                var next = Load(controller);
                Require(next.Friends.First(friend => friend.Puuid == FriendPuuid).Attempted &&
                    !controller.SetScheduled(next.LobbyKey, FriendPuuid, true),
                    "Repeated scheduling in the same lobby must not resend an invitation.");
                Require(controller.SetScheduled(next.LobbyKey, OtherPuuid, true),
                    "Another eligible friend should remain schedulable.");
                Require(controller.SetScheduled(next.LobbyKey, OtherPuuid, false) && controller.PendingCount == 0,
                    "The user must be able to cancel a pending friend invite.");
            }
        }

        private static void ValidatePermissionAndAlreadyJoined()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-2", false));
            read.Set(LeagueFriendInvitationController.FriendsPath, Friends("chat", "offline"));
            using (var controller = new LeagueFriendInvitationController(read, write))
            {
                controller.Observe(Phase("Lobby"));
                var catalog = Load(controller);
                Require(controller.SetScheduled(catalog.LobbyKey, FriendPuuid, true), "Friend scheduling failed.");
                Run(controller);
                Require(write.Calls.Count == 0 && controller.PendingCount == 1,
                    "No invite is allowed when localMember cannot invite others.");

                read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-2", true, FriendPuuid));
                Run(controller);
                Require(write.Calls.Count == 0, "Existing party members must never receive an invitation.");

                read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-2", true));
                Run(controller);
                Require(write.Calls.Count == 1, "Permission recovery should allow a fresh checked invite.");
            }
        }

        private static void ValidateLobbyReplacementAndExit()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-3", true));
            read.Set(LeagueFriendInvitationController.FriendsPath, Friends("offline", "offline"));
            using (var controller = new LeagueFriendInvitationController(read, write))
            {
                controller.Observe(Phase("Lobby"));
                var catalog = Load(controller);
                Require(controller.SetScheduled(catalog.LobbyKey, FriendPuuid, true), "Friend scheduling failed.");
                read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-4", true));
                read.Set(LeagueFriendInvitationController.FriendsPath, Friends("chat", "offline"));
                Run(controller);
                Require(controller.PendingCount == 0 && write.Calls.Count == 0,
                    "A new lobby must discard a prior lobby's pending invitations.");
                Require(!controller.SetScheduled(catalog.LobbyKey, OtherPuuid, true),
                    "An old on-demand catalog must not be allowed to select for the new lobby.");
                var fresh = Load(controller);
                Require(controller.SetScheduled(fresh.LobbyKey, OtherPuuid, true),
                    "The current lobby should be able to schedule a new friend.");
                controller.Observe(Phase("ChampSelect"));
                Require(controller.PendingCount == 0 && !controller.SetScheduled(fresh.LobbyKey, OtherPuuid, true),
                    "Leaving Lobby must immediately clear the ephemeral queue.");
                Require(write.Calls.Count == 0, "Leaving Lobby should prevent stale writes.");
            }
        }

        private static void ValidateFriendRemovedAndInvalidData()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-5", true));
            read.Set(LeagueFriendInvitationController.FriendsPath, Friends("offline", "offline"));
            using (var controller = new LeagueFriendInvitationController(read, write))
            {
                controller.Observe(Phase("Lobby"));
                var catalog = Load(controller);
                Require(controller.SetScheduled(catalog.LobbyKey, FriendPuuid, true), "Friend scheduling failed.");
                read.Set(LeagueFriendInvitationController.FriendsPath,
                    "[{\"puuid\":\"invalid/identifier\",\"availability\":\"chat\",\"summonerId\":12345}]");
                Run(controller);
                Require(controller.PendingCount == 0 && write.Calls.Count == 0,
                    "A deleted or malformed friend must be removed without inviting.");
                var invalid = Load(controller);
                Require(invalid != null && invalid.Friends.Count == 0, "Malformed PUUID must not become a selectable friend.");
                read.Set(LeagueFriendInvitationController.FriendsPath, "{broken-json");
                Require(Load(controller) == null, "Malformed friend list must fail closed.");
            }
        }

        private static void ValidateCancelBeforeWrite()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueFriendInvitationController.LobbyPath, Lobby("party-6", true));
            read.Set(LeagueFriendInvitationController.FriendsPath, Friends("chat", "offline"));
            using (var controller = new LeagueFriendInvitationController(read, write))
            using (var cts = new CancellationTokenSource())
            {
                controller.Observe(Phase("Lobby"));
                var catalog = Load(controller);
                Require(controller.SetScheduled(catalog.LobbyKey, FriendPuuid, true), "Friend scheduling failed.");
                read.BeforeRead = (path, count) =>
                {
                    if (path == LeagueFriendInvitationController.FriendsPath && count == 2) cts.Cancel();
                };
                try
                {
                    controller.EvaluateOnceForSmokeTestAsync(cts.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Cancelled invitation preflight was not canceled.");
                }
                catch (OperationCanceledException) { }
                Require(write.Calls.Count == 0, "Canceled preflight must never submit an outgoing invitation.");
            }
        }

        private static LeagueFriendInvitationCatalog Load(LeagueFriendInvitationController controller)
        {
            return controller.LoadCatalogAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private static void Run(LeagueFriendInvitationController controller)
        {
            controller.EvaluateOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private static LeagueDashboardPhaseState Phase(string phase)
        {
            return new LeagueDashboardPhaseState { Connected = true, Phase = phase };
        }

        private static string Lobby(string partyId, bool allowInvite, string memberPuuid = null)
        {
            var members = memberPuuid == null ? "[]" : "[{\"puuid\":\"" + memberPuuid + "\"}]";
            return "{\"partyId\":\"" + partyId + "\",\"localMember\":{\"summonerId\":100,\"allowedInviteOthers\":" +
                (allowInvite ? "true" : "false") + "},\"members\":" + members + "}";
        }

        private static string Friends(string firstAvailability, string secondAvailability)
        {
            return "[{\"puuid\":\"" + FriendPuuid + "\",\"gameName\":\"Example\",\"gameTag\":\"CN1\",\"availability\":\"" +
                firstAvailability + "\",\"summonerId\":12345},{\"puuid\":\"" + OtherPuuid +
                "\",\"gameName\":\"Friend\",\"availability\":\"" + secondAvailability + "\",\"summonerId\":23456}]";
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class Read : ILeagueClientApi
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
            internal int Count;
            internal Action<string, int> BeforeRead;

            internal void Set(string path, string value) { _values[path] = value; }

            internal int GetCount(string path)
            {
                int count;
                return _counts.TryGetValue(path, out count) ? count : 0;
            }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Count++;
                int previous;
                _counts.TryGetValue(path, out previous);
                _counts[path] = previous + 1;
                if (BeforeRead != null) BeforeRead(path, _counts[path]);
                cancellationToken.ThrowIfCancellationRequested();
                string value;
                return Task.FromResult(_values.TryGetValue(path, out value) ? Encoding.UTF8.GetBytes(value) : null);
            }
        }

        private sealed class Write : ILeagueLobbyInviteWriteApi, ILeagueLobbyNoticeWriteApi
        {
            internal readonly List<long> Calls = new List<long>();
            internal readonly List<string> Notices = new List<string>();
            internal int InviteStatus = 204;
            public Task<LeagueClientWriteResponse> TryInviteAsync(long summonerId, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls.Add(summonerId);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = InviteStatus, Body = new byte[0] });
            }

            public Task<LeagueClientWriteResponse> TrySendNoticeAsync(
                string conversationId, string message, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Notices.Add(conversationId + "|" + message);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 201, Body = new byte[0] });
            }
        }
    }
}
