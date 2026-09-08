using System;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueGameflowEventDispatcher
    {
        internal static int DispatchSafely(
            Action<LeagueDashboardPhaseState> handler,
            LeagueDashboardPhaseState state,
            string scope)
        {
            if (handler == null) return 0;

            var failures = 0;
            foreach (var entry in handler.GetInvocationList())
            {
                var subscriber = entry as Action<LeagueDashboardPhaseState>;
                if (subscriber == null) continue;
                try
                {
                    // Every subscriber gets its own snapshot. A UI or automation consumer that
                    // mutates its argument cannot corrupt the canonical state or another consumer.
                    subscriber(Clone(state));
                }
                catch (Exception exception)
                {
                    failures++;
                    AppLog.Info(
                        "League Gameflow subscriber failed; scope=" + (scope ?? "unknown") +
                        "; handler=" + Describe(subscriber) +
                        "; error=" + exception.GetType().Name + ": " + exception.Message);
                }
            }
            return failures;
        }

        internal static LeagueDashboardPhaseState Clone(LeagueDashboardPhaseState state)
        {
            return state == null ? null : new LeagueDashboardPhaseState
            {
                Connected = state.Connected,
                ClientProcessDetected = state.ClientProcessDetected,
                GameProcessDetected = state.GameProcessDetected,
                Phase = state.Phase,
                Activity = state.Activity,
                BudgetName = state.BudgetName,
                UpdatedAtUtc = state.UpdatedAtUtc
            };
        }

        private static string Describe(Delegate subscriber)
        {
            if (subscriber == null || subscriber.Method == null) return "unknown";
            var type = subscriber.Method.DeclaringType;
            return (type == null ? "unknown" : type.Name) + "." + subscriber.Method.Name;
        }
    }

    internal sealed class LeagueGameflowMonitor : IDisposable
    {
        private readonly object _sync = new object();
        private readonly LeagueDashboardPhaseService _phaseService;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private LeagueDashboardPhaseState _current;
        private bool _started;
        private bool _disposed;

        public LeagueGameflowMonitor(ILeagueClientApi client, PerformanceBudgetProvider budgets)
        {
            _phaseService = new LeagueDashboardPhaseService(client, budgets);
        }

        public event Action<LeagueDashboardPhaseState> StateChanged;

        public LeagueDashboardPhaseState Current
        {
            get
            {
                lock (_sync) return LeagueGameflowEventDispatcher.Clone(_current);
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started || _disposed) return;
                _started = true;
            }
            Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            while (!_lifetime.IsCancellationRequested)
            {
                LeagueDashboardPhaseState next = null;
                try
                {
                    next = await _phaseService.RefreshAsync(_lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (_lifetime.IsCancellationRequested) return;
                }
                catch (Exception exception)
                {
                    AppLog.Info("League Gameflow monitor refresh skipped: " + exception.Message);
                }

                if (next != null)
                {
                    lock (_sync) _current = LeagueGameflowEventDispatcher.Clone(next);
                    LeagueGameflowEventDispatcher.DispatchSafely(StateChanged, next, "monitor");
                }

                try
                {
                    await Task.Delay(ResolveDelay(next), _lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        internal static TimeSpan ResolveDelay(LeagueDashboardPhaseState state)
        {
            // A disconnected/not-yet-running client is exactly where a long sleep is most
            // noticeable: startup and reconnect automation cannot react until this monitor
            // observes the new session. Match the established lightweight recovery cadence.
            if (state == null || !state.Connected) return TimeSpan.FromSeconds(3);
            switch (state.Activity)
            {
                case LeagueActivityLevel.ChampSelect: return TimeSpan.FromSeconds(2);
                case LeagueActivityLevel.Queueing: return TimeSpan.FromSeconds(3);
                case LeagueActivityLevel.InGame: return TimeSpan.FromSeconds(10);
                default: return TimeSpan.FromSeconds(5);
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _lifetime.Cancel();
        }
    }
}
