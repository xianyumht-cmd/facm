using System.Windows.Forms;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceHistoryDetailAdapter _historyDetailAdapter;

        private UnifiedWorkspaceHistoryDetailAdapter HistoryDetailAdapter
        {
            get
            {
                if (_historyDetailAdapter == null)
                    _historyDetailAdapter = new UnifiedWorkspaceHistoryDetailAdapter(
                        delegate { return CanPublishOnRoute("player-advanced"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        ClearAdvancedMatchDetail,
                        delegate { ResetAdvancedHistorySnapshot(); SyncAdvancedHistoryList(); },
                        RememberAdvancedMatchDetail);
                return _historyDetailAdapter;
            }
        }

        internal void OpenAdvancedHistoryPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("player-advanced");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready)
            {
                OpenAdvancedHistoryData();
                SendRegionalOptions();
            }
        }

        internal void OpenAdvancedHistoryData()
        {
            var module = LeagueHubUiBridge.UnifiedPlayer;
            if (HistoryAdapter.HasAdvancedPage(module))
                SyncAdvancedHistoryList();
            else
                StartHistoryRead(20, false);
        }

        private void SyncAdvancedHistoryList()
        {
            HistoryAdapter.SyncAdvancedList(LeagueHubUiBridge.UnifiedPlayer);
        }

        internal void LoadAdvancedHistoryDetail(string raw)
        {
            HistoryDetailAdapter.StartLocal(LeagueHubUiBridge.UnifiedPlayer,
                HistoryAdapter.Snapshot, HistoryAdapter.Session, raw);
        }

        private void ResetAdvancedHistorySnapshot()
        {
            CancelAdvancedHistoryDetail();
            if (_historyAdapter != null) _historyAdapter.ClearSnapshot();
            CancelRegionalHistoryRead();
        }

        internal void CancelAdvancedHistoryDetail()
        {
            if (_historyDetailAdapter != null) _historyDetailAdapter.Cancel();
            else ClearAdvancedMatchDetail();
        }
    }
}
