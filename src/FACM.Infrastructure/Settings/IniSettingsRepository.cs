using FACM.Core.Settings;

namespace FACM.Infrastructure.Settings;

public sealed class IniSettingsRepository : ISettingsRepository
{
    private readonly string _path;

    public IniSettingsRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<LegacySettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path)) return new LegacySettingsSnapshot();
        var lines = await File.ReadAllLinesAsync(_path, cancellationToken).ConfigureAwait(false);
        return LegacySettingsCodec.Parse(lines);
    }

    public async Task SaveAsync(LegacySettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllLinesAsync(_path, LegacySettingsCodec.Serialize(settings), cancellationToken).ConfigureAwait(false);
    }
}
