using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.League;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeagueEfficiencyModule : IFacmModule
    {
        public const string ModuleId = "league-efficiency";
        public const string ExitGameAction = "exit-game";
        public const string CloseLobbyAction = "close-lobby";

        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            SettingsModule.ModuleId,
            LeagueClientModule.ModuleId,
            LeagueDashboardModule.ModuleId
        };

        private static readonly Dictionary<string, int> ActionIds = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { ExitGameAction, 0x5A11 },
            { CloseLobbyAction, 0x5A12 }
        };

        private readonly SettingsModule _settingsModule;
        private readonly LeagueClientModule _leagueClient;
        private readonly LeagueDashboardModule _dashboard;
        private readonly object _honorSync = new object();
        private LeagueNativeHotkeyService _hotkeys;
        private LeagueEfficiencyActionService _actions;
        private LeaguePostGameAutomationController _postGame;
        private LeagueMatchmakingAutomationController _matchmaking;
        private LeagueMatchmakingStopController _matchmakingStop;
        private LeagueGameflowReconnectController _reconnect;
        private LeagueAutoPickBanController _autoSelect;
        private LeagueLobbyInvitationController _lobbyInvitations;
        private LeagueFriendInvitationController _friendInvitations;
        private LeagueAramTeamSideController _aramSide;
        private LeagueLoginStatusRestoreController _loginSignature;
        private LeagueOfflineStatusLockController _offlineLock;
        private LeagueAutoReplyController _autoReply;
        private LeagueHonorAttemptStatus _lastHonorStatus;
        private bool _autoSelectTemporarilyDisabled;
        private bool _disposed;

        public LeagueEfficiencyModule(SettingsModule settingsModule, LeagueClientModule leagueClient, LeagueDashboardModule dashboard)
        {
            _settingsModule = settingsModule ?? throw new ArgumentNullException(nameof(settingsModule));
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        }

        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public event Action<LeagueHonorAttemptStatus> HonorStatusChanged;

        public LeagueHonorAttemptStatus LastHonorStatus
        {
            get
            {
                lock (_honorSync) return _lastHonorStatus == null ? null : _lastHonorStatus.Clone();
            }
        }

        public void Initialize()
        {
            if (_settingsModule.Settings == null)
                throw new InvalidOperationException("Settings module must initialize before League Efficiency.");

            _actions = new LeagueEfficiencyActionService();
            _hotkeys = new LeagueNativeHotkeyService(ActionIds);
            _hotkeys.HotkeyPressed += HandleHotkey;

            string error;
            if (!TryApplyBindings(
                    _settingsModule.Settings.LeagueExitGameHotkey,
                    _settingsModule.Settings.LeagueCloseLobbyHotkey,
                    false,
                    out error))
                AppLog.Warning("League efficiency saved hotkeys were not registered: " + error);

            _postGame = new LeaguePostGameAutomationController(_leagueClient, (ILeaguePostGameWriteApi)_leagueClient);
            _postGame.HonorAttemptCompleted += HandleHonorAttemptCompleted;
            _postGame.Configure(AutoHonorEnabled, AutoReturnLobbyEnabled, AutoHonorStrategy);

            var matchmakingWriter = (ILeagueMatchmakingWriteApi)_leagueClient;
            _matchmaking = new LeagueMatchmakingAutomationController(_leagueClient, matchmakingWriter);
            _matchmaking.Configure(AutoMatchmakingEnabled, AutoAcceptEnabled, AutoMatchmakingMinPartySize, AutoMatchmakingStartDelayMs, AutoAcceptDelayMs, AutoMatchmakingWaitForInvitees);
            _matchmakingStop = new LeagueMatchmakingStopController(_leagueClient, matchmakingWriter);
            ConfigureMatchmakingStopController();
            _reconnect = new LeagueGameflowReconnectController(_leagueClient, (ILeagueGameflowReconnectWriteApi)_leagueClient);
            _reconnect.Configure(AutoReconnectEnabled);
            _autoSelect = new LeagueAutoPickBanController(_leagueClient,
                (ILeagueChampSelectActionWriteApi)_leagueClient, (ILeagueBenchSwapWriteApi)_leagueClient,
                (ILeagueChampionTradeWriteApi)_leagueClient);
            ConfigureAutoSelect();
            _lobbyInvitations = new LeagueLobbyInvitationController(_leagueClient,
                matchmakingWriter, (ILeagueLobbyNoticeWriteApi)_leagueClient);
            ConfigureLobbyInvitations();
            _friendInvitations = new LeagueFriendInvitationController(_leagueClient,
                (ILeagueLobbyInviteWriteApi)_leagueClient, (ILeagueLobbyNoticeWriteApi)_leagueClient);
            _aramSide = new LeagueAramTeamSideController(_leagueClient, (ILeagueChampSelectSideWriteApi)_leagueClient);
            ConfigureAramTeamSide();
            _loginSignature = new LeagueLoginStatusRestoreController(
                new LeaguePresenceService(_leagueClient, (ILeaguePresenceWriteApi)_leagueClient));
            ConfigureLoginSignature();
            _offlineLock = new LeagueOfflineStatusLockController(
                new LeaguePresenceService(_leagueClient, (ILeaguePresenceWriteApi)_leagueClient));
            _offlineLock.Configure(LockOfflineStatusEnabled);
            _autoReply = new LeagueAutoReplyController(_leagueClient, (ILeaguePrivateChatWriteApi)_leagueClient,
                () => _leagueClient.CurrentSession, _offlineLock);
            ConfigureAutoReply();
            AppSettings.Saved += HandleAppSettingsSaved;

            _dashboard.GameflowStateChanged += HandleGameflowState;
            var current = _dashboard.CurrentGameflowState;
            if (current != null)
            {
                _postGame.Observe(current);
                _matchmaking.Observe(current);
                _matchmakingStop.Observe(current);
                _reconnect.Observe(current);
                _autoSelect.Observe(current);
                _lobbyInvitations.Observe(current);
                _friendInvitations.Observe(current);
                _aramSide.Observe(current);
                _loginSignature.Observe(current);
                _autoReply.Observe(current);
            }
        }

        public Form CreateForm(UiTextCatalog ui)
        {
            ThrowIfDisposed();
            return new LeagueEfficiencyForm(this, ui);
        }

        public string ExitGameHotkey { get { return _settingsModule.Settings.LeagueExitGameHotkey ?? string.Empty; } }
        public string CloseLobbyHotkey { get { return _settingsModule.Settings.LeagueCloseLobbyHotkey ?? string.Empty; } }
        public bool AutoHonorEnabled { get { return _settingsModule.Settings.LeagueAutoHonorTeammateEnabled; } }
        public string AutoHonorStrategy { get { return LeagueHonorStrategy.Normalize(_settingsModule.Settings.LeagueAutoHonorStrategy); } }
        public bool AutoReturnLobbyEnabled { get { return _settingsModule.Settings.LeagueAutoReturnLobbyEnabled; } }
        public bool AutoMatchmakingEnabled { get { return _settingsModule.Settings.LeagueAutoMatchmakingEnabled; } }
        public bool AutoAcceptEnabled { get { return _settingsModule.Settings.LeagueAutoAcceptEnabled; } }
        public bool AutoReconnectEnabled { get { return _settingsModule.Settings.LeagueAutoReconnectEnabled; } }
        public bool AutoAramTeamSideEnabled { get { return _settingsModule.Settings.LeagueAutoAramTeamSideEnabled; } }
        public bool AutoAramTeamSideVisibleToTeam { get { return _settingsModule.Settings.LeagueAutoAramTeamSideVisibleToTeam; } }
        public bool AutoLoginStatusMessageEnabled { get { return _settingsModule.Settings.LeagueAutoLoginStatusMessageEnabled; } }
        public string AutoLoginStatusMessage { get { return _settingsModule.Settings.LeagueAutoLoginStatusMessage ?? string.Empty; } }
        public bool AutoLoginRankedStatusEnabled { get { return _settingsModule.Settings.LeagueAutoLoginRankedStatusEnabled; } }
        public string AutoLoginRankedQueue { get { return _settingsModule.Settings.LeagueAutoLoginRankedQueue; } }
        public string AutoLoginRankedTier { get { return _settingsModule.Settings.LeagueAutoLoginRankedTier; } }
        public string AutoLoginRankedDivision { get { return _settingsModule.Settings.LeagueAutoLoginRankedDivision; } }
        public bool LockOfflineStatusEnabled { get { return _settingsModule.Settings.LeagueLockOfflineStatusEnabled; } }
        public bool AutoHandleInvitationsEnabled { get { return _settingsModule.Settings.LeagueAutoHandleInvitationsEnabled; } }
        public bool SkipInvitationsWhenAway { get { return _settingsModule.Settings.LeagueSkipInvitationsWhenAway; } }
        public bool AutoSkipLeaderEnabled { get { return _settingsModule.Settings.LeagueAutoSkipLeaderEnabled; } }
        public string InvitationStrategiesJson { get { return _settingsModule.Settings.LeagueInvitationStrategies; } }
        public int ScheduledFriendInvitationCount { get { return _friendInvitations == null ? 0 : _friendInvitations.PendingCount; } }
        public bool AnnounceFriendInvitations { get { return _friendInvitations != null && _friendInvitations.AnnounceEnabled; } }

        public bool SetFriendInvitationAnnouncement(string lobbyKey, bool enabled)
        {
            ThrowIfDisposed();
            return _friendInvitations != null && _friendInvitations.SetAnnounce(lobbyKey, enabled);
        }

        public Task<LeagueFriendInvitationCatalog> LoadInvitationFriendsAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return _friendInvitations.LoadCatalogAsync(cancellationToken);
        }

        public bool SetScheduledFriendInvitation(string lobbyKey, string puuid, bool selected)
        {
            ThrowIfDisposed();
            if (!_friendInvitations.SetScheduled(lobbyKey, puuid, selected)) return false;
            _friendInvitations.Observe(_dashboard.CurrentGameflowState);
            return true;
        }
        public bool AutoPickEnabled { get { return _settingsModule.Settings.LeagueAutoPickEnabled; } }
        public bool AutoSelectTemporarilyDisabled { get { return _autoSelectTemporarilyDisabled; } }
        public bool AutoBanEnabled { get { return _settingsModule.Settings.LeagueAutoBanEnabled; } }
        public int AutoPickChampionId { get { return _settingsModule.Settings.LeagueAutoPickChampionId; } }
        public int AutoBanChampionId { get { return _settingsModule.Settings.LeagueAutoBanChampionId; } }
        public string AutoSelectProfilesJson { get { return _settingsModule.Settings.LeagueAutoSelectProfiles; } }
        public int AutoMatchmakingMinPartySize { get { return _settingsModule.Settings.LeagueAutoMatchmakingMinPartySize; } }
        public bool AutoMatchmakingWaitForInvitees { get { return _settingsModule.Settings.LeagueAutoMatchmakingWaitForInvitees; } }
        public int AutoMatchmakingStartDelayMs { get { return _settingsModule.Settings.LeagueAutoMatchmakingStartDelayMs; } }
        public int AutoAcceptDelayMs { get { return _settingsModule.Settings.LeagueAutoAcceptDelayMs; } }
        public string AutoMatchmakingStopPolicy { get { return LeagueMatchmakingStopPolicyCodec.Normalize(_settingsModule.Settings.LeagueAutoMatchmakingStopPolicy); } }
        public int AutoMatchmakingStopAfterMs { get { return _settingsModule.Settings.LeagueAutoMatchmakingStopAfterMs; } }

        public Task<LeagueEfficiencyActionResult> RunExitGameAsync()
        {
            ThrowIfDisposed();
            if (_actions == null) throw new InvalidOperationException("League Efficiency action service is not initialized.");
            return _actions.ExitGameAsync();
        }

        public bool TryUpdateBindings(string exitGame, string closeLobby, out string error)
        {
            return TryApplyBindings(exitGame, closeLobby, true, out error);
        }

        public void UpdatePostGameSettings(bool autoHonor, bool autoReturn, string honorStrategy)
        {
            ThrowIfDisposed();
            _settingsModule.Settings.LeagueAutoHonorTeammateEnabled = autoHonor;
            _settingsModule.Settings.LeagueAutoHonorStrategy = LeagueHonorStrategy.Normalize(honorStrategy);
            _settingsModule.Settings.LeagueAutoReturnLobbyEnabled = autoReturn;
            _settingsModule.Settings.Save();
            if (_postGame != null)
            {
                _postGame.Configure(autoHonor, autoReturn, AutoHonorStrategy);
                var current = _dashboard.CurrentGameflowState;
                if (current != null) _postGame.Observe(current);
            }
        }

        public void UpdateReconnectSetting(bool enabled)
        {
            ThrowIfDisposed();
            _settingsModule.Settings.LeagueAutoReconnectEnabled = enabled;
            _settingsModule.Settings.Save();
            if (_reconnect == null) return;
            _reconnect.Configure(enabled);
            _reconnect.Observe(_dashboard.CurrentGameflowState);
        }

        public void UpdateAramTeamSideSettings(bool enabled, bool visibleToTeam)
        {
            ThrowIfDisposed();
            _settingsModule.Settings.LeagueAutoAramTeamSideEnabled = enabled;
            _settingsModule.Settings.LeagueAutoAramTeamSideVisibleToTeam = visibleToTeam;
            _settingsModule.Settings.Save();
            ConfigureAramTeamSide();
            _aramSide?.Observe(_dashboard.CurrentGameflowState);
        }

        private void ConfigureAramTeamSide()
        {
            _aramSide?.Configure(AutoAramTeamSideEnabled, AutoAramTeamSideVisibleToTeam);
        }

        public void UpdateLoginStatusMessage(bool enabled, string message)
        {
            ThrowIfDisposed();
            _settingsModule.Settings.LeagueAutoLoginStatusMessageEnabled = enabled;
            _settingsModule.Settings.LeagueAutoLoginStatusMessage = LeagueLoginStatusRestoreController.NormalizeSavedMessage(message);
            _settingsModule.Settings.Save();
            ConfigureLoginSignature();
            _loginSignature?.Observe(_dashboard.CurrentGameflowState);
        }

        public void UpdateLoginRankedStatus(bool enabled, string queue, string tier, string division)
        {
            ThrowIfDisposed();
            string safeQueue;
            string safeTier;
            string safeDivision;
            if (!LeaguePresenceService.TryNormalizeRankedStatusForSmokeTest(
                queue, tier, division, out safeQueue, out safeTier, out safeDivision))
                throw new ArgumentException("Invalid automatic displayed-rank setting.");
            _settingsModule.Settings.LeagueAutoLoginRankedStatusEnabled = enabled;
            _settingsModule.Settings.LeagueAutoLoginRankedQueue = safeQueue;
            _settingsModule.Settings.LeagueAutoLoginRankedTier = safeTier;
            _settingsModule.Settings.LeagueAutoLoginRankedDivision = safeDivision;
            _settingsModule.Settings.Save();
            ConfigureLoginSignature();
            _loginSignature?.Observe(_dashboard.CurrentGameflowState);
        }

        public bool AutoReplyEnabled { get { return _settingsModule.Settings.LeagueAutoReplyEnabled; } }
        public bool AutoReplyAwayOnly { get { return _settingsModule.Settings.LeagueAutoReplyAwayOnly; } }
        public string AutoReplyText { get { return _settingsModule.Settings.LeagueAutoReplyText ?? ""; } }

        public void UpdateAutoReply(bool enabled, bool awayOnly, string text)
        {
            ThrowIfDisposed();
            _settingsModule.Settings.LeagueAutoReplyEnabled = enabled;
            _settingsModule.Settings.LeagueAutoReplyAwayOnly = awayOnly;
            _settingsModule.Settings.LeagueAutoReplyText = LeagueAutoReplyController.NormalizeText(text);
            _settingsModule.Settings.Save();
        }

        private void ConfigureAutoReply()
        {
            _autoReply?.Configure(AutoReplyEnabled, AutoReplyAwayOnly, AutoReplyText, LockOfflineStatusEnabled);
            _autoReply?.Observe(_dashboard.CurrentGameflowState);
        }

        public void UpdateLockOfflineStatus(bool enabled)
        {
            ThrowIfDisposed();
            _settingsModule.Settings.LeagueLockOfflineStatusEnabled = enabled;
            _settingsModule.Settings.Save();
            _offlineLock?.Configure(enabled);
            ConfigureAutoReply();
        }

        private void ConfigureLoginSignature()
        {
            _loginSignature?.Configure(AutoLoginStatusMessageEnabled, AutoLoginStatusMessage,
                AutoLoginRankedStatusEnabled, AutoLoginRankedQueue, AutoLoginRankedTier, AutoLoginRankedDivision);
        }

        public void UpdateInvitationSettings(bool handleInvitations, bool skipWhenAway, string policiesJson, bool autoSkipLeader)
        {
            ThrowIfDisposed();
            var canonical = LeagueInvitationPolicies.NormalizeJson(policiesJson);
            if (!string.Equals(canonical, policiesJson, StringComparison.Ordinal))
                throw new ArgumentException("Invalid lobby invitation policies.", nameof(policiesJson));
            _settingsModule.Settings.LeagueAutoHandleInvitationsEnabled = handleInvitations;
            _settingsModule.Settings.LeagueSkipInvitationsWhenAway = skipWhenAway;
            _settingsModule.Settings.LeagueInvitationStrategies = canonical;
            _settingsModule.Settings.LeagueAutoSkipLeaderEnabled = autoSkipLeader;
            _settingsModule.Settings.Save();
            ConfigureLobbyInvitations();
            _lobbyInvitations?.Observe(_dashboard.CurrentGameflowState);
        }

        private void ConfigureLobbyInvitations()
        {
            _lobbyInvitations?.Configure(AutoHandleInvitationsEnabled, SkipInvitationsWhenAway,
                InvitationStrategiesJson, AutoSkipLeaderEnabled);
        }

        private void HandleAppSettingsSaved(AppSettings settings)
        {
            if (_disposed || _lobbyInvitations == null || !ReferenceEquals(settings, _settingsModule.Settings)) return;
            // Cloud restore updates the same Settings object. Pick up restored preferences without
            // requiring the user to close/reopen the game client or Quick Tools.
            _matchmaking?.Configure(AutoMatchmakingEnabled, AutoAcceptEnabled, AutoMatchmakingMinPartySize,
                AutoMatchmakingStartDelayMs, AutoAcceptDelayMs, AutoMatchmakingWaitForInvitees);
            _matchmaking?.Observe(_dashboard.CurrentGameflowState);
            ConfigureMatchmakingStopController();
            _matchmakingStop?.Observe(_dashboard.CurrentGameflowState);
            ConfigureLobbyInvitations();
            _lobbyInvitations.Observe(_dashboard.CurrentGameflowState);
            ConfigureAramTeamSide();
            _aramSide?.Observe(_dashboard.CurrentGameflowState);
            ConfigureLoginSignature();
            _loginSignature?.Observe(_dashboard.CurrentGameflowState);
            _offlineLock?.Configure(LockOfflineStatusEnabled);
            ConfigureAutoReply();
        }

        public Task<IReadOnlyList<LeagueProfileChampionOption>> LoadChampionsAsync(System.Threading.CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return new LeagueProfileCustomizationService(_leagueClient, (ILeagueProfileWriteApi)_leagueClient)
                .LoadChampionsAsync(cancellationToken);
        }

        public void UpdateAutoSelectSettings(bool autoPick, int pickId, bool autoBan, int banId)
        {
            ThrowIfDisposed();
            var profiles = LeagueAutoSelectProfiles.Parse(AutoSelectProfilesJson);
            if (autoPick && pickId <= 0 && !profiles.Any(p => p.Picks.Count > 0) ||
                autoBan && banId <= 0 && !profiles.Any(p => p.Bans.Count > 0))
                throw new ArgumentException("Enabled auto-select requires a configured champion.");
            _settingsModule.Settings.LeagueAutoPickEnabled = autoPick;
            _settingsModule.Settings.LeagueAutoBanEnabled = autoBan;
            _settingsModule.Settings.LeagueAutoPickChampionId = Math.Max(0, Math.Min(10000, pickId));
            _settingsModule.Settings.LeagueAutoBanChampionId = Math.Max(0, Math.Min(10000, banId));
            _settingsModule.Settings.Save();
            ConfigureAutoSelect();
            _autoSelect?.Observe(_dashboard.CurrentGameflowState);
        }

        public void UpdateAutoSelectTemporaryPause(bool paused)
        {
            ThrowIfDisposed();
            _autoSelectTemporarilyDisabled = paused;
            _autoSelect?.SetTemporarilyDisabled(paused);
        }

        private void ConfigureAutoSelect()
        {
            _autoSelect?.Configure(AutoPickEnabled, AutoPickChampionId, AutoBanEnabled,
                AutoBanChampionId, AutoSelectProfilesJson);
        }

        public void UpdateAutoSelectProfiles(string profiles)
        {
            ThrowIfDisposed();
            var canonical = LeagueAutoSelectProfiles.Serialize(LeagueAutoSelectProfiles.Parse(profiles));
            if (!string.Equals(canonical, profiles, StringComparison.Ordinal))
                throw new ArgumentException("Invalid automatic-selection profile configuration.");
            _settingsModule.Settings.LeagueAutoSelectProfiles = canonical;
            _settingsModule.Settings.Save();
            ConfigureAutoSelect();
            _autoSelect?.Observe(_dashboard.CurrentGameflowState);
        }

        public void UpdateMatchmakingSettings(
            bool autoSearch,
            bool autoAccept,
            int minimumPartySize,
            int searchStartDelayMs,
            int acceptDelayMs,
            string stopPolicy,
            int stopAfterMs,
            bool waitForInvitees)
        {
            ThrowIfDisposed();
            minimumPartySize = Math.Max(1, Math.Min(5, minimumPartySize));
            searchStartDelayMs = Math.Max(0, Math.Min(60000, searchStartDelayMs));
            acceptDelayMs = Math.Max(0, Math.Min(15000, acceptDelayMs));
            stopPolicy = LeagueMatchmakingStopPolicyCodec.Normalize(stopPolicy);
            stopAfterMs = Math.Max(1000, Math.Min(600000, stopAfterMs));
            _settingsModule.Settings.LeagueAutoMatchmakingEnabled = autoSearch;
            _settingsModule.Settings.LeagueAutoMatchmakingWaitForInvitees = waitForInvitees;
            _settingsModule.Settings.LeagueAutoAcceptEnabled = autoAccept;
            _settingsModule.Settings.LeagueAutoMatchmakingMinPartySize = minimumPartySize;
            _settingsModule.Settings.LeagueAutoMatchmakingStartDelayMs = searchStartDelayMs;
            _settingsModule.Settings.LeagueAutoAcceptDelayMs = acceptDelayMs;
            _settingsModule.Settings.LeagueAutoMatchmakingStopPolicy = stopPolicy;
            _settingsModule.Settings.LeagueAutoMatchmakingStopAfterMs = stopAfterMs;
            _settingsModule.Settings.Save();
            if (_matchmaking != null)
                _matchmaking.Configure(autoSearch, autoAccept, minimumPartySize, searchStartDelayMs, acceptDelayMs, waitForInvitees);
            ConfigureMatchmakingStopController();

            var current = _dashboard.CurrentGameflowState;
            if (current != null)
            {
                if (_matchmaking != null) _matchmaking.Observe(current);
                if (_matchmakingStop != null) _matchmakingStop.Observe(current);
            }
        }

        private void ConfigureMatchmakingStopController()
        {
            if (_matchmakingStop == null) return;
            // The stop strategy belongs to the auto-matchmaking workflow. Turning auto matchmaking
            // off also turns this automatic DELETE behavior off, even when an old policy is saved.
            var effective = AutoMatchmakingEnabled
                ? AutoMatchmakingStopPolicy
                : LeagueMatchmakingStopPolicyCodec.Never;
            _matchmakingStop.Configure(effective, AutoMatchmakingStopAfterMs);
        }

        private bool TryApplyBindings(string exitGame, string closeLobby, bool persist, out string error)
        {
            ThrowIfDisposed();
            LeagueHotkeyBinding exitBinding;
            LeagueHotkeyBinding lobbyBinding;
            if (!LeagueHotkeyBinding.TryParse(exitGame, out exitBinding, out error)) return false;
            if (!LeagueHotkeyBinding.TryParse(closeLobby, out lobbyBinding, out error)) return false;

            var requested = new Dictionary<string, LeagueHotkeyBinding>(StringComparer.Ordinal)
            {
                { ExitGameAction, exitBinding },
                { CloseLobbyAction, lobbyBinding }
            };
            if (!_hotkeys.TryApply(requested, out error)) return false;

            if (persist)
            {
                _settingsModule.Settings.LeagueExitGameHotkey = exitBinding.ToString();
                _settingsModule.Settings.LeagueCloseLobbyHotkey = lobbyBinding.ToString();
                _settingsModule.Settings.Save();
            }
            return true;
        }

        private void HandleHonorAttemptCompleted(LeagueHonorAttemptStatus status)
        {
            if (_disposed || status == null) return;
            var copy = status.Clone();
            lock (_honorSync) _lastHonorStatus = copy;
            var handler = HonorStatusChanged;
            if (handler != null)
            {
                try { handler(copy.Clone()); }
                catch (Exception exception) { AppLog.Info("League honor UI observer skipped: " + exception.Message); }
            }
        }

        private void HandleGameflowState(LeagueDashboardPhaseState state)
        {
            if (_disposed) return;
            if (_postGame != null) _postGame.Observe(state);
            if (_matchmaking != null) _matchmaking.Observe(state);
            if (_matchmakingStop != null) _matchmakingStop.Observe(state);
            if (_reconnect != null) _reconnect.Observe(state);
            if (_autoSelect != null) _autoSelect.Observe(state);
            if (_lobbyInvitations != null) _lobbyInvitations.Observe(state);
            if (_friendInvitations != null) _friendInvitations.Observe(state);
            if (_aramSide != null) _aramSide.Observe(state);
            if (_loginSignature != null) _loginSignature.Observe(state);
            _autoReply?.Observe(state);
        }

        private void HandleHotkey(string action)
        {
            if (_disposed || _actions == null) return;
            if (string.Equals(action, ExitGameAction, StringComparison.Ordinal))
                RunQuietly(_actions.ExitGameAsync(), "League exit-game hotkey");
            else if (string.Equals(action, CloseLobbyAction, StringComparison.Ordinal))
                RunQuietly(_actions.CloseLobbyAsync(), "League close-lobby hotkey");
        }

        private static async void RunQuietly(Task<LeagueEfficiencyActionResult> task, string operation)
        {
            try
            {
                var result = await task.ConfigureAwait(true);
                AppLog.Info(operation + ": " + (result == null ? "no-result" : result.Status + "/" + result.Detail));
            }
            catch (Exception exception)
            {
                AppLog.Error(operation + " failed", exception);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LeagueEfficiencyModule));
            if (_hotkeys == null) throw new InvalidOperationException("League Efficiency module is not initialized.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _autoReply?.Dispose();
            AppSettings.Saved -= HandleAppSettingsSaved;
            _dashboard.GameflowStateChanged -= HandleGameflowState;
            if (_loginSignature != null)
            {
                _loginSignature.Dispose();
                _loginSignature = null;
            }
            if (_offlineLock != null)
            {
                _offlineLock.Dispose();
                _offlineLock = null;
            }
            if (_aramSide != null)
            {
                _aramSide.Dispose();
                _aramSide = null;
            }
            if (_friendInvitations != null)
            {
                _friendInvitations.Dispose();
                _friendInvitations = null;
            }
            if (_lobbyInvitations != null)
            {
                _lobbyInvitations.Dispose();
                _lobbyInvitations = null;
            }
            if (_autoSelect != null)
            {
                _autoSelect.Dispose();
                _autoSelect = null;
            }
            if (_reconnect != null)
            {
                _reconnect.Dispose();
                _reconnect = null;
            }
            if (_matchmakingStop != null)
            {
                _matchmakingStop.Dispose();
                _matchmakingStop = null;
            }
            if (_matchmaking != null)
            {
                _matchmaking.Dispose();
                _matchmaking = null;
            }
            if (_postGame != null)
            {
                _postGame.HonorAttemptCompleted -= HandleHonorAttemptCompleted;
                _postGame.Dispose();
                _postGame = null;
            }
            if (_hotkeys != null)
            {
                _hotkeys.HotkeyPressed -= HandleHotkey;
                _hotkeys.Dispose();
                _hotkeys = null;
            }
            lock (_honorSync) _lastHonorStatus = null;
            HonorStatusChanged = null;
            _actions = null;
        }
    }
}
