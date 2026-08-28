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

    public DesktopPetPreferenceService(ISettings2Repository settings, IDesktopPetRuntime runtime)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
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
            // Match 3.5.15: remember the user's explicit choice before the async runtime starts. If the
            // runtime immediately rejects it, the service repairs Enabled=false below so F stays usable.
            loaded.Settings.Pets.StyleId = pet.Id;
            loaded.Settings.Pets.Enabled = true;
            await _settings.SaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
        }

        var result = await _runtime.ApplyAsync(true, pet, cancellationToken).ConfigureAwait(false);
        if (result.Success)
            return Snapshot(true, pet, recoveryReadOnly, recoveryReadOnly ? "session-only:" + result.Detail : result.Detail);

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

    private DesktopPetPreferenceSnapshot Snapshot(
        bool enabled,
        FacmPetDefinition pet,
        bool recoveryReadOnly,
        string detail) =>
        new(enabled, pet, recoveryReadOnly, _runtime.Current, detail);

    private static bool IsRecoveryReadOnly(SettingsLoadOrigin origin) =>
        origin is SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;
}
