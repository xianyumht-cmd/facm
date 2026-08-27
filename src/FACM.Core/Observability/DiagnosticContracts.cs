using FACM.Core.State;

namespace FACM.Core.Observability;

public enum DiagnosticResult
{
    Success,
    Failure,
    Cancelled,
    Skipped
}

public sealed record DiagnosticEvent(
    DateTimeOffset TimestampUtc,
    string ActionId,
    string Module,
    long DurationMs,
    DiagnosticResult Result,
    string Reason,
    LeagueProductState LeagueState,
    string ClientVersion,
    IReadOnlyDictionary<string, string> Data);

public interface IDiagnosticSink
{
    Task WriteAsync(DiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default);
}

public static class DiagnosticEventFactory
{
    public static DiagnosticEvent Create(
        string actionId,
        string module,
        long durationMs,
        DiagnosticResult result,
        string reason,
        LeagueProductState leagueState,
        string clientVersion,
        IReadOnlyDictionary<string, string>? data = null,
        DateTimeOffset? timestampUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        if (durationMs < 0) throw new ArgumentOutOfRangeException(nameof(durationMs));

        var created = new DiagnosticEvent(
            timestampUtc ?? DateTimeOffset.UtcNow,
            Limit(actionId.Trim(), 128),
            Limit(module.Trim(), 128),
            durationMs,
            result,
            Limit(reason ?? string.Empty, 1024),
            leagueState,
            Limit(clientVersion ?? string.Empty, 128),
            data ?? new Dictionary<string, string>());
        return DiagnosticRedactor.Redact(created);
    }

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

public static class DiagnosticRedactor
{
    private static readonly string[] SensitiveKeyFragments =
    [
        "token", "password", "passwd", "cookie", "authorization", "secret", "credential", "auth"
    ];

    private static readonly string[] SensitiveAssignments =
    [
        "token=", "password=", "passwd=", "cookie=", "authorization=", "secret=", "credential="
    ];

    public static DiagnosticEvent Redact(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        var safe = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in diagnosticEvent.Data ?? new Dictionary<string, string>())
        {
            var key = pair.Key ?? string.Empty;
            safe[key] = IsSensitiveKey(key) ? "[redacted]" : ScrubText(pair.Value ?? string.Empty);
        }

        return diagnosticEvent with
        {
            ActionId = ScrubText(diagnosticEvent.ActionId),
            Module = ScrubText(diagnosticEvent.Module),
            Reason = ScrubText(diagnosticEvent.Reason),
            ClientVersion = ScrubText(diagnosticEvent.ClientVersion),
            Data = safe
        };
    }

    public static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return SensitiveKeyFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    public static string ScrubText(string? value)
    {
        var result = value ?? string.Empty;
        foreach (var marker in SensitiveAssignments)
        {
            var searchFrom = 0;
            while (searchFrom < result.Length)
            {
                var start = result.IndexOf(marker, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                var valueStart = start + marker.Length;
                var valueEnd = valueStart;
                while (valueEnd < result.Length && !IsDelimiter(result[valueEnd])) valueEnd++;
                result = result[..valueStart] + "[redacted]" + result[valueEnd..];
                searchFrom = valueStart + "[redacted]".Length;
            }
        }
        return result;
    }

    private static bool IsDelimiter(char value) =>
        value is ';' or '&' or ',' or '|' or '\r' or '\n';
}
