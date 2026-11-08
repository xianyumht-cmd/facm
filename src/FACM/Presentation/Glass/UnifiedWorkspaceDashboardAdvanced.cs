using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        internal void OpenAdvancedDashboardPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("dashboard-advanced");
            _browser.Dock = System.Windows.Forms.DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartDashboardRead();
        }

        internal void StartAdvancedRespawnRead()
        {
            if (_navigation.Current != "dashboard-advanced" || _closing) return;
            DashboardAdapter.StartRespawn(LeagueHubUiBridge.UnifiedDashboard);
        }

        internal void CancelAdvancedRespawnRead()
        {
            if (_dashboardAdapter != null) _dashboardAdapter.CancelRespawn();
        }
    }
}
