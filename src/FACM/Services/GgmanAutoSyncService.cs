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
        private readonly SynchronizationContext _uiContext;
        private bool _restoringApp;
        private DateTime _settingsChangeNotBeforeUtc;
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
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            RuntimePaths.Initialize();
            _statePath = Path.Combine(RuntimePaths.DataDirectory, "account-auto-sync-state.json");
            _state = LoadState();
            Current = this;
            _timer = new System.Windows.Forms.Timer { Interval = 5000 };
            _timer.Tick += async delegate { await TickAsync(); };
            GgmanAccountSession.Changed += OnAccountChanged;
            AppSettings.Saved += OnSettingsSaved;
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

        internal void SetGameAutoRestoreEnabled(bool enabled)
        {
            if (_disposed || _settings.GameAutoRestoreEnabled == enabled) return;
            _settings.GameAutoRestoreEnabled = enabled;
            _settings.Save();
            _nextGameProfileCheckUtc = DateTime.MinValue;
            _lastAppliedEscKey = null;
            _lastAppliedLobbyKey = null;
            _timer.Interval = 1500;
            UpdateStatus();
        }

        private void OnSettingsSaved(AppSettings settings)
        {
            if (_disposed || _restoringApp || !ReferenceEquals(_settings, settings) ||
                _uiContext == null) return;
            _uiContext.Post(delegate(object ignored)
            {
                if (_disposed || !_settings.AutoConfigSyncEnabled ||
                    GgmanAccountSession.Current == null) return;
                _settingsChangeNotBeforeUtc = DateTime.UtcNow.AddSeconds(3);
                _timer.Interval = 3000;
            }, null);
        }

        private void RestoreApp(GgmanPortableSettingsProfile profile)
        {
            _restoringApp = true;
            try { GgmanPortableSettingsStore.Restore(profile, _settings); }
            finally { _restoringApp = false; }
        }

        internal async Task FlushBeforeExitAsync()
        {
            if (_disposed || !_settings.AutoConfigSyncEnabled ||
                GgmanAccountSession.Current == null) return;
            for (var attempt = 0; attempt < 10 && _busy; attempt++)
                await Task.Delay(100);
            if (_busy || _disposed) return;
            _notBeforeUtc = DateTime.MinValue;
            _settingsChangeNotBeforeUtc = DateTime.MinValue;
            await TickAsync();
        }

        private void OnAccountChanged(object sender, EventArgs args)
        {
            CancelCurrent();
            _conflict = null;
            _conflictHasRemote = false;
            _errors = 0;
            _notBeforeUtc = DateTime.MinValue;
            _settingsChangeNotBeforeUtc = DateTime.MinValue;
            if (!_disposed) _timer.Interval = 1500;
            _nextGameProfileCheckUtc = DateTime.MinValue;
            _lastAppliedEscKey = null;
            _lastAppliedLobbyKey = null;
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
            if (_disposed || _busy ||
                (!_settings.AutoConfigSyncEnabled && !_settings.GameAutoRestoreEnabled) ||
                GgmanAccountSession.Current == null || DateTime.UtcNow < _notBeforeUtc ||
                DateTime.UtcNow < _settingsChangeNotBeforeUtc)
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
                current.SessionKey != account.SessionKey)
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
            if (_settings.AutoConfigSyncEnabled)
            using (var appClient = new GgmanAppSettingsCloudClient())
            using (var textClient = new GgmanUiTextCloudClient())
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
                    () => RestoreApp(appRemote.Profile),
                    token, 0, appPristine, true);
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

            }
            if (_settings.GameAutoRestoreEnabled)
                await TryRestorePublishedGameAsync(account, token);
        }

        internal static bool IsBackgroundUploadAllowed(string kind)
        {
            return kind == "app" || kind == "text";
        }

        private DateTime _nextGameProfileCheckUtc = DateTime.MinValue;
        private string _lastAppliedEscKey;
        private string _lastAppliedLobbyKey;
        private string _observedRiotAccountHash;
        private string _lastGameflowPhase;

        internal void NotifyRiotAccount(string accountHash)
        {
            if (string.IsNullOrWhiteSpace(accountHash) || accountHash.Length != 64 ||
                _uiContext == null || _disposed) return;
            _uiContext.Post(ignored =>
            {
                if (_disposed || !_settings.GameAutoRestoreEnabled ||
                    GgmanAccountSession.Current == null ||
                    string.Equals(_observedRiotAccountHash, accountHash, StringComparison.Ordinal))
                    return;
                _observedRiotAccountHash = accountHash;
                _nextGameProfileCheckUtc = DateTime.UtcNow.AddSeconds(2);
                _timer.Interval = 2500;
            }, null);
        }

        internal void NotifyGameflowPhase(string phase)
        {
            if (_uiContext == null || _disposed) return;
            _uiContext.Post(ignored =>
            {
                if (_disposed) return;
                var prior = _lastGameflowPhase;
                _lastGameflowPhase = phase;
                if (!_settings.GameAutoRestoreEnabled ||
                    GgmanAccountSession.Current == null ||
                    !IsSafeLobbyTransition(prior, phase)) return;
                _nextGameProfileCheckUtc = DateTime.MinValue;
                _timer.Interval = 1500;
            }, null);
        }

        private static bool IsSafeLobbyTransition(string prior, string phase)
        {
            if (string.Equals(prior, phase, StringComparison.OrdinalIgnoreCase)) return false;
            return string.Equals(phase, "Lobby", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(phase, "None", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(phase, "EndOfGame", StringComparison.OrdinalIgnoreCase);
        }

        private async Task TryRestorePublishedGameAsync(GgmanAccountIdentity account,
            CancellationToken token)
        {
            if (DateTime.UtcNow < _nextGameProfileCheckUtc) return;
            if (GameProfileStore.IsLeagueClientRunning() &&
                string.IsNullOrWhiteSpace(_observedRiotAccountHash))
                return;

            try
            {
                using (var cloud = new GgmanGameProfileCloudClient())
                {
                    var saved = await cloud.GetAsync(account, token);
                    RequireSession(account, token);
                    if (saved == null) return;
                    var directory = await Task.Run(
                        () => EscGameDirectoryLocator.Find(_settings.GamePath, token), token);
                    RequireSession(account, token);
                    if (string.IsNullOrWhiteSpace(directory)) return;
                    var riot = _observedRiotAccountHash ??
                        GameProfileStore.ReadLocalAccountMarker(directory);
                    var key = Hash(account.SessionKey + "|" + account.UserId + "|" +
                        Path.GetFullPath(directory).ToUpperInvariant() + "|" + riot) +
                        ":" + saved.Version;

                    if (!GameProfileStore.IsGameProcessAbsent())
                        return;

                    if (!string.Equals(_lastAppliedEscKey, key, StringComparison.Ordinal))
                    {
                        try
                        {
                            RequireSession(account, token);
                            await Task.Run(() =>
                            {
                                EscSettingsBundle local = null;
                                try { local = EscSettingsBackup.Capture(directory); }
                                catch (FileNotFoundException) { }
                                if (!GameProfileStore.MatchesEsc(local, saved.Profile.Esc))
                                    GameProfileStore.RestoreEsc(saved.Profile, directory);
                            }, token);
                            RequireSession(account, token);
                            _lastAppliedEscKey = key;
                            AppLog.Info("Registered game ESC restored; revision=" + saved.Version);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error)
                        {
                            AppLog.Info("Game ESC restore pending: " + error.GetType().Name);
                        }
                    }

                    if (!string.Equals(_lastAppliedLobbyKey, key, StringComparison.Ordinal))
                    {
                        try
                        {
                            RequireSession(account, token);
                            await Task.Run(() =>
                            {
                                if (!GameProfileStore.MatchesLobby(saved.Profile, directory))
                                    GameProfileStore.RestoreLobby(saved.Profile, directory);
                            }, token);
                            RequireSession(account, token);
                            _lastAppliedLobbyKey = key;
                            AppLog.Info("Registered lobby preference file restored; revision=" +
                                saved.Version);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error)
                        {
                            AppLog.Info("Lobby YAML restore pending: " + error.GetType().Name);
                        }
                    }

                    if (_lastAppliedEscKey != key || _lastAppliedLobbyKey != key)
                    {
                        _nextGameProfileCheckUtc = DateTime.UtcNow.AddSeconds(15);
                        _timer.Interval = 15000;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                _nextGameProfileCheckUtc = DateTime.UtcNow.AddSeconds(30);
                _timer.Interval = 30000;
                AppLog.Info("Game profile restore deferred: " + error.GetType().Name);
            }
        }

        internal async Task<long> PublishGameProfileAsync()
        {
            if (_disposed || _busy)
                throw new InvalidOperationException("云同步正在执行，请稍后再试。");
            var account = GgmanEscCloudClient.RequireRegisteredSession();
            if (!GameProfileStore.IsGameProcessAbsent())
                throw new InvalidOperationException("请先退出训练或正式对局，回到大厅后即可上传设置。大厅无需关闭。");
            var token = _sessionCancellation.Token;
            _busy = true;
            UpdateStatus();
            try
            {
                var directory = await Task.Run(
                    () => EscGameDirectoryLocator.Find(_settings.GamePath, token), token);
                if (string.IsNullOrWhiteSpace(directory))
                    throw new DirectoryNotFoundException("请先在 GGman 中设置英雄联盟安装目录。");
                var profile = await Task.Run(() => GameProfileStore.CaptureForUpload(directory), token);
                RequireSession(account, token);
                using (var cloud = new GgmanGameProfileCloudClient())
                {
                    var existing = await cloud.GetAsync(account, token);
                    RequireSession(account, token);
                    var version = await cloud.SetAsync(account, profile,
                        existing == null ? 0 : existing.Version, token);
                    RequireSession(account, token);
                    _nextGameProfileCheckUtc = DateTime.MinValue;
                    return version;
                }
            }
            finally
            {
                _busy = false;
                UpdateStatus();
            }
        }

        private enum SyncAction { Unchanged, Adopt, Upload, Restore, Conflict }

        private static SyncAction DecideApp(GgmanAutoSyncCursor previous, string localHash,
            long remoteVersion, string remoteHash, bool otherOwner)
        {
            if (remoteVersion == 0)
                return otherOwner || previous != null ? SyncAction.Conflict : SyncAction.Upload;
            if (string.Equals(localHash, remoteHash, StringComparison.Ordinal))
                return previous != null && !otherOwner && previous.Version == remoteVersion &&
                    string.Equals(previous.Fingerprint, localHash, StringComparison.Ordinal)
                    ? SyncAction.Unchanged : SyncAction.Adopt;
            if (otherOwner || previous == null || remoteVersion != previous.Version ||
                !string.Equals(remoteHash, previous.Fingerprint, StringComparison.Ordinal))
                return SyncAction.Restore;
            return !string.Equals(localHash, previous.Fingerprint, StringComparison.Ordinal)
                ? SyncAction.Upload : SyncAction.Unchanged;
        }

        private static SyncAction Decide(GgmanAutoSyncCursor previous, string localHash,
            long remoteVersion, string remoteHash, bool otherOwner, bool pristine)
        {
            if (remoteVersion > 0 && localHash != null &&
                string.Equals(localHash, remoteHash, StringComparison.Ordinal))
            {
                if (!otherOwner && previous != null && previous.Version == remoteVersion &&
                    string.Equals(previous.Fingerprint, localHash, StringComparison.Ordinal))
                    return SyncAction.Unchanged;
                return SyncAction.Adopt;
            }
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

        private async Task<bool> SyncOneAsync(string kind, GgmanAutoSyncAccountState history,
            string owner, bool otherOwner, GgmanAutoSyncCursor previous, string localHash,
            long remoteVersion, string remoteHash, Func<Task<long>> upload,
            Action restore, CancellationToken token, int choice = 0, bool pristine = false,
            bool cloudFirst = false)
        {
            var decision = choice == 1 ? SyncAction.Upload :
                choice == 2 ? SyncAction.Restore :
                cloudFirst ? DecideApp(previous, localHash, remoteVersion, remoteHash, otherOwner) :
                Decide(previous, localHash, remoteVersion, remoteHash, otherOwner, pristine);
            if (decision == SyncAction.Adopt)
            {
                PersistCursor(history, owner, kind, remoteVersion, remoteHash);
                return true;
            }
            if (decision == SyncAction.Unchanged) return true;
            if (decision == SyncAction.Upload && !IsBackgroundUploadAllowed(kind))
                throw new InvalidOperationException("Game configuration requires explicit upload.");
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
                return false;
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
            return true;
        }

        private void PersistCursor(GgmanAutoSyncAccountState history, string owner,
            string kind, long version, string hash)
        {
            PersistCursorState(_state, history, owner, kind, version, hash, SaveState);
        }

        private static void PersistCursorState(GgmanAutoSyncState state,
            GgmanAutoSyncAccountState history, string owner, string kind,
            long version, string hash, Action save)
        {
            if (state == null || history == null || save == null)
                throw new ArgumentNullException("Auto sync checkpoint requires valid state and storage.");
            if (kind != "app" && kind != "text" && kind != "esc")
                throw new ArgumentException("Unknown auto sync category.", nameof(kind));

            var oldCursor = kind == "app" ? history.App :
                kind == "text" ? history.Text : history.Esc;
            var oldOwner = kind == "app" ? state.AppOwner :
                kind == "text" ? state.TextOwner : state.EscOwner;
            var oldLastOwner = state.LastOwner;
            var cursor = new GgmanAutoSyncCursor { Version = version, Fingerprint = hash };

            try
            {
                if (kind == "app") { history.App = cursor; state.AppOwner = owner; }
                else if (kind == "text") { history.Text = cursor; state.TextOwner = owner; }
                else { history.Esc = cursor; state.EscOwner = owner; }
                if (string.IsNullOrEmpty(state.LastOwner)) state.LastOwner = owner;
                save();
            }
            catch
            {
                if (kind == "app") { history.App = oldCursor; state.AppOwner = oldOwner; }
                else if (kind == "text") { history.Text = oldCursor; state.TextOwner = oldOwner; }
                else { history.Esc = oldCursor; state.EscOwner = oldOwner; }
                state.LastOwner = oldLastOwner;
                throw;
            }
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
                var resolved = false;
                var owner = Hash(account.UserId);
                GgmanAutoSyncAccountState history;
                if (!_state.Accounts.TryGetValue(owner, out history)) return;
                using (var appClient = new GgmanAppSettingsCloudClient())
                using (var textClient = new GgmanUiTextCloudClient())
                {
                    RequireSession(account, token);
                    if (kind == "app")
                    {
                        var remote = await appClient.GetAsync(account, token);
                        var local = GgmanPortableSettingsStore.Capture(_settings);
                        RequireSession(account, token);
                        resolved = await SyncOneAsync(kind, history, owner, false, history.App,
                            FingerprintApp(local), remote == null ? 0 : remote.Version,
                            remote == null ? null : FingerprintApp(remote.Profile),
                            async () => await appClient.SetAsync(account, local,
                                remote == null ? 0 : remote.Version, token),
                            () => RestoreApp(remote.Profile),
                            token, keepLocal ? 1 : 2);
                    }
                    else if (kind == "text")
                    {
                        var remote = await textClient.GetAsync(account, token);
                        var local = UiTextCustomizationStore.Capture();
                        RequireSession(account, token);
                        resolved = await SyncOneAsync(kind, history, owner, false, history.Text,
                            FingerprintText(local), remote == null ? 0 : remote.Version,
                            remote == null ? null : FingerprintText(remote.Profile),
                            async () => await textClient.SetAsync(account, local,
                                remote == null ? 0 : remote.Version, token),
                            () => UiTextCustomizationStore.Apply(remote.Profile),
                            token, keepLocal ? 1 : 2);
                    }

                }
                if (resolved)
                {
                    _conflict = null;
                    _conflictHasRemote = false;
                    _errors = 0;
                    _notBeforeUtc = DateTime.MinValue;
                    _timer.Interval = 1500;
                }
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
            GameProfileStore.ValidateForSmokeTest();
            if (!IsSafeLobbyTransition("InProgress", "Lobby") ||
                IsSafeLobbyTransition("Lobby", "Lobby") ||
                IsSafeLobbyTransition("InProgress", "InProgress"))
                throw new InvalidOperationException("Live lobby phase trigger changed.");
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
                Decide(previous, "old", 5, "old", false, false) != SyncAction.Unchanged ||
                Decide(previous, "old", 5, "old", true, false) != SyncAction.Adopt ||
                Decide(previous, "new", 5, "old", false, false) != SyncAction.Upload ||
                Decide(previous, "old", 6, "new", false, false) != SyncAction.Restore ||
                Decide(previous, "new", 6, "newer", false, false) != SyncAction.Conflict ||
                Decide(previous, "new", 5, "old", true, false) != SyncAction.Conflict)
                throw new InvalidOperationException("Auto sync conflict-selection rules changed.");

            if (IsBackgroundUploadAllowed("esc") || !IsBackgroundUploadAllowed("app") ||
                !IsBackgroundUploadAllowed("text"))
                throw new InvalidOperationException("Game profiles must never auto-upload.");

            if (DecideApp(null, "local", 7, "remote", false) != SyncAction.Restore ||
                DecideApp(previous, "local", 6, "new-remote", false) != SyncAction.Restore ||
                DecideApp(previous, "new-local", 5, "old", false) != SyncAction.Upload ||
                DecideApp(previous, "local", 5, "remote", true) != SyncAction.Restore ||
                DecideApp(null, "local", 0, null, true) != SyncAction.Conflict ||
                DecideApp(null, "local", 0, null, false) != SyncAction.Upload)
                throw new InvalidOperationException("Cloud-first app sync rules changed.");

            var state = new GgmanAutoSyncState();
            state.Accounts.Add(key, demo);
            var failed = false;
            try
            {
                PersistCursorState(state, demo, key, "app", 1, "first",
                    () => { throw new IOException("checkpoint unavailable"); });
            }
            catch (IOException) { failed = true; }
            if (!failed || demo.App != null || state.AppOwner != string.Empty ||
                state.LastOwner != string.Empty)
                throw new InvalidOperationException("Failed checkpoint changed in-memory ownership.");

            PersistCursorState(state, demo, key, "app", 1, "first", () => { });
            var committed = demo.App;
            failed = false;
            try
            {
                PersistCursorState(state, demo, "another-owner", "app", 2, "second",
                    () => { throw new IOException("checkpoint unavailable"); });
            }
            catch (IOException) { failed = true; }
            if (!failed || !ReferenceEquals(demo.App, committed) ||
                state.AppOwner != key || state.LastOwner != key)
                throw new InvalidOperationException("Failed checkpoint replaced a committed cursor.");

            foreach (var category in new[] { "text", "esc" })
            {
                failed = false;
                try
                {
                    PersistCursorState(state, demo, "another-owner", category, 1, "test",
                        () => { throw new IOException("checkpoint unavailable"); });
                }
                catch (IOException) { failed = true; }
                if (!failed || demo.Text != null || demo.Esc != null ||
                    state.TextOwner != string.Empty || state.EscOwner != string.Empty)
                    throw new InvalidOperationException("Failed checkpoint changed category ownership.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            GgmanAccountSession.Changed -= OnAccountChanged;
            AppSettings.Saved -= OnSettingsSaved;
            CancelCurrent();
            _timer.Dispose();
            _sessionCancellation.Dispose();
            DisposeRetired();
            Current = null;
        }
    }
}
