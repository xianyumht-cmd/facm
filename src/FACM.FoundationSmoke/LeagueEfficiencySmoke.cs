using FACM.Core.League;
using FACM.Core.Settings;
using FACM.Infrastructure.League;

internal static class LeagueEfficiencySmoke
{
    private const int BindingStressIterations = 30;

    public static async Task RunAsync()
    {
        ValidateHotkeyGrammar();
        await ValidateInitializationAndPersistenceAsync();
        await ValidateFailedRegistrationDoesNotPersistAsync();
        await ValidateRecoveryIsReadOnlyAsync();
        await ValidateHotkeyDispatchAsync();
        await ValidateRepeatedBindingTransactionsAsync();
    }

    private static void ValidateHotkeyGrammar()
    {
        True(LeagueHotkeyBinding.TryParse(string.Empty, out var disabled, out _), "empty binding parse");
        True(!disabled.Enabled, "empty binding disables hotkey");

        True(LeagueHotkeyBinding.TryParse("control+f9", out var ctrlF9, out _), "Ctrl+F9 parse");
        Equal("Ctrl+F9", ctrlF9.ToString(), "Ctrl+F9 canonical form");

        True(LeagueHotkeyBinding.TryParse("Windows+f10", out var winF10, out _), "Win+F10 parse");
        Equal("Win+F10", winF10.ToString(), "Win+F10 canonical form");

        True(LeagueHotkeyBinding.TryParse("F9", out var bareFunction, out _), "bare function key allowed");
        Equal("F9", bareFunction.ToString(), "bare function canonical form");

        True(!LeagueHotkeyBinding.TryParse("A", out _, out var bareLetterError), "bare letter rejected");
        True(bareLetterError.Contains("裸字母", StringComparison.Ordinal), "bare letter reason");
        True(!LeagueHotkeyBinding.TryParse("1", out _, out _), "bare digit rejected");
        True(!LeagueHotkeyBinding.TryParse("Ctrl", out _, out _), "modifier-only rejected");
        True(!LeagueHotkeyBinding.TryParse("Ctrl+A+B", out _, out _), "multiple primary keys rejected");

        True(!LeagueEfficiencyRuntime.TryParseBindings(
            "Ctrl+F9",
            "control+f9",
            out _,
            out var duplicateError),
            "duplicate action hotkeys rejected");
        True(duplicateError.Contains("快捷键冲突", StringComparison.Ordinal), "duplicate conflict reason");
    }

    private static async Task ValidateInitializationAndPersistenceAsync()
    {
        var settings = new FakeSettingsRepository(new Settings2Document
        {
            League = new LeagueSettings
            {
                ExitGameHotkey = "control+f9",
                CloseLobbyHotkey = "Alt+F10"
            }
        });
        var actions = new FakeActions();
        using var hotkeys = new FakeHotkeys();
        using var runtime = new LeagueEfficiencyRuntime(settings, actions, hotkeys);

        await runtime.InitializeAsync();
        Equal(1, hotkeys.ApplyCalls, "saved hotkeys registered once at initialization");
        Equal("Ctrl+F9", runtime.State.ExitGameHotkey, "initialized exit-game canonical binding");
        Equal("Alt+F10", runtime.State.CloseLobbyHotkey, "initialized close-lobby canonical binding");
        Equal(0, settings.SaveCalls, "initialization does not rewrite settings");

        var updated = await runtime.UpdateBindingsAsync("Shift+F7", "Win+F8");
        True(updated, "valid binding update succeeds");
        Equal(1, settings.SaveCalls, "successful binding update persists once");
        Equal("Shift+F7", settings.Document.League.ExitGameHotkey, "persisted exit-game canonical binding");
        Equal("Win+F8", settings.Document.League.CloseLobbyHotkey, "persisted close-lobby canonical binding");
    }

    private static async Task ValidateFailedRegistrationDoesNotPersistAsync()
    {
        var settings = new FakeSettingsRepository(new Settings2Document
        {
            League = new LeagueSettings
            {
                ExitGameHotkey = "Ctrl+F9",
                CloseLobbyHotkey = "Ctrl+F10"
            }
        });
        var actions = new FakeActions();
        using var hotkeys = new FakeHotkeys();
        using var runtime = new LeagueEfficiencyRuntime(settings, actions, hotkeys);
        await runtime.InitializeAsync();

        hotkeys.NextApplySucceeds = false;
        var updated = await runtime.UpdateBindingsAsync("Alt+F7", "Alt+F8");
        True(!updated, "occupied hotkey update fails");
        Equal(0, settings.SaveCalls, "failed registration must not persist settings");
        Equal("Ctrl+F9", settings.Document.League.ExitGameHotkey, "failed registration preserves exit-game setting");
        Equal("Ctrl+F10", settings.Document.League.CloseLobbyHotkey, "failed registration preserves close-lobby setting");
    }

    private static async Task ValidateRecoveryIsReadOnlyAsync()
    {
        var settings = new FakeSettingsRepository(
            new Settings2Document
            {
                League = new LeagueSettings
                {
                    ExitGameHotkey = "Ctrl+F9",
                    CloseLobbyHotkey = string.Empty
                }
            },
            SettingsLoadOrigin.RecoveredLastKnownGood);
        using var hotkeys = new FakeHotkeys();
        using var runtime = new LeagueEfficiencyRuntime(settings, new FakeActions(), hotkeys);
        await runtime.InitializeAsync();

        True(runtime.State.IsRecoveryReadOnly, "recovery load marks runtime read-only");
        True(await runtime.UpdateBindingsAsync("Alt+F5", "Alt+F6"), "recovery session can apply temporary hotkeys");
        Equal(0, settings.SaveCalls, "recovery session never overwrites primary settings");
        Equal("Ctrl+F9", settings.Document.League.ExitGameHotkey, "recovery primary exit-game setting preserved");
    }

    private static async Task ValidateHotkeyDispatchAsync()
    {
        var settings = new FakeSettingsRepository(new Settings2Document());
        var actions = new FakeActions();
        using var hotkeys = new FakeHotkeys();
        using var runtime = new LeagueEfficiencyRuntime(settings, actions, hotkeys);
        await runtime.InitializeAsync();

        hotkeys.Raise(LeagueEfficiencyAction.ExitGame);
        await actions.ExitSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Equal(1, actions.ExitCalls, "exit-game hotkey dispatch count");
        Equal(0, actions.LobbyCalls, "exit-game hotkey cannot dispatch close-lobby");

        hotkeys.Raise(LeagueEfficiencyAction.CloseLobby);
        await actions.LobbySignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Equal(1, actions.ExitCalls, "close-lobby hotkey cannot dispatch exit-game");
        Equal(1, actions.LobbyCalls, "close-lobby hotkey dispatch count");
    }

    private static async Task ValidateRepeatedBindingTransactionsAsync()
    {
        var settings = new FakeSettingsRepository(new Settings2Document
        {
            League = new LeagueSettings
            {
                ExitGameHotkey = "Ctrl+F9",
                CloseLobbyHotkey = "Ctrl+F10"
            }
        });
        using var hotkeys = new FakeHotkeys();
        using var runtime = new LeagueEfficiencyRuntime(settings, new FakeActions(), hotkeys);
        await runtime.InitializeAsync();

        string[] exits = ["Ctrl+F5", "Alt+F5", "Shift+F5", "Win+F5", "Ctrl+F6"];
        string[] lobbies = ["Ctrl+F7", "Alt+F7", "Shift+F7", "Win+F7", "Ctrl+F8"];
        for (var cycle = 0; cycle < BindingStressIterations; cycle++)
        {
            var exit = exits[cycle % exits.Length];
            var lobby = lobbies[cycle % lobbies.Length];
            True(await runtime.UpdateBindingsAsync(exit, lobby),
                "repeated hotkey transaction failed at cycle " + cycle);
            Equal(exit, runtime.State.ExitGameHotkey, "repeated runtime exit hotkey " + cycle);
            Equal(lobby, runtime.State.CloseLobbyHotkey, "repeated runtime lobby hotkey " + cycle);
            Equal(exit, settings.Document.League.ExitGameHotkey, "repeated persisted exit hotkey " + cycle);
            Equal(lobby, settings.Document.League.CloseLobbyHotkey, "repeated persisted lobby hotkey " + cycle);
        }

        Equal(BindingStressIterations, settings.SaveCalls, "repeated hotkey settings transaction count");
        Equal(BindingStressIterations + 1, hotkeys.ApplyCalls, "repeated hotkey registration transaction count");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }

    private sealed class FakeSettingsRepository : ISettings2Repository
    {
        public FakeSettingsRepository(
            Settings2Document document,
            SettingsLoadOrigin origin = SettingsLoadOrigin.ExistingV2)
        {
            Document = document;
            Origin = origin;
        }

        public Settings2Document Document { get; }
        public SettingsLoadOrigin Origin { get; }
        public int SaveCalls { get; private set; }

        public Task<Settings2LoadResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Settings2LoadResult(Document, Origin));
        }

        public Task SaveAsync(Settings2Document settings, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeActions : ILeagueEfficiencyActionService
    {
        public int ExitCalls { get; private set; }
        public int LobbyCalls { get; private set; }
        public TaskCompletionSource<bool> ExitSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> LobbySignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LeagueEfficiencyActionResult> ExitGameAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExitCalls++;
            ExitSignal.TrySetResult(true);
            return Task.FromResult(new LeagueEfficiencyActionResult("success", "game-exit", 1));
        }

        public Task<LeagueEfficiencyActionResult> CloseLobbyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LobbyCalls++;
            LobbySignal.TrySetResult(true);
            return Task.FromResult(new LeagueEfficiencyActionResult("success", "lobby-exit", 1));
        }
    }

    private sealed class FakeHotkeys : ILeagueGlobalHotkeyService
    {
        public int ApplyCalls { get; private set; }
        public bool NextApplySucceeds { get; set; } = true;
        public event EventHandler<LeagueGlobalHotkeyPressedEventArgs>? HotkeyPressed;

        public bool TryApply(
            IReadOnlyDictionary<LeagueEfficiencyAction, LeagueHotkeyBinding> bindings,
            out string error)
        {
            ApplyCalls++;
            if (!NextApplySucceeds)
            {
                NextApplySucceeds = true;
                error = "快捷键被系统或其它程序占用：smoke";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public void Raise(LeagueEfficiencyAction action) =>
            HotkeyPressed?.Invoke(this, new LeagueGlobalHotkeyPressedEventArgs(action));

        public void Dispose() => HotkeyPressed = null;
    }
}
