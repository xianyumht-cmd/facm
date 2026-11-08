using System.Windows.Forms;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceLiveAdapter _liveAdapter;

        private UnifiedWorkspaceLiveAdapter LiveAdapter
        {
            get
            {
                if (_liveAdapter == null)
                    _liveAdapter = new UnifiedWorkspaceLiveAdapter(
                        delegate { return CanPublishOnRoute(LeagueHubNavigation.Live); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); });
                return _liveAdapter;
            }
        }

        internal void OpenLivePage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Live);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartLiveRead(false);
        }

        internal void StartLiveRead(bool includeSgp)
        {
            LiveAdapter.Start(LeagueHubUiBridge.UnifiedLive, includeSgp);
        }

        internal void CancelLiveRead()
        {
            if (_liveAdapter != null) _liveAdapter.Cancel();
        }
    }
}
