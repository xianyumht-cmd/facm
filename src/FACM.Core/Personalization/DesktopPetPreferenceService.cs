using FACM.Core.Settings;
using FACM.Core.Runtime;

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
    private readonly IComponentAvailability _componentAvailability;
    private readonly SemaphoreSlim _failureRepairGate = new(1, 1);

    public DesktopPetPreferenceService(
        ISettings2Repository settings,
        IDesktopPetRuntime runtime,
        IComponentAvailability? componentAvailability = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _componentAvailability = componentAvailability ?? AlwaysAvailableComponentAvailability.Instance;
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

        var requiredComponent = FacmComponentIds.ForPet(pet);
        if (!_componentAvailability.IsAvailable(requiredComponent))
        {
            _ = await _runtime.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
            return Snapshot(
                false,
                pet,
                recoveryReadOnly,
                ComponentUnavailableDetail(pet, requiredComponent));
        }

        var result = await _runtime.ApplyAsync(true, pet, cancellationToken).ConfigureAwait(false);
        if (result.Success)
            return Snapshot(true, pet, recoveryReadOnly, result.Detail);

        // A configured pet may never make FACM disappear. If the optional component is absent, keep
        // the user's enabled/style preference for a future component install; do not silently turn it
        // off in stable settings. Other runtime failures retain the historical repair behavior.
        if (IsComponentUnavailable(result.Detail))
        {
            _ = await _runtime.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
            return Snapshot(false, pet, recoveryReadOnly, result.Detail);
        }

        // A configured pet may never make FACM disappear. If startup is rejected, return to the
        // built-in launcher and repair only Pets.Enabled without overwriting unrelated settings.
        _ = await _runtime.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
        if (!recoveryReadOnly)
        {
            _ = await _settings.UpdateAsync(
                settings => settings.Pets.Enabled = false,
                allowRecoveryRebuild: false,
                cancellationToken).ConfigureAwait(false);
        }
        return Snapshot(false, pet, recoveryReadOnly, "pet-start-failed:" + result.Detail);
    }

    public async Task<DesktopPetPreferenceSnapshot> SelectPetAsync(
        string? petId,
        CancellationToken cancellationToken = default)
    {
        var pet = FacmPetCatalog.Get(petId);

        var requiredComponent = FacmComponentIds.ForPet(pet);
        if (!_componentAvailability.IsAvailable(requiredComponent))
        {
            var loaded = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            var componentRecoveryReadOnly = IsRecoveryReadOnly(loaded.Origin);
            if (!componentRecoveryReadOnly)
            {
                _ = await _settings.UpdateAsync(
                    settings => settings.Pets.StyleId = pet.Id,
                    allowRecoveryRebuild: false,
                    cancellationToken).ConfigureAwait(false);
            }
            _ = await _runtime.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
            return Snapshot(false, pet, componentRecoveryReadOnly, ComponentUnavailableDetail(pet, requiredComponent));
        }

        // Persist the explicit style choice but do not claim enabled until PetHost reaches ready.
        // Recovery settings remain session-only and are never rebuilt by personalization.
        var pending = await _settings.UpdateAsync(
            settings =>
            {
                settings.Pets.StyleId = pet.Id;
                settings.Pets.Enabled = false;
            },
            allowRecoveryRebuild: false,
            cancellationToken).ConfigureAwait(false);
        var recoveryReadOnly = !pending.Persisted;

        var result = await _runtime.ApplyAsync(true, pet, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            if (!recoveryReadOnly)
            {
                try
                {
                    var committed = await _settings.UpdateAsync(
                        settings =>
                        {
                            settings.Pets.StyleId = pet.Id;
                            settings.Pets.Enabled = true;
                        },
                        allowRecoveryRebuild: false,
                        cancellationToken).ConfigureAwait(false);
                    if (!committed.Persisted)
                    {
                        _ = await _runtime.ApplyAsync(false, pet, cancellationToken).ConfigureAwait(false);
                        return Snapshot(false, pet, true, "pet-start-failed:settings-became-recovery-read-only");
                    }
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
            _ = await _settings.UpdateAsync(
                settings =>
                {
                    settings.Pets.StyleId = pet.Id;
                    settings.Pets.Enabled = false;
                },
                allowRecoveryRebuild: false,
                cancellationToken).ConfigureAwait(false);
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
            _ = await _settings.UpdateAsync(
                settings => settings.Pets.Enabled = false,
                allowRecoveryRebuild: false,
                cancellationToken).ConfigureAwait(false);
        }
        return Snapshot(false, pet, recoveryReadOnly, result.Detail);
    }

    public async Task ResetPositionAsync(CancellationToken cancellationToken = default)
    {
        await _runtime.ResetPositionAsync(cancellationToken).ConfigureAwait(false);
        _ = await _settings.UpdateAsync(
            settings =>
            {
                settings.Pets.BallX = int.MinValue;
                settings.Pets.BallY = int.MinValue;
            },
            allowRecoveryRebuild: false,
            cancellationToken).ConfigureAwait(false);
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

            // Re-check after the async settings read so an explicit re-enable racing this recovery does not
            // get overwritten by an older process-exit notification.
            current = _runtime.Current;
            if (current.StartRequested || current.PetVisible || !string.Equals(current.Detail, failedState.Detail, StringComparison.Ordinal))
                return;

            _ = await _settings.UpdateAsync(
                settings => settings.Pets.Enabled = false,
                allowRecoveryRebuild: false).ConfigureAwait(false);
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

    private static bool IsComponentUnavailable(string detail) =>
        detail.StartsWith("component-unavailable;", StringComparison.OrdinalIgnoreCase);

    private static string ComponentUnavailableDetail(FacmPetDefinition pet, string requiredComponent) =>
        $"component-unavailable;requestedStyleId={pet.Id};requiredComponent={requiredComponent}";
}
