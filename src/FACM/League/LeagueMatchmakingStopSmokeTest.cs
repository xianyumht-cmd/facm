using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueMatchmakingStopSmokeTest
    {
        public static void Validate()
        {
            ValidatePolicyCodec();
            ValidateSearchStateProjection();
            ValidateFixedDurationThreshold();
            ValidateReadyCheckFence();
            ValidateWriteFence();
            ValidateTransientRecoveryAndSessionFence();
            ValidateTransientRecoveryFlow();
            AppSettings.ValidateMatchmakingStopPreferencesForSmokeTest();
        }

        private static void ValidatePolicyCodec()
        {
            Require(LeagueMatchmakingStopPolicyCodec.Parse("never") == LeagueMatchmakingStopPolicy.Never,
                "Matchmaking stop policy lost never mapping.");
            Require(LeagueMatchmakingStopPolicyCodec.Parse("FIXED") == LeagueMatchmakingStopPolicy.FixedTime,
                "Matchmaking stop policy lost fixed mapping.");
            Require(LeagueMatchmakingStopPolicyCodec.Parse("estimated") == LeagueMatchmakingStopPolicy.EstimatedTime,
                "Matchmaking stop policy lost estimated mapping.");
            Require(LeagueMatchmakingStopPolicyCodec.Parse("unexpected") == LeagueMatchmakingStopPolicy.Never,
                "Unknown matchmaking stop policy did not fail closed to never.");
        }

        private static void ValidateSearchStateProjection()
        {
            var below = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"searchState\":\"Searching\",\"timeInQueue\":41.5,\"estimatedQueueTime\":50}"));
            Require(below != null && below.IsCurrentlyInQueue && below.HasUsableEstimate,
                "Matchmaking stop parser lost queue estimate context.");
            Require(!LeagueMatchmakingStopController.ShouldStopForEstimate(below),
                "Estimated stop fired before the client estimate was reached.");

            var reached = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"searchState\":\"Searching\",\"timeInQueue\":50,\"estimatedQueueTimeInSeconds\":50}"));
            Require(reached != null && LeagueMatchmakingStopController.ShouldStopForEstimate(reached),
                "Estimated stop did not fire when the client estimate was reached.");

            var penalized = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"searchState\":\"Searching\",\"timeInQueue\":75,\"estimatedQueueTime\":60,\"lowPriorityData\":{\"penaltyTime\":20}}"));
            Require(penalized != null && penalized.PenaltyTimeSeconds == 20 &&
                    !LeagueMatchmakingStopController.ShouldStopForEstimate(penalized),
                "Estimated stop counted low-priority penalty waiting time as real queue time.");
            penalized.TimeInQueueSeconds = 80;
            Require(LeagueMatchmakingStopController.ShouldStopForEstimate(penalized),
                "Estimated stop did not use the queue duration after penalty time elapsed.");

            var transitioning = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"searchState\":\"Found\",\"timeInQueue\":999,\"estimatedQueueTime\":60}"));
            Require(transitioning != null && !LeagueMatchmakingStopController.ShouldStopForEstimate(transitioning),
                "Estimated stop could delete a matchmaking search after Searching ended.");
            var withoutState = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"timeInQueue\":999,\"estimatedQueueTime\":60}"));
            Require(withoutState != null && !LeagueMatchmakingStopController.ShouldStopForEstimate(withoutState),
                "Unknown search state did not fail closed.");
            var malformedPenalty = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"searchState\":\"Searching\",\"timeInQueue\":999,\"estimatedQueueTime\":60,\"lowPriorityData\":{\"penaltyTime\":\"bad\"}}"));
            Require(malformedPenalty != null && !LeagueMatchmakingStopController.ShouldStopForEstimate(malformedPenalty),
                "Invalid low-priority penalty data did not fail closed.");

            var missing = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"timeInQueue\":999}"));
            Require(missing != null && !missing.HasUsableEstimate && !LeagueMatchmakingStopController.ShouldStopForEstimate(missing),
                "Missing matchmaking estimate did not fail closed.");

            var stopped = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":false,\"timeInQueue\":80,\"estimatedQueueTime\":60}"));
            Require(stopped != null && !LeagueMatchmakingStopController.ShouldStopForEstimate(stopped),
                "Stopped matchmaking state was treated as an active estimated-stop candidate.");

            Require(LeagueMatchmakingStopController.ParseSearchState(Bytes("not-json")) == null,
                "Malformed matchmaking search payload did not fail closed.");
            Require(LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"searchState\":\"Searching\",\"timeInQueue\":999}")) == null &&
                LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":\"false\",\"searchState\":\"Searching\"}")) == null,
                "Unknown queue membership must never confirm a successful DELETE.");
        }

        private static void ValidateReadyCheckFence()
        {
            Require(LeagueMatchmakingStopController.IsMatchmakingForSmokeTest("Matchmaking", LeagueActivityLevel.Queueing),
                "Matchmaking stop controller lost Matchmaking recognition.");
            Require(!LeagueMatchmakingStopController.IsMatchmakingForSmokeTest("ReadyCheck", LeagueActivityLevel.Queueing),
                "Matchmaking stop controller could remain armed in ReadyCheck.");
            Require(!LeagueMatchmakingStopController.IsMatchmakingForSmokeTest("Lobby", LeagueActivityLevel.Client),
                "Matchmaking stop controller remained armed in Lobby.");
        }

        private static void ValidateFixedDurationThreshold()
        {
            var normal = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"searchState\":\"Searching\",\"timeInQueue\":100}"));
            Require(normal != null && LeagueMatchmakingStopController.FixedStopRemaining(normal, 120000) == TimeSpan.FromSeconds(20),
                "Fixed stop did not account for existing actual queue time.");
            Require(!LeagueMatchmakingStopController.ShouldStopForFixed(normal, 120000),
                "Fixed stop fired before its queue-duration threshold.");

            var penalized = LeagueMatchmakingStopController.ParseSearchState(Bytes(
                "{\"isCurrentlyInQueue\":true,\"searchState\":\"Searching\",\"timeInQueue\":170,\"lowPriorityData\":{\"penaltyTime\":60}}"));
            Require(penalized != null && LeagueMatchmakingStopController.FixedStopRemaining(penalized, 120000) == TimeSpan.FromSeconds(10),
                "Fixed stop counted the low-priority penalty as searchable queue time.");
            penalized.TimeInQueueSeconds = 180;
            Require(LeagueMatchmakingStopController.ShouldStopForFixed(penalized, 120000),
                "Fixed stop missed the exact post-penalty duration threshold.");

            penalized.SearchState = "Found";
            Require(!LeagueMatchmakingStopController.FixedStopRemaining(penalized, 120000).HasValue,
                "Fixed stop remained eligible after search-state transition.");
            penalized.SearchState = "Searching";
            penalized.PenaltyTimeSeconds = -1;
            Require(!LeagueMatchmakingStopController.ShouldStopForFixed(penalized, 120000),
                "Fixed stop accepted invalid low-priority penalty time.");
            normal.TimeInQueueSeconds = null;
            Require(!LeagueMatchmakingStopController.ShouldStopForFixed(normal, 120000),
                "Fixed stop guessed a queue duration when time data was unavailable.");
        }

        private static void ValidateTransientRecoveryAndSessionFence()
        {
            Require(LeagueMatchmakingStopController.MissingSearchRetryDelay(1) == TimeSpan.FromSeconds(2) &&
                LeagueMatchmakingStopController.MissingSearchRetryDelay(2) == TimeSpan.FromSeconds(4) &&
                LeagueMatchmakingStopController.MissingSearchRetryDelay(5) == TimeSpan.FromSeconds(30) &&
                LeagueMatchmakingStopController.MissingSearchRetryDelay(100) == TimeSpan.FromSeconds(30),
                "Missing matchmaking reads must back off without permanently abandoning the active phase.");
            var original = new LeagueClientSession("LeagueClientUx", 1, 2999, "one", "https", "test");
            var same = new LeagueClientSession("LeagueClientUx", 1, 2999, "one", "https", "test");
            var replacement = new LeagueClientSession("LeagueClientUx", 2, 2999, "two", "https", "test");
            Require(LeagueMatchmakingStopController.SameEpisodeSession(original, same) &&
                !LeagueMatchmakingStopController.SameEpisodeSession(original, replacement) &&
                !LeagueMatchmakingStopController.SameEpisodeSession(original, null),
                "Matchmaking stop must abandon a previous LCU session before a DELETE.");
        }

        private static void ValidateTransientRecoveryFlow()
        {
            var session = new LeagueClientSession("LeagueClientUx", 1, 2999, "one", "https", "test");
            var active = Bytes("{\"isCurrentlyInQueue\":true,\"searchState\":\"Searching\",\"timeInQueue\":120}");
            var stopped = Bytes("{\"isCurrentlyInQueue\":false,\"searchState\":\"Idle\"}");
            var reader = new StopSequenceReader(null, null, active, active,
                Bytes("{\"searchState\":\"Searching\"}"), active, active, stopped);
            var writer = new StopCountingWriter();
            var clock = new StopImmediateClock();
            using (var controller = new LeagueMatchmakingStopController(reader, writer, () => session, clock))
            {
                controller.Configure(LeagueMatchmakingStopPolicy.FixedTime, 60000);
                controller.Observe(new LeagueDashboardPhaseState
                {
                    Connected = true, Phase = "Matchmaking", Activity = LeagueActivityLevel.Queueing
                });
                Require(writer.Calls == 2 && reader.Calls == 7 &&
                    clock.Delays.Count == 2 && clock.Delays[0] == TimeSpan.FromSeconds(2) &&
                    clock.Delays[1] == LeagueMatchmakingStopController.RetryDelay,
                    "Missing LCU data or unknown post-write membership abandoned or falsely confirmed queue stop.");
            }

            var replacement = new LeagueClientSession("LeagueClientUx", 2, 2999, "two", "https", "test");
            var current = session;
            var switchedReader = new StopSequenceReader(() => current = replacement, active);
            var switchedWriter = new StopCountingWriter();
            using (var controller = new LeagueMatchmakingStopController(switchedReader, switchedWriter,
                () => current, new StopImmediateClock()))
            {
                controller.Configure(LeagueMatchmakingStopPolicy.FixedTime, 60000);
                controller.Observe(new LeagueDashboardPhaseState
                {
                    Connected = true, Phase = "Matchmaking", Activity = LeagueActivityLevel.Queueing
                });
                Require(switchedWriter.Calls == 0,
                    "Previous LCU matchmaking episode issued DELETE after session replacement.");
            }

            var disconnectedReader = new StopSequenceReader(null, active);
            using (var controller = new LeagueMatchmakingStopController(disconnectedReader,
                new StopCountingWriter(), () => session, new StopImmediateClock()))
            {
                controller.Configure(LeagueMatchmakingStopPolicy.FixedTime, 60000);
                controller.Observe(new LeagueDashboardPhaseState
                {
                    Connected = false, Phase = "Matchmaking", Activity = LeagueActivityLevel.Queueing
                });
                Require(disconnectedReader.Calls == 0,
                    "Disconnected Gameflow must not start a queue cancellation episode.");
            }
        }

        private sealed class StopSequenceReader : ILeagueClientApi
        {
            private readonly Queue<byte[]> _responses;
            private readonly Action _firstRead;
            internal int Calls;

            internal StopSequenceReader(Action firstRead, params byte[][] responses)
            {
                _firstRead = firstRead;
                _responses = new Queue<byte[]>(responses);
            }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                if (path != LeagueMatchmakingStopController.SearchStatePath)
                    throw new InvalidOperationException("Unexpected matchmaking read route.");
                if (++Calls == 1) _firstRead?.Invoke();
                return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : null);
            }
        }

        private sealed class StopCountingWriter : ILeagueMatchmakingWriteApi
        {
            internal int Calls;
            public Task<LeagueClientWriteResponse> TrySendAsync(string method, string path, CancellationToken token)
            {
                if (method != "DELETE" || path != LeagueMatchmakingWriteApiClient.SearchPath)
                    throw new InvalidOperationException("Unexpected matchmaking write route.");
                Calls++;
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 200 });
            }
        }

        private sealed class StopImmediateClock : ILeagueMatchmakingClock
        {
            internal readonly List<TimeSpan> Delays = new List<TimeSpan>();
            public Task Delay(TimeSpan delay, CancellationToken token)
            {
                Delays.Add(delay);
                if (Delays.Count > 8)
                    throw new InvalidOperationException("Matchmaking recovery exceeded its bounded test sequence.");
                return Task.CompletedTask;
            }
        }

        private static void ValidateWriteFence()
        {
            Require(LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeagueMatchmakingWriteApiClient.SearchPath),
                "Matchmaking start write was removed from the transport allowlist.");
            Require(LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("DELETE", LeagueMatchmakingWriteApiClient.SearchPath),
                "Matchmaking stop write is not fenced into the transport allowlist.");
            Require(LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeagueMatchmakingWriteApiClient.AcceptPath),
                "ReadyCheck accept write was removed from the transport allowlist.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("DELETE", LeagueMatchmakingWriteApiClient.AcceptPath),
                "ReadyCheck endpoint incorrectly accepts DELETE.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("DELETE", "/lol-lobby/v2/lobby"),
                "Matchmaking stop transport can delete the lobby.");
            Require(!LeagueMatchmakingWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-chat/v1/me"),
                "Matchmaking transport escaped its narrow endpoint allowlist.");
        }

        private static byte[] Bytes(string value)
        {
            return Encoding.UTF8.GetBytes(value ?? string.Empty);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
