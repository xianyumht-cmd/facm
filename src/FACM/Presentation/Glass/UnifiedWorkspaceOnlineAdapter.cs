using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Online;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // The single workbench owns online view state; the signed updater remains the only installer.
    internal sealed class UnifiedWorkspaceOnlineAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Func<CancellationToken, Task<OnlineSnapshot>> _fetch;
        private readonly Func<bool> _centerAvailable;
        private readonly Action _exit;
        private readonly AppSettings _settings;
        private CancellationTokenSource _read;
        private CancellationTokenSource _install;
        private int _generation;
        private OnlineSnapshot _snapshot;
        private DateTime _checkedUtc;
        private bool _installing;
        private bool _forceRequired;
        private bool _automaticPrompt;

        internal bool IsInstalling { get { return _installing; } }
        internal bool RequiresForce { get { return _forceRequired; } }

        internal UnifiedWorkspaceOnlineAdapter(Func<bool> canPublish, Action<string> publish,
            Func<CancellationToken, Task<OnlineSnapshot>> fetch, Func<bool> centerAvailable,
            Action exit, AppSettings settings)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
            _centerAvailable = centerAvailable ?? throw new ArgumentNullException(nameof(centerAvailable));
            _exit = exit ?? throw new ArgumentNullException(nameof(exit));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        internal void Present(OnlineSnapshot snapshot, bool force, bool automatic)
        {
            if (_installing) return;
            _forceRequired |= force || (snapshot != null && snapshot.ForceUpdateRequired);
            _automaticPrompt = !_forceRequired && automatic;
            CancelRead();
            _snapshot = snapshot ?? new OnlineSnapshot();
            _checkedUtc = DateTime.UtcNow;
            SendState(_snapshot);
            CompleteAnnouncementAsync(_snapshot);
        }

        private async void CompleteAnnouncementAsync(OnlineSnapshot expected)
        {
            var pending = expected == null ? null : expected.AnnouncementPending;
            if (pending == null) return;
            try
            {
                var announcement = await pending;
                if (!ReferenceEquals(expected, _snapshot)) return;
                expected.Announcement = announcement;
                expected.AnnouncementPending = null;
                SendState(expected);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            { AppLog.Info("Unified announcement fetch failed: " + exception.GetType().Name); }
        }

        internal void ExitForced()
        {
            if (_forceRequired && !_installing) _exit();
        }

        internal void OpenAnnouncementLink()
        {
            if (!_canPublish() || _installing) return;
            var announcement = _snapshot == null ? null : _snapshot.Announcement;
            var url = announcement == null || !announcement.Enabled ? null : announcement.LinkUrl;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
                return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified announcement link failed: " + exception.GetType().Name);
                PostStatus("link-failed");
            }
        }

        internal void SyncInitialState()
        {
            if (_snapshot != null && _read == null) SendState(_snapshot);
            else StartRead();
        }

        internal void StartRead()
        {
            if (!_canPublish() || _installing) return;
            CancelRead();
            _snapshot = null;
            _checkedUtc = DateTime.MinValue;
            PostStatus("checking");
            var source = new CancellationTokenSource();
            _read = source;
            ReadAsync(source, _generation);
        }

        private async void ReadAsync(CancellationTokenSource source, int generation)
        {
            try
            {
                var snapshot = await _fetch(source.Token);
                if (!ReadCurrent(source, generation)) return;
                _snapshot = snapshot;
                _checkedUtc = DateTime.UtcNow;
                _forceRequired |= snapshot != null && snapshot.ForceUpdateRequired;
                SendState(snapshot);
                CompleteAnnouncementAsync(snapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified online check failed: " + exception.GetType().Name);
                if (ReadCurrent(source, generation)) PostStatus("unavailable");
            }
            finally
            {
                if (ReferenceEquals(_read, source)) _read = null;
                source.Dispose();
            }
        }

        private bool ReadCurrent(CancellationTokenSource source, int generation)
        {
            return _canPublish() && !source.IsCancellationRequested &&
                ReferenceEquals(source, _read) && generation == _generation;
        }

        private void SendState(OnlineSnapshot snapshot)
        {
            if (!_canPublish()) return;
            var canInstall = OnlineCenterForm.CanInstallUpdateForSmokeTest(snapshot) && _centerAvailable();
            var automaticPrompt = _automaticPrompt && canInstall;
            _automaticPrompt = false;
            var announcement = snapshot == null || snapshot.Announcement == null ||
                !snapshot.Announcement.Enabled ? null : snapshot.Announcement;
            Uri link;
            var hasAnnouncementLink = announcement != null &&
                Uri.TryCreate(announcement.LinkUrl, UriKind.Absolute, out link) &&
                link.Scheme == Uri.UriSchemeHttps;
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "online-state",
                current = snapshot == null || snapshot.CurrentVersion == null ? null : snapshot.CurrentVersion.ToString(),
                latest = snapshot == null || snapshot.LatestVersion == null ? null : snapshot.LatestVersion.ToString(),
                error = snapshot == null || !string.IsNullOrWhiteSpace(snapshot.ErrorMessage),
                available = snapshot != null && snapshot.UpdateAvailable,
                forced = _forceRequired || (snapshot != null && snapshot.ForceUpdateRequired),
                canInstall, automaticPrompt,
                notes = snapshot == null || snapshot.Update == null ? null : snapshot.Update.ReleaseNotes,
                source = snapshot == null ? null : snapshot.MetadataSourceName,
                autoEnabled = _settings.AutoUpdateEnabled,
                announcementLink = hasAnnouncementLink,
                announcement = announcement == null ? null : new
                {
                    title = announcement.Title, body = announcement.Body
                }
            }));
        }

        internal void ApplyAutoSetting(string value)
        {
            if (!_canPublish() || _installing || _forceRequired ||
                (value != "true" && value != "false")) return;
            try
            {
                _settings.AutoUpdateEnabled = value == "true";
                _settings.Save();
                if (_snapshot != null) SendState(_snapshot);
                else PostStatus("saved");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified update preference save failed: " + exception.GetType().Name);
                PostStatus("failed");
            }
        }

        internal async void Install()
        {
            if (!_canPublish() || _installing || !_centerAvailable() ||
                !OnlineCenterForm.CanInstallUpdateForSmokeTest(_snapshot)) return;
            if (DateTime.UtcNow - _checkedUtc > TimeSpan.FromMinutes(10))
            { PostStatus("expired"); return; }
            var manifest = _snapshot.Update;
            _installing = true;
            var source = new CancellationTokenSource();
            _install = source;
            PostStatus("downloading");
            try
            {
                var progress = new Progress<int>(value =>
                {
                    if (_canPublish() && ReferenceEquals(_install, source)) PostProgress(value);
                });
                var package = await UpdateInstaller.DownloadAsync(manifest, progress, source.Token);
                if (!_canPublish() || source.IsCancellationRequested ||
                    !ReferenceEquals(_install, source)) return;
                PostStatus("installing");
                UpdateInstaller.StartReplacement(package);
                _exit();
            }
            catch (OperationCanceledException) { PostStatus("canceled"); }
            catch (Exception exception)
            {
                AppLog.Error("Unified signed update failed", exception);
                PostStatus("failed");
            }
            finally
            {
                if (ReferenceEquals(_install, source))
                {
                    _install = null;
                    _installing = false;
                }
                source.Dispose();
            }
        }

        internal void CancelInstallation()
        {
            var source = _install;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        private void PostProgress(int percent)
        {
            if (_canPublish())
                _publish(new JavaScriptSerializer().Serialize(new
                { type = "online-progress", percent = Math.Max(0, Math.Min(100, percent)) }));
        }

        private void PostStatus(string status)
        {
            if (_canPublish())
                _publish(new JavaScriptSerializer().Serialize(new { type = "online-status", status }));
        }

        internal void CancelRead()
        {
            _generation++;
            var read = _read;
            _read = null;
            if (read != null && !read.IsCancellationRequested) read.Cancel();
            var install = _install;
            _install = null;
            if (install != null && !install.IsCancellationRequested) install.Cancel();
        }

        internal static void ValidateForSmokeTest()
        {
            var posts = 0;
            var adapter = new UnifiedWorkspaceOnlineAdapter(() => true,
                delegate { posts++; }, delegate(CancellationToken token) { return Task.FromResult<OnlineSnapshot>(null); },
                () => false, delegate { }, new AppSettings());
            adapter.Present(null, true, true);
            if (!adapter.RequiresForce || adapter.IsInstalling || posts != 1)
                throw new InvalidOperationException("Forced update adapter state changed.");
            adapter.CancelRead();
            var inactive = new UnifiedWorkspaceOnlineAdapter(() => false,
                delegate { throw new InvalidOperationException("Inactive online adapter published."); },
                delegate(CancellationToken token) { return Task.FromResult<OnlineSnapshot>(null); },
                () => false, delegate { }, new AppSettings());
            inactive.StartRead();
            if (inactive._read != null || inactive._generation != 0)
                throw new InvalidOperationException("Update adapter route isolation changed.");
        }
    }
}
