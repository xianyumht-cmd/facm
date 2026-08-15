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
        public const string CredentialsAction = "credentials";

        private static readonly IReadOnlyList<string> ModuleDependencies = new[] { SettingsModule.ModuleId };
        private static readonly Dictionary<string, int> ActionIds = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { ExitGameAction, 0x5A11 },
            { CloseLobbyAction, 0x5A12 },
            { CredentialsAction, 0x5A13 }
        };

        private readonly SettingsModule _settingsModule;
        private LeagueHotkeyService _hotkeys;
        private LeagueEfficiencyActionService _actions;
        private bool _disposed;

        public LeagueEfficiencyModule(SettingsModule settingsModule)
        {
            _settingsModule = settingsModule ?? throw new ArgumentNullException(nameof(settingsModule));
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
                    _settingsModule.Settings.LeagueCredentialHotkey,
                    false,
                    out error))
                AppLog.Warning("League efficiency saved hotkeys were not registered: " + error);

            LeagueEfficiencyUiBridge.Install(this);
        }

        public Form CreateForm(UiTextCatalog ui)
        {
            ThrowIfDisposed();
            return new LeagueEfficiencyForm(this, ui);
        }

        public string ExitGameHotkey { get { return _settingsModule.Settings.LeagueExitGameHotkey ?? string.Empty; } }
        public string CloseLobbyHotkey { get { return _settingsModule.Settings.LeagueCloseLobbyHotkey ?? string.Empty; } }
        public string CredentialHotkey { get { return _settingsModule.Settings.LeagueCredentialHotkey ?? string.Empty; } }

        public bool TryUpdateBindings(string exitGame, string closeLobby, string credentials, out string error)
        {
            return TryApplyBindings(exitGame, closeLobby, credentials, true, out error);
        }

        private bool TryApplyBindings(string exitGame, string closeLobby, string credentials, bool persist, out string error)
        {
            ThrowIfDisposed();
            LeagueHotkeyBinding exitBinding;
            LeagueHotkeyBinding lobbyBinding;
            LeagueHotkeyBinding credentialBinding;
            if (!LeagueHotkeyBinding.TryParse(exitGame, out exitBinding, out error)) return false;
            if (!LeagueHotkeyBinding.TryParse(closeLobby, out lobbyBinding, out error)) return false;
            if (!LeagueHotkeyBinding.TryParse(credentials, out credentialBinding, out error)) return false;

            var requested = new Dictionary<string, LeagueHotkeyBinding>(StringComparer.Ordinal)
            {
                { ExitGameAction, exitBinding },
                { CloseLobbyAction, lobbyBinding },
                { CredentialsAction, credentialBinding }
            };
            if (!_hotkeys.TryApply(requested, out error)) return false;

            if (persist)
            {
                _settingsModule.Settings.LeagueExitGameHotkey = exitBinding.ToString();
                _settingsModule.Settings.LeagueCloseLobbyHotkey = lobbyBinding.ToString();
                _settingsModule.Settings.LeagueCredentialHotkey = credentialBinding.ToString();
                _settingsModule.Settings.Save();
            }
            return true;
        }

        private void HandleHotkey(string action)
        {
            if (_disposed || _actions == null) return;
            if (string.Equals(action, CredentialsAction, StringComparison.Ordinal))
            {
                try
                {
                    LeagueHotkeyBinding binding;
                    string error;
                    if (!LeagueHotkeyBinding.TryParse(CredentialHotkey, out binding, out error) || binding == null || !binding.Enabled)
                    {
                        AppLog.Warning("League credential hotkey: invalid saved binding");
                        return;
                    }

                    if (!LeagueHotkeyReleaseWaiter.WaitUntilReleased(binding))
                    {
                        AppLog.Warning("League credential hotkey: trigger-release-timeout");
                        return;
                    }

                    var result = _actions.InputCredentialsFromClipboard();
                    AppLog.Info("League credential hotkey: " +
                        (result == null ? "no-result" : result.Status + "/" + result.Detail));
                }
                catch (Exception exception)
                {
                    AppLog.Error("League credential hotkey failed", exception);
                }
                return;
            }

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
            LeagueEfficiencyUiBridge.Uninstall();
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
