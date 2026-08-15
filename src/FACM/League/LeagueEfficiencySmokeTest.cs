using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueEfficiencySmokeTest
    {
        public static void Validate()
        {
            ValidateHotkeys();
            ValidateSettings();
            ValidateCredentials();
            ValidateProcessActions();
            ValidateUiContract();
        }

        private static void ValidateHotkeys()
        {
            LeagueHotkeyBinding binding;
            string error;
            Require(LeagueHotkeyBinding.TryParse("F8", out binding, out error) && binding.Enabled && binding.ToString() == "F8",
                "Bare function-key hotkey did not round-trip.");
            Require(LeagueHotkeyBinding.TryParse("Ctrl+Alt+L", out binding, out error) && binding.ToString() == "Ctrl+Alt+L",
                "Modified hotkey did not round-trip.");
            Require(!LeagueHotkeyBinding.TryParse("A", out binding, out error), "Bare letter must be rejected.");
            Require(!LeagueHotkeyBinding.TryParse("7", out binding, out error), "Bare digit must be rejected.");
            Require(LeagueHotkeyBinding.TryParse(string.Empty, out binding, out error) && !binding.Enabled,
                "Empty hotkey must mean disabled.");
            Require(LeagueHotkeyService.UsesDedicatedMessageThreadForSmokeTest(),
                "League hotkeys must be isolated from FACM window focus on a dedicated message thread.");

            var backend = new FakeHotkeyBackend();
            var ids = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { LeagueEfficiencyModule.ExitGameAction, 1 },
                { LeagueEfficiencyModule.CloseLobbyAction, 2 },
                { LeagueEfficiencyModule.CredentialsAction, 3 }
            };
            using (var manager = new LeagueHotkeyRegistrationManager(IntPtr.Zero, backend, ids))
            {
                Require(manager.TryApply(Bindings("F8", "F9", "Ctrl+Alt+L"), out error),
                    "Initial hotkey registration failed: " + error);
                Require(backend.Active.Count == 3, "Initial hotkey registration count mismatch.");

                Require(!manager.TryApply(Bindings("F10", "F10", "Ctrl+Alt+L"), out error),
                    "Duplicate FACM hotkeys must be rejected.");
                Require(backend.Active.Count == 3, "Duplicate validation must preserve old bindings.");

                backend.FailNextId = 3;
                Require(!manager.TryApply(Bindings("F6", "F7", "Ctrl+Alt+K"), out error),
                    "Backend registration failure must fail transaction.");
                Require(backend.Active.ContainsKey(1) && backend.Active[1].VirtualKey == (uint)Keys.F8,
                    "Failed hotkey transaction did not restore exit-game binding.");
                Require(backend.Active.ContainsKey(2) && backend.Active[2].VirtualKey == (uint)Keys.F9,
                    "Failed hotkey transaction did not restore close-lobby binding.");
            }
            Require(backend.Active.Count == 0, "Disposing hotkey manager must unregister every binding.");

            using (var module = new LeagueEfficiencyModule(new SettingsModule()))
            {
                Require(module.Dependencies.Count == 1 && module.Dependencies[0] == SettingsModule.ModuleId,
                    "League Efficiency hotkeys must remain event-driven and Settings-only.");
            }
        }

        private static void ValidateSettings()
        {
            var settings = new AppSettings();
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueExitGameHotkey=F8");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueCloseLobbyHotkey=Ctrl+F9");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueCredentialHotkey=Ctrl+Alt+L");
            Require(settings.LeagueExitGameHotkey == "F8", "Exit-game hotkey setting did not parse.");
            Require(settings.LeagueCloseLobbyHotkey == "Ctrl+F9", "Close-lobby hotkey setting did not parse.");
            Require(settings.LeagueCredentialHotkey == "Ctrl+Alt+L", "Credential hotkey setting did not parse.");
            var serialized = string.Join("\n", settings.BuildLinesForSmokeTest());
            Require(serialized.Contains("LeagueExitGameHotkey=F8"), "Exit-game hotkey setting did not serialize.");
            Require(serialized.Contains("LeagueCredentialHotkey=Ctrl+Alt+L"), "Credential hotkey setting did not serialize.");
            Require(serialized.IndexOf("password", StringComparison.OrdinalIgnoreCase) < 0 &&
                    serialized.IndexOf("account=", StringComparison.OrdinalIgnoreCase) < 0,
                "Settings serialization must never contain League credentials.");
        }

        private static void ValidateCredentials()
        {
            string account;
            string password;
            Require(LeagueCredentialParser.TryParse("123456789-----1316464saf", out account, out password),
                "Expected rental-account clipboard format did not parse.");
            Require(account == "123456789" && password == "1316464saf", "Credential parser returned wrong fields.");
            Require(LeagueCredentialParser.TryParse("abc-def", out account, out password), "Single hyphen separator must be accepted.");
            Require(LeagueCredentialParser.TryParse("account---pass-word", out account, out password) && password == "pass-word",
                "Hyphens after the first separator run must remain part of the password.");
            Require(!LeagueCredentialParser.TryParse("-----password", out account, out password), "Empty account must be rejected.");
            Require(!LeagueCredentialParser.TryParse("account-----", out account, out password), "Empty password must be rejected.");
            Require(!LeagueCredentialParser.TryParse("account---pass\nword", out account, out password), "Newline credential must fail closed.");

            var platform = new FakeDesktopPlatform { Clipboard = "123-----abc" };
            var service = new LeagueEfficiencyActionService(platform);
            var success = service.InputCredentialsFromClipboard();
            Require(success.Status == "success" && platform.SendCount == 1,
                "Valid credentials must type into the user's currently focused fields without process/title gating.");
            Require(platform.LastFirst == "123" && platform.LastSecond == "abc",
                "Credential input sequence fields were wrong.");

            platform.Clipboard = "invalidclipboard";
            var invalid = service.InputCredentialsFromClipboard();
            Require(invalid.Status == "invalid" && platform.SendCount == 1,
                "Invalid clipboard content must not inject keyboard input.");
        }

        private static void ValidateProcessActions()
        {
            var platform = new FakeDesktopPlatform();
            platform.AddProcess(10, "League of Legends(TM)");
            platform.AddProcess(20, "LeagueClient");
            platform.AddProcess(21, "LeagueClientUx");
            platform.AddProcess(30, "notepad");
            var service = new LeagueEfficiencyActionService(platform);

            var lobby = service.CloseLobbyAsync().GetAwaiter().GetResult();
            Require(lobby.Status == "success" && !platform.IsProcessAlive(20) && !platform.IsProcessAlive(21),
                "Close-lobby hotkey must close League lobby immediately even while the game is running.");
            Require(platform.IsProcessAlive(10), "Close-lobby hotkey must not close the League game process.");
            Require(platform.IsProcessAlive(30), "Close-lobby hotkey touched an unrelated process.");

            var exit = service.ExitGameAsync().GetAwaiter().GetResult();
            Require(exit.Status == "success", "Exit-game action did not kill Tencent League of Legends(TM).");
            Require(!platform.IsProcessAlive(10), "Tencent League of Legends(TM) process remained alive.");
            Require(platform.IsProcessAlive(30), "Exit-game action touched an unrelated process.");
            Require(platform.CloseRequested.Count == 0,
                "Efficiency process actions must use immediate precise PID kill, not graceful window close.");

            var noTarget = service.ExitGameAsync().GetAwaiter().GetResult();
            Require(noTarget.Status == "no-target", "No-target exit must be a no-op.");
        }

        private static void ValidateUiContract()
        {
            Require(LeagueEfficiencyUiBridge.HasTrayAccessForSmokeTest(), "League Efficiency tray bridge lost MainForm tray access.");
            foreach (var pair in LeagueEfficiencyText.DefaultsForSmokeTest())
                Require(!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value),
                    "League Efficiency UI text contract contains an empty key/default.");
        }

        private static Dictionary<string, LeagueHotkeyBinding> Bindings(string exit, string lobby, string credentials)
        {
            string error;
            LeagueHotkeyBinding a;
            LeagueHotkeyBinding b;
            LeagueHotkeyBinding c;
            Require(LeagueHotkeyBinding.TryParse(exit, out a, out error), error);
            Require(LeagueHotkeyBinding.TryParse(lobby, out b, out error), error);
            Require(LeagueHotkeyBinding.TryParse(credentials, out c, out error), error);
            return new Dictionary<string, LeagueHotkeyBinding>(StringComparer.Ordinal)
            {
                { LeagueEfficiencyModule.ExitGameAction, a },
                { LeagueEfficiencyModule.CloseLobbyAction, b },
                { LeagueEfficiencyModule.CredentialsAction, c }
            };
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeHotkeyBackend : ILeagueHotkeyBackend
        {
            internal sealed class Registered
            {
                public uint Modifiers;
                public uint VirtualKey;
            }

            public readonly Dictionary<int, Registered> Active = new Dictionary<int, Registered>();
            public int FailNextId = -1;

            public bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey)
            {
                if (id == FailNextId)
                {
                    FailNextId = -1;
                    return false;
                }
                Active[id] = new Registered { Modifiers = modifiers, VirtualKey = virtualKey };
                return true;
            }

            public bool Unregister(IntPtr windowHandle, int id)
            {
                Active.Remove(id);
                return true;
            }
        }

        private sealed class FakeDesktopPlatform : ILeagueDesktopPlatform
        {
            private readonly Dictionary<int, string> _alive = new Dictionary<int, string>();
            public readonly List<int> CloseRequested = new List<int>();
            public readonly List<int> Killed = new List<int>();
            public string Clipboard = string.Empty;
            public int SendCount;
            public string LastFirst;
            public string LastSecond;

            public void AddProcess(int id, string name) { _alive[id] = name; }

            public IReadOnlyList<LeagueProcessSnapshot> GetProcesses()
            {
                return _alive.Select(pair => new LeagueProcessSnapshot { Id = pair.Key, Name = pair.Value }).ToArray();
            }

            public bool IsProcessAlive(int processId) { return _alive.ContainsKey(processId); }

            public bool RequestClose(int processId)
            {
                if (!_alive.ContainsKey(processId)) return false;
                CloseRequested.Add(processId);
                _alive.Remove(processId);
                return true;
            }

            public bool Kill(int processId)
            {
                if (!_alive.ContainsKey(processId)) return false;
                Killed.Add(processId);
                _alive.Remove(processId);
                return true;
            }

            public string ReadClipboardText() { return Clipboard; }

            public bool SendTextAndTabThenText(string first, string second)
            {
                SendCount++;
                LastFirst = first;
                LastSecond = second;
                return true;
            }
        }
    }
}
