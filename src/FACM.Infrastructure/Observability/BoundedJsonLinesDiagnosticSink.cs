using System.Text;
using System.Text.Json;
using FACM.Core.Observability;

namespace FACM.Infrastructure.Observability;

public sealed class BoundedJsonLinesDiagnosticSink : IDiagnosticSink, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _path;
    private readonly long _maxBytes;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public BoundedJsonLinesDiagnosticSink(string path, long maxBytes = 4 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxBytes < 4096) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _path = Path.GetFullPath(path);
        _maxBytes = maxBytes;
    }

    public async Task WriteAsync(DiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        var safe = DiagnosticRedactor.Redact(diagnosticEvent);
        var line = JsonSerializer.Serialize(safe, JsonOptions) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(line);
        if (bytes.LongLength > _maxBytes)
            throw new InvalidDataException("Diagnostic event exceeds the bounded sink capacity.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Diagnostic directory is unavailable.");
            Directory.CreateDirectory(directory);

            var existingBytes = File.Exists(_path) ? new FileInfo(_path).Length : 0;
            if (existingBytes + bytes.LongLength > _maxBytes && File.Exists(_path))
            {
                File.Move(_path, _path + ".1", overwrite: true);
            }

            await using var stream = new FileStream(
                _path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous);
            await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}
