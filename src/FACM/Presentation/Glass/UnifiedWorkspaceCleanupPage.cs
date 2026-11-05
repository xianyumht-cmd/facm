using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // No second cleanup engine: all previews and writes pass through CleanupModule / SafeCleanupService.
    // Only the operating-system directory picker and UAC are allowed to leave this HTML page.
    internal sealed partial class UnifiedWorkspaceForm
    {
        private CleanupPlan _cleanupPreview;
        private string _cleanupReviewToken;
        private DateTime _cleanupReviewExpiresUtc;
        private int _cleanupGeneration;
        private bool _cleanupBusy;
        private bool _cleanupExecuting;
        private bool _cleanupDriverStarted;

        private void OpenUnifiedCleanupPage(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open("cleanup");
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncUnifiedCleanupState();
        }

        private void SyncUnifiedCleanupState()
        {
            if (!_ready || IsDisposed || _closing || _browser.CoreWebView2 == null ||
                _navigation.Current != "cleanup") return;
            var valid = !string.IsNullOrWhiteSpace(_settings.GamePath) && _cleanup.IsValidGameRoot(_settings.GamePath);
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "cleanup-state",
                configured = _cleanup.IsConfigured,
                path = valid ? _settings.GamePath : null,
                valid,
                administrator = _cleanup.IsAdministrator,
                busy = _cleanupBusy,
                executing = _cleanupExecuting,
                previewActive = !string.IsNullOrEmpty(_cleanupReviewToken),
                driverStarted = _cleanupDriverStarted
            }));
        }

        private void ResetCleanupPreview()
        {
            _cleanupGeneration++;
            _cleanupPreview = null;
            _cleanupReviewToken = null;
            _cleanupReviewExpiresUtc = DateTime.MinValue;
        }

        private void SelectUnifiedCleanupDirectory()
        {
            if (_navigation.Current != "cleanup" || _cleanupBusy || _cleanupExecuting) return;
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = CleanupRepairUiText.FolderDialog;
                if (_cleanup.IsValidGameRoot(_settings.GamePath))
                    dialog.SelectedPath = _settings.GamePath;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                SaveUnifiedCleanupDirectory(dialog.SelectedPath);
            }
        }

        private async void DetectUnifiedCleanupDirectory()
        {
            if (_navigation.Current != "cleanup" || _cleanupBusy || _cleanupExecuting) return;
            ResetCleanupPreview();
            _cleanupBusy = true;
            SyncUnifiedCleanupState();
            try
            {
                var found = await Task.Run(() => _cleanup.FindGameRoot());
                if (!IsDisposed && !_closing && _navigation.Current == "cleanup")
                    SaveUnifiedCleanupDirectory(found);
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified cleanup location detection failed: " + exception.GetType().Name);
                PostUnifiedCleanupStatus("detect-failed");
            }
            finally
            {
                _cleanupBusy = false;
                SyncUnifiedCleanupState();
            }
        }

        private void SaveUnifiedCleanupDirectory(string directory)
        {
            if (_cleanupBusy && _cleanupExecuting) return;
            try
            {
                var root = _cleanup.ResolveGameRoot(directory);
                if (string.IsNullOrWhiteSpace(root) || !_cleanup.IsValidGameRoot(root))
                {
                    PostUnifiedCleanupStatus("invalid-directory");
                    return;
                }
                ResetCleanupPreview();
                _settings.GamePath = root;
                _settings.Save();
                PostUnifiedCleanupStatus("directory-saved");
                SyncUnifiedCleanupState();
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified cleanup directory save failed: " + exception.GetType().Name);
                PostUnifiedCleanupStatus("invalid-directory");
            }
        }

        private void OpenUnifiedDriverRepair()
        {
            if (_navigation.Current != "cleanup" || _cleanupBusy || _cleanupExecuting) return;
            try
            {
                _ownerBall.RunToolA();
                _cleanupDriverStarted = true;
                PostUnifiedCleanupStatus("driver-started");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified driver repair launch failed: " + exception.GetType().Name);
                PostUnifiedCleanupStatus("failed");
            }
            SyncUnifiedCleanupState();
        }

        private async void PreviewUnifiedCleanup()
        {
            if (_navigation.Current != "cleanup" || _cleanupBusy || _cleanupExecuting) return;
            ResetCleanupPreview();
            if (!_cleanup.IsConfigured || !_cleanup.IsValidGameRoot(_settings.GamePath))
            {
                PostUnifiedCleanupStatus("invalid-directory"); return;
            }
            if (!_cleanup.IsAdministrator)
            {
                PostUnifiedCleanupStatus("need-admin"); return;
            }
            var running = _cleanup.GetRunningRelatedProcesses();
            if (running != null && running.Count > 0)
            {
                PostUnifiedCleanupStatus("game-running"); return;
            }
            var generation = _cleanupGeneration;
            var root = _settings.GamePath;
            _cleanupBusy = true;
            PostUnifiedCleanupStatus("scanning");
            SyncUnifiedCleanupState();
            try
            {
                // The original cleanup engine uses its own WinForms modal progress window
                // on the UI thread; do not open it inside the unified HTML workspace.
                var plan = await Task.Run(() => _cleanup.CreatePlan(root));
                if (_closing || IsDisposed || _navigation.Current != "cleanup" ||
                    _cleanupGeneration != generation) return;
                if (plan == null || plan.Targets == null || plan.Targets.Count > 350)
                {
                    PostUnifiedCleanupStatus("too-many"); return;
                }
                if (plan.DeletableTargets.Count == 0)
                {
                    PostUnifiedCleanupStatus("no-targets"); return;
                }
                _cleanupPreview = plan;
                _cleanupReviewToken = Guid.NewGuid().ToString("N");
                _cleanupReviewExpiresUtc = DateTime.UtcNow.AddMinutes(2);
                _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                {
                    type = "cleanup-preview",
                    token = _cleanupReviewToken,
                    root = plan.GameRoot,
                    count = plan.DeletableTargets.Count,
                    blocked = plan.BlockedCount,
                    files = plan.FileCount,
                    directories = plan.DirectoryCount,
                    bytes = plan.EstimatedBytes,
                    targets = plan.Targets.Select(target => new
                    {
                        path = target.Path,
                        group = target.Group,
                        blocked = target.Blocked,
                        detail = target.Detail,
                        bytes = target.EstimatedBytes,
                        files = target.FileCount
                    }).ToArray()
                }));
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified cleanup preview failed: " + exception.GetType().Name);
                PostUnifiedCleanupStatus("failed");
            }
            finally
            {
                _cleanupBusy = false;
                SyncUnifiedCleanupState();
            }
        }

        private async void ConfirmUnifiedCleanup(string token)
        {
            if (_navigation.Current != "cleanup" || _cleanupBusy || _cleanupExecuting) return;
            if (string.IsNullOrEmpty(token) || token.Length != 32 ||
                !string.Equals(token, _cleanupReviewToken, StringComparison.Ordinal) ||
                _cleanupPreview == null || DateTime.UtcNow >= _cleanupReviewExpiresUtc)
            {
                ResetCleanupPreview();
                PostUnifiedCleanupStatus("preview-expired"); return;
            }
            var plan = _cleanupPreview;
            ResetCleanupPreview(); // A preview may only ever be executed once.
            if (!_cleanup.IsAdministrator || !_cleanup.IsValidGameRoot(_settings.GamePath) ||
                !string.Equals(_cleanup.ResolveGameRoot(_settings.GamePath), plan.GameRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                PostUnifiedCleanupStatus("context-changed"); return;
            }
            var running = _cleanup.GetRunningRelatedProcesses();
            if (running != null && running.Count > 0)
            {
                PostUnifiedCleanupStatus("game-running"); return;
            }
            _cleanupBusy = true;
            _cleanupExecuting = true;
            PostUnifiedCleanupStatus("deleting");
            SyncUnifiedCleanupState();
            try
            {
                var result = await Task.Run(() => _cleanup.Execute(plan));
                if (!IsDisposed && !_closing && _navigation.Current == "cleanup")
                {
                    _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                    {
                        type = "cleanup-complete",
                        deletedFiles = result == null ? 0 : result.DeletedFiles,
                        deletedDirectories = result == null ? 0 : result.DeletedDirectories,
                        failures = result == null ? -1 : result.Failures.Count
                    }));
                }
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified cleanup execution failed: " + exception.GetType().Name);
                PostUnifiedCleanupStatus("failed");
            }
            finally
            {
                _cleanupExecuting = false;
                _cleanupBusy = false;
                SyncUnifiedCleanupState();
            }
        }

        private void RestartUnifiedCleanupElevated()
        {
            if (_navigation.Current != "cleanup" || _cleanupBusy || _cleanupExecuting ||
                _cleanup.IsAdministrator) return;
            if (_cleanup.RestartElevatedForCleanup()) _ownerBall.ExitApplication();
            else PostUnifiedCleanupStatus("elevation-failed");
        }

        private void PostUnifiedCleanupStatus(string status)
        {
            if (!_ready || IsDisposed || _closing || _browser.CoreWebView2 == null ||
                _navigation.Current != "cleanup") return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "cleanup-result", status
            }));
        }
    }
}
