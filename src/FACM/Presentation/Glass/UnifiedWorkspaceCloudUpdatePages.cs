using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.League;
using FACM.Online;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Cloud and update UI exclusively delegate to the existing account, sync and signed updater owners.
    internal sealed partial class UnifiedWorkspaceForm
    {
        private GgmanAutoSyncService _cloudObserver;
        private bool _cloudEventsHooked;
        private bool _cloudBusy;
        private CancellationTokenSource _onlineReadCancellation;
        private CancellationTokenSource _onlineInstallCancellation;
        private int _onlineGeneration;
        private OnlineSnapshot _onlineSnapshot;
        private DateTime _onlineCheckedUtc;
        private bool _onlineInstalling;
        private bool _onlineForceRequired;
        private bool _onlineAutomaticPrompt;

        internal bool RequiresForcedOnlineUpdate { get { return _onlineForceRequired; } }

        internal static bool IsRouteAllowedDuringForcedUpdate(bool forced, string route)
        {
            return !forced || string.Equals(route, "updates", StringComparison.Ordinal);
        }

        internal void PresentOnlineSnapshot(OnlineSnapshot snapshot, bool forceMode, bool automaticPrompt)
        {
            if (IsDisposed || _closing || _onlineInstalling) return;
            _onlineForceRequired |= forceMode || (snapshot != null && snapshot.ForceUpdateRequired);
            _onlineAutomaticPrompt = !_onlineForceRequired && automaticPrompt;
            if (_navigation.Current != "updates") OpenNativeRoute("updates", true);
            CancelOnlineRead();
            _onlineSnapshot = snapshot ?? new OnlineSnapshot();
            _onlineCheckedUtc = DateTime.UtcNow;
            SendOnlineState(_onlineSnapshot);
            _ = CompletePendingOnlineAnnouncementAsync(_onlineSnapshot);
        }

        private async Task CompletePendingOnlineAnnouncementAsync(OnlineSnapshot expected)
        {
            var pending = expected == null ? null : expected.AnnouncementPending;
            if (pending == null) return;
            try
            {
                var announcement = await pending;
                if (!ReferenceEquals(expected, _onlineSnapshot) || IsDisposed || _closing) return;
                expected.Announcement = announcement;
                expected.AnnouncementPending = null;
                SendOnlineState(expected);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified announcement fetch failed: " + exception.GetType().Name);
            }
        }

        internal void ExitForcedOnline()
        {
            if (!_onlineForceRequired || _onlineInstalling) return;
            _ownerBall.ExitApplication();
        }

        internal void OpenOnlineAnnouncementLink()
        {
            if (_navigation.Current != "updates" || _onlineInstalling) return;
            var announcement = _onlineSnapshot == null ? null : _onlineSnapshot.Announcement;
            var url = announcement == null || !announcement.Enabled ? null : announcement.LinkUrl;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
                return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri, UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified announcement link failed: " + exception.GetType().Name);
                PostOnlineStatus("link-failed");
            }
        }

        private void HookCloudEvents()
        {
            if (_cloudEventsHooked) return;
            _cloudEventsHooked = true;
            GgmanAccountSession.Changed += HandleCloudChanged;
            _cloudObserver = GgmanAutoSyncService.Current;
            if (_cloudObserver != null) _cloudObserver.StateChanged += HandleCloudChanged;
        }

        private void UnhookCloudEvents()
        {
            if (!_cloudEventsHooked) return;
            _cloudEventsHooked = false;
            GgmanAccountSession.Changed -= HandleCloudChanged;
            if (_cloudObserver != null) _cloudObserver.StateChanged -= HandleCloudChanged;
            _cloudObserver = null;
        }

        private void HandleCloudChanged(object sender, EventArgs args)
        {
            if (IsDisposed || _closing || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(SyncCloudState)); }
                catch (InvalidOperationException) { }
            }
            else SyncCloudState();
        }

        internal void OpenCloudPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("cloud-sync");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            HookCloudEvents();
            SyncCloudState();
        }

        internal void SyncCloudState()
        {
            if (!_ready || IsDisposed || _closing ||
                _browser.CoreWebView2 == null || _navigation.Current != "cloud-sync") return;
            var service = GgmanAutoSyncService.Current;
            var account = GgmanAccountSession.Current;
            var status = service == null ? _ui.Get(UiTextKeys.AutoSyncWaiting) : _ui.Get(service.StatusKey);
            if (service != null && !string.IsNullOrWhiteSpace(service.ConflictCategory))
            {
                var category = service.ConflictCategory == "app" ? UiTextKeys.UnifiedSyncSettings :
                    service.ConflictCategory == "text" ? UiTextKeys.UnifiedSyncText : UiTextKeys.UnifiedSyncEsc;
                status = string.Format(status, _ui.Get(category));
            }
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "cloud-state",
                localOnly = _settings.LocalOnlyMode,
                loggedIn = account != null,
                account = account == null ? null : account.Email,
                autoSync = _settings.AutoConfigSyncEnabled,
                autoGameRestore = _settings.GameAutoRestoreEnabled,
                status,
                busy = _cloudBusy || (service != null && service.IsBusy),
                available = service != null,
                conflict = service == null ? null : service.ConflictCategory,
                conflictHasRemote = service != null && service.ConflictHasRemote
            }));
        }

        internal void ApplyCloudToggle(string request)
        {
            if (_navigation.Current != "cloud-sync" || _cloudBusy) return;
            if (_settings.LocalOnlyMode) { PostCloudResult("local-only"); return; }
            var parts = (request ?? string.Empty).Split(':');
            if (parts.Length != 2 || (parts[1] != "true" && parts[1] != "false"))
            {
                PostCloudResult("invalid"); return;
            }
            var service = GgmanAutoSyncService.Current;
            if (service == null || service.IsBusy) { PostCloudResult("busy"); return; }
            try
            {
                if (parts[0] == "config") service.SetEnabled(parts[1] == "true");
                else if (parts[0] == "game") service.SetGameAutoRestoreEnabled(parts[1] == "true");
                else { PostCloudResult("invalid"); return; }
                SyncCloudState();
                PostCloudResult("saved");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified cloud preference update failed: " + exception.GetType().Name);
                PostCloudResult("failed");
                SyncCloudState();
            }
        }

        internal async void PublishCloudGameProfile()
        {
            if (_navigation.Current != "cloud-sync" || _cloudBusy) return;
            if (_settings.LocalOnlyMode) { PostCloudResult("local-only"); return; }
            var service = GgmanAutoSyncService.Current;
            var account = GgmanAccountSession.Current;
            if (account == null || service == null || service.IsBusy)
            {
                PostCloudResult("unavailable"); return;
            }
            _cloudBusy = true;
            SyncCloudState();
            try
            {
                var version = await service.PublishGameProfileAsync();
                if (!GgmanAccountSession.IsCurrent(account))
                {
                    PostCloudResult("context-changed"); return;
                }
                PostCloudResult("published", version);
            }
            catch (OperationCanceledException)
            {
                PostCloudResult("context-changed");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified game profile upload failed: " + exception.GetType().Name);
                PostCloudResult("failed");
            }
            finally
            {
                _cloudBusy = false;
                SyncCloudState();
            }
        }

        internal async void ResolveCloudConflict()
        {
            if (_navigation.Current != "cloud-sync" || _cloudBusy) return;
            if (_settings.LocalOnlyMode) { PostCloudResult("local-only"); return; }
            var service = GgmanAutoSyncService.Current;
            var account = GgmanAccountSession.Current;
            if (service == null || account == null || service.IsBusy ||
                string.IsNullOrEmpty(service.ConflictCategory))
            {
                PostCloudResult("unavailable"); return;
            }
            if (service.ConflictCategory == "text" && UiTextEditorPanel.HasPendingVisibleChanges)
            {
                PostCloudResult("unsaved"); return;
            }
            _cloudBusy = true;
            SyncCloudState();
            try
            {
                await service.ResolveConflictAsync(true);
                if (!GgmanAccountSession.IsCurrent(account))
                {
                    PostCloudResult("context-changed"); return;
                }
                PostCloudResult(string.IsNullOrEmpty(service.ConflictCategory) ? "resolved" : "failed");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified cloud conflict resolution failed: " + exception.GetType().Name);
                PostCloudResult("failed");
            }
            finally
            {
                _cloudBusy = false;
                SyncCloudState();
            }
        }

        private void PostCloudResult(string status, long version = 0)
        {
            if (!_ready || IsDisposed || _closing || _browser.CoreWebView2 == null ||
                _navigation.Current != "cloud-sync") return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "cloud-result", status, version
            }));
        }

        internal void OpenUpdatesPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("updates");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartOnlineRead();
        }

        internal void SyncOnlineInitialState()
        {
            if (_onlineSnapshot != null && _onlineReadCancellation == null)
                SendOnlineState(_onlineSnapshot);
            else StartOnlineRead();
        }

        internal void StartOnlineRead()
        {
            if (_navigation.Current != "updates" || _onlineInstalling) return;
            CancelOnlineRead();
            _onlineSnapshot = null;
            _onlineCheckedUtc = DateTime.MinValue;
            PostOnlineStatus("checking");
            var source = new CancellationTokenSource();
            _onlineReadCancellation = source;
            ReadOnlineAsync(source, _onlineGeneration);
        }

        private async void ReadOnlineAsync(CancellationTokenSource source, int generation)
        {
            try
            {
                var snapshot = await _ownerBall.FetchUnifiedOnlineSnapshotAsync(source.Token);
                if (!OnlineCurrent(source, generation)) return;
                _onlineSnapshot = snapshot;
                _onlineCheckedUtc = DateTime.UtcNow;
                _onlineForceRequired |= snapshot != null && snapshot.ForceUpdateRequired;
                SendOnlineState(snapshot);
                _ = CompletePendingOnlineAnnouncementAsync(snapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified online check failed: " + exception.GetType().Name);
                if (OnlineCurrent(source, generation)) PostOnlineStatus("unavailable");
            }
            finally
            {
                if (ReferenceEquals(_onlineReadCancellation, source)) _onlineReadCancellation = null;
                source.Dispose();
            }
        }

        private bool OnlineCurrent(CancellationTokenSource source, int generation)
        {
            return !IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                _navigation.Current == "updates" && !source.IsCancellationRequested &&
                ReferenceEquals(_onlineReadCancellation, source) && generation == _onlineGeneration;
        }

        private void SendOnlineState(OnlineSnapshot snapshot)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null ||
                _navigation.Current != "updates") return;
            var canInstall = OnlineCenterForm.CanInstallUpdateForSmokeTest(snapshot) &&
                _ownerBall.UnifiedOnlineCenterAvailable;
            var automaticPrompt = _onlineAutomaticPrompt && canInstall;
            _onlineAutomaticPrompt = false;
            var announcement = snapshot == null || snapshot.Announcement == null ||
                !snapshot.Announcement.Enabled ? null : snapshot.Announcement;
            Uri link;
            var hasAnnouncementLink = announcement != null &&
                Uri.TryCreate(announcement.LinkUrl, UriKind.Absolute, out link) &&
                link.Scheme == Uri.UriSchemeHttps;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "online-state",
                current = snapshot == null || snapshot.CurrentVersion == null ? null : snapshot.CurrentVersion.ToString(),
                latest = snapshot == null || snapshot.LatestVersion == null ? null : snapshot.LatestVersion.ToString(),
                error = snapshot == null || !string.IsNullOrWhiteSpace(snapshot.ErrorMessage),
                available = snapshot != null && snapshot.UpdateAvailable,
                forced = _onlineForceRequired || (snapshot != null && snapshot.ForceUpdateRequired),
                canInstall,
                automaticPrompt,
                notes = snapshot == null || snapshot.Update == null ? null : snapshot.Update.ReleaseNotes,
                source = snapshot == null ? null : snapshot.MetadataSourceName,
                autoEnabled = _settings.AutoUpdateEnabled,
                announcementLink = hasAnnouncementLink,
                announcement = announcement == null ? null : new
                    {
                        title = announcement.Title,
                        body = announcement.Body
                    }
            }));
        }

        internal void ApplyOnlineAutoSetting(string setting)
        {
            if (_navigation.Current != "updates" || _onlineInstalling ||
                _onlineForceRequired ||
                (setting != "true" && setting != "false")) return;
            try
            {
                _settings.AutoUpdateEnabled = setting == "true";
                _settings.Save();
                if (_onlineSnapshot != null) SendOnlineState(_onlineSnapshot);
                else PostOnlineStatus("saved");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified update preference save failed: " + exception.GetType().Name);
                PostOnlineStatus("failed");
            }
        }

        internal async void BeginOnlineInstallation()
        {
            if (_navigation.Current != "updates" || _onlineInstalling ||
                !_ownerBall.UnifiedOnlineCenterAvailable ||
                !OnlineCenterForm.CanInstallUpdateForSmokeTest(_onlineSnapshot)) return;
            if (DateTime.UtcNow - _onlineCheckedUtc > TimeSpan.FromMinutes(10))
            {
                PostOnlineStatus("expired"); return;
            }
            var manifest = _onlineSnapshot.Update;
            _onlineInstalling = true;
            var source = new CancellationTokenSource();
            _onlineInstallCancellation = source;
            PostOnlineStatus("downloading");
            try
            {
                var progress = new Progress<int>(value =>
                {
                    if (!_closing && !IsDisposed && _navigation.Current == "updates" &&
                        ReferenceEquals(_onlineInstallCancellation, source))
                        PostOnlineProgress(value);
                });
                var package = await UpdateInstaller.DownloadAsync(manifest, progress, source.Token);
                if (_closing || IsDisposed || source.IsCancellationRequested ||
                    _navigation.Current != "updates") return;
                PostOnlineStatus("installing");
                UpdateInstaller.StartReplacement(package);
                _ownerBall.ExitApplication();
            }
            catch (OperationCanceledException)
            {
                PostOnlineStatus("canceled");
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified signed update failed", exception);
                PostOnlineStatus("failed");
            }
            finally
            {
                if (ReferenceEquals(_onlineInstallCancellation, source))
                    _onlineInstallCancellation = null;
                _onlineInstalling = false;
                source.Dispose();
            }
        }

        internal void CancelOnlineInstallation()
        {
            var source = _onlineInstallCancellation;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        private void PostOnlineProgress(int percent)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null ||
                _navigation.Current != "updates") return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "online-progress", percent = Math.Max(0, Math.Min(100, percent))
            }));
        }

        private void PostOnlineStatus(string status)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null ||
                _navigation.Current != "updates") return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "online-status", status
            }));
        }

        internal void CancelOnlineRead()
        {
            _onlineGeneration++;
            var read = _onlineReadCancellation;
            _onlineReadCancellation = null;
            if (read != null && !read.IsCancellationRequested) read.Cancel();
            var install = _onlineInstallCancellation;
            _onlineInstallCancellation = null;
            if (install != null && !install.IsCancellationRequested) install.Cancel();
        }
    }
}
