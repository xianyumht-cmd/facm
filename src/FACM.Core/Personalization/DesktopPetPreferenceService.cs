using FACM.Core.Settings;

namespace FACM.Core.Personalization;

public sealed record DesktopPetPreferenceSnapshot(
    bool Enabled,
    FacmPetDefinition Pet,
    bool RecoveryReadOnly,
    DesktopPetRuntimeState Runtime,
    string Detail);

public sealed class DesktopPetPreferenceService
{
    private readonly ISettings2Repository _settings;
    private readonly IDesktopPetRuntime _runtime;
    private readonly SemaphoreSlim _failureRepairGate = new(1, 1);

    public DesktopPetPreferenceService(ISettings2Repository settings, IDesktopPetRuntime runtime)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _runtime.StateChanged += OnRuntimeStateChanged;
    }

    public event EventHandler<DesktopPetRuntimeState>? RuntimeStateChanged
    {
        add => _runtime.StateChanged += value;
        remove => _runtime.StateChanged -= value;
    }

    public async Task<DesktopPetPreferenceSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var pet = FacmPetCatalog.Get(loaded.Settings.Pets.StyleId);
        var recoveryReadOnly = IsRecoveryReadOnly(loaded.Origin);
        if (!loaded.Settings.Pets.Enabled)
        {
            var disabled = await _runtime.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
            return Snapshot(false, pet, recoveryReadOnly, disabled.Detail);
        }

        var result = await _runtime.ApplyAsync(true, pet, cancellationToken).ConfigureAwait(false);
        if (result.Success)
            return Snapshot(true, pet, recoveryReadOnly, result.Detail);

        // A configured pet may never make FACM disappear. If startup is rejected synchronously,
        // return to the built-in launcher and repair the preference when the settings file is writable.
        _ = await _runtime.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
        if (!recoveryReadOnly)
        {
            loaded.Settings.Pets.Enabled = false;
            await _settings.SaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
        }
        return Snapshot(false, pet, recoveryReadOnly, "pet-start-failed:" + result.Detail);
    }

    public async Task<DesktopPetPreferenceSnapshot> SelectPetAsync(
        string? petId,
        CancellationToken cancellationToken = default)
    {
        var pet = FacmPetCatalog.Get(petId);
        var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var recoveryReadOnly = IsRecoveryReadOnly(loaded.Origin);

        if (!recoveryReadOnly)
        {
            // Persist the explicit style choice immediately, but do not claim the desktop pet is enabled
            // until the controlled PetHost has actually reached ready. This prevents an interrupted/slow
            // first extraction from leaving Settings2 enabled while no pet is visible.
            loaded.Settings.Pets.StyleId = pet.Id;
            loaded.Settings.Pets.Enabled = false;
            await _settings.SaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
        }

        var result = await _runtime.ApplyAsync(true, pet, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            if (!recoveryReadOnly)
            {
                loaded.Settings.Pets.Enabled = true;
                try
                {
                    await _settings.SaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    _ = await _runtime.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
            return Snapshot(true, pet, recoveryReadOnly, recoveryReadOnly ? "session-only:" + result.Detail : result.Detail);
        }

        _ = await _runtime.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
        if (!recoveryReadOnly)
        {
            loaded.Settings.Pets.Enabled = false;
            await _settings.SaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
        }
        return Snapshot(false, pet, recoveryReadOnly, "pet-start-failed:" + result.Detail);
    }

    public async Task<DesktopPetPreferenceSnapshot> RestoreDefaultLauncherAsync(
        CancellationToken cancellationToken = default)
    {
        var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var pet = FacmPetCatalog.Get(loaded.Settings.Pets.StyleId);
        var recoveryReadOnly = IsRecoveryReadOnly(loaded.Origin);
        var result = await _runtime.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
        if (!recoveryReadOnly)
        {
            loaded.Settings.Pets.Enabled = false;
            await _settings.SaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
        }
        return Snapshot(false, pet, recoveryReadOnly, result.Detail);
    }

    public async Task ResetPositionAsync(CancellationToken cancellationToken = default)
    {
        await _runtime.ResetPositionAsync(cancellationToken).ConfigureAwait(false);
        var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (IsRecoveryReadOnly(loaded.Origin)) return;
        loaded.Settings.Pets.BallX = int.MinValue;
        loaded.Settings.Pets.BallY = int.MinValue;
        await _settings.SaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
    }

    private void OnRuntimeStateChanged(object? sender, DesktopPetRuntimeState state)
    {
        if (state.StartRequested || state.PetVisible ||
            !state.Detail.StartsWith("runtime-failed:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _ = RepairFailedRuntimePreferenceAsync(state);
    }

    private async Task RepairFailedRuntimePreferenceAsync(DesktopPetRuntimeState failedState)
    {
        await _failureRepairGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var current = _runtime.Current;
            if (current.StartRequested || current.PetVisible || !string.Equals(current.Detail, failedState.Detail, StringComparison.Ordinal))
                return;

            var loaded = await _settings.LoadAsync().ConfigureAwait(false);
            if (IsRecoveryReadOnly(loaded.Origin) || !loaded.Settings.Pets.Enabled) return;

            // Re-check after the async settings read so a user re-enable racing this recovery does not get
            // overwritten by an older process-exit notification.
            current = _runtime.Current;
            if (current.StartRequested || current.PetVisible || !string.Equals(current.Detail, failedState.Detail, StringComparison.Ordinal))
                return;

            loaded.Settings.Pets.Enabled = false;
            await _settings.SaveAsync(loaded.Settings).ConfigureAwait(false);
        }
        catch
        {
            // Runtime recovery already restored the always-available F entry. A settings repair failure
            // must not turn that fail-soft path into a process failure.
        }
        finally
        {
            _failureRepairGate.Release();
        }
    }

    private DesktopPetPreferenceSnapshot Snapshot(
        bool enabled,
        FacmPetDefinition pet,
        bool recoveryReadOnly,
        string detail) =>
        new(enabled, pet, recoveryReadOnly, _runtime.Current, detail);

    private static bool IsRecoveryReadOnly(SettingsLoadOrigin origin) =>
        origin is SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;
}
