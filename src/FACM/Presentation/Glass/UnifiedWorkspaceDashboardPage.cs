using System.Windows.Forms;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceDashboardAdapter _dashboardAdapter;

        private UnifiedWorkspaceDashboardAdapter DashboardAdapter
        {
            get
            {
                if (_dashboardAdapter == null)
                    _dashboardAdapter = new UnifiedWorkspaceDashboardAdapter(
                        delegate { return CanPublishOnRoute(LeagueHubNavigation.Dashboard, "dashboard-advanced"); },
                        delegate { return CanPublishOnRoute("dashboard-advanced"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); });
                return _dashboardAdapter;
            }
        }

        internal void OpenDashboardPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Dashboard);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartDashboardRead();
        }

        internal void StartDashboardRead()
        {
            DashboardAdapter.StartDashboard(LeagueHubUiBridge.UnifiedDashboard);
        }

        internal void CancelDashboardRead()
        {
            if (_dashboardAdapter != null) _dashboardAdapter.CancelDashboard();
        }
    }
}
