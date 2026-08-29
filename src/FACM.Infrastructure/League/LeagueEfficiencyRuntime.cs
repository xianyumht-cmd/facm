using FACM.Core.League;
using FACM.Core.Settings;

namespace FACM.Infrastructure.League;

/// <summary>
/// Process-scoped FACM 3.5 League efficiency coordinator. Hotkeys are registered once for the FACM
/// process and dispatch only the two narrow efficiency actions. Settings 2.0 remains the persistence
/// owner; recovery loads are runtime-usable but read-only.
/// </summary>
public sealed class LeagueEfficiencyRuntime : ILeagueEfficiencyRuntime
{
    private readonly object _sync = new();
    private readonly ISettings2Repository _settings;
    private readonly ILeagueEfficiencyActionService _actions;
    private readonly ILeagueGlobalHotkeyService _hotkeys;
    private readonly SemaphoreSlim _configurationGate = new(1, 1);
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private LeagueEfficiencyRuntimeState _state = LeagueEfficiencyRuntimeState.Initial;
    private bool _initialized;
    private bool _disposed;

    public LeagueEfficiencyRuntime(
        ISettings2Repository settings,
        ILeagueEfficiencyActionService actions,
        ILeagueGlobalHotkeyService hotkeys)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        _hotkeys.HotkeyPressed += OnHotkeyPressed;
    }

    public LeagueEfficiencyRuntimeState State
    {
        get { lock (_sync) return _state; }
    }

    public event EventHandler<LeagueEfficiencyRuntimeStateChangedEventArgs>? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _configurationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_initialized) return;

            var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            var recoveryReadOnly = loaded.Origin is SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;
            var exitText = loaded.Settings.League.ExitGameHotkey ?? string.Empty;
            var lobbyText = loaded.Settings.League.CloseLobbyHotkey ?? string.Empty;

            if (!TryParseBindings(exitText, lobbyText, out var bindings, out var error))
            {
                _hotkeys.TryApply(DisabledBindings(), out _);
                Publish(new LeagueEfficiencyRuntimeState(
                    exitText,
                    lobbyText,
                    "hotkey-invalid",
                    error,
                    recoveryReadOnly,
                    false));
                _initialized = true;
                return;
            }

            if (!_hotkeys.TryApply(bindings, out error))
            {
                Publish(new LeagueEfficiencyRuntimeState(
                    bindings[LeagueEfficiencyAction.ExitGame].ToString(),
                    bindings[LeagueEfficiencyAction.CloseLobby].ToString(),
                    "hotkey-unavailable",
                    error,
                    recoveryReadOnly,
                    false));
                _initialized = true;
                return;
            }

            Publish(new LeagueEfficiencyRuntimeState(
                bindings[LeagueEfficiencyAction.ExitGame].ToString(),
                bindings[LeagueEfficiencyAction.CloseLobby].ToString(),
                "ready",
                recoveryReadOnly ? "recovery-read-only" : "registered",
                recoveryReadOnly,
                false));
            _initialized = true;
        }
        finally
        {
            _configurationGate.Release();
        }
    }

    public async Task<bool> UpdateBindingsAsync(
        string exitGameHotkey,
        string closeLobbyHotkey,
        CancellationToken cancellationToken = default)
    {
        await _configurationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_initialized)
                throw new InvalidOperationException("League efficiency runtime is not initialized.");

            var current = State;
            Publish(current with { Status = "saving", Detail = string.Empty, IsBusy = true });

            if (!TryParseBindings(exitGameHotkey, closeLobbyHotkey, out var bindings, out var error))
            {
                Publish(current with { Status = "hotkey-invalid", Detail = error, IsBusy = false });
                return false;
            }

            if (!_hotkeys.TryApply(bindings, out error))
            {
                Publish(current with { Status = "hotkey-unavailable", Detail = error, IsBusy = false });
                return false;
            }

            var rollbackBindings = TryParseBindings(
                    current.ExitGameHotkey,
                    current.CloseLobbyHotkey,
                    out var previousBindings,
                    out _)
                ? previousBindings
                : DisabledBindings();
            var exitCanonical = bindings[LeagueEfficiencyAction.ExitGame].ToString();
            var lobbyCanonical = bindings[LeagueEfficiencyAction.CloseLobby].ToString();

            Settings2UpdateResult updated;
            try
            {
                updated = await _settings.UpdateAsync(
                    settings =>
                    {
                        settings.League.ExitGameHotkey = exitCanonical;
                        settings.League.CloseLobbyHotkey = lobbyCanonical;
                    },
                    allowRecoveryRebuild: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Runtime registration and persisted settings are one user transaction. If persistence
                // fails, restore the previous registered pair instead of leaving a hidden session-only
                // binding that disagrees with Settings 2.0.
                _hotkeys.TryApply(rollbackBindings, out _);
                Publish(current with { Status = "failed", Detail = "settings-save-failed-rolled-back", IsBusy = false });
                throw;
            }

            Publish(new LeagueEfficiencyRuntimeState(
                exitCanonical,
                lobbyCanonical,
                "ready",
                updated.Persisted ? "registered-and-saved" : "applied-session-only-recovery",
                !updated.Persisted,
                false));
            return true;
        }
        catch
        {
            var current = State;
            if (current.IsBusy)
                Publish(current with { Status = "failed", Detail = "settings-or-registration-failed", IsBusy = false });
            throw;
        }
        finally
        {
            _configurationGate.Release();
        }
    }

    public async Task<LeagueEfficiencyActionResult> RunActionAsync(
        LeagueEfficiencyAction action,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _actionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = State;
            Publish(current with { Status = "running", Detail = action.ToString(), IsBusy = true });
            var result = action switch
            {
                LeagueEfficiencyAction.ExitGame => await _actions.ExitGameAsync(cancellationToken).ConfigureAwait(false),
                LeagueEfficiencyAction.CloseLobby => await _actions.CloseLobbyAsync(cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown League efficiency action.")
            };
            Publish(State with
            {
                Status = "ready",
                Detail = result.Status + "/" + result.Detail + "/" + result.AffectedProcesses,
                IsBusy = false
            });
            return result;
        }
        catch
        {
            Publish(State with { Status = "failed", Detail = action + "-failed", IsBusy = false });
            throw;
        }
        finally
        {
            _actionGate.Release();
        }
    }

    private void OnHotkeyPressed(object? sender, LeagueGlobalHotkeyPressedEventArgs args)
    {
        if (_disposed) return;
        _ = RunHotkeyActionSafelyAsync(args.Action);
    }

    private async Task RunHotkeyActionSafelyAsync(LeagueEfficiencyAction action)
    {
        if (!await _actionGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            var result = action switch
            {
                LeagueEfficiencyAction.ExitGame => await _actions.ExitGameAsync().ConfigureAwait(false),
                LeagueEfficiencyAction.CloseLobby => await _actions.CloseLobbyAsync().ConfigureAwait(false),
                _ => null
            };
            if (result is not null)
            {
                Publish(State with
                {
                    Status = "ready",
                    Detail = "hotkey/" + result.Status + "/" + result.Detail + "/" + result.AffectedProcesses,
                    IsBusy = false
                });
            }
        }
        catch
        {
            Publish(State with { Status = "failed", Detail = "hotkey-action-failed", IsBusy = false });
        }
        finally
        {
            _actionGate.Release();
        }
    }

    internal static bool TryParseBindings(
        string? exitGameHotkey,
        string? closeLobbyHotkey,
        out IReadOnlyDictionary<LeagueEfficiencyAction, LeagueHotkeyBinding> bindings,
        out string error)
    {
        bindings = DisabledBindings();
        if (!LeagueHotkeyBinding.TryParse(exitGameHotkey, out var exitBinding, out error)) return false;
        if (!LeagueHotkeyBinding.TryParse(closeLobbyHotkey, out var lobbyBinding, out error)) return false;
        if (exitBinding.Enabled && lobbyBinding.Enabled &&
            string.Equals(exitBinding.ToString(), lobbyBinding.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            error = "快捷键冲突：" + exitBinding;
            return false;
        }

        bindings = new Dictionary<LeagueEfficiencyAction, LeagueHotkeyBinding>
        {
            [LeagueEfficiencyAction.ExitGame] = exitBinding,
            [LeagueEfficiencyAction.CloseLobby] = lobbyBinding
        };
        error = string.Empty;
        return true;
    }

    private static IReadOnlyDictionary<LeagueEfficiencyAction, LeagueHotkeyBinding> DisabledBindings() =>
        new Dictionary<LeagueEfficiencyAction, LeagueHotkeyBinding>
        {
            [LeagueEfficiencyAction.ExitGame] = LeagueHotkeyBinding.Disabled,
            [LeagueEfficiencyAction.CloseLobby] = LeagueHotkeyBinding.Disabled
        };

    private void Publish(LeagueEfficiencyRuntimeState state)
    {
        EventHandler<LeagueEfficiencyRuntimeStateChangedEventArgs>? handler;
        lock (_sync)
        {
            if (_disposed) return;
            _state = state;
            handler = StateChanged;
        }
        handler?.Invoke(this, new LeagueEfficiencyRuntimeStateChangedEventArgs(state));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _hotkeys.HotkeyPressed -= OnHotkeyPressed;
        _hotkeys.Dispose();
        if (_actions is IDisposable disposable) disposable.Dispose();
        StateChanged = null;
    }
}
