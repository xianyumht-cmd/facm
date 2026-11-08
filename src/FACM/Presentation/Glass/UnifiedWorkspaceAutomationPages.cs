using System.Windows.Forms;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceAutomationAdapter _automationAdapter;

        private UnifiedWorkspaceAutomationAdapter AutomationAdapter
        {
            get
            {
                if (_automationAdapter == null)
                    _automationAdapter = new UnifiedWorkspaceAutomationAdapter(
                        delegate(string route) { return CanPublishOnRoute(route); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        delegate { return LeagueHubUiBridge.UnifiedEfficiency; }, _ui);
                return _automationAdapter;
            }
        }

        internal void OpenPostgameSettings(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("postgame-settings");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncPostgameSettings();
        }

        internal void SyncPostgameSettings()
        {
            AutomationAdapter.SyncPostgame();
        }

        internal void ApplyPostgameSettings(string json)
        {
            AutomationAdapter.ApplyPostgame(json);
        }

        internal void OpenAutoSelection(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("auto-selection");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncAutoSelection();
            StartAutoSelectionCatalog();
        }

        internal void StartAutoSelectionCatalog()
        {
            AutomationAdapter.StartChampionCatalog();
        }

        internal void CancelChampionCatalog()
        {
            if (_automationAdapter != null) _automationAdapter.CancelChampionCatalog();
        }

        internal void ApplyAutoSelection(string json)
        {
            AutomationAdapter.ApplyAutoSelection(json);
        }

        internal void ApplySelectionPause(string value)
        {
            AutomationAdapter.ApplySelectionPause(value);
        }

        internal void SyncAutoSelection()
        {
            AutomationAdapter.SyncAutoSelection();
        }

        internal void OpenMatchmakingSettings(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("matchmaking-settings");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncMatchmakingSettings();
        }

        internal void ApplyMatchmakingSettings(string request)
        {
            AutomationAdapter.ApplyMatchmaking(request);
        }

        internal void SyncMatchmakingSettings()
        {
            AutomationAdapter.SyncMatchmaking();
        }
    }
}
