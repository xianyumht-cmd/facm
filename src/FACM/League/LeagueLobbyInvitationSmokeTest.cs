using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueLobbyInvitationSmokeTest
    {
        internal static void Validate()
        {
            ValidateSettingsAndCloud();
            ValidateWriteFence();
            ValidatePolicies();
            ValidateInvitationAcceptAndNoDuplicate();
            ValidateInvitationDeclineAndAway();
            ValidateInvalidAndStaleInvitations();
            ValidateCancelledPreflight();
            ValidateLeaderPromotion();
            ValidateDisabledByDefault();
        }

        private static void ValidateSettingsAndCloud()
        {
            var settings = new AppSettings();
            Require(!settings.LeagueAutoHandleInvitationsEnabled && !settings.LeagueAutoSkipLeaderEnabled &&
                !settings.LeagueSkipInvitationsWhenAway, "Invitation automation and away veto defaults must match pinned v1.5.1.");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueAutoHandleInvitationsEnabled=True");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueAutoSkipLeaderEnabled=True");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueSkipInvitationsWhenAway=False");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueInvitationStrategies={\"RANKED_SOLO_5x5\":\"accept\",\"<DEFAULT>\":\"decline\"}");
            Require(settings.LeagueAutoHandleInvitationsEnabled && settings.LeagueAutoSkipLeaderEnabled &&
                !settings.LeagueSkipInvitationsWhenAway && settings.LeagueInvitationStrategies.Contains("RANKED_SOLO_5x5"),
                "Invitation settings failed to parse.");
            var roundTrip = AppSettings.ParseLines(settings.BuildLinesForSmokeTest());
            Require(roundTrip.LeagueAutoHandleInvitationsEnabled && roundTrip.LeagueAutoSkipLeaderEnabled &&
                roundTrip.LeagueInvitationStrategies == settings.LeagueInvitationStrategies,
                "Invitation INI persistence failed.");
            var cloud = CloudSettingsSnapshot.Capture(settings);
            var restored = new AppSettings();
            cloud.ApplyTo(restored);
            Require(restored.LeagueAutoHandleInvitationsEnabled && restored.LeagueAutoSkipLeaderEnabled &&
                restored.LeagueInvitationStrategies == settings.LeagueInvitationStrategies &&
                !restored.LeagueSkipInvitationsWhenAway, "Cloud invitation settings roundtrip failed.");
            GgmanPortableSettingsStore.Validate(new GgmanPortableSettingsProfile { Settings = cloud });
            cloud.LeagueInvitationStrategies = "{bad}";
            try
            {
                GgmanPortableSettingsStore.Validate(new GgmanPortableSettingsProfile { Settings = cloud });
                throw new InvalidOperationException("Invalid cloud invitation strategy was accepted.");
            }
            catch (System.IO.InvalidDataException) { }
        }

        private static void ValidateWriteFence()
        {
            Require(Allowed("POST", "/lol-lobby/v2/received-invitations/abc-12/accept"),
                "Invitation accept was blocked.");
            Require(Allowed("POST", "/lol-lobby/v2/received-invitations/abc-12/decline"),
                "Invitation decline was blocked.");
            Require(Allowed("POST", "/lol-lobby/v2/lobby/members/12345/promote"),
                "Lobby leader promotion was blocked.");
            Require(!Allowed("POST", "/lol-lobby/v2/received-invitations/../../danger/accept") &&
                !Allowed("DELETE", "/lol-lobby/v2/received-invitations/abc-12/accept") &&
                !Allowed("POST", "/lol-lobby/v2/received-invitations/abc-12/revoke") &&
                !Allowed("POST", "/lol-lobby/v2/lobby/members/0/promote") &&
                !Allowed("POST", "/lol-lobby/v2/lobby/members/123/create") &&
                !Allowed("POST", "/lol-lobby/v2/lobby/members/-3/promote") &&
                !Allowed("POST", "/lol-lobby/v2/lobby/members/1%2F2/promote"),
                "LCU write allowlist unexpectedly admitted an unsafe target.");
        }

        private static void ValidatePolicies()
        {
            var policies = LeagueInvitationPolicies.Parse("{\"<DEFAULT>\":\"accept\",\"ARAM_UNRANKED_5x5\":\"ignore\"}");
            Require(LeagueInvitationPolicies.Resolve(policies, "UNKNOWN") == LeagueInvitationPolicies.Accept &&
                LeagueInvitationPolicies.Resolve(policies, "ARAM_UNRANKED_5x5") == LeagueInvitationPolicies.Ignore,
                "Per-mode ignore must override the default strategy.");
            Require(LeagueInvitationPolicies.Parse("{\"<DEFAULT>\":\"delete\"}").Count == 0 &&
                LeagueInvitationPolicies.Parse("{\"UNEXPECTED\":\"accept\"}").Count == 0,
                "Invalid policies must fail closed.");
        }

        private static void ValidateInvitationAcceptAndNoDuplicate()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueLobbyInvitationController.ReceivedInvitationsPath,
                Invitations("inv-1", "RANKED_SOLO_5x5", "Pending", true));
            using (var controller = new LeagueLobbyInvitationController(read, write))
            {
                controller.Configure(true, false, "{\"RANKED_SOLO_5x5\":\"accept\"}", false);
                Run(controller, "None");
                Run(controller, "None");
            }
            Require(write.Calls.Count == 1 && write.Calls[0].EndsWith("/inv-1/accept", StringComparison.Ordinal),
                "Eligible invitation must be accepted once.");
        }

        private static void ValidateInvitationDeclineAndAway()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueLobbyInvitationController.ReceivedInvitationsPath,
                Invitations("inv-2", "NORMAL", "Pending", true));
            read.Set(LeagueLobbyInvitationController.ChatMePath, "{\"availability\":\"away\"}");
            using (var controller = new LeagueLobbyInvitationController(read, write))
            {
                controller.Configure(true, true, "{\"<DEFAULT>\":\"decline\"}", false);
                Run(controller, "Lobby");
                Require(write.Calls.Count == 0, "Away state must defer invitation decline.");
                read.Set(LeagueLobbyInvitationController.ChatMePath, "{\"availability\":\"chat\"}");
                Run(controller, "Lobby");
            }
            Require(write.Calls.Count == 1 && write.Calls[0].EndsWith("/inv-2/decline", StringComparison.Ordinal),
                "Available client must apply its configured decline policy.");
        }

        private static void ValidateInvalidAndStaleInvitations()
        {
            var read = new Read();
            var write = new Write();
            using (var controller = new LeagueLobbyInvitationController(read, write))
            {
                controller.Configure(true, false, "{\"<DEFAULT>\":\"accept\"}", false);
                read.Set(LeagueLobbyInvitationController.ReceivedInvitationsPath,
                    Invitations("../../invalid", "NORMAL", "Pending", true));
                Run(controller, "None");
                read.Set(LeagueLobbyInvitationController.ReceivedInvitationsPath,
                    Invitations("inv-3", "NORMAL", "Pending", false));
                Run(controller, "None");
                read.Set(LeagueLobbyInvitationController.ReceivedInvitationsPath,
                    Invitations("inv-3", "NORMAL", "Accepted", true));
                Run(controller, "None");
            }
            Require(write.Calls.Count == 0, "Invalid, blocked, or settled invitations must not be modified.");

            var staleRead = new Read();
            var staleWrite = new Write();
            staleRead.SetSequence(LeagueLobbyInvitationController.ReceivedInvitationsPath,
                Invitations("inv-4", "NORMAL", "Pending", true),
                Invitations("inv-4", "NORMAL", "Accepted", true));
            using (var controller = new LeagueLobbyInvitationController(staleRead, staleWrite))
            {
                controller.Configure(true, false, "{\"<DEFAULT>\":\"accept\"}", false);
                Run(controller, "None");
            }
            Require(staleWrite.Calls.Count == 0, "Settled invitation must block a stale preflight write.");
        }

        private static void ValidateCancelledPreflight()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueLobbyInvitationController.ReceivedInvitationsPath,
                Invitations("inv-cancel", "NORMAL", "Pending", true));
            using (var controller = new LeagueLobbyInvitationController(read, write))
            using (var cts = new CancellationTokenSource())
            {
                controller.Configure(true, false, "{\"<DEFAULT>\":\"decline\"}", false);
                cts.Cancel();
                try
                {
                    controller.EvaluateOnceForSmokeTestAsync("None", cts.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Cancelled read must stop before any invitation POST.");
                }
                catch (OperationCanceledException) { }
            }
            Require(write.Calls.Count == 0, "Cancelled invitation episode performed a write.");
        }

        private static void ValidateLeaderPromotion()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueLobbyInvitationController.LobbyPath,
                "{\"partyId\":\"party-1\",\"localMember\":{\"summonerId\":10,\"isLeader\":true}," +
                "\"members\":[{\"summonerId\":10,\"ready\":true},{\"summonerId\":20,\"ready\":false}," +
                "{\"summonerId\":30,\"ready\":true}]}");
            using (var controller = new LeagueLobbyInvitationController(read, write))
            {
                controller.Configure(false, true, "{}", true);
                Run(controller, "Lobby");
                Run(controller, "Lobby");
                Run(controller, "InProgress");
            }
            Require(write.Calls.Count == 1 && write.Calls[0] ==
                "POST /lol-lobby/v2/lobby/members/30/promote", "Ready party member should get leadership once.");
            read.Set(LeagueLobbyInvitationController.LobbyPath,
                "{\"localMember\":{\"summonerId\":10,\"isLeader\":false},\"members\":[{\"summonerId\":20}]}");
            using (var controller = new LeagueLobbyInvitationController(read, write))
            {
                controller.Configure(false, true, "{}", true);
                Run(controller, "Lobby");
            }
            Require(write.Calls.Count == 1, "Non-leader must not promote another member.");
        }

        private static void ValidateDisabledByDefault()
        {
            var read = new Read();
            var write = new Write();
            read.Set(LeagueLobbyInvitationController.ReceivedInvitationsPath,
                Invitations("inv-5", "NORMAL", "Pending", true));
            using (var controller = new LeagueLobbyInvitationController(read, write))
            {
                Run(controller, "None");
            }
            Require(read.Count == 0 && write.Calls.Count == 0, "Disabled automation must not read or write lobby APIs.");
        }

        private static void Run(LeagueLobbyInvitationController controller, string phase)
        {
            controller.EvaluateOnceForSmokeTestAsync(phase, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static bool Allowed(string method, string path)
        {
            return LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest(method, path);
        }

        private static string Invitations(string id, string game, string state, bool canAccept)
        {
            return "[{\"invitationId\":\"" + id + "\",\"state\":\"" + state +
                "\",\"canAcceptInvitation\":" + (canAccept ? "true" : "false") +
                ",\"gameConfig\":{\"inviteGameType\":\"" + game + "\"}}]";
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class Read : ILeagueClientApi
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, Queue<string>> _sequences = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
            internal int Count;
            internal void Set(string path, string json) { _values[path] = json; }
            internal void SetSequence(string path, params string[] responses)
            {
                _sequences[path] = new Queue<string>(responses);
            }
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref Count);
                string value;
                Queue<string> queue;
                if (_sequences.TryGetValue(path, out queue) && queue.Count > 0)
                    return Task.FromResult(Encoding.UTF8.GetBytes(queue.Dequeue()));
                return Task.FromResult(_values.TryGetValue(path, out value) ? Encoding.UTF8.GetBytes(value) : null);
            }
        }

        private sealed class Write : ILeagueMatchmakingWriteApi
        {
            internal readonly List<string> Calls = new List<string>();
            public Task<LeagueClientWriteResponse> TrySendAsync(string method, string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls.Add(method + " " + path);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 204, Body = new byte[0] });
            }
        }
    }
}
