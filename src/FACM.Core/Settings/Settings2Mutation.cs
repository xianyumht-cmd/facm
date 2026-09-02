namespace FACM.Core.Settings;

public sealed record Settings2UpdateResult(
    Settings2Document Settings,
    SettingsLoadOrigin Origin,
    bool Persisted);

/// <summary>
/// Optional production-grade Settings 2.0 mutation boundary. A mutation must load the latest
/// document, apply one narrow change and persist it while holding one repository transaction gate.
/// </summary>
public interface IAtomicSettings2Repository : ISettings2Repository
{
    Task<Settings2UpdateResult> UpdateAsync(
        Action<Settings2Document> mutation,
        bool allowRecoveryRebuild = false,
        CancellationToken cancellationToken = default);
}

public static class Settings2MutationExtensions
{
    /// <summary>
    /// Atomically applies a narrow Settings 2.0 mutation when the repository supports the production
    /// mutation contract. Lightweight test repositories retain a compatibility fallback.
    /// </summary>
    public static async Task<Settings2UpdateResult> UpdateAsync(
        this ISettings2Repository repository,
        Action<Settings2Document> mutation,
        bool allowRecoveryRebuild = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(mutation);

        if (repository is IAtomicSettings2Repository atomic)
            return await atomic.UpdateAsync(mutation, allowRecoveryRebuild, cancellationToken).ConfigureAwait(false);

        var loaded = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var recoveryReadOnly = loaded.Origin is
            SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;
        if (recoveryReadOnly && !allowRecoveryRebuild)
            return new Settings2UpdateResult(loaded.Settings, loaded.Origin, Persisted: false);

        mutation(loaded.Settings);
        Settings2Validator.ThrowIfInvalid(loaded.Settings);
        await repository.SaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
        return new Settings2UpdateResult(loaded.Settings, SettingsLoadOrigin.ExistingV2, Persisted: true);
    }
}
