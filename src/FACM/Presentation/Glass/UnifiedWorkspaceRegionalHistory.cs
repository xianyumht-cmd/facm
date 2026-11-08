using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceRegionalHistoryAdapter _regionalHistoryAdapter;

        private UnifiedWorkspaceRegionalHistoryAdapter RegionalHistoryAdapter
        {
            get
            {
                if (_regionalHistoryAdapter == null)
                    _regionalHistoryAdapter = new UnifiedWorkspaceRegionalHistoryAdapter(
                        delegate { return CanPublishOnRoute("player-advanced"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        CancelAdvancedHistoryDetail);
                return _regionalHistoryAdapter;
            }
        }

        internal void SendRegionalOptions()
        {
            RegionalHistoryAdapter.SendOptions(LeagueHubUiBridge.UnifiedPlayer);
        }

        internal void RefreshRegionalCatalog()
        {
            RegionalHistoryAdapter.RefreshCatalog(LeagueHubUiBridge.UnifiedPlayer);
        }

        internal void SearchRegionalRiotId(string request)
        {
            RegionalHistoryAdapter.Search(LeagueHubUiBridge.UnifiedPlayer,
                HistoryAdapter.Snapshot, HistoryAdapter.Session, request);
        }

        internal void ReadRegionalMatchDetail(string raw)
        {
            RegionalHistoryAdapter.ReadDetail(LeagueHubUiBridge.UnifiedPlayer, HistoryDetailAdapter, raw);
        }

        internal void CancelRegionalHistoryRead()
        {
            if (_regionalHistoryAdapter != null) _regionalHistoryAdapter.Cancel();
        }
    }
}
