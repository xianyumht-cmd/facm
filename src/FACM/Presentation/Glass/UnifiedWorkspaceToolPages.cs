using System;
using System.Windows.Forms;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceQuickToolsAdapter _quickToolsAdapter;
        private UnifiedWorkspaceSkinAdapter _skinAdapter;

        private UnifiedWorkspaceQuickToolsAdapter QuickToolsAdapter
        {
            get
            {
                if (_quickToolsAdapter == null)
                    _quickToolsAdapter = new UnifiedWorkspaceQuickToolsAdapter(
                        delegate { return CanPublishOnRoute(LeagueHubNavigation.Efficiency); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        delegate { return LeagueHubUiBridge.UnifiedEfficiency; });
                return _quickToolsAdapter;
            }
        }

        private UnifiedWorkspaceSkinAdapter SkinAdapter
        {
            get
            {
                if (_skinAdapter == null)
                    _skinAdapter = new UnifiedWorkspaceSkinAdapter(
                        delegate { return _ready && !_closing && !IsDisposed && _browser.CoreWebView2 != null; },
                        delegate { return CanPublishOnRoute("appearance"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        _settings);
                return _skinAdapter;
            }
        }

        internal void OpenQuickTools(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Efficiency);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncQuickSettings();
            SyncRepairState();
        }

        internal void OpenQuickInsights() { QuickToolsAdapter.OpenInsights(); }
        internal void HandleQuickRoom(string message) { QuickToolsAdapter.HandleRoom(message); }
        internal void RefreshQuickInsights() { QuickToolsAdapter.RefreshInsights(); }
        internal void CancelQuickInsights() { if (_quickToolsAdapter != null) _quickToolsAdapter.CancelInsights(); }

        internal void ApplyQuickSetting(string request) { QuickToolsAdapter.Apply(request); }
        internal void SyncQuickSettings() { QuickToolsAdapter.Sync(); }
        internal void CloseQuickLobby() { QuickToolsAdapter.CloseLobby(); }
        internal void SaveQuickHotkeys(string json) { QuickToolsAdapter.SaveHotkeys(json); }

        internal void OpenClientStatus(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("client-status");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncGameflowState();
        }

        internal void OpenAppearance(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("appearance");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncThemeOptions();
            SyncUnifiedPetOptions();
        }

        internal void ApplyTheme(string id) { SkinAdapter.Apply(id); }
        private void SyncThemeOptions(bool applied = false) { SkinAdapter.Sync(applied); }
    }
}
