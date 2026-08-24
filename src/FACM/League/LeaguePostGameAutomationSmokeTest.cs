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
            ValidateBallotCompatibility();
            ValidateV2VerifiedByTeamChoices();
            ValidateResponseLostButAppliedDoesNotRetry();
            ValidateSafeRetryAfterReadbackProvesNotApplied();
            ValidateV2UnsupportedFallsBackToLegacy();
            ValidateNoVoteSkipsWrite();
            ValidateExactlyOnceCycle();
            ValidateReturnOnly();
            ValidatePhaseContract();
        }

        private static void ValidateTransportFence()
        {
            Require(LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeaguePostGameWriteApiClient.HonorV2Path),
                "Gate 6 transport blocked Honor V2 write.");
            Require(LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeaguePostGameWriteApiClient.HonorPath),
                "Gate 6 transport blocked legacy honor write.");
            Require(LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeaguePostGameWriteApiClient.HonorBallotSubmitPath),
                "Gate 6 transport blocked ballot submit.");
            Require(LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeaguePostGameWriteApiClient.PlayAgainPath),
                "Gate 6 transport blocked play-again.");
            Require(!LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-matchmaking/v1/ready-check/accept"),
                "Gate 6 transport must hard-block ready-check accept.");
            Require(!LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("PATCH", "/lol-champ-select/v1/session/actions/1"),
                "Gate 6 transport must hard-block Champ Select action writes.");
            Require(!LeaguePostGameWriteApiClient.IsAllowedTargetForSmokeTest("DELETE", LeaguePostGameWriteApiClient.PlayAgainPath),
                "Gate 6 transport must reject non-POST verbs.");
        }

        private static void ValidateBallotCompatibility()
        {
            var read = new FakeReadApi();
            var write = new FakePostGameWriteApi();
            using (var controller = new LeaguePostGameAutomationController(read, write, new FakeClock(), count => 0))
            {
                var modern = controller.ParseBallot(Bytes(BallotJson(1, true, true)));
                Require(modern != null && modern.GameId == 123 && modern.Votes == 1 && modern.HasVoteCount,
                    "Modern Honor V2 ballot metadata did not parse.");
                Require(modern.Allies.Count == 3 && modern.Allies.Any(item => item.SummonerId == 101) && modern.Allies.Any(item => item.BotPlayer),
                    "Modern eligibleAllies/summonerId did not parse.");

                var legacyShape = controller.ParseBallot(Bytes(
                    "{\"gameId\":456,\"eligiblePlayers\":[{\"puuid\":\"ally-old\",\"summonerID\":202,\"botPlayer\":false}]}"));
                Require(legacyShape != null && legacyShape.GameId == 456 && legacyShape.Votes == 1 && !legacyShape.HasVoteCount,
                    "Compatible Honor ballot without votePool did not receive a safe single-vote default.");
                Require(legacyShape.Allies.Count == 1 && legacyShape.Allies[0].SummonerId == 202,
                    "eligiblePlayers/summonerID compatibility parsing failed.");
            }
            Require(LeaguePostGameAutomationController.BallotPath == "/lol-honor-v2/v1/ballot",
                "Honor V2 ballot path must not rely on a trailing-slash redirect.");
        }

        private static void ValidateV2VerifiedByTeamChoices()
        {
            var read = StandardRead();
            read.Set(LeaguePostGameAutomationController.TeamChoicesPath, "[101]");
            var write = new FakePostGameWriteApi();
            LeagueHonorAttemptStatus result = null;
            using (var controller = new LeaguePostGameAutomationController(read, write, new FakeClock(), count => 0))
            {
                controller.HonorAttemptCompleted += status => result = status;
                controller.RunCycleForSmokeTestAsync("EndOfGame", true, false, CancellationToken.None).GetAwaiter().GetResult();
            }

            var calls = write.Calls.Where(call => call.Path == LeaguePostGameWriteApiClient.HonorV2Path).ToList();
            Require(calls.Count == 1, "Honor V2 success must emit exactly one write.");
            Require(calls[0].Json.Contains("\"summonerId\":101") && calls[0].Json.Contains("\"gameId\":123"),
                "Honor V2 request lost summonerId/gameId context.");
            Require(calls[0].Json.Contains("ally-a") && calls[0].Json.Contains("\"honorType\":\"HEART\""),
                "Honor V2 request lost target or honor category.");
            Require(write.Calls.All(call => call.Path != LeaguePostGameWriteApiClient.HonorPath),
                "Verified Honor V2 must not also write the legacy route.");
            Require(result != null && result.State == "success" && result.Route == "v2" && result.Attempts == 1,
                "Team-choices verification did not produce a confirmed Honor V2 result.");
        }

        private static void ValidateResponseLostButAppliedDoesNotRetry()
        {
            var read = StandardRead();
            read.Set(LeaguePostGameAutomationController.TeamChoicesPath, "[101]");
            var write = new FakePostGameWriteApi();
            write.V2Statuses.Enqueue(null);
            LeagueHonorAttemptStatus result = null;
            using (var controller = new LeaguePostGameAutomationController(read, write, new FakeClock(), count => 0))
            {
                controller.HonorAttemptCompleted += status => result = status;
                controller.RunCycleForSmokeTestAsync("EndOfGame", true, false, CancellationToken.None).GetAwaiter().GetResult();
            }

            Require(write.Calls.Count(call => call.Path == LeaguePostGameWriteApiClient.HonorV2Path) == 1,
                "A lost HTTP response with positive readback must never trigger a duplicate vote.");
            Require(result != null && result.State == "success" && result.HttpStatus == 0,
                "Positive readback must win over an ambiguous transport result.");
        }

        private static void ValidateSafeRetryAfterReadbackProvesNotApplied()
        {
            var read = StandardRead();
            read.Set(LeaguePostGameAutomationController.TeamChoicesPath, "[]");
            var write = new FakePostGameWriteApi();
            write.V2Statuses.Enqueue(500);
            write.V2Statuses.Enqueue(204);
            write.OnV2Call = count =>
            {
                if (count == 2) read.Set(LeaguePostGameAutomationController.TeamChoicesPath, "[101]");
            };
            LeagueHonorAttemptStatus result = null;
            using (var controller = new LeaguePostGameAutomationController(read, write, new FakeClock(), count => 0))
            {
                controller.HonorAttemptCompleted += status => result = status;
                controller.RunCycleForSmokeTestAsync("EndOfGame", true, false, CancellationToken.None).GetAwaiter().GetResult();
            }

            Require(write.Calls.Count(call => call.Path == LeaguePostGameWriteApiClient.HonorV2Path) == 2,
                "An explicit failed submit plus unchanged authoritative readback must get exactly one safe retry.");
            Require(result != null && result.State == "success" && result.Attempts == 2 && result.Detail.Contains("safe-retry"),
                "Safe retry did not finish as a verified success.");
        }

        private static void ValidateV2UnsupportedFallsBackToLegacy()
        {
            var read = StandardRead();
            read.Set(LeaguePostGameAutomationController.TeamChoicesPath, "[]");
            var write = new FakePostGameWriteApi();
            write.V2Statuses.Enqueue(404);
            write.OnBallotSubmit = delegate
            {
                read.Set(LeaguePostGameAutomationController.BallotPath, BallotJson(0, false, false));
            };
            LeagueHonorAttemptStatus result = null;
            using (var controller = new LeaguePostGameAutomationController(read, write, new FakeClock(), count => 0))
            {
                controller.HonorAttemptCompleted += status => result = status;
                controller.RunCycleForSmokeTestAsync("EndOfGame", true, false, CancellationToken.None).GetAwaiter().GetResult();
            }

            Require(write.Calls.Count(call => call.Path == LeaguePostGameWriteApiClient.HonorV2Path) == 1,
                "Unsupported Honor V2 route must be attempted once.");
            Require(write.Calls.Count(call => call.Path == LeaguePostGameWriteApiClient.HonorPath) == 1,
                "Honor V2 404/405 must fall back to the legacy honor route once.");
            Require(write.Calls.Count(call => call.Path == LeaguePostGameWriteApiClient.HonorBallotSubmitPath) == 1,
                "Legacy fallback must submit its ballot once.");
            Require(result != null && result.State == "success" && result.Route == "legacy",
                "Legacy fallback was not verified successfully.");
        }

        private static void ValidateNoVoteSkipsWrite()
        {
            var read = StandardRead();
            read.Set(LeaguePostGameAutomationController.BallotPath, BallotJson(0, true, true));
            var write = new FakePostGameWriteApi();
            LeagueHonorAttemptStatus result = null;
            using (var controller = new LeaguePostGameAutomationController(read, write, new FakeClock(), count => 0))
            {
                controller.HonorAttemptCompleted += status => result = status;
                controller.RunCycleForSmokeTestAsync("EndOfGame", true, false, CancellationToken.None).GetAwaiter().GetResult();
            }
            Require(write.Calls.Count == 0, "No-vote ballot must not emit any honor write.");
            Require(result != null && result.State == "skipped" && result.Detail == "no-votes",
                "No-vote ballot did not produce an observable skipped result.");
        }

        private static void ValidateExactlyOnceCycle()
        {
            var read = StandardRead();
            read.Set(LeaguePostGameAutomationController.TeamChoicesPath, "[101]");
            var write = new FakePostGameWriteApi();
            using (var controller = new LeaguePostGameAutomationController(read, write, new FakeClock(), count => 0))
            {
                controller.Configure(true, false);
                var end = State("EndOfGame");
                controller.ObserveForSmokeTestAsync(end).GetAwaiter().GetResult();
                var afterFirst = write.Calls.Count;
                controller.ObserveForSmokeTestAsync(State("PreEndOfGame")).GetAwaiter().GetResult();
                controller.ObserveForSmokeTestAsync(State("EndOfGame")).GetAwaiter().GetResult();
                Require(write.Calls.Count == afterFirst,
                    "Repeated post-game phase observations must not start a second honor transaction in the same game.");

                controller.ObserveForSmokeTestAsync(State("Lobby")).GetAwaiter().GetResult();
                controller.ObserveForSmokeTestAsync(end).GetAwaiter().GetResult();
                Require(write.Calls.Count > afterFirst,
                    "Leaving post-game must reset the exactly-once cycle for the next game.");
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

        private static FakeReadApi StandardRead()
        {
            var read = new FakeReadApi();
            read.Set(LeaguePostGameAutomationController.BallotPath, BallotJson(1, true, true));
            read.Set(LeaguePostGameAutomationController.CurrentSummonerPath, "{\"puuid\":\"self\"}");
            read.Set(LeaguePostGameAutomationController.TeamChoicesPath, "[]");
            read.Set(LeaguePostGameAutomationController.VoteCompletionPath, "{\"fullTeamVote\":false,\"gameId\":123}");
            return read;
        }

        private static LeagueDashboardPhaseState State(string phase)
        {
            return new LeagueDashboardPhaseState { Connected = true, Phase = phase };
        }

        private static string BallotJson(int votes, bool includeSelf, bool includeAlly)
        {
            var rows = new List<string>();
            if (includeSelf) rows.Add("{\"botPlayer\":false,\"puuid\":\"self\",\"summonerId\":100}");
            if (includeAlly) rows.Add("{\"botPlayer\":false,\"puuid\":\"ally-a\",\"summonerId\":101}");
            rows.Add("{\"botPlayer\":true,\"puuid\":\"bot\",\"summonerId\":999}");
            return "{" +
                "\"gameId\":123," +
                "\"votePool\":{\"votes\":" + votes + "}," +
                "\"eligibleAllies\":[" + string.Join(",", rows) + "]," +
                "\"eligibleOpponents\":[{\"botPlayer\":false,\"puuid\":\"enemy\",\"summonerId\":777}]" +
                "}";
        }

        private static byte[] Bytes(string json)
        {
            return Encoding.UTF8.GetBytes(json ?? string.Empty);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeReadApi : ILeagueClientApi
        {
            private readonly Dictionary<string, byte[]> _responses = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            public int RequestCount;

            public void Set(string path, string json) { _responses[path] = Bytes(json); }
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
            public readonly Queue<int?> V2Statuses = new Queue<int?>();
            public Action<int> OnV2Call;
            public Action OnBallotSubmit;
            private int _v2Calls;

            public Task<LeagueClientWriteResponse> TrySendAsync(
                string method,
                string path,
                string json,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls.Add(new Call { Method = method, Path = path, Json = json });
                if (path == LeaguePostGameWriteApiClient.HonorV2Path)
                {
                    _v2Calls++;
                    if (OnV2Call != null) OnV2Call(_v2Calls);
                    if (V2Statuses.Count > 0)
                    {
                        var code = V2Statuses.Dequeue();
                        if (!code.HasValue) return Task.FromResult<LeagueClientWriteResponse>(null);
                        return Task.FromResult(Response(code.Value));
                    }
                    return Task.FromResult(Response(204));
                }
                if (path == LeaguePostGameWriteApiClient.HonorBallotSubmitPath && OnBallotSubmit != null)
                    OnBallotSubmit();
                return Task.FromResult(Response(204));
            }

            private static LeagueClientWriteResponse Response(int status)
            {
                return new LeagueClientWriteResponse { StatusCode = status, Body = new byte[0] };
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
