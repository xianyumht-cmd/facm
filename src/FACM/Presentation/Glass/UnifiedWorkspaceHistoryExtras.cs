using FACM.AppHost.Modules;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceHistoryExtrasAdapter _historyExtrasAdapter;

        private UnifiedWorkspaceHistoryExtrasAdapter HistoryExtrasAdapter
        {
            get
            {
                if (_historyExtrasAdapter == null)
                    _historyExtrasAdapter = new UnifiedWorkspaceHistoryExtrasAdapter(
                        delegate { return CanPublishOnRoute("player-advanced"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); });
                return _historyExtrasAdapter;
            }
        }

        private void RememberAdvancedMatchDetail(string id,
            LeagueEncounterMatchDetailResult detail, LeagueClientSession session)
        {
            HistoryExtrasAdapter.Remember(id, detail, session);
        }

        private void ClearAdvancedMatchDetail()
        {
            if (_historyExtrasAdapter != null) _historyExtrasAdapter.Clear();
        }

        internal void ReadAdvancedRadar(string raw)
        {
            HistoryExtrasAdapter.ReadRadar(LeagueHubUiBridge.UnifiedPlayer, raw);
        }

        internal void ReadAdvancedTimeline(string requestedId)
        {
            HistoryExtrasAdapter.ReadTimeline(LeagueHubUiBridge.UnifiedPlayer, requestedId);
        }

        internal void StartAdvancedReplay(string requestedId)
        {
            HistoryExtrasAdapter.StartReplay(LeagueHubUiBridge.UnifiedPlayer,
                HistoryAdapter.Snapshot, requestedId);
        }

        internal void ReadAdvancedReplayFolder()
        {
            HistoryExtrasAdapter.ReadReplayFolder(LeagueHubUiBridge.UnifiedPlayer,
                HistoryAdapter.Snapshot);
        }
    }
}
