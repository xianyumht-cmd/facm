using FACM.Core.State;

namespace FACM.Core.League;

public sealed record LeagueDiagnosticScope(
    string CorrelationId,
    string Source,
    string Phase);

public static class LeagueDiagnosticContext
{
    private static readonly AsyncLocal<LeagueDiagnosticScope?> CurrentScope = new();

    public static LeagueDiagnosticScope? Current => CurrentScope.Value;

    public static string CreateCorrelationId() => Guid.NewGuid().ToString("N");

    public static IDisposable Begin(string correlationId, string source, string phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);

        var previous = CurrentScope.Value;
        CurrentScope.Value = new LeagueDiagnosticScope(correlationId, source, phase);
        return new Scope(previous);
    }

    private sealed class Scope(LeagueDiagnosticScope? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CurrentScope.Value = previous;
        }
    }
}

public sealed record LeagueHttpDiagnostic(
    string RequestId,
    string CorrelationId,
    string Source,
    string Phase,
    string Event,
    string Method,
    string Endpoint,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DurationMs,
    int? StatusCode,
    string Outcome,
    bool SessionInvalidated,
    int InFlightAtStart,
    int InFlightAtEnd,
    int MaxInFlightObserved);

public sealed record LeagueWorkbenchDiagnostic(
    string CorrelationId,
    string Event,
    string Stage,
    string Outcome,
    string Reason,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DurationMs);

public sealed record LeagueGameflowDiagnostic(
    string PollId,
    string CorrelationId,
    string Event,
    string Outcome,
    string Reason,
    string Phase,
    LeagueConnectionState ConnectionState,
    LeagueProductState ProductState,
    bool? Changed,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DurationMs);
