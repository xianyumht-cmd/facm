using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.Online;

namespace FACM.Services
{
    internal sealed class GgmanAutoSyncCursor
    {
        public long Version { get; set; }
        public string Fingerprint { get; set; }
    }

    internal sealed class GgmanAutoSyncAccountState
    {
        public GgmanAutoSyncCursor App { get; set; }
        public GgmanAutoSyncCursor Text { get; set; }
        public GgmanAutoSyncCursor Esc { get; set; }
    }

    internal sealed class GgmanAutoSyncState
    {
        public int SchemaVersion { get; set; } = 1;
        public string LastOwner { get; set; } = string.Empty;
        public string AppOwner { get; set; } = string.Empty;
        public string TextOwner { get; set; } = string.Empty;
        public string EscOwner { get; set; } = string.Empty;
        public Dictionary<string, GgmanAutoSyncAccountState> Accounts { get; set; } =
            new Dictionary<string, GgmanAutoSyncAccountState>(StringComparer.Ordinal);
    }

    internal sealed class GgmanAutoSyncService : IDisposable
    {
        private readonly AppSettings _settings;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 };
        private readonly string _statePath;
        private GgmanAutoSyncState _state;
        private CancellationTokenSource _sessionCancellation = new CancellationTokenSource();
        private readonly List<CancellationTokenSource> _retired = new List<CancellationTokenSource>();
        private bool _busy;
        private bool _disposed;
        private DateTime _notBeforeUtc;
        private int _errors;
        private string _conflict;
        private bool _conflictHasRemote;
        private string _status = UiTextKeys.AutoSyncWaiting;
        internal static GgmanAutoSyncService Current { get; private set; }
        internal event EventHandler StateChanged;

        internal string StatusKey { get { return _status; } }
        internal string ConflictCategory { get { return _conflict; } }
        internal bool ConflictHasRemote { get { return _conflictHasRemote; } }
        internal bool IsBusy { get { return _busy; } }

        internal GgmanAutoSyncService(AppSettings settings)
        {
            if (Current != null) throw new InvalidOperationException("Auto sync already started.");
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            RuntimePaths.Initialize();
            _statePath = Path.Combine(RuntimePaths.DataDirectory, "account-auto-sync-state.json");
            _state = LoadState();
            Current = this;
            _timer = new System.Windows.Forms.Timer { Interval = 5000 };
            _timer.Tick += async delegate { await TickAsync(); };
            GgmanAccountSession.Changed += OnAccountChanged;
            _timer.Start();
            UpdateStatus();
        }

        internal void SetEnabled(bool enabled)
        {
            if (_disposed || _settings.AutoConfigSyncEnabled == enabled) return;
            _settings.AutoConfigSyncEnabled = enabled;
            _settings.Save();
            CancelCurrent();
            _conflict = null;
            _conflictHasRemote = false;
            _errors = 0;
            _notBeforeUtc = DateTime.MinValue;
            _timer.Interval = 5000;
            UpdateStatus();
        }

        private void OnAccountChanged(object sender, EventArgs args)
        {
            CancelCurrent();
            _conflict = null;
            _conflictHasRemote = false;
            _errors = 0;
            _notBeforeUtc = DateTime.MinValue;
            if (!_disposed) _timer.Interval = 1500;
            UpdateStatus();
        }

        private void CancelCurrent()
        {
            var old = _sessionCancellation;
            _sessionCancellation = new CancellationTokenSource();
            old.Cancel();
            _retired.Add(old);
            if (!_busy) DisposeRetired();
        }

        private void DisposeRetired()
        {
            foreach (var source in _retired) source.Dispose();
            _retired.Clear();
        }

        private void UpdateStatus()
        {
            _status = !_settings.AutoConfigSyncEnabled ? UiTextKeys.AutoSyncOff :
                GgmanAccountSession.Current == null ? UiTextKeys.AutoSyncWaiting :
                _conflict != null ? UiTextKeys.AutoSyncConflict :
                _busy ? UiTextKeys.AutoSyncWorking : _errors > 0 ?
                UiTextKeys.AutoSyncRetrying : UiTextKeys.AutoSyncReady;
            var changed = StateChanged;
            if (changed != null) changed(this, EventArgs.Empty);
        }

        private async Task TickAsync()
        {
            if (_disposed || _busy || !_settings.AutoConfigSyncEnabled ||
                GgmanAccountSession.Current == null || DateTime.UtcNow < _notBeforeUtc)
                return;
            _timer.Interval = 90000;
            var account = GgmanAccountSession.Current;
            var token = _sessionCancellation.Token;
            _busy = true;
            UpdateStatus();
            try
            {
                await SynchronizeAsync(account, token);
                _errors = 0;
                _notBeforeUtc = DateTime.MinValue;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                _errors = Math.Min(5, _errors + 1);
                _notBeforeUtc = DateTime.UtcNow.AddSeconds(Math.Min(900, 20 * (1 << _errors)));
                AppLog.Info("Account auto sync deferred: " + error.GetType().Name);
            }
            finally
            {
                _busy = false;
                DisposeRetired();
                UpdateStatus();
            }
        }

        private static void RequireSession(GgmanAccountIdentity account, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var current = GgmanAccountSession.Current;
            if (current == null || current.UserId != account.UserId ||
                current.AccessToken != account.AccessToken)
                throw new OperationCanceledException("Account session changed.");
        }

        private async Task SynchronizeAsync(GgmanAccountIdentity account, CancellationToken token)
        {
            var owner = Hash(account.UserId);
            GgmanAutoSyncAccountState history;
            if (!_state.Accounts.TryGetValue(owner, out history))
            {
                history = new GgmanAutoSyncAccountState();
                _state.Accounts.Add(owner, history);
            }
            var appOtherOwner = IsOtherOwner(owner, _state.AppOwner);
            _conflict = null;
            _conflictHasRemote = false;
            using (var appClient = new GgmanAppSettingsCloudClient())
            using (var textClient = new GgmanUiTextCloudClient())
            using (var escClient = new GgmanEscCloudClient())
            {
                RequireSession(account, token);
                var appRemote = await appClient.GetAsync(account, token);
                RequireSession(account, token);
                var appLocal = GgmanPortableSettingsStore.Capture(_settings);
                var appHash = FingerprintApp(appLocal);
                var appPristine = appHash == FingerprintApp(
                    GgmanPortableSettingsStore.Capture(new AppSettings()));
                await SyncOneAsync("app", history, owner, appOtherOwner, history.App,
                    appHash, appRemote == null ? 0 : appRemote.Version,
                    appRemote == null ? null : FingerprintApp(appRemote.Profile),
                    async () => await appClient.SetAsync(account, appLocal,
                        appRemote == null ? 0 : appRemote.Version, token),
                    () => GgmanPortableSettingsStore.Restore(appRemote.Profile, _settings),
                    token, 0, appPristine);
                RequireSession(account, token);

                var textRemote = await textClient.GetAsync(account, token);
                var textLocal = UiTextCustomizationStore.Capture();
                var textHash = FingerprintText(textLocal);
                await SyncOneAsync("text", history, owner,
                    IsOtherOwner(owner, _state.TextOwner), history.Text,
                    textHash, textRemote == null ? 0 : textRemote.Version,
                    textRemote == null ? null : FingerprintText(textRemote.Profile),
                    async () => await textClient.SetAsync(account, textLocal,
                        textRemote == null ? 0 : textRemote.Version, token),
                    () => UiTextCustomizationStore.Apply(textRemote.Profile), token, 0,
                    textLocal.Text.Count == 0 && textLocal.Replace.Count == 0);
                RequireSession(account, token);

                var dir = await Task.Run(() => EscGameDirectoryLocator.Find(_settings.GamePath, token));
                RequireSession(account, token);
                if (!string.IsNullOrEmpty(dir))
                {
                    EscSettingsBundle escLocal = null;
                    try { escLocal = EscSettingsBackup.Capture(dir); }
                    catch (FileNotFoundException) { }
                    var escRemote = await escClient.GetAsync(account, token);
                    RequireSession(account, token);
                    if (escLocal == null && escRemote != null)
                    {
                        EscSettingsBackup.Restore(escRemote.Bundle, dir);
                        PersistCursor(history, owner, "esc", escRemote.Version,
                            FingerprintEsc(escRemote.Bundle));
                    }
                    else if (escLocal != null)
                        await SyncOneAsync("esc", history, owner,
                            IsOtherOwner(owner, _state.EscOwner), history.Esc,
                            FingerprintEsc(escLocal), escRemote == null ? 0 : escRemote.Version,
                            escRemote == null ? null : FingerprintEsc(escRemote.Bundle),
                            async () => await escClient.SetAsync(account, escLocal,
                                escRemote == null ? 0 : escRemote.Version, token),
                            () => EscSettingsBackup.Restore(escRemote.Bundle, dir), token);
                }
            }
        }

        private enum SyncAction { Unchanged, Adopt, Upload, Restore, Conflict }

        private static SyncAction Decide(GgmanAutoSyncCursor previous, string localHash,
            long remoteVersion, string remoteHash, bool otherOwner, bool pristine)
        {
            if (remoteVersion > 0 && localHash != null &&
                string.Equals(localHash, remoteHash, StringComparison.Ordinal))
                return SyncAction.Adopt;
            if (otherOwner) return SyncAction.Conflict;
            if (previous == null)
            {
                if (remoteVersion == 0)
                    return otherOwner ? SyncAction.Conflict : SyncAction.Upload;
                return !otherOwner && pristine ? SyncAction.Restore : SyncAction.Conflict;
            }
            var localChanged = localHash != previous.Fingerprint;
            var remoteChanged = remoteVersion != previous.Version;
            if (remoteVersion == 0 && previous.Version > 0 ||
                localChanged && remoteChanged)
                return SyncAction.Conflict;
            if (remoteChanged) return SyncAction.Restore;
            if (localChanged) return SyncAction.Upload;
            return SyncAction.Unchanged;
        }

        private bool IsOtherOwner(string owner, string categoryOwner)
        {
            if (!string.IsNullOrEmpty(categoryOwner)) return categoryOwner != owner;
            return !string.IsNullOrEmpty(_state.LastOwner) && _state.LastOwner != owner;
        }

        private async Task SyncOneAsync(string kind, GgmanAutoSyncAccountState history,
            string owner, bool otherOwner, GgmanAutoSyncCursor previous, string localHash,
            long remoteVersion, string remoteHash, Func<Task<long>> upload,
            Action restore, CancellationToken token, int choice = 0, bool pristine = false)
        {
            var decision = choice == 1 ? SyncAction.Upload :
                choice == 2 ? SyncAction.Restore :
                Decide(previous, localHash, remoteVersion, remoteHash, otherOwner, pristine);
            if (decision == SyncAction.Adopt)
            {
                PersistCursor(history, owner, kind, remoteVersion, remoteHash);
                return;
            }
            if (decision == SyncAction.Unchanged) return;
            if (kind == "text" && FACM.League.UiTextEditorPanel.HasPendingVisibleChanges &&
                decision == SyncAction.Restore)
                decision = SyncAction.Conflict;
            if (decision == SyncAction.Restore && remoteVersion == 0)
                decision = SyncAction.Conflict;
            if (decision == SyncAction.Conflict)
            {
                if (_conflict == null)
                {
                    _conflict = kind;
                    _conflictHasRemote = remoteVersion > 0;
                }
                return;
            }

            token.ThrowIfCancellationRequested();
            if (decision == SyncAction.Restore)
            {
                restore();
                PersistCursor(history, owner, kind, remoteVersion, remoteHash);
            }
            else
            {
                var version = await upload();
                token.ThrowIfCancellationRequested();
                PersistCursor(history, owner, kind, version, localHash);
            }
        }

        private void PersistCursor(GgmanAutoSyncAccountState history, string owner,
            string kind, long version, string hash)
        {
            var cursor = new GgmanAutoSyncCursor { Version = version, Fingerprint = hash };
            if (kind == "app") { history.App = cursor; _state.AppOwner = owner; }
            else if (kind == "text") { history.Text = cursor; _state.TextOwner = owner; }
            else if (kind == "esc") { history.Esc = cursor; _state.EscOwner = owner; }
            if (string.IsNullOrEmpty(_state.LastOwner)) _state.LastOwner = owner;
            SaveState();
        }

        internal async Task ResolveConflictAsync(bool keepLocal)
        {
            if (_busy || _disposed || _conflict == null || !_settings.AutoConfigSyncEnabled) return;
            var kind = _conflict;
            var account = GgmanAccountSession.Current;
            if (account == null) return;
            _busy = true;
            UpdateStatus();
            var token = _sessionCancellation.Token;
            try
            {
                var owner = Hash(account.UserId);
                GgmanAutoSyncAccountState history;
                if (!_state.Accounts.TryGetValue(owner, out history)) return;
                using (var appClient = new GgmanAppSettingsCloudClient())
                using (var textClient = new GgmanUiTextCloudClient())
                using (var escClient = new GgmanEscCloudClient())
                {
                    RequireSession(account, token);
                    if (kind == "app")
                    {
                        var remote = await appClient.GetAsync(account, token);
                        var local = GgmanPortableSettingsStore.Capture(_settings);
                        RequireSession(account, token);
                        await SyncOneAsync(kind, history, owner, false, history.App,
                            FingerprintApp(local), remote == null ? 0 : remote.Version,
                            remote == null ? null : FingerprintApp(remote.Profile),
                            async () => await appClient.SetAsync(account, local,
                                remote == null ? 0 : remote.Version, token),
                            () => GgmanPortableSettingsStore.Restore(remote.Profile, _settings),
                            token, keepLocal ? 1 : 2);
                    }
                    else if (kind == "text")
                    {
                        var remote = await textClient.GetAsync(account, token);
                        var local = UiTextCustomizationStore.Capture();
                        RequireSession(account, token);
                        await SyncOneAsync(kind, history, owner, false, history.Text,
                            FingerprintText(local), remote == null ? 0 : remote.Version,
                            remote == null ? null : FingerprintText(remote.Profile),
                            async () => await textClient.SetAsync(account, local,
                                remote == null ? 0 : remote.Version, token),
                            () => UiTextCustomizationStore.Apply(remote.Profile),
                            token, keepLocal ? 1 : 2);
                    }
                    else if (kind == "esc")
                    {
                        var dir = await Task.Run(() => EscGameDirectoryLocator.Find(_settings.GamePath, token));
                        if (string.IsNullOrWhiteSpace(dir)) return;
                        var local = EscSettingsBackup.Capture(dir);
                        var remote = await escClient.GetAsync(account, token);
                        RequireSession(account, token);
                        await SyncOneAsync(kind, history, owner, false, history.Esc,
                            FingerprintEsc(local), remote == null ? 0 : remote.Version,
                            remote == null ? null : FingerprintEsc(remote.Bundle),
                            async () => await escClient.SetAsync(account, local,
                                remote == null ? 0 : remote.Version, token),
                            () => EscSettingsBackup.Restore(remote.Bundle, dir),
                            token, keepLocal ? 1 : 2);
                    }
                }
                _conflict = null;
                _conflictHasRemote = false;
                _errors = 0;
                _notBeforeUtc = DateTime.MinValue;
                _timer.Interval = 1500;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                _errors++;
                AppLog.Info("Auto sync resolution failed: " + error.GetType().Name);
            }
            finally
            {
                _busy = false;
                DisposeRetired();
                UpdateStatus();
            }
        }

        private GgmanAutoSyncState LoadState()
        {
            try
            {
                if (File.Exists(_statePath))
                {
                    var raw = File.ReadAllText(_statePath, Encoding.UTF8);
                    var restored = _json.Deserialize<GgmanAutoSyncState>(raw);
                    if (restored != null && restored.SchemaVersion == 1 &&
                        restored.Accounts != null && restored.LastOwner != null)
                        return restored;
                }
            }
            catch (Exception error) { AppLog.Info("Auto sync metadata unavailable: " + error.GetType().Name); }
            return new GgmanAutoSyncState();
        }

        private void SaveState()
        {
            var text = _json.Serialize(_state);
            if (Encoding.UTF8.GetByteCount(text) > 128 * 1024)
                throw new InvalidDataException("Auto sync metadata exceeds limit.");
            var temp = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(_statePath)) File.Replace(temp, _statePath, null);
                else File.Move(temp, _statePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty)))
                    .Replace("-", string.Empty);
        }

        private static string FingerprintApp(GgmanPortableSettingsProfile value)
        {
            return Hash(GgmanPortableSettingsStore.Serialize(value));
        }

        private static string FingerprintText(UiTextProfile value)
        {
            var normalized = new UiTextProfile();
            foreach (var pair in value.Text.OrderBy(x => x.Key, StringComparer.Ordinal))
                normalized.Text.Add(pair.Key, pair.Value);
            foreach (var pair in value.Replace.OrderBy(x => x.Key, StringComparer.Ordinal))
                normalized.Replace.Add(pair.Key, pair.Value);
            return Hash(UiTextCustomizationStore.Serialize(normalized));
        }

        private static string FingerprintEsc(EscSettingsBundle value)
        {
            EscSettingsBackup.Validate(value);
            return Hash(string.Join("|", value.Files.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Name.ToLowerInvariant() + ":" + x.Sha256.ToLowerInvariant())));
        }

        internal static void ValidateForSmokeTest()
        {
            var demo = new GgmanAutoSyncAccountState();
            var key = Hash("test-uid");
            if (key.Length != 64 || key.Contains("test-uid") ||
                FingerprintText(new UiTextProfile()) != FingerprintText(new UiTextProfile()))
                throw new InvalidOperationException("Auto sync owner/fingerprint smoke failed.");
            if (demo.App != null || demo.Text != null || demo.Esc != null)
                throw new InvalidOperationException("Auto sync metadata must start unlinked.");
            var previous = new GgmanAutoSyncCursor { Version = 5, Fingerprint = "old" };
            if (Decide(null, "local", 0, null, false, false) != SyncAction.Upload ||
                Decide(null, "local", 3, "remote", false, true) != SyncAction.Restore ||
                Decide(null, "local", 3, "remote", false, false) != SyncAction.Conflict ||
                Decide(null, "local", 0, null, true, false) != SyncAction.Conflict ||
                Decide(previous, "old", 5, "old", false, false) != SyncAction.Adopt ||
                Decide(previous, "new", 5, "old", false, false) != SyncAction.Upload ||
                Decide(previous, "old", 6, "new", false, false) != SyncAction.Restore ||
                Decide(previous, "new", 6, "newer", false, false) != SyncAction.Conflict ||
                Decide(previous, "new", 5, "old", true, false) != SyncAction.Conflict)
                throw new InvalidOperationException("Auto sync conflict-selection rules changed.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            GgmanAccountSession.Changed -= OnAccountChanged;
            CancelCurrent();
            _timer.Dispose();
            _sessionCancellation.Dispose();
            DisposeRetired();
            Current = null;
        }
    }
}
