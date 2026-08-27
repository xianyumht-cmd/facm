using System.Text;
using System.Text.Json;
using FACM.Core.Settings;

namespace FACM.Infrastructure.Settings;

public interface ISettings2RecoveryStore
{
    Task<Settings2Document?> TryLoadAsync(CancellationToken cancellationToken = default);
    Task<bool> TrySaveAsync(Settings2Document settings, CancellationToken cancellationToken = default);
}

public sealed class JsonSettings2RecoveryStore : ISettings2RecoveryStore
{
    private const long MaxDocumentBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;

    public JsonSettings2RecoveryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<Settings2Document?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path)) return null;

        try
        {
            var info = new FileInfo(_path);
            if (info.Length is < 1 or > MaxDocumentBytes) return null;
            var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            if (Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes) return null;
            var settings = JsonSerializer.Deserialize<Settings2Document>(json, JsonOptions);
            Settings2Validator.ThrowIfInvalid(settings);
            return settings;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    public async Task<bool> TrySaveAsync(Settings2Document settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings2Validator.ThrowIfInvalid(settings);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions) + Environment.NewLine;
            var bytes = Encoding.UTF8.GetBytes(json);
            if (bytes.LongLength > MaxDocumentBytes) return false;

            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Settings recovery directory is unavailable.");
            Directory.CreateDirectory(directory);
            var temp = Path.Combine(directory, Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var stream = new FileStream(
                    temp,
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
                File.Move(temp, _path, overwrite: true);
                return true;
            }
            finally
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                    // Best-effort cleanup only. LKG failure must not damage validated primary settings.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

public sealed class RecoveringSettings2Repository(
    ISettings2Repository primary,
    ISettings2RecoveryStore recovery) : ISettings2Repository
{
    public async Task<Settings2LoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var loaded = await primary.LoadAsync(cancellationToken).ConfigureAwait(false);
            _ = await recovery.TrySaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
            return loaded;
        }
        catch (InvalidDataException)
        {
            var lastKnownGood = await recovery.TryLoadAsync(cancellationToken).ConfigureAwait(false);
            if (lastKnownGood is not null)
                return new Settings2LoadResult(lastKnownGood, SettingsLoadOrigin.RecoveredLastKnownGood);

            var safeDefaults = Settings2Document.CreateDefault();
            safeDefaults.Online.AutoUpdateEnabled = false;
            Settings2Validator.ThrowIfInvalid(safeDefaults);
            return new Settings2LoadResult(safeDefaults, SettingsLoadOrigin.RecoveryDefaults);
        }
    }

    public async Task SaveAsync(Settings2Document settings, CancellationToken cancellationToken = default)
    {
        await primary.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        _ = await recovery.TrySaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
