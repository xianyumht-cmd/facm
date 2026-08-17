using System;
using System.Collections.Generic;
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
        private LeagueHotkeyService _hotkeys;
        private LeagueEfficiencyActionService _actions;
        private LeaguePostGameAutomationController _postGame;
        private LeagueMatchmakingAutomationController _matchmaking;
        private bool _disposed;

        public LeagueEfficiencyModule(SettingsModule settingsModule, LeagueClientModule leagueClient, LeagueDashboardModule dashboard)
        {
            _settingsModule = settingsModule ?? throw new ArgumentNullException(nameof(settingsModule));
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        }

        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public void Initialize()
        {
            if (_settingsModule.Settings == null)
                throw new InvalidOperationException("Settings module must initialize before League Efficiency.");

            _actions = new LeagueEfficiencyActionService();
            _hotkeys = new LeagueHotkeyService(ActionIds);
            _hotkeys.HotkeyPressed += HandleHotkey;

            string error;
            if (!TryApplyBindings(
                    _settingsModule.Settings.LeagueExitGameHotkey,
                    _settingsModule.Settings.LeagueCloseLobbyHotkey,
                    false,
                    out error))
                AppLog.Warning("League efficiency saved hotkeys were not registered: " + error);

            _postGame = new LeaguePostGameAutomationController(_leagueClient, (ILeaguePostGameWriteApi)_leagueClient);
            _postGame.Configure(AutoHonorEnabled, AutoReturnLobbyEnabled);
            _matchmaking = new LeagueMatchmakingAutomationController(_leagueClient, (ILeagueMatchmakingWriteApi)_leagueClient);
            _matchmaking.Configure(AutoMatchmakingEnabled, AutoAcceptEnabled);

            _dashboard.GameflowStateChanged += HandleGameflowState;
            var current = _dashboard.CurrentGameflowState;
            if (current != null)
            {
                _postGame.Observe(current);
                _matchmaking.Observe(current);
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
        public bool AutoReturnLobbyEnabled { get { return _settingsModule.Settings.LeagueAutoReturnLobbyEnabled; } }
        public bool AutoMatchmakingEnabled { get { return _settingsModule.Settings.LeagueAutoMatchmakingEnabled; } }
        public bool AutoAcceptEnabled { get { return _settingsModule.Settings.LeagueAutoAcceptEnabled; } }

        public bool TryUpdateBindings(string exitGame, string closeLobby, out string error)
        {
            return TryApplyBindings(exitGame, closeLobby, true, out error);
        }

        /// <summary>
        /// The module graph is initialized before the primary WinForms message loop starts. Real-machine 3.4
        /// feedback showed that relying only on that pre-loop registration can leave the global shortcuts inert
        /// until the user activates a FACM window. Reapply the already-saved bindings exactly once on the first
        /// primary Application.Idle boundary so no user click is required. Registration remains transactional.
        /// </summary>
        public void RearmSavedHotkeysAfterPrimaryLoopStarts()
        {
            ThrowIfDisposed();
            string error;
            if (!TryApplyBindings(
                    _settingsModule.Settings.LeagueExitGameHotkey,
                    _settingsModule.Settings.LeagueCloseLobbyHotkey,
                    false,
                    out error))
            {
                AppLog.Warning("League efficiency startup hotkey rearm failed: " + error);
                return;
            }
            AppLog.Info("League efficiency saved hotkeys rearmed after primary message loop startup.");
        }

        public void UpdatePostGameSettings(bool autoHonor, bool autoReturn)
        {
            ThrowIfDisposed();
            _settingsModule.Settings.LeagueAutoHonorTeammateEnabled = autoHonor;
            _settingsModule.Settings.LeagueAutoReturnLobbyEnabled = autoReturn;
            _settingsModule.Settings.Save();
            if (_postGame != null)
            {
                _postGame.Configure(autoHonor, autoReturn);
                var current = _dashboard.CurrentGameflowState;
                if (current != null) _postGame.Observe(current);
            }
        }

        public void UpdateMatchmakingSettings(bool autoSearch, bool autoAccept)
        {
            ThrowIfDisposed();
            _settingsModule.Settings.LeagueAutoMatchmakingEnabled = autoSearch;
            _settingsModule.Settings.LeagueAutoAcceptEnabled = autoAccept;
            _settingsModule.Settings.Save();
            if (_matchmaking != null)
            {
                _matchmaking.Configure(autoSearch, autoAccept);
                var current = _dashboard.CurrentGameflowState;
                if (current != null) _matchmaking.Observe(current);
            }
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

        private void HandleGameflowState(LeagueDashboardPhaseState state)
        {
            if (_disposed) return;
            if (_postGame != null) _postGame.Observe(state);
            if (_matchmaking != null) _matchmaking.Observe(state);
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
            _dashboard.GameflowStateChanged -= HandleGameflowState;
            if (_matchmaking != null)
            {
                _matchmaking.Dispose();
                _matchmaking = null;
            }
            if (_postGame != null)
            {
                _postGame.Dispose();
                _postGame = null;
            }
            if (_hotkeys != null)
            {
                _hotkeys.HotkeyPressed -= HandleHotkey;
                _hotkeys.Dispose();
                _hotkeys = null;
            }
            _actions = null;
        }
    }
}
