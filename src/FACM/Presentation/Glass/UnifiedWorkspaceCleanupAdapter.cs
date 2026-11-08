using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Per-workspace plan and one-use confirmation state, without a second cleanup engine.
    internal sealed class UnifiedWorkspaceCleanupAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly AppSettings _settings;
        private readonly CleanupModule _cleanup;
        private readonly Action _runDriver;
        private readonly Action _exit;
        private CleanupPlan _cleanupPreview;
        private string _cleanupReviewToken;
        private DateTime _cleanupReviewExpiresUtc;
        private int _cleanupGeneration;
        private bool _cleanupBusy;
        private bool _cleanupExecuting;
        private bool _cleanupDriverStarted;

        internal bool IsBusy { get { return _cleanupBusy; } }
        internal bool IsExecuting { get { return _cleanupExecuting; } }
        internal string SuggestedDirectory
        {
            get { return _cleanup.IsValidGameRoot(_settings.GamePath) ? _settings.GamePath : null; }
        }

        internal UnifiedWorkspaceCleanupAdapter(Func<bool> canPublish, Action<string> publish,
            AppSettings settings, CleanupModule cleanup, Action runDriver, Action exit)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
            _runDriver = runDriver ?? throw new ArgumentNullException(nameof(runDriver));
            _exit = exit ?? throw new ArgumentNullException(nameof(exit));
        }

        internal void Sync()
        {
            if (!_canPublish()) return;
            var valid = !string.IsNullOrWhiteSpace(_settings.GamePath) && _cleanup.IsValidGameRoot(_settings.GamePath);
            _publish(new JavaScriptSerializer().Serialize(new
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

        internal void ResetPreview()
        {
            _cleanupGeneration++;
            _cleanupPreview = null;
            _cleanupReviewToken = null;
            _cleanupReviewExpiresUtc = DateTime.MinValue;
        }

        internal async void DetectDirectory()
        {
            if (!_canPublish() || _cleanupBusy || _cleanupExecuting) return;
            ResetPreview();
            _cleanupBusy = true;
            Sync();
            try
            {
                var found = await Task.Run(() => _cleanup.FindGameRoot());
                if (_canPublish()) SaveDirectory(found);
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified cleanup location detection failed: " + exception.GetType().Name);
                PostStatus("detect-failed");
            }
            finally
            {
                _cleanupBusy = false;
                Sync();
            }
        }

        internal void SaveDirectory(string directory)
        {
            if (_cleanupBusy && _cleanupExecuting) return;
            try
            {
                var root = _cleanup.ResolveGameRoot(directory);
                if (string.IsNullOrWhiteSpace(root) || !_cleanup.IsValidGameRoot(root))
                {
                    PostStatus("invalid-directory");
                    return;
                }
                ResetPreview();
                _settings.GamePath = root;
                _settings.Save();
                PostStatus("directory-saved");
                Sync();
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified cleanup directory save failed: " + exception.GetType().Name);
                PostStatus("invalid-directory");
            }
        }

        internal void OpenDriverRepair()
        {
            if (!_canPublish() || _cleanupBusy || _cleanupExecuting) return;
            try
            {
                _runDriver();
                _cleanupDriverStarted = true;
                PostStatus("driver-started");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified driver repair launch failed: " + exception.GetType().Name);
                PostStatus("failed");
            }
            Sync();
        }

        internal async void Preview()
        {
            if (!_canPublish() || _cleanupBusy || _cleanupExecuting) return;
            ResetPreview();
            if (!_cleanup.IsConfigured || !_cleanup.IsValidGameRoot(_settings.GamePath))
            {
                PostStatus("invalid-directory"); return;
            }
            if (!_cleanup.IsAdministrator)
            {
                PostStatus("need-admin"); return;
            }
            var running = _cleanup.GetRunningRelatedProcesses();
            if (running != null && running.Count > 0)
            {
                PostStatus("game-running"); return;
            }
            var generation = _cleanupGeneration;
            var root = _settings.GamePath;
            _cleanupBusy = true;
            PostStatus("scanning");
            Sync();
            try
            {
                // The original cleanup engine uses its own WinForms modal progress window
                // on the UI thread; do not open it inside the unified HTML workspace.
                var plan = await Task.Run(() => _cleanup.CreatePlan(root));
                if (!_canPublish() || _cleanupGeneration != generation) return;
                if (plan == null || plan.Targets == null || plan.Targets.Count > 350)
                {
                    PostStatus("too-many"); return;
                }
                if (plan.DeletableTargets.Count == 0)
                {
                    PostStatus("no-targets"); return;
                }
                _cleanupPreview = plan;
                _cleanupReviewToken = Guid.NewGuid().ToString("N");
                _cleanupReviewExpiresUtc = DateTime.UtcNow.AddMinutes(2);
                _publish(new JavaScriptSerializer().Serialize(new
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
                PostStatus("failed");
            }
            finally
            {
                _cleanupBusy = false;
                Sync();
            }
        }

        internal async void Confirm(string token)
        {
            if (!_canPublish() || _cleanupBusy || _cleanupExecuting) return;
            if (!IsValidReviewToken(token, _cleanupReviewToken, _cleanupReviewExpiresUtc) ||
                _cleanupPreview == null)
            {
                ResetPreview();
                PostStatus("preview-expired"); return;
            }
            var plan = _cleanupPreview;
            ResetPreview(); // A preview may only ever be executed once.
            if (!_cleanup.IsAdministrator || !_cleanup.IsValidGameRoot(_settings.GamePath) ||
                !string.Equals(_cleanup.ResolveGameRoot(_settings.GamePath), plan.GameRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                PostStatus("context-changed"); return;
            }
            var running = _cleanup.GetRunningRelatedProcesses();
            if (running != null && running.Count > 0)
            {
                PostStatus("game-running"); return;
            }
            _cleanupBusy = true;
            _cleanupExecuting = true;
            PostStatus("deleting");
            Sync();
            try
            {
                var result = await Task.Run(() => _cleanup.Execute(plan));
                if (_canPublish())
                {
                    _publish(new JavaScriptSerializer().Serialize(new
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
                PostStatus("failed");
            }
            finally
            {
                _cleanupExecuting = false;
                _cleanupBusy = false;
                Sync();
            }
        }

        internal void RestartElevated()
        {
            if (!_canPublish() || _cleanupBusy || _cleanupExecuting ||
                _cleanup.IsAdministrator) return;
            if (_cleanup.RestartElevatedForCleanup()) _exit();
            else PostStatus("elevation-failed");
        }

        internal void PostStatus(string status)
        {
            if (!_canPublish()) return;
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "cleanup-result", status
            }));
        }

        internal static void ValidateForSmokeTest()
        {
            var token = Guid.NewGuid().ToString("N");
            if (!IsValidReviewToken(token, token, DateTime.UtcNow.AddSeconds(5)) ||
                IsValidReviewToken(token, "different", DateTime.UtcNow.AddSeconds(5)) ||
                IsValidReviewToken(token, token, DateTime.UtcNow.AddSeconds(-1)) ||
                IsValidReviewToken("short", "short", DateTime.UtcNow.AddSeconds(5)))
                throw new InvalidOperationException("Cleanup one-use confirmation token bounds changed.");
        }

        private static bool IsValidReviewToken(string token, string expected, DateTime expiresUtc)
        {
            return !string.IsNullOrEmpty(token) && token.Length == 32 &&
                string.Equals(token, expected, StringComparison.Ordinal) && DateTime.UtcNow < expiresUtc;
        }
    }
}
