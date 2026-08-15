using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal static class LeaguePostGameAutomationSmokeTest
    {
        public static void Validate()
        {
            ValidateTransportFence();
            ValidateHonorThenReturn();
            ValidateHonorFailureStillReturns();
            ValidateExactlyOnceCycle();
            ValidateReturnOnly();
            ValidatePhaseContract();
        }

        private static void ValidateTransportFence()
        {
            Require(LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeaguePostGameWriteApiClient.HonorPath),
                "Gate 6 transport blocked honor write.");
            Require(LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeaguePostGameWriteApiClient.HonorBallotSubmitPath),
                "Gate 6 transport blocked ballot submit.");
            Require(LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeaguePostGameWriteApiClient.PlayAgainPath),
                "Gate 6 transport blocked play-again.");
            Require(!LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-matchmaking/v1/ready-check/accept"),
                "Gate 6 transport must hard-block ready-check accept.");
            Require(!LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-lobby/v2/lobby/matchmaking/search"),
                "Gate 6 transport must hard-block matchmaking search.");
            Require(!LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("PATCH", "/lol-champ-select/v1/session/actions/1"),
                "Gate 6 transport must hard-block Champ Select action writes.");
            Require(!LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("DELETE", LeaguePostGameWriteApiClient.PlayAgainPath),
                "Gate 6 transport must reject non-POST verbs.");
        }

        private static void ValidateHonorThenReturn()
        {
            var read = new FakeReadApi();
            read.Set(LeaguePostGameAutomationController.BallotPath, BallotJson());
            read.Set(LeaguePostGameAutomationController.CurrentSummonerPath, "{\"puuid\":\"self\"}");
            var write = new FakePostGameWriteApi();
            var clock = new FakeClock();
            using (var controller = new LeaguePostGameAutomationController(read, write, clock, count => 0))
            {
                controller.RunCycleForSmokeTestAsync("EndOfGame", true, true, CancellationToken.None).GetAwaiter().GetResult();
            }

            var honorWrites = write.Calls.Where(call => call.Path == LeaguePostGameWriteApiClient.HonorPath).ToList();
            Require(honorWrites.Count == 1, "Gate 6 must honor at most one teammate even when vote pool > 1.");
            Require(honorWrites[0].Json != null && honorWrites[0].Json.Contains("\"honorType\":\"HEART\""),
                "Gate 6 honor category must stay HEART for first Tencent candidate.");
            Require(honorWrites[0].Json.Contains("ally-a"), "Gate 6 did not choose an eligible ally.");
            Require(!honorWrites[0].Json.Contains("enemy"), "Gate 6 must never honor an opponent.");
            Require(!honorWrites[0].Json.Contains("self"), "Gate 6 must exclude local player defensively.");
            Require(write.Calls.Count(call => call.Path == LeaguePostGameWriteApiClient.HonorBallotSubmitPath) == 1,
                "Successful honor must submit ballot exactly once.");
            Require(write.Calls.Count(call => call.Path == LeaguePostGameWriteApiClient.PlayAgainPath) == 1,
                "Successful honor+return transaction must play-again exactly once.");
        }

        private static void ValidateHonorFailureStillReturns()
        {
            var read = new FakeReadApi();
            read.Set(LeaguePostGameAutomationController.BallotPath, BallotJson());
            read.Set(LeaguePostGameAutomationController.CurrentSummonerPath, "{\"puuid\":\"self\"}");
            var write = new FakePostGameWriteApi { FailHonor = true };
            using (var controller = new LeaguePostGameAutomationController(read, write, new FakeClock(), count => 0))
            {
                controller.RunCycleForSmokeTestAsync("EndOfGame", true, true, CancellationToken.None).GetAwaiter().GetResult();
            }
            Require(write.Calls.Count(call => call.Path == LeaguePostGameWriteApiClient.HonorPath) == 1,
                "Failed honor must not retry within the same transaction.");
            Require(write.Calls.Count(call => call.Path == LeaguePostGameWriteApiClient.HonorBallotSubmitPath) == 0,
                "Failed honor must not submit ballot.");
            Require(write.Calls.Count(call => call.Path == LeaguePostGameWriteApiClient.PlayAgainPath) == 1,
                "Honor failure must not permanently block auto-return lobby.");
        }

        private static void ValidateExactlyOnceCycle()
        {
            var read = new FakeReadApi();
            read.Set(LeaguePostGameAutomationController.BallotPath, BallotJson());
            read.Set(LeaguePostGameAutomationController.CurrentSummonerPath, "{\"puuid\":\"self\"}");
            var write = new FakePostGameWriteApi();
            using (var controller = new LeaguePostGameAutomationController(read, write, new FakeClock(), count => 0))
            {
                controller.Configure(true, true);
                var end = State("EndOfGame");
                controller.ObserveForSmokeTestAsync(end).GetAwaiter().GetResult();
                var afterFirst = write.Calls.Count;
                controller.ObserveForSmokeTestAsync(State("PreEndOfGame")).GetAwaiter().GetResult();
                controller.ObserveForSmokeTestAsync(State("EndOfGame")).GetAwaiter().GetResult();
                Require(write.Calls.Count == afterFirst,
                    "Repeated post-game phase observations must not start a second transaction in the same cycle.");

                controller.ObserveForSmokeTestAsync(State("Lobby")).GetAwaiter().GetResult();
                controller.ObserveForSmokeTestAsync(end).GetAwaiter().GetResult();
                Require(write.Calls.Count > afterFirst,
                    "Leaving post-game must reset the cycle for the next game.");
            }
        }

        private static void ValidateReturnOnly()
        {
            var read = new FakeReadApi();
            var write = new FakePostGameWriteApi();
            var clock = new FakeClock();
            using (var controller = new LeaguePostGameAutomationController(read, write, clock, count => 0))
            {
                controller.RunCycleForSmokeTestAsync("PreEndOfGame", false, true, CancellationToken.None).GetAwaiter().GetResult();
            }
            Require(read.RequestCount == 0, "Return-only mode must not poll honor endpoints.");
            Require(write.Calls.Count == 1 && write.Calls[0].Path == LeaguePostGameWriteApiClient.PlayAgainPath,
                "Return-only mode must emit exactly one play-again write.");
            Require(clock.Delays.Count == 1 && clock.Delays[0] == TimeSpan.FromMilliseconds(3250),
                "PreEndOfGame return-only delay changed unexpectedly.");
        }

        private static void ValidatePhaseContract()
        {
            Require(LeaguePostGameAutomationController.IsPostGamePhase("WaitingForStats"), "WaitingForStats must be post-game.");
            Require(LeaguePostGameAutomationController.IsPostGamePhase("PreEndOfGame"), "PreEndOfGame must be post-game.");
            Require(LeaguePostGameAutomationController.IsPostGamePhase("EndOfGame"), "EndOfGame must be post-game.");
            Require(!LeaguePostGameAutomationController.IsPostGamePhase("InProgress"), "InProgress must never run Gate 6.");
            Require(!LeaguePostGameAutomationController.IsPostGamePhase("ReadyCheck"), "ReadyCheck must never run Gate 6.");
            Require(!LeaguePostGameAutomationController.IsPostGamePhase("ChampSelect"), "ChampSelect must never run Gate 6.");
            Require(!LeaguePostGameAutomationController.IsPostGamePhase("Lobby"), "Lobby must not be treated as post-game.");
        }

        private static LeagueDashboardPhaseState State(string phase)
        {
            return new LeagueDashboardPhaseState { Connected = true, Phase = phase };
        }

        private static byte[] BallotJson()
        {
            var json = "{" +
                "\"gameId\":123," +
                "\"votePool\":{\"votes\":3}," +
                "\"eligibleAllies\":[" +
                    "{\"botPlayer\":false,\"puuid\":\"self\"}," +
                    "{\"botPlayer\":false,\"puuid\":\"ally-a\"}," +
                    "{\"botPlayer\":false,\"puuid\":\"ally-a\"}," +
                    "{\"botPlayer\":true,\"puuid\":\"bot\"}" +
                "]," +
                "\"eligibleOpponents\":[{\"botPlayer\":false,\"puuid\":\"enemy\"}]" +
                "}";
            return Encoding.UTF8.GetBytes(json);
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
            public void Set(string path, byte[] bytes) { _responses[path] = bytes; }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequestCount++;
                byte[] value;
                _responses.TryGetValue(path, out value);
                return Task.FromResult(value);
            }
        }

        private sealed class FakePostGameWriteApi : ILeaguePostGameWriteApi
        {
            internal sealed class Call
            {
                public string Method;
                public string Path;
                public string Json;
            }

            public readonly List<Call> Calls = new List<Call>();
            public bool FailHonor;

            public Task<LeagueClientWriteResponse> TrySendAsync(
                string method,
                string path,
                string json,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls.Add(new Call { Method = method, Path = path, Json = json });
                return Task.FromResult(new LeagueClientWriteResponse
                {
                    StatusCode = FailHonor && path == LeaguePostGameWriteApiClient.HonorPath ? 500 : 204,
                    Body = new byte[0]
                });
            }
        }

        private sealed class FakeClock : ILeaguePostGameClock
        {
            public readonly List<TimeSpan> Delays = new List<TimeSpan>();
            public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Delays.Add(delay);
                return Task.CompletedTask;
            }
        }
    }
}
