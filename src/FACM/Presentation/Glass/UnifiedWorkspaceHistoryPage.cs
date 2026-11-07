using System.Windows.Forms;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceHistoryAdapter _historyAdapter;

        private UnifiedWorkspaceHistoryAdapter HistoryAdapter
        {
            get
            {
                if (_historyAdapter == null)
                    _historyAdapter = new UnifiedWorkspaceHistoryAdapter(
                        delegate { return CanPublishOnRoute(LeagueHubNavigation.Player, "player-advanced"); },
                        delegate { return _navigation.Current == "player-advanced"; },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        ResetAdvancedHistorySnapshot,
                        SyncAdvancedHistoryList);
                return _historyAdapter;
            }
        }

        internal void OpenHistoryPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Player);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartHistoryRead(10, false);
        }

        internal void StartHistoryRead(int count, bool force)
        {
            HistoryAdapter.Start(LeagueHubUiBridge.UnifiedPlayer, count, force);
        }

        internal void CancelHistoryRead()
        {
            if (_historyAdapter != null) _historyAdapter.Cancel();
        }
    }
}
