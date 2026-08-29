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

public sealed class RecoveringSettings2Repository : IAtomicSettings2Repository
{
    private readonly ISettings2Repository _primary;
    private readonly ISettings2RecoveryStore _recovery;
    private readonly SemaphoreSlim _accessGate = new(1, 1);

    public RecoveringSettings2Repository(
        ISettings2Repository primary,
        ISettings2RecoveryStore recovery)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
    }

    public async Task<Settings2LoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _accessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(refreshRecoveryOnHealthy: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _accessGate.Release();
        }
    }

    public async Task SaveAsync(Settings2Document settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _accessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _accessGate.Release();
        }
    }

    public async Task<Settings2UpdateResult> UpdateAsync(
        Action<Settings2Document> mutation,
        bool allowRecoveryRebuild = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await _accessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Load + narrow mutation + primary/LKG persistence is one process-local transaction. This
            // prevents unrelated features (F drag, theme, pets, League toggles, maintenance) from
            // overwriting each other's fields with stale whole-document snapshots.
            var loaded = await LoadCoreAsync(refreshRecoveryOnHealthy: false, cancellationToken).ConfigureAwait(false);
            var recoveryReadOnly = loaded.Origin is
                SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults;
            if (recoveryReadOnly && !allowRecoveryRebuild)
                return new Settings2UpdateResult(loaded.Settings, loaded.Origin, Persisted: false);

            mutation(loaded.Settings);
            Settings2Validator.ThrowIfInvalid(loaded.Settings);
            await SaveCoreAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
            return new Settings2UpdateResult(loaded.Settings, SettingsLoadOrigin.ExistingV2, Persisted: true);
        }
        finally
        {
            _accessGate.Release();
        }
    }

    private async Task<Settings2LoadResult> LoadCoreAsync(
        bool refreshRecoveryOnHealthy,
        CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await _primary.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (refreshRecoveryOnHealthy)
                _ = await _recovery.TrySaveAsync(loaded.Settings, cancellationToken).ConfigureAwait(false);
            return loaded;
        }
        catch (InvalidDataException)
        {
            var lastKnownGood = await _recovery.TryLoadAsync(cancellationToken).ConfigureAwait(false);
            if (lastKnownGood is not null)
                return new Settings2LoadResult(lastKnownGood, SettingsLoadOrigin.RecoveredLastKnownGood);

            var safeDefaults = Settings2Document.CreateDefault();
            safeDefaults.Online.AutoUpdateEnabled = false;
            Settings2Validator.ThrowIfInvalid(safeDefaults);
            return new Settings2LoadResult(safeDefaults, SettingsLoadOrigin.RecoveryDefaults);
        }
    }

    private async Task SaveCoreAsync(Settings2Document settings, CancellationToken cancellationToken)
    {
        await _primary.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        _ = await _recovery.TrySaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
