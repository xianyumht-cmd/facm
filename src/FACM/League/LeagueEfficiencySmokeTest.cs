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
            Require(LeagueHotkeyService.UsesDedicatedMessageThreadForSmokeTest(),
                "League hotkeys must remain independent of FACM window focus on a dedicated message thread.");
            Require(LeagueHotkeyService.ReadyWaitsForMessagePumpForSmokeTest(),
                "League hotkey readiness must not publish until the dedicated message pump has dispatched a probe.");

            var backend = new FakeHotkeyBackend();
            var ids = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { LeagueEfficiencyModule.ExitGameAction, 1 },
                { LeagueEfficiencyModule.CloseLobbyAction, 2 }
            };
            using (var manager = new LeagueHotkeyRegistrationManager(IntPtr.Zero, backend, ids))
            {
                Require(manager.TryApply(Bindings("F8", "F9"), out error), "Initial hotkey registration failed: " + error);
                Require(backend.Active.Count == 2, "Initial hotkey registration count mismatch.");
                Require(!manager.TryApply(Bindings("F10", "F10"), out error), "Duplicate FACM hotkeys must be rejected.");
                Require(backend.Active.Count == 2, "Duplicate validation must preserve old bindings.");
                backend.FailNextId = 2;
                Require(!manager.TryApply(Bindings("F6", "F7"), out error), "Backend registration failure must fail transaction.");
                Require(backend.Active.ContainsKey(1) && backend.Active[1].VirtualKey == (uint)Keys.F8,
                    "Failed hotkey transaction did not restore prior bindings.");
            }
            Require(backend.Active.Count == 0, "Disposing hotkey manager must unregister every binding.");
        }

        private static void ValidateSettings()
        {
            var settings = AppSettings.ParseLines(new[]
            {
                "LeagueExitGameHotkey=F8",
                "LeagueCloseLobbyHotkey=Ctrl+F9",
                "LeagueAutoHonorTeammateEnabled=True",
                "LeagueAutoReturnLobbyEnabled=True",
                "LeagueAutoMatchmakingEnabled=True",
                "LeagueAutoAcceptEnabled=True"
            });
            Require(settings.LeagueExitGameHotkey == "F8", "Exit-game hotkey setting did not parse.");
            Require(settings.LeagueCloseLobbyHotkey == "Ctrl+F9", "Close-lobby hotkey setting did not parse.");
            Require(settings.LeagueAutoHonorTeammateEnabled && settings.LeagueAutoReturnLobbyEnabled,
                "Post-game automation settings did not parse.");
            Require(settings.LeagueAutoMatchmakingEnabled && settings.LeagueAutoAcceptEnabled,
                "Next-game automation settings did not parse.");
            var serialized = string.Join("\n", settings.BuildLines());
            Require(serialized.Contains("LeagueExitGameHotkey=F8"), "Exit-game hotkey setting did not serialize.");
            Require(serialized.Contains("LeagueAutoAcceptEnabled=True"), "Auto-accept setting did not serialize.");
            Require(serialized.IndexOf("Credential", StringComparison.OrdinalIgnoreCase) < 0 &&
                    serialized.IndexOf("password", StringComparison.OrdinalIgnoreCase) < 0 &&
                    serialized.IndexOf("account=", StringComparison.OrdinalIgnoreCase) < 0,
                "Formal settings must not contain the abandoned credential-input feature.");
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
                "Close-lobby hotkey must close the League lobby even while the game is running.");
            Require(platform.IsProcessAlive(10), "Close-lobby hotkey must not close the game process.");

            var exit = service.ExitGameAsync().GetAwaiter().GetResult();
            Require(exit.Status == "success" && !platform.IsProcessAlive(10),
                "Exit-game hotkey must kill Tencent League of Legends(TM).");
            Require(platform.IsProcessAlive(30), "Efficiency process actions touched an unrelated process.");
        }

        private static void ValidateUiContract()
        {
            Require(LeagueEfficiencyUiBridge.HasTrayAccessForSmokeTest(), "League Efficiency tray bridge lost MainForm tray access.");
            foreach (var pair in LeagueEfficiencyText.DefaultsForSmokeTest())
            {
                Require(!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value),
                    "League Efficiency UI text contract contains an empty key/default.");
                Require(pair.Key.IndexOf("Credential", StringComparison.OrdinalIgnoreCase) < 0,
                    "Abandoned credential UI key must not ship.");
            }
        }

        private static Dictionary<string, LeagueHotkeyBinding> Bindings(string exit, string lobby)
        {
            string error;
            LeagueHotkeyBinding a;
            LeagueHotkeyBinding b;
            Require(LeagueHotkeyBinding.TryParse(exit, out a, out error), error);
            Require(LeagueHotkeyBinding.TryParse(lobby, out b, out error), error);
            return new Dictionary<string, LeagueHotkeyBinding>(StringComparer.Ordinal)
            {
                { LeagueEfficiencyModule.ExitGameAction, a },
                { LeagueEfficiencyModule.CloseLobbyAction, b }
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
            public void AddProcess(int id, string name) { _alive[id] = name; }
            public IReadOnlyList<LeagueProcessSnapshot> GetProcesses()
            {
                return _alive.Select(pair => new LeagueProcessSnapshot { Id = pair.Key, Name = pair.Value }).ToArray();
            }
            public bool IsProcessAlive(int processId) { return _alive.ContainsKey(processId); }
            public bool Kill(int processId) { return _alive.Remove(processId); }
        }
    }
}
