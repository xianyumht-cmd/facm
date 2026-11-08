using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceGameflowAdapter _gameflowAdapter;

        private UnifiedWorkspaceGameflowAdapter GameflowAdapter
        {
            get
            {
                if (_gameflowAdapter == null)
                    _gameflowAdapter = new UnifiedWorkspaceGameflowAdapter(
                        delegate { return _ready && !_closing && !IsDisposed && _browser.CoreWebView2 != null; },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); });
                return _gameflowAdapter;
            }
        }

        internal void UpdateGameflowState(LeagueDashboardPhaseState state)
        {
            if (!IsDisposed) GameflowAdapter.Update(state);
        }

        private void SyncGameflowState() { GameflowAdapter.Sync(); }

        internal void PostLeaguePageStatus(string type, string status)
        {
            GameflowAdapter.PostPageStatus(type, status);
        }
    }
}
