using System;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
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
                lock (_sync) return Clone(_current);
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
                    lock (_sync) _current = Clone(next);
                    var handler = StateChanged;
                    if (handler != null) handler(Clone(next));
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
            if (state == null || !state.Connected) return TimeSpan.FromSeconds(10);
            switch (state.Activity)
            {
                case LeagueActivityLevel.ChampSelect: return TimeSpan.FromSeconds(2);
                case LeagueActivityLevel.Queueing: return TimeSpan.FromSeconds(3);
                case LeagueActivityLevel.InGame: return TimeSpan.FromSeconds(10);
                default: return TimeSpan.FromSeconds(5);
            }
        }

        private static LeagueDashboardPhaseState Clone(LeagueDashboardPhaseState state)
        {
            return state == null ? null : new LeagueDashboardPhaseState
            {
                Connected = state.Connected,
                Phase = state.Phase,
                Activity = state.Activity,
                BudgetName = state.BudgetName,
                UpdatedAtUtc = state.UpdatedAtUtc
            };
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
