using System;
using System.Windows.Forms;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceProfileAdapter _profileAdapter;

        private UnifiedWorkspaceProfileAdapter ProfileAdapter
        {
            get
            {
                if (_profileAdapter == null)
                    _profileAdapter = new UnifiedWorkspaceProfileAdapter(
                        delegate { return CanPublishOnRoute(LeagueHubNavigation.Profile); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        delegate
                        {
                            if (_closing || IsDisposed || !IsHandleCreated) return;
                            try { BeginInvoke(new Action(SyncLocalProfile)); }
                            catch (InvalidOperationException) { }
                        });
                return _profileAdapter;
            }
        }

        private void HookLocalStats()
        {
            ProfileAdapter.Observe(LeagueHubUiBridge.UnifiedPersonalStats);
        }

        private void UnhookLocalStats()
        {
            if (_profileAdapter != null) _profileAdapter.Stop();
        }

        internal void OpenLocalProfilePage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Profile);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            HookLocalStats();
            SyncLocalProfile();
        }

        internal void SyncLocalProfile()
        {
            if (!CanPublishOnRoute(LeagueHubNavigation.Profile)) return;
            ProfileAdapter.Sync(LeagueHubUiBridge.UnifiedPersonalStats, _settings.LocalOnlyMode);
        }
    }
}
