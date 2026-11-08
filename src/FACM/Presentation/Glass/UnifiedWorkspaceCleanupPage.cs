using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceCleanupAdapter _cleanupAdapter;

        private UnifiedWorkspaceCleanupAdapter CleanupAdapter
        {
            get
            {
                if (_cleanupAdapter == null)
                    _cleanupAdapter = new UnifiedWorkspaceCleanupAdapter(
                        delegate { return CanPublishOnRoute("cleanup"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        _settings, _cleanup,
                        delegate { _ownerBall.RunToolA(); },
                        delegate { _ownerBall.ExitApplication(); });
                return _cleanupAdapter;
            }
        }

        internal void OpenUnifiedCleanupPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("cleanup");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncUnifiedCleanupState();
        }

        internal void SyncUnifiedCleanupState() { CleanupAdapter.Sync(); }
        internal void ResetCleanupPreview() { if (_cleanupAdapter != null) _cleanupAdapter.ResetPreview(); }
        internal void DetectUnifiedCleanupDirectory() { CleanupAdapter.DetectDirectory(); }
        internal void OpenUnifiedDriverRepair() { CleanupAdapter.OpenDriverRepair(); }
        internal void PreviewUnifiedCleanup() { CleanupAdapter.Preview(); }
        internal void ConfirmUnifiedCleanup(string token) { CleanupAdapter.Confirm(token); }
        internal void RestartUnifiedCleanupElevated() { CleanupAdapter.RestartElevated(); }
        private void PostUnifiedCleanupStatus(string status) { CleanupAdapter.PostStatus(status); }

        internal void SelectUnifiedCleanupDirectory()
        {
            if (!CanPublishOnRoute("cleanup") || CleanupAdapter.IsBusy || CleanupAdapter.IsExecuting)
                return;
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = CleanupRepairUiText.FolderDialog;
                var suggested = CleanupAdapter.SuggestedDirectory;
                if (!string.IsNullOrEmpty(suggested)) dialog.SelectedPath = suggested;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    CleanupAdapter.SaveDirectory(dialog.SelectedPath);
            }
        }
    }
}
