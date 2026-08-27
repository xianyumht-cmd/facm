using System.Text;
using System.Text.Json;
using FACM.Core.Recovery;
using FACM.Core.Runtime;

namespace FACM.Infrastructure.Recovery;

public sealed class JsonRecoveryStateStore : IRecoveryStateStore
{
    private const long MaxDocumentBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly IClock _clock;

    public JsonRecoveryStateStore(string path, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<RecoveryStateLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path))
            return new RecoveryStateLoadResult(RecoveryStateSnapshot.CreateInitial(_clock.UtcNow), RecoveryLoadOrigin.Missing);

        try
        {
            var info = new FileInfo(_path);
            if (info.Length is < 1 or > MaxDocumentBytes) return Malformed();
            var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            if (Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes) return Malformed();
            var state = JsonSerializer.Deserialize<RecoveryStateSnapshot>(json, JsonOptions);
            RecoveryStateValidator.ThrowIfInvalid(state);
            return new RecoveryStateLoadResult(state!, RecoveryLoadOrigin.Existing);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return Malformed();
        }
    }

    public async Task SaveAsync(RecoveryStateSnapshot state, CancellationToken cancellationToken = default)
    {
        RecoveryStateValidator.ThrowIfInvalid(state);
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(state, JsonOptions) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.LongLength > MaxDocumentBytes)
            throw new InvalidDataException("Recovery state exceeds the bounded document size.");

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Recovery state directory is unavailable.");
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
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // Best-effort cleanup only; do not mask the primary recovery-state operation.
            }
        }
    }

    private RecoveryStateLoadResult Malformed() => new(
        RecoveryStateSnapshot.CreateInitial(_clock.UtcNow),
        RecoveryLoadOrigin.Malformed);
}
