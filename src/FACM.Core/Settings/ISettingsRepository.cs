namespace FACM.Core.Settings;

public interface ISettingsRepository
{
    Task<LegacySettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(LegacySettingsSnapshot settings, CancellationToken cancellationToken = default);
}
