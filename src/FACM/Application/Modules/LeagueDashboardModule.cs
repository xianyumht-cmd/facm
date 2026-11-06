using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
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
        private LeagueDodgeProbeService _dodgeProbe;

        public LeagueDashboardModule(LeagueClientModule leagueClient, PerformanceModule performance)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        }

        public const string ModuleId = "league-dashboard";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        internal PerformanceBudgetProvider BudgetProvider { get { return _performance.Budgets; } }

        public event Action<LeagueDashboardPhaseState> GameflowStateChanged;

        public LeagueDashboardPhaseState CurrentGameflowState
        {
            get { return _monitor == null ? null : _monitor.Current; }
        }

        public void Initialize()
        {
            _monitor = new LeagueGameflowMonitor(_leagueClient, _performance.Budgets);
            _dodgeProbe = new LeagueDodgeProbeService(_leagueClient);
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
            // The dashboard monitor is the single authoritative Gameflow owner. The read-only Dodge
            // Probe is phase-driven from this same snapshot and never creates a competing Gameflow loop.
            var dodgeProbe = _dodgeProbe;
            if (dodgeProbe != null)
            {
                try { dodgeProbe.Observe(state); }
                catch (Exception exception) { AppLog.Info("League Dodge Probe observe skipped: " + exception.Message); }
            }

            // Share one transition-only notification with open Player details dialogs.
            LeaguePlayerPostgameRefreshBridge.Observe(state);
            LeagueGameflowEventDispatcher.DispatchSafely(GameflowStateChanged, state, "dashboard-module");
        }

        internal async Task<LeagueDashboardSnapshot> ReadUnifiedDashboardAsync(CancellationToken cancellationToken)
        {
            var state = CurrentGameflowState;
            if (state == null || !state.Connected)
                return new LeagueDashboardSnapshot
                {
                    Connected = state != null && state.Connected,
                    ClientProcessDetected = state != null && state.ClientProcessDetected,
                    GameProcessDetected = state != null && state.GameProcessDetected,
                    Phase = state == null ? null : state.Phase,
                    UpdatedAtUtc = state == null ? DateTime.UtcNow : state.UpdatedAtUtc
                };
            var session = _leagueClient.CurrentSession;
            if (session == null) return null;
            var service = new LeagueDashboardDetailsService(_leagueClient, _performance.Budgets);
            var snapshot = await service.LoadAsync(state, cancellationToken);
            return LeagueEncounterReplayService.SessionMatches(session, _leagueClient.CurrentSession)
                ? snapshot : null;
        }

        // An explicit UI request reads the existing Live Client Data endpoint.
        // Do not add a competing Gameflow/respawn polling loop to WebView2.
        internal async Task<LeagueRespawnTimerSample> ReadUnifiedRespawnAsync(
            CancellationToken cancellationToken)
        {
            var phase = CurrentGameflowState;
            if (phase == null || !phase.Connected ||
                !string.Equals(phase.Phase, "InProgress", StringComparison.OrdinalIgnoreCase))
                return null;
            var session = _leagueClient.CurrentSession;
            if (session == null) return null;
            var snapshot = await ReadUnifiedDashboardAsync(cancellationToken);
            if (snapshot == null || !snapshot.Connected ||
                string.IsNullOrWhiteSpace(snapshot.AccountName) ||
                !LeagueEncounterReplayService.SessionMatches(session, _leagueClient.CurrentSession))
                return null;
            using (var reader = new LeagueRespawnTimerReader())
            {
                var payload = await reader.ReadAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested ||
                    !LeagueEncounterReplayService.SessionMatches(session, _leagueClient.CurrentSession) ||
                    CurrentGameflowState == null ||
                    !string.Equals(CurrentGameflowState.Phase, "InProgress", StringComparison.OrdinalIgnoreCase))
                    return null;
                return LeagueRespawnTimerProjection.Project(payload, snapshot.AccountName, null);
            }
        }

        public Form CreateDashboardForm(UiTextCatalog ui)
        {
            return new LeagueDashboardForm(_leagueClient, _performance.Budgets, ui);
        }

        internal LeagueClientSession UnifiedPresenceSession { get { return _leagueClient.CurrentSession; } }
        internal LeaguePresenceService CreateUnifiedPresenceService()
        {
            return new LeaguePresenceService(_leagueClient, (ILeaguePresenceWriteApi)_leagueClient);
        }

        public Form CreatePresenceForm(UiTextCatalog ui, ThemeDefinition theme)
        {
            return new LeaguePresenceForm(
                new LeaguePresenceService(_leagueClient, (ILeaguePresenceWriteApi)_leagueClient),
                new LeagueProfileCustomizationService(_leagueClient, (ILeagueProfileWriteApi)_leagueClient),
                new LeagueRegaliaCustomizationService(_leagueClient, (ILeagueRegaliaWriteApi)_leagueClient),
                new LeagueChallengePreferencesService(_leagueClient, (ILeagueChallengePreferencesWriteApi)_leagueClient),
                new LeagueEmoteLoadoutService(_leagueClient, (ILeagueEmoteLoadoutWriteApi)_leagueClient),
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
            if (_dodgeProbe != null) _dodgeProbe.Dispose();
            _dodgeProbe = null;
            _monitor = null;
            LeaguePlayerPostgameRefreshBridge.Reset();
        }
    }
}
