using System.Text;
using System.Text.Json;
using FACM.Core.Settings;

namespace FACM.Infrastructure.Settings;

public interface ISettings2FileStore
{
    bool Exists(string path);
    Task<string> ReadTextAsync(string path, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ReadLinesAsync(string path, CancellationToken cancellationToken);
    Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken);
}

public sealed class PhysicalSettings2FileStore : ISettings2FileStore
{
    public bool Exists(string path) => File.Exists(path);

    public Task<string> ReadTextAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public async Task<IReadOnlyList<string>> ReadLinesAsync(string path, CancellationToken cancellationToken) =>
        await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);

    public async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Settings directory is unavailable.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup only. Never mask the primary write/replace failure.
            }
        }
    }
}

public sealed class Settings2Repository : ISettings2Repository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _settings2Path;
    private readonly string _legacySettingsPath;
    private readonly ISettings2FileStore _files;

    public Settings2Repository(
        string settings2Path,
        string legacySettingsPath,
        ISettings2FileStore? files = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settings2Path);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacySettingsPath);
        _settings2Path = Path.GetFullPath(settings2Path);
        _legacySettingsPath = Path.GetFullPath(legacySettingsPath);
        _files = files ?? new PhysicalSettings2FileStore();
    }

    public async Task<Settings2LoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_files.Exists(_settings2Path))
        {
            var json = await _files.ReadTextAsync(_settings2Path, cancellationToken).ConfigureAwait(false);
            var settings = Deserialize(json);
            return new Settings2LoadResult(settings, SettingsLoadOrigin.ExistingV2);
        }

        Settings2Document migrated;
        SettingsLoadOrigin origin;
        if (_files.Exists(_legacySettingsPath))
        {
            var lines = await _files.ReadLinesAsync(_legacySettingsPath, cancellationToken).ConfigureAwait(false);
            migrated = Settings2Migration.FromLegacy(LegacySettingsCodec.Parse(lines));
            origin = SettingsLoadOrigin.MigratedLegacy;
        }
        else
        {
            migrated = Settings2Document.CreateDefault();
            Settings2Validator.ThrowIfInvalid(migrated);
            origin = SettingsLoadOrigin.Defaults;
        }

        // This creates only settings.v2.json. The legacy INI is deliberately read-only here so
        // FACM 3.5.15 remains a valid rollback target throughout the migration.
        await SaveAsync(migrated, cancellationToken).ConfigureAwait(false);
        return new Settings2LoadResult(migrated, origin);
    }

    public Task SaveAsync(Settings2Document settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings2Validator.ThrowIfInvalid(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions) + Environment.NewLine;
        return _files.WriteAtomicAsync(_settings2Path, json, cancellationToken);
    }

    private static Settings2Document Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("FACM Settings 2.0 file is empty.");

        Settings2Document? settings;
        try
        {
            settings = JsonSerializer.Deserialize<Settings2Document>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("FACM Settings 2.0 JSON is corrupted.", exception);
        }

        Settings2Validator.ThrowIfInvalid(settings);
        return settings!;
    }
}
