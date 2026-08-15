using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueMatchmakingAutomationSmokeTest
    {
        public static void Validate()
        {
            ValidateSettings();
            ValidateTransportFence();
            ValidateTencentLobbyWithoutOptionalFields();
            ValidateEligibleLobbyExactlyOnce();
            ValidateLobbyBlocks();
            ValidateTencentReadyCheckWithoutFingerprintFields();
            ValidateReadyCheckExactlyOnce();
            ValidateExplicitReadyResponseBlocks();
            ValidateNonTargetPhase();
        }

        private static void ValidateSettings()
        {
            var settings = new AppSettings();
            Require(!settings.LeagueAutoMatchmakingEnabled && !settings.LeagueAutoAcceptEnabled,
                "Legacy settings must default Gate 7 OFF.");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueAutoMatchmakingEnabled=True");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueAutoAcceptEnabled=True");
            Require(settings.LeagueAutoMatchmakingEnabled && settings.LeagueAutoAcceptEnabled,
                "Gate 7 settings did not parse.");
            var serialized = string.Join("\n", settings.BuildLinesForSmokeTest());
            Require(serialized.Contains("LeagueAutoMatchmakingEnabled=True"), "Auto-matchmaking setting did not serialize.");
            Require(serialized.Contains("LeagueAutoAcceptEnabled=True"), "Auto-accept setting did not serialize.");
        }

        private static void ValidateTransportFence()
        {
            Require(LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeagueMatchmakingWriteApiClient.SearchPath),
                "Gate 7 transport blocked matchmaking search.");
            Require(LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeagueMatchmakingWriteApiClient.AcceptPath),
                "Gate 7 transport blocked ready-check accept.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("DELETE", LeagueMatchmakingWriteApiClient.SearchPath),
                "Gate 7 transport must hard-block matchmaking cancellation.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-matchmaking/v1/ready-check/decline"),
                "Gate 7 transport must hard-block ready-check decline.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-champ-select/v1/session/actions/1"),
                "Gate 7 transport must hard-block Champ Select writes.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeaguePostGameWriteApiClient.PlayAgainPath),
                "Gate 7 transport must not inherit Gate 6 play-again permission.");
        }

        private static void ValidateTencentLobbyWithoutOptionalFields()
        {
            var read = new FakeReadApi();
            // Tencent compatibility fixture: no partyId, queueId is unavailable/0,
            // allowedStartActivity is false, and warnings/restrictions are non-empty.
            // None of those optional fields may veto a client-reported canStartActivity Lobby.
            read.Set(LeagueMatchmakingAutomationController.LobbyPath,
                LobbyJson(0, true, false, true, new[] { "self" }, true));
            var write = new FakeWriteApi();
            using (var controller = new LeagueMatchmakingAutomationController(read, write, new FakeClock()))
                controller.EvaluateLobbyOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(write.Calls.Count(call => call.Path == LeagueMatchmakingWriteApiClient.SearchPath) == 1,
                "Tencent Lobby without partyId/queueId/allowedStartActivity must still start matchmaking when canStartActivity+leader are true.");
        }

        private static void ValidateEligibleLobbyExactlyOnce()
        {
            var read = new FakeReadApi();
            read.Set(LeagueMatchmakingAutomationController.LobbyPath,
                LobbyJson(420, true, true, true, new[] { "self", "ally" }, false));
            var write = new FakeWriteApi();
            using (var controller = new LeagueMatchmakingAutomationController(read, write, new FakeClock()))
            {
                controller.EvaluateLobbyOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
                Require(write.Calls.Count(call => call.Path == LeagueMatchmakingWriteApiClient.SearchPath) == 1,
                    "Eligible Lobby must start matchmaking exactly once.");
                controller.EvaluateLobbyOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
                Require(write.Calls.Count(call => call.Path == LeagueMatchmakingWriteApiClient.SearchPath) == 1,
                    "Same Lobby fingerprint must not repeat matchmaking search.");

                read.Set(LeagueMatchmakingAutomationController.LobbyPath,
                    LobbyJson(420, true, true, true, new[] { "self", "ally", "ally2" }, false));
                controller.EvaluateLobbyOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
                Require(write.Calls.Count(call => call.Path == LeagueMatchmakingWriteApiClient.SearchPath) == 2,
                    "Changed member context should allow one new matchmaking attempt.");
            }
        }

        private static void ValidateLobbyBlocks()
        {
            AssertLobbyBlocked(LobbyJson(420, false, true, true, new[] { "self" }, false), "canStartActivity=false");
            AssertLobbyBlocked(LobbyJson(420, true, true, false, new[] { "self" }, false), "not leader");
            AssertLobbyBlocked(LobbyJson(420, true, true, true, new string[0], false), "no real members");
        }

        private static void ValidateTencentReadyCheckWithoutFingerprintFields()
        {
            var read = new FakeReadApi();
            // Missing/partial search data must not block a ReadyCheck phase accept attempt.
            read.Set(LeagueMatchmakingAutomationController.SearchStatePath,
                "{\"isCurrentlyInQueue\":true,\"readyCheck\":{\"state\":\"InProgress\",\"playerResponse\":\"None\"}}");
            var write = new FakeWriteApi();
            using (var controller = new LeagueMatchmakingAutomationController(read, write, new FakeClock()))
                controller.EvaluateReadyOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(write.Calls.Count(call => call.Path == LeagueMatchmakingWriteApiClient.AcceptPath) == 1,
                "Tencent ReadyCheck without lobbyId/queueId must still accept once.");

            read = new FakeReadApi();
            write = new FakeWriteApi();
            using (var controller = new LeagueMatchmakingAutomationController(read, write, new FakeClock()))
                controller.EvaluateReadyOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(write.Calls.Count(call => call.Path == LeagueMatchmakingWriteApiClient.AcceptPath) == 1,
                "Unavailable /lol-matchmaking/v1/search must be best-effort and must not block ReadyCheck accept.");
        }

        private static void ValidateReadyCheckExactlyOnce()
        {
            var read = new FakeReadApi();
            read.Set(LeagueMatchmakingAutomationController.SearchStatePath,
                "{\"readyCheck\":{\"state\":\"InProgress\",\"playerResponse\":\"None\"}}");
            var write = new FakeWriteApi();
            using (var controller = new LeagueMatchmakingAutomationController(read, write, new FakeClock()))
            {
                controller.EvaluateReadyOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
                controller.EvaluateReadyOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            Require(write.Calls.Count(call => call.Path == LeagueMatchmakingWriteApiClient.AcceptPath) == 1,
                "Same continuous ReadyCheck episode must accept exactly once.");
        }

        private static void ValidateExplicitReadyResponseBlocks()
        {
            AssertReadyBlocked("{\"readyCheck\":{\"state\":\"InProgress\",\"playerResponse\":\"Accepted\"}}", "already accepted");
            AssertReadyBlocked("{\"readyCheck\":{\"state\":\"InProgress\",\"playerResponse\":\"Declined\"}}", "user declined");

            var read = new FakeReadApi();
            read.Set(LeagueMatchmakingAutomationController.SearchStatePath,
                "{\"readyCheck\":{\"state\":\"None\",\"playerResponse\":\"None\"}}");
            var write = new FakeWriteApi();
            using (var controller = new LeagueMatchmakingAutomationController(read, write, new FakeClock()))
                controller.EvaluateReadyOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(write.Calls.Count(call => call.Path == LeagueMatchmakingWriteApiClient.AcceptPath) == 1,
                "Search-state shape/state must not be a hard prerequisite once Gameflow is ReadyCheck.");
        }

        private static void ValidateNonTargetPhase()
        {
            var read = new FakeReadApi();
            var write = new FakeWriteApi();
            using (var controller = new LeagueMatchmakingAutomationController(read, write, new FakeClock()))
            {
                controller.Configure(true, true);
                controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "InProgress" });
                controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "ChampSelect" });
                controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "EndOfGame" });
            }
            Require(read.RequestCount == 0 && write.Calls.Count == 0,
                "Non Lobby/ReadyCheck phases must cause zero Gate 7 read/write work.");
        }

        private static void AssertLobbyBlocked(string lobbyJson, string reason)
        {
            var read = new FakeReadApi();
            read.Set(LeagueMatchmakingAutomationController.LobbyPath, lobbyJson);
            var write = new FakeWriteApi();
            using (var controller = new LeagueMatchmakingAutomationController(read, write, new FakeClock()))
                controller.EvaluateLobbyOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(write.Calls.Count == 0, "Blocked Lobby emitted search: " + reason);
        }

        private static void AssertReadyBlocked(string searchJson, string reason)
        {
            var read = new FakeReadApi();
            read.Set(LeagueMatchmakingAutomationController.SearchStatePath, searchJson);
            var write = new FakeWriteApi();
            using (var controller = new LeagueMatchmakingAutomationController(read, write, new FakeClock()))
                controller.EvaluateReadyOnceForSmokeTestAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(write.Calls.Count == 0, "Blocked ReadyCheck emitted accept: " + reason);
        }

        private static string LobbyJson(int queueId, bool canStart, bool allowed, bool leader, string[] members, bool noisyMetadata)
        {
            var memberJson = string.Join(",", (members ?? new string[0]).Select(value =>
                "{\"puuid\":\"" + value + "\",\"summonerId\":123,\"isBot\":false,\"isSpectator\":false}"));
            var metadata = noisyMetadata ? "[{\"code\":\"informational\"}]" : "[]";
            return "{" +
                   "\"canStartActivity\":" + canStart.ToString().ToLowerInvariant() + "," +
                   "\"localMember\":{\"allowedStartActivity\":" + allowed.ToString().ToLowerInvariant() + ",\"isLeader\":" + leader.ToString().ToLowerInvariant() + "}," +
                   "\"gameConfig\":{\"queueId\":" + queueId + "}," +
                   "\"members\":[" + memberJson + "]," +
                   "\"restrictions\":" + metadata + ",\"warnings\":" + metadata + "}";
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeReadApi : ILeagueClientApi
        {
            private readonly Dictionary<string, byte[]> _responses = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            public int RequestCount;
            public void Set(string path, string json) { _responses[path] = Encoding.UTF8.GetBytes(json); }
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequestCount++;
                byte[] value;
                _responses.TryGetValue(path, out value);
                return Task.FromResult(value);
            }
        }

        private sealed class FakeWriteApi : ILeagueMatchmakingWriteApi
        {
            internal sealed class Call { public string Method; public string Path; }
            public readonly List<Call> Calls = new List<Call>();
            public Task<LeagueClientWriteResponse> TrySendAsync(string method, string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls.Add(new Call { Method = method, Path = path });
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 204, Body = new byte[0] });
            }
        }

        private sealed class FakeClock : ILeagueMatchmakingClock
        {
            public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }
    }
}
