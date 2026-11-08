using System.Windows.Forms;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspacePresenceAdapter _presenceAdapter;

        private UnifiedWorkspacePresenceAdapter PresenceAdapter
        {
            get
            {
                if (_presenceAdapter == null)
                    _presenceAdapter = new UnifiedWorkspacePresenceAdapter(
                        delegate { return CanPublishOnRoute(LeagueHubNavigation.Presence); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); });
                return _presenceAdapter;
            }
        }

        internal void OpenPresencePage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Presence);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartPresenceRead();
        }

        internal void StartPresenceRead()
        {
            PresenceAdapter.Start(LeagueHubUiBridge.UnifiedDashboard);
        }

        internal void ApplyPresenceChange(string kind, string request)
        {
            PresenceAdapter.Apply(LeagueHubUiBridge.UnifiedDashboard, kind, request);
        }

        internal void CancelPresenceOperation()
        {
            if (_presenceAdapter != null) _presenceAdapter.Cancel();
        }
    }
}
