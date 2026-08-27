using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FACM.Core.Observability;
using FACM.Core.State;

namespace FACM.Infrastructure.Observability;

public sealed class FileDiagnosticsSnapshotSource : IDiagnosticsSnapshotSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IProductStateReader _productState;
    private readonly string _currentLogPath;
    private readonly string _appVersion;
    private readonly DiagnosticsExportPolicy _policy;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly IReadOnlyDictionary<string, string> _runtimeFacts;

    public FileDiagnosticsSnapshotSource(
        IProductStateReader productState,
        string currentLogPath,
        string appVersion,
        DiagnosticsExportPolicy? policy = null,
        Func<DateTimeOffset>? utcNow = null,
        IReadOnlyDictionary<string, string>? runtimeFacts = null)
    {
        _productState = productState ?? throw new ArgumentNullException(nameof(productState));
        ArgumentException.ThrowIfNullOrWhiteSpace(currentLogPath);
        _currentLogPath = Path.GetFullPath(currentLogPath);
        _appVersion = appVersion ?? string.Empty;
        _policy = (policy ?? DiagnosticsExportPolicy.Default).Validate();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _runtimeFacts = runtimeFacts ?? new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["framework"] = RuntimeInformation.FrameworkDescription,
            ["os"] = RuntimeInformation.OSDescription,
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString()
        };
    }

    public async Task<DiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var events = new Queue<DiagnosticEvent>();
        var malformed = 0;
        var skippedFiles = 0;
        var eventsTruncated = false;
        long totalInput = 0;

        // Rotation is read first so the bounded queue naturally retains the most recent events from
        // the current file when MaxEvents is reached. No directory enumeration is permitted here.
        foreach (var path in new[] { _currentLogPath + ".1", _currentLogPath })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path)) continue;

            var info = new FileInfo(path);
            var observedLength = info.Length;
            if (observedLength < 0 || observedLength > _policy.MaxInputFileBytes)
            {
                skippedFiles++;
                continue;
            }
            if (totalInput + observedLength > _policy.MaxTotalInputBytes)
            {
                skippedFiles++;
                continue;
            }

            var bytes = await ReadInitialBytesAsync(path, observedLength, cancellationToken).ConfigureAwait(false);
            totalInput += bytes.LongLength;
            var text = Encoding.UTF8.GetString(bytes);
            using var reader = new StringReader(text);
            while (reader.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;

                DiagnosticEvent? parsed;
                try
                {
                    parsed = JsonSerializer.Deserialize<DiagnosticEvent>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    malformed++;
                    continue;
                }
                catch (NotSupportedException)
                {
                    malformed++;
                    continue;
                }

                if (parsed is null)
                {
                    malformed++;
                    continue;
                }

                var safe = DiagnosticsExportSanitizer.ScrubEvent(parsed);
                if (events.Count == _policy.MaxEvents)
                {
                    events.Dequeue();
                    eventsTruncated = true;
                }
                events.Enqueue(safe);
            }
        }

        var facts = _runtimeFacts.ToDictionary(
            pair => DiagnosticsExportSanitizer.ScrubText(pair.Key),
            pair => DiagnosticsExportSanitizer.ScrubText(pair.Value),
            StringComparer.Ordinal);

        var snapshot = new DiagnosticsSnapshot(
            _utcNow(),
            DiagnosticsExportSanitizer.ScrubText(_appVersion),
            _productState.Current,
            facts,
            events.ToArray(),
            malformed,
            skippedFiles,
            eventsTruncated);
        return DiagnosticsExportSanitizer.ScrubSnapshot(snapshot);
    }

    private static async Task<byte[]> ReadInitialBytesAsync(
        string path,
        long observedLength,
        CancellationToken cancellationToken)
    {
        if (observedLength == 0) return Array.Empty<byte>();
        if (observedLength > int.MaxValue) throw new InvalidDataException("Diagnostic input is too large to buffer safely.");

        var buffer = new byte[(int)observedLength];
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            read += count;
        }

        if (read == buffer.Length) return buffer;
        return buffer[..read];
    }
}

public sealed class DiagnosticsBundleExporter : IDiagnosticsBundleExporter
{
    public const string SummaryEntryName = "summary.txt";
    public const string EventsEntryName = "events.jsonl";
    public const string ManifestEntryName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _outputDirectory;
    private readonly DiagnosticsExportPolicy _policy;
    private readonly Func<DateTimeOffset> _utcNow;

    public DiagnosticsBundleExporter(
        string outputDirectory,
        DiagnosticsExportPolicy? policy = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _outputDirectory = Path.GetFullPath(outputDirectory);
        _policy = (policy ?? DiagnosticsExportPolicy.Default).Validate();
        if (_policy.MaxZipEntries < 3) throw new ArgumentOutOfRangeException(nameof(policy), "Diagnostics bundle requires three allowlisted entries.");
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<DiagnosticsExportReceipt> ExportAsync(
        DiagnosticsSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_outputDirectory);

        var safe = DiagnosticsExportSanitizer.ScrubSnapshot(snapshot);
        var selectedEvents = SelectEventsForEntry(safe.Events, out var entryTruncated);
        var exportSnapshot = safe with
        {
            Events = selectedEvents,
            EventsTruncated = safe.EventsTruncated || entryTruncated
        };

        var summaryBytes = Encoding.UTF8.GetBytes(
            DiagnosticsSummaryFormatter.Format(exportSnapshot, _policy.MaxSummaryChars));
        var eventsBytes = BuildEventsBytes(selectedEvents);
        var manifestBytes = BuildManifestBytes(exportSnapshot);

        ValidateEntry(SummaryEntryName, summaryBytes);
        ValidateEntry(EventsEntryName, eventsBytes);
        ValidateEntry(ManifestEntryName, manifestBytes);
        var uncompressedTotal = checked(summaryBytes.LongLength + eventsBytes.LongLength + manifestBytes.LongLength);
        if (uncompressedTotal > _policy.MaxBundleBytes)
            throw new InvalidDataException("Diagnostics bundle exceeds the uncompressed output bound.");

        var now = _utcNow().ToUniversalTime();
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        var fileName = $"facm-diagnostics-{now:yyyyMMddTHHmmssfffZ}-{suffix}.zip";
        var finalPath = Path.Combine(_outputDirectory, fileName);
        var tempPath = Path.Combine(_outputDirectory, ".facm-diagnostics-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await WriteEntryAsync(archive, SummaryEntryName, summaryBytes, cancellationToken).ConfigureAwait(false);
                    await WriteEntryAsync(archive, EventsEntryName, eventsBytes, cancellationToken).ConfigureAwait(false);
                    await WriteEntryAsync(archive, ManifestEntryName, manifestBytes, cancellationToken).ConfigureAwait(false);
                }
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            var bundleBytes = new FileInfo(tempPath).Length;
            if (bundleBytes > _policy.MaxBundleBytes)
                throw new InvalidDataException("Diagnostics ZIP exceeds the output bound.");

            File.Move(tempPath, finalPath, overwrite: false);
            return new DiagnosticsExportReceipt(
                finalPath,
                bundleBytes,
                3,
                selectedEvents.Count,
                exportSnapshot.EventsTruncated);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private IReadOnlyList<DiagnosticEvent> SelectEventsForEntry(
        IReadOnlyList<DiagnosticEvent> events,
        out bool truncated)
    {
        var ordered = events
            .Select(DiagnosticsExportSanitizer.ScrubEvent)
            .OrderBy(item => item.TimestampUtc)
            .ThenBy(item => item.ActionId, StringComparer.Ordinal)
            .ToArray();
        var selectedReverse = new List<DiagnosticEvent>();
        long bytes = 0;
        truncated = false;

        for (var index = ordered.Length - 1; index >= 0; index--)
        {
            var line = SerializeEventLine(ordered[index]);
            if (line.LongLength > _policy.MaxEntryBytes || bytes + line.LongLength > _policy.MaxEntryBytes)
            {
                truncated = true;
                continue;
            }
            selectedReverse.Add(ordered[index]);
            bytes += line.LongLength;
        }

        selectedReverse.Reverse();
        if (selectedReverse.Count < ordered.Length) truncated = true;
        return selectedReverse;
    }

    private static byte[] BuildEventsBytes(IReadOnlyList<DiagnosticEvent> events)
    {
        using var stream = new MemoryStream();
        foreach (var item in events)
        {
            var line = SerializeEventLine(item);
            stream.Write(line, 0, line.Length);
        }
        return stream.ToArray();
    }

    private static byte[] SerializeEventLine(DiagnosticEvent diagnosticEvent) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            DiagnosticsExportSanitizer.ScrubEvent(diagnosticEvent),
            JsonOptions) + "\n");

    private static byte[] BuildManifestBytes(DiagnosticsSnapshot snapshot)
    {
        var manifest = new
        {
            schemaVersion = 1,
            generatedAtUtc = snapshot.GeneratedAtUtc.ToUniversalTime(),
            appVersion = DiagnosticsExportSanitizer.ScrubText(snapshot.AppVersion),
            application = snapshot.ProductState.Application.ToString(),
            league = snapshot.ProductState.League.ToString(),
            eventCount = snapshot.Events.Count,
            malformedLinesSkipped = snapshot.MalformedLinesSkipped,
            inputFilesSkipped = snapshot.InputFilesSkipped,
            eventsTruncated = snapshot.EventsTruncated,
            entries = new[] { SummaryEntryName, EventsEntryName, ManifestEntryName }
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private void ValidateEntry(string name, byte[] bytes)
    {
        if (bytes.LongLength > _policy.MaxEntryBytes)
            throw new InvalidDataException($"Diagnostics entry '{name}' exceeds the entry bound.");
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string name,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only. The .tmp name contains no user or machine identity.
        }
    }
}
