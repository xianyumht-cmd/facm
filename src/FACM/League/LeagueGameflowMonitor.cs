using System;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;

namespace FACM.League
{
    internal sealed class LeagueGameflowMonitor : IDisposable
    {
        private readonly LeagueDashboardPhaseService _phaseService;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private LeagueDashboardPhaseState _current;
        private bool _started;

        public LeagueGameflowMonitor(ILeagueClientApi client, PerformanceBudgetProvider budgets)
        {
            _phaseService = new LeagueDashboardPhaseService(client, budgets);
        }

        public event Action<LeagueDashboardPhaseState> StateChanged;
        public LeagueDashboardPhaseState Current { get { return _current; } }

        public void Start()
        {
            if (_started) return;
            _started = true;
            Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var next = await _phaseService.RefreshAsync(_lifetime.Token).ConfigureAwait(false);
                _current = next;
                var handler = StateChanged;
                if (handler != null) handler(next);
                await Task.Delay(ResolveDelay(next), _lifetime.Token).ConfigureAwait(false);
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

        public void Dispose()
        {
            if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
