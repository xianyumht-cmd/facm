using System;
using System.Threading;
using System.Windows.Forms;
using FACM.Online;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceCloudSyncAdapter _cloudAdapter;
        private UnifiedWorkspaceOnlineAdapter _onlineAdapter;

        private UnifiedWorkspaceCloudSyncAdapter CloudAdapter
        {
            get
            {
                if (_cloudAdapter == null)
                    _cloudAdapter = new UnifiedWorkspaceCloudSyncAdapter(
                        delegate { return CanPublishOnRoute("cloud-sync"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        DispatchCloudEventOnUi, _settings, _ui);
                return _cloudAdapter;
            }
        }

        private UnifiedWorkspaceOnlineAdapter OnlineAdapter
        {
            get
            {
                if (_onlineAdapter == null)
                    _onlineAdapter = new UnifiedWorkspaceOnlineAdapter(
                        delegate { return CanPublishOnRoute("updates"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        delegate(CancellationToken token) { return _ownerBall.FetchUnifiedOnlineSnapshotAsync(token); },
                        delegate { return _ownerBall.UnifiedOnlineCenterAvailable; },
                        delegate { _ownerBall.ExitApplication(); }, _settings);
                return _onlineAdapter;
            }
        }

        internal bool RequiresForcedOnlineUpdate { get { return _onlineAdapter != null && _onlineAdapter.RequiresForce; } }
        private bool OnlineInstalling { get { return _onlineAdapter != null && _onlineAdapter.IsInstalling; } }

        internal static bool IsRouteAllowedDuringForcedUpdate(bool forced, string route)
        {
            return !forced || string.Equals(route, "updates", StringComparison.Ordinal);
        }

        internal void PresentOnlineSnapshot(OnlineSnapshot snapshot, bool forceMode, bool automaticPrompt)
        {
            if (IsDisposed || _closing || OnlineInstalling) return;
            if (_navigation.Current != "updates") OpenNativeRoute("updates", true);
            OnlineAdapter.Present(snapshot, forceMode, automaticPrompt);
        }

        internal void ExitForcedOnline() { if (_onlineAdapter != null) _onlineAdapter.ExitForced(); }
        internal void OpenOnlineAnnouncementLink() { OnlineAdapter.OpenAnnouncementLink(); }

        private void DispatchCloudEventOnUi(Action work)
        {
            if (IsDisposed || _closing || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(work); }
                catch (InvalidOperationException) { }
            }
            else work();
        }

        private void HookCloudEvents() { CloudAdapter.Hook(); }
        private void UnhookCloudEvents() { if (_cloudAdapter != null) _cloudAdapter.Unhook(); }

        internal void OpenCloudPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("cloud-sync");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            HookCloudEvents();
            SyncCloudState();
        }

        internal void SyncCloudState() { CloudAdapter.Sync(); }
        internal void ApplyCloudToggle(string request) { CloudAdapter.Toggle(request); }
        internal void PublishCloudGameProfile() { CloudAdapter.PublishGameProfile(); }
        internal void ResolveCloudConflict() { CloudAdapter.ResolveConflict(); }
        internal void LeaveCloudPage() { if (_cloudAdapter != null) _cloudAdapter.Leave(); }

        internal void OpenUpdatesPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("updates");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartOnlineRead();
        }

        internal void SyncOnlineInitialState() { OnlineAdapter.SyncInitialState(); }
        internal void StartOnlineRead() { OnlineAdapter.StartRead(); }
        internal void ApplyOnlineAutoSetting(string setting) { OnlineAdapter.ApplyAutoSetting(setting); }
        internal void BeginOnlineInstallation() { OnlineAdapter.Install(); }
        internal void CancelOnlineInstallation() { if (_onlineAdapter != null) _onlineAdapter.CancelInstallation(); }
        internal void CancelOnlineRead() { if (_onlineAdapter != null) _onlineAdapter.CancelRead(); }
    }
}
