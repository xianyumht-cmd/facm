using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FACM.Core.State;

namespace FACM.Core.Observability;

public sealed record DiagnosticsExportPolicy(
    int MaxEvents,
    long MaxInputFileBytes,
    long MaxTotalInputBytes,
    int MaxZipEntries,
    long MaxEntryBytes,
    long MaxBundleBytes,
    int MaxSummaryChars)
{
    public static DiagnosticsExportPolicy Default { get; } = new(
        MaxEvents: 500,
        MaxInputFileBytes: 4 * 1024 * 1024,
        MaxTotalInputBytes: 8 * 1024 * 1024,
        MaxZipEntries: 3,
        MaxEntryBytes: 4 * 1024 * 1024,
        MaxBundleBytes: 8 * 1024 * 1024,
        MaxSummaryChars: 64 * 1024).Validate();

    public DiagnosticsExportPolicy Validate()
    {
        if (MaxEvents is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(MaxEvents));
        if (MaxInputFileBytes is < 4096 or > 32 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaxInputFileBytes));
        if (MaxTotalInputBytes < MaxInputFileBytes || MaxTotalInputBytes > 64 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaxTotalInputBytes));
        if (MaxZipEntries is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(MaxZipEntries));
        if (MaxEntryBytes is < 4096 or > 32 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaxEntryBytes));
        if (MaxBundleBytes < MaxEntryBytes || MaxBundleBytes > 64 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaxBundleBytes));
        if (MaxSummaryChars is < 1024 or > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaxSummaryChars));
        return this;
    }
}

public sealed record DiagnosticsSnapshot(
    DateTimeOffset GeneratedAtUtc,
    string AppVersion,
    ProductStateSnapshot ProductState,
    IReadOnlyDictionary<string, string> RuntimeFacts,
    IReadOnlyList<DiagnosticEvent> Events,
    int MalformedLinesSkipped,
    int InputFilesSkipped,
    bool EventsTruncated);

public sealed record DiagnosticsExportReceipt(
    string BundlePath,
    long BundleBytes,
    int EntryCount,
    int ExportedEventCount,
    bool EventsTruncated);

public interface IDiagnosticsSnapshotSource
{
    Task<DiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface IDiagnosticsBundleExporter
{
    Task<DiagnosticsExportReceipt> ExportAsync(
        DiagnosticsSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

public static partial class DiagnosticsExportSanitizer
{
    private const string Redacted = "[redacted]";
    private const string PathRedacted = "[path]";

    [GeneratedRegex(@"(?i)\b(Basic|Bearer)\s+[A-Za-z0-9._~+/=-]+", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9])[A-Z]:\\(?:[^\\/:*?\""<>|\r\n]+\\)*[^\\/:*?\""<>|\r\n]*", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"\\\\[^\\\s]+\\[^\r\n;,|\""<>]+", RegexOptions.CultureInvariant)]
    private static partial Regex UncPathRegex();

    public static string ScrubText(string? value)
    {
        var safe = DiagnosticRedactor.ScrubText(value);
        safe = AuthorizationRegex().Replace(safe, match => match.Groups[1].Value + " " + Redacted);
        safe = WindowsPathRegex().Replace(safe, PathRedacted);
        safe = UncPathRegex().Replace(safe, PathRedacted);
        return safe;
    }

    public static DiagnosticEvent ScrubEvent(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        var redacted = DiagnosticRedactor.Redact(diagnosticEvent);
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in redacted.Data)
        {
            var key = pair.Key ?? string.Empty;
            data[key] = DiagnosticRedactor.IsSensitiveKey(key)
                ? Redacted
                : ScrubText(pair.Value);
        }

        return redacted with
        {
            ActionId = ScrubText(redacted.ActionId),
            Module = ScrubText(redacted.Module),
            Reason = ScrubText(redacted.Reason),
            ClientVersion = ScrubText(redacted.ClientVersion),
            Data = data
        };
    }

    public static DiagnosticsSnapshot ScrubSnapshot(DiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var facts = snapshot.RuntimeFacts
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                pair => ScrubText(pair.Key),
                pair => ScrubText(pair.Value),
                StringComparer.Ordinal);
        var events = snapshot.Events.Select(ScrubEvent).ToArray();
        return snapshot with
        {
            AppVersion = ScrubText(snapshot.AppVersion),
            RuntimeFacts = facts,
            Events = events
        };
    }
}

public static class DiagnosticsSummaryFormatter
{
    public static string Format(DiagnosticsSnapshot snapshot, int? maxChars = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var safe = DiagnosticsExportSanitizer.ScrubSnapshot(snapshot);
        var limit = maxChars ?? DiagnosticsExportPolicy.Default.MaxSummaryChars;
        if (limit < 256) throw new ArgumentOutOfRangeException(nameof(maxChars));

        var builder = new StringBuilder();
        builder.AppendLine("FACM Diagnostics Summary");
        builder.Append("GeneratedUtc=").AppendLine(safe.GeneratedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        builder.Append("AppVersion=").AppendLine(safe.AppVersion);
        builder.Append("Revision=").AppendLine(safe.ProductState.Revision.ToString(CultureInfo.InvariantCulture));
        builder.Append("Application=").AppendLine(safe.ProductState.Application.ToString());
        builder.Append("League=").AppendLine(safe.ProductState.League.ToString());
        builder.Append("UpdateMetadata=").AppendLine(safe.ProductState.Services.UpdateMetadata.ToString());
        builder.Append("LeagueTransport=").AppendLine(safe.ProductState.Services.LeagueTransport.ToString());
        builder.Append("PetHost=").AppendLine(safe.ProductState.Services.PetHost.ToString());

        foreach (var pair in safe.RuntimeFacts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            builder.Append("Runtime.").Append(pair.Key).Append('=').AppendLine(pair.Value);

        builder.Append("Events=").AppendLine(safe.Events.Count.ToString(CultureInfo.InvariantCulture));
        builder.Append("MalformedLinesSkipped=").AppendLine(safe.MalformedLinesSkipped.ToString(CultureInfo.InvariantCulture));
        builder.Append("InputFilesSkipped=").AppendLine(safe.InputFilesSkipped.ToString(CultureInfo.InvariantCulture));
        builder.Append("EventsTruncated=").AppendLine(safe.EventsTruncated ? "True" : "False");

        foreach (var item in safe.Events.OrderBy(item => item.TimestampUtc).ThenBy(item => item.ActionId, StringComparer.Ordinal))
        {
            builder.Append("Event=")
                .Append(item.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(item.Module).Append('|')
                .Append(item.ActionId).Append('|')
                .Append(item.Result).Append('|')
                .Append(item.DurationMs.ToString(CultureInfo.InvariantCulture)).Append("ms|")
                .Append(item.LeagueState).Append('|')
                .AppendLine(item.Reason);
        }

        var text = builder.ToString();
        if (text.Length <= limit) return text;
        const string marker = "\n[summary-truncated]\n";
        return text[..Math.Max(0, limit - marker.Length)] + marker;
    }
}
