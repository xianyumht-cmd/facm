using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceRepairAdapter _repairAdapter;

        private UnifiedWorkspaceRepairAdapter RepairAdapter
        {
            get
            {
                if (_repairAdapter == null)
                    _repairAdapter = new UnifiedWorkspaceRepairAdapter(
                        delegate { return CanPublishOnRoute(LeagueHubNavigation.Repair); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        delegate { return LeagueHubUiBridge.UnifiedRepair; },
                        delegate { return LeagueHubUiBridge.UnifiedEfficiency; });
                return _repairAdapter;
            }
        }

        internal void OpenRepairPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Repair);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncRepairState();
        }

        internal void SyncRepairState() { RepairAdapter.Sync(); }

        internal void ApplyRepairAction(string action) { RepairAdapter.Apply(action); }

        internal void CancelRepairOperation()
        {
            if (_repairAdapter != null) _repairAdapter.Cancel();
        }
    }
}
