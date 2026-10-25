using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueGameflowReconnectSmokeTest
    {
        public static void Validate()
        {
            Require(!new AppSettings().LeagueAutoReconnectEnabled, "Automatic reconnect must default off.");
            var parsed = AppSettings.ParseLines(new[] { "LeagueAutoReconnectEnabled=True" });
            Require(parsed.LeagueAutoReconnectEnabled &&
                    string.Join("\n", parsed.BuildLines()).Contains("LeagueAutoReconnectEnabled=True"),
                "Reconnect setting did not round-trip.");
            Require(LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("POST", LeagueClientWriteApiClient.ReconnectPath),
                "Reconnect POST was not allowed.");
            Require(!LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("DELETE", LeagueClientWriteApiClient.ReconnectPath) &&
                    !LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-gameflow/v1/reconnect/other"),
                "Reconnect write escaped its exact target and method fence.");
            Require(LeagueGameflowReconnectController.IsReconnectPhase(Bytes("\"Reconnect\"")) &&
                    !LeagueGameflowReconnectController.IsReconnectPhase(Bytes("\"InProgress\"")) &&
                    !LeagueGameflowReconnectController.IsReconnectPhase(Bytes("{\"phase\":\"Reconnect\"}")),
                "Reconnect phase validation accepted ambiguous LCU data.");

            ValidateDefaultsAndRepeatedEvents();
            ValidatePhaseCancellation();
            ValidateDisableAndFreshEpisode();
            ValidatePreflightAndFailureNoRetry();
            ValidateDisposal();
        }

        private static void ValidateDefaultsAndRepeatedEvents()
        {
            var clock = new FakeClock();
            var read = new FakeRead();
            var write = new FakeWrite();
            using (var controller = new LeagueGameflowReconnectController(read, write, clock))
            {
                controller.Observe(Reconnecting());
                Require(clock.Pending.Count == 0, "Reconnect scheduled without consent.");
                controller.Configure(true);
                var task = controller.PendingForSmokeTest;
                controller.Observe(Reconnecting());
                Require(clock.Pending.Count == 1 && clock.Pending[0].Delay == LeagueGameflowReconnectController.ReconnectDelay,
                    "Reconnect repeated schedule or lost the ten-second delay.");
                clock.Release(0);
                task.GetAwaiter().GetResult();
                Require(read.Count == 1 && write.Count == 1, "Reconnect should preflight and send exactly once.");
                controller.Observe(Reconnecting());
                Require(clock.Pending.Count == 1 && write.Count == 1, "Reconnect repeated a submitted write.");
            }
        }

        private static void ValidatePhaseCancellation()
        {
            var clock = new FakeClock();
            var write = new FakeWrite();
            var read = new FakeRead();
            using (var controller = new LeagueGameflowReconnectController(read, write, clock))
            {
                controller.Configure(true);
                controller.Observe(Reconnecting());
                var task = controller.PendingForSmokeTest;
                controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "InProgress" });
                task.GetAwaiter().GetResult();
                Require(clock.Pending[0].Cancelled && read.Count == 0 && write.Count == 0,
                    "Leaving Reconnect did not cancel its delayed command.");
                controller.Observe(Reconnecting());
                Require(clock.Pending.Count == 2, "A later Reconnect episode did not schedule again.");
                clock.Release(1);
                controller.PendingForSmokeTest.GetAwaiter().GetResult();
                Require(write.Count == 1, "A later Reconnect episode did not submit its one write.");
            }
        }

        private static void ValidateDisableAndFreshEpisode()
        {
            var clock = new FakeClock();
            var write = new FakeWrite();
            using (var controller = new LeagueGameflowReconnectController(new FakeRead(), write, clock))
            {
                controller.Configure(true);
                controller.Observe(Reconnecting());
                var previous = controller.PendingForSmokeTest;
                controller.Configure(false);
                previous.GetAwaiter().GetResult();
                Require(clock.Pending[0].Cancelled && write.Count == 0,
                    "Disabling reconnect failed to cancel the pending operation.");
                controller.Configure(true);
                Require(clock.Pending.Count == 2, "Enabling reconnect in the active phase did not reschedule.");
                clock.Release(1);
                controller.PendingForSmokeTest.GetAwaiter().GetResult();
                Require(write.Count == 1, "Re-enabled reconnect did not run once.");
            }
        }

        private static void ValidatePreflightAndFailureNoRetry()
        {
            var clock = new FakeClock();
            var read = new FakeRead { Phase = "\"InProgress\"" };
            var write = new FakeWrite { StatusCode = 403 };
            using (var controller = new LeagueGameflowReconnectController(read, write, clock))
            {
                controller.Configure(true);
                controller.Observe(Reconnecting());
                clock.Release(0);
                controller.PendingForSmokeTest.GetAwaiter().GetResult();
                Require(write.Count == 0, "Stale Reconnect status allowed a write.");
                controller.Observe(new LeagueDashboardPhaseState { Connected = false, Phase = "Reconnect" });
                read.Phase = "\"Reconnect\"";
                controller.Observe(Reconnecting());
                clock.Release(1);
                controller.PendingForSmokeTest.GetAwaiter().GetResult();
                controller.Observe(Reconnecting());
                Require(write.Count == 1 && clock.Pending.Count == 2,
                    "Failed HTTP reconnect must not blindly retry inside the same phase.");
            }
        }

        private static void ValidateDisposal()
        {
            var clock = new FakeClock();
            var write = new FakeWrite();
            var controller = new LeagueGameflowReconnectController(new FakeRead(), write, clock);
            controller.Configure(true);
            controller.Observe(Reconnecting());
            var task = controller.PendingForSmokeTest;
            controller.Dispose();
            task.GetAwaiter().GetResult();
            Require(clock.Pending[0].Cancelled && write.Count == 0, "Disposal left a reconnect write armed.");
        }

        private static LeagueDashboardPhaseState Reconnecting()
        {
            return new LeagueDashboardPhaseState
            {
                Connected = true,
                Phase = "Reconnect",
                Activity = LeagueActivityLevel.InGame
            };
        }

        private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeRead : ILeagueClientApi
        {
            public string Phase = "\"Reconnect\"";
            public int Count;

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                Require(path == LeagueGameflowReconnectController.PhasePath, "Unexpected reconnect preflight path.");
                cancellationToken.ThrowIfCancellationRequested();
                Count++;
                return Task.FromResult(Bytes(Phase));
            }
        }

        private sealed class FakeWrite : ILeagueGameflowReconnectWriteApi
        {
            public int Count;
            public int StatusCode = 204;

            public Task<LeagueClientWriteResponse> TryReconnectAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Count++;
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = StatusCode });
            }
        }

        private sealed class FakeClock : ILeagueMatchmakingClock
        {
            internal sealed class PendingDelay
            {
                public TimeSpan Delay;
                public TaskCompletionSource<bool> Completion =
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                public bool Cancelled;
            }

            public readonly List<PendingDelay> Pending = new List<PendingDelay>();

            public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
            {
                var item = new PendingDelay { Delay = delay };
                Pending.Add(item);
                cancellationToken.Register(() =>
                {
                    item.Cancelled = true;
                    item.Completion.TrySetCanceled();
                });
                return item.Completion.Task;
            }

            public void Release(int index)
            {
                Require(index < Pending.Count, "Expected reconnect delay was not scheduled.");
                Pending[index].Completion.TrySetResult(true);
            }
        }
    }
}
