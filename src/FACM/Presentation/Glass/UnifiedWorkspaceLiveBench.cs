using System.Windows.Forms;
using FACM.AppHost.Modules;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceLiveBenchAdapter _liveBenchAdapter;

        private UnifiedWorkspaceLiveBenchAdapter LiveBenchAdapter
        {
            get
            {
                if (_liveBenchAdapter == null)
                    _liveBenchAdapter = new UnifiedWorkspaceLiveBenchAdapter(
                        delegate { return CanPublishOnRoute("live-advanced"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        delegate { return LeagueHubUiBridge.UnifiedLive; });
                return _liveBenchAdapter;
            }
        }

        internal void OpenLiveBenchPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("live-advanced");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartLiveBenchRead();
        }

        internal void StartLiveBenchRead() { LiveBenchAdapter.Start(); }

        internal void SwapLiveBenchChampion(string request) { LiveBenchAdapter.Swap(request); }

        internal void CancelLiveBenchRead()
        {
            if (_liveBenchAdapter != null) _liveBenchAdapter.Cancel();
        }
    }
}
