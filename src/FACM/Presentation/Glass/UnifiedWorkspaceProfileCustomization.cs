using System.Windows.Forms;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceProfileCustomizationAdapter _profileCustomizationAdapter;

        private UnifiedWorkspaceProfileCustomizationAdapter ProfileCustomizationAdapter
        {
            get
            {
                if (_profileCustomizationAdapter == null)
                    _profileCustomizationAdapter = new UnifiedWorkspaceProfileCustomizationAdapter(
                        delegate { return CanPublishOnRoute("presence-advanced"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); });
                return _profileCustomizationAdapter;
            }
        }

        internal void OpenProfileCustomizationPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("presence-advanced");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartProfileCustomizationRead();
        }

        internal void StartProfileCustomizationRead()
        {
            ProfileCustomizationAdapter.Start(LeagueHubUiBridge.UnifiedDashboard);
        }

        internal void LoadProfileSkins(string raw)
        {
            ProfileCustomizationAdapter.LoadSkins(LeagueHubUiBridge.UnifiedDashboard, raw);
        }

        internal void ApplyProfileSkin(string raw)
        {
            ProfileCustomizationAdapter.ApplySkin(LeagueHubUiBridge.UnifiedDashboard, raw);
        }

        internal void ApplyProfileCustomizationAction(string action)
        {
            ProfileCustomizationAdapter.ApplyAction(LeagueHubUiBridge.UnifiedDashboard, action);
        }

        internal void CancelProfileCustomization()
        {
            if (_profileCustomizationAdapter != null) _profileCustomizationAdapter.Cancel();
        }
    }
}
