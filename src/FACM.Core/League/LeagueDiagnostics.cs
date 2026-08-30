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
    int MaxInFlightObserved,
    string NotFoundClassification = "",
    string GameflowPhase = "",
    string ExceptionType = "",
    string HResult = "",
    int ThreadId = 0);

public sealed record LeagueWorkbenchDiagnostic(
    string CorrelationId,
    string Event,
    string Stage,
    string Outcome,
    string Reason,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DurationMs,
    string ExceptionType = "",
    string HResult = "",
    int ThreadId = 0,
    string SynchronizationContext = "",
    string NavigationState = "",
    string WindowState = "");

public sealed record LeagueAutomationDiagnostic(
    string CorrelationId,
    string Feature,
    string Phase,
    string Event,
    string Outcome,
    string Reason,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DurationMs,
    string ExceptionType = "",
    string HResult = "",
    int ThreadId = 0,
    string ConfigurationSource = "",
    bool? AutoSearchEnabled = null,
    bool? AutoAcceptEnabled = null,
    DateTimeOffset? ObservedUtc = null,
    long? DetectionDelayMs = null,
    long? EvaluationDelayMs = null,
    long? HttpDelayMs = null);

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
    long DurationMs,
    string ExceptionType = "",
    string HResult = "",
    int ThreadId = 0,
    DateTimeOffset? ObservationTimestampUtc = null);

public sealed record LeagueSessionDiscoveryDiagnostic(
    string DiscoveryId,
    string Event,
    string Source,
    int? ProcessId,
    int? Port,
    long DurationMs,
    string Outcome,
    bool CacheHit,
    bool NegativeCacheHit,
    bool JoinedExistingDiscovery,
    int ThreadId,
    string Caller,
    string? Reason);

public sealed record LeaguePostGameDiagnostic(
    string CorrelationId,
    string Event,
    string Phase,
    string Operation,
    string Outcome,
    string Reason,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DurationMs,
    string Route = "",
    int? HttpStatus = null,
    int Attempt = 0,
    string TargetPuuidSuffix = "",
    string ExceptionType = "",
    string HResult = "",
    int ThreadId = 0);
