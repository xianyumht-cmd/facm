using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.League;
using FACM.Performance;
using FACM.Services;
using FACM.Theming;

namespace FACM.AppHost.Modules
{
    internal sealed class LeagueDashboardModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[] { LeagueClientModule.ModuleId, PerformanceModule.ModuleId };
        private readonly LeagueClientModule _leagueClient;
        private readonly PerformanceModule _performance;
        private LeagueGameflowMonitor _monitor;

        public LeagueDashboardModule(LeagueClientModule leagueClient, PerformanceModule performance)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        }

        public const string ModuleId = "league-dashboard";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public event Action<LeagueDashboardPhaseState> GameflowStateChanged;

        public LeagueDashboardPhaseState CurrentGameflowState
        {
            get { return _monitor == null ? null : _monitor.Current; }
        }

        public void Initialize()
        {
            _monitor = new LeagueGameflowMonitor(_leagueClient, _performance.Budgets);
            _monitor.StateChanged += ForwardGameflowState;
            LeaguePresenceUiBridge.Install(this);
            Application.Idle += StartMonitor;
        }

        private void StartMonitor(object sender, EventArgs e)
        {
            Application.Idle -= StartMonitor;
            if (_monitor != null) _monitor.Start();
        }

        private void ForwardGameflowState(LeagueDashboardPhaseState state)
        {
            var handler = GameflowStateChanged;
            if (handler != null) handler(state);
        }

        public Form CreateDashboardForm(UiTextCatalog ui)
        {
            return new LeagueDashboardForm(_leagueClient, _performance.Budgets, ui);
        }

        public Form CreatePresenceForm(UiTextCatalog ui, ThemeDefinition theme)
        {
            return new LeaguePresenceForm(
                new LeaguePresenceService(_leagueClient, (ILeaguePresenceWriteApi)_leagueClient),
                ui,
                theme);
        }

        public void Dispose()
        {
            Application.Idle -= StartMonitor;
            LeaguePresenceUiBridge.Uninstall(this);
            if (_monitor != null)
            {
                _monitor.StateChanged -= ForwardGameflowState;
                _monitor.Dispose();
            }
            _monitor = null;
        }
    }
}
