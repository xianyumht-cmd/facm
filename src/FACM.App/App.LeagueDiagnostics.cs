using System.Globalization;
using FACM.Core.League;
using FACM.Core.Observability;
using FACM.Core.State;

namespace FACM.App;

public partial class App
{
    private void ReportLeagueSessionDiagnostic(LeagueSessionDiscoveryDiagnostic diagnostic)
    {
        var data = new Dictionary<string, string>
        {
            ["discoveryId"] = diagnostic.DiscoveryId,
            ["source"] = diagnostic.Source,
            ["event"] = diagnostic.Event,
            ["outcome"] = diagnostic.Outcome,
            ["cacheHit"] = diagnostic.CacheHit.ToString(CultureInfo.InvariantCulture),
            ["negativeCacheHit"] = diagnostic.NegativeCacheHit.ToString(CultureInfo.InvariantCulture),
            ["joinedExistingDiscovery"] = diagnostic.JoinedExistingDiscovery.ToString(CultureInfo.InvariantCulture),
            ["threadId"] = diagnostic.ThreadId.ToString(CultureInfo.InvariantCulture),
            ["caller"] = diagnostic.Caller
        };
        if (diagnostic.ProcessId is int processId)
            data["pid"] = processId.ToString(CultureInfo.InvariantCulture);
        if (diagnostic.Port is int port)
            data["port"] = port.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(diagnostic.Reason))
            data["reason"] = diagnostic.Reason;

        var action = diagnostic.Event switch
        {
            "discovery-start" => "league.session.discovery-start",
            "discovery-finish" => "league.session.discovery-finish",
            "cache-hit" => "league.session.cache-hit",
            "invalidate" => "league.session.invalidate",
            _ => "league.session.discovery"
        };
        QueueDiagnostic(DiagnosticEventFactory.Create(
            action,
            "FACM.League.Session",
            diagnostic.DurationMs,
            ResolveSessionDiagnosticResult(diagnostic),
            diagnostic.Reason ?? diagnostic.Outcome,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            CurrentAppVersion(),
            data));
    }

    private void ReportLeagueHttpDiagnostic(LeagueHttpDiagnostic diagnostic)
    {
        var data = new Dictionary<string, string>
        {
            ["requestId"] = diagnostic.RequestId,
            ["correlationId"] = diagnostic.CorrelationId,
            ["event"] = diagnostic.Event,
            ["source"] = diagnostic.Source,
            ["phase"] = diagnostic.Phase,
            ["method"] = diagnostic.Method,
            ["endpoint"] = diagnostic.Endpoint,
            ["startedUtc"] = diagnostic.StartedUtc.ToString("O", CultureInfo.InvariantCulture),
            ["finishedUtc"] = diagnostic.FinishedUtc.ToString("O", CultureInfo.InvariantCulture),
            ["outcome"] = diagnostic.Outcome,
            ["sessionInvalidated"] = diagnostic.SessionInvalidated.ToString(CultureInfo.InvariantCulture),
            ["inFlightAtStart"] = diagnostic.InFlightAtStart.ToString(CultureInfo.InvariantCulture),
            ["inFlightAtEnd"] = diagnostic.InFlightAtEnd.ToString(CultureInfo.InvariantCulture),
            ["maxInFlightObserved"] = diagnostic.MaxInFlightObserved.ToString(CultureInfo.InvariantCulture)
        };
        if (diagnostic.StatusCode is int statusCode)
            data["statusCode"] = statusCode.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(diagnostic.NotFoundClassification))
            data["notFoundClassification"] = diagnostic.NotFoundClassification;
        if (!string.IsNullOrWhiteSpace(diagnostic.GameflowPhase))
            data["gameflowPhase"] = diagnostic.GameflowPhase;
        if (!string.IsNullOrWhiteSpace(diagnostic.ExceptionType))
            data["exceptionType"] = diagnostic.ExceptionType;
        if (!string.IsNullOrWhiteSpace(diagnostic.HResult))
            data["hResult"] = diagnostic.HResult;
        if (diagnostic.ThreadId > 0)
            data["threadId"] = diagnostic.ThreadId.ToString(CultureInfo.InvariantCulture);

        QueueDiagnostic(DiagnosticEventFactory.Create(
            "league.http",
            "FACM.League.Transport",
            diagnostic.DurationMs,
            ResolveDiagnosticResult(diagnostic.Event, diagnostic.Outcome),
            diagnostic.Outcome,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            CurrentAppVersion(),
            data,
            diagnostic.Event == "started" ? diagnostic.StartedUtc : diagnostic.FinishedUtc));
    }

    private void ReportLeagueWorkbenchDiagnostic(LeagueWorkbenchDiagnostic diagnostic)
    {
        QueueDiagnostic(DiagnosticEventFactory.Create(
            "league.workbench",
            "FACM.League.Workbench",
            diagnostic.DurationMs,
            ResolveDiagnosticResult(diagnostic.Event, diagnostic.Outcome),
            diagnostic.Reason,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            CurrentAppVersion(),
                new Dictionary<string, string>
                {
                ["correlationId"] = diagnostic.CorrelationId,
                ["event"] = diagnostic.Event,
                ["stage"] = diagnostic.Stage,
                ["outcome"] = diagnostic.Outcome,
                ["reason"] = diagnostic.Reason,
                ["startedUtc"] = diagnostic.StartedUtc.ToString("O", CultureInfo.InvariantCulture),
                    ["finishedUtc"] = diagnostic.FinishedUtc.ToString("O", CultureInfo.InvariantCulture)
                }
                .AlsoAddIfNotEmpty("exceptionType", diagnostic.ExceptionType)
                .AlsoAddIfNotEmpty("hResult", diagnostic.HResult)
                .AlsoAddIfPositive("threadId", diagnostic.ThreadId)
                .AlsoAddIfNotEmpty("synchronizationContext", diagnostic.SynchronizationContext)
                .AlsoAddIfNotEmpty("navigationState", diagnostic.NavigationState)
                .AlsoAddIfNotEmpty("windowState", diagnostic.WindowState),
            diagnostic.Event == "started" ? diagnostic.StartedUtc : diagnostic.FinishedUtc));
    }

    private void ReportLeagueGameflowDiagnostic(LeagueGameflowDiagnostic diagnostic)
    {
        var data = new Dictionary<string, string>
        {
            ["pollId"] = diagnostic.PollId,
            ["correlationId"] = diagnostic.CorrelationId,
            ["event"] = diagnostic.Event,
            ["outcome"] = diagnostic.Outcome,
            ["reason"] = diagnostic.Reason,
            ["phase"] = diagnostic.Phase,
            ["connectionState"] = diagnostic.ConnectionState.ToString(),
            ["productState"] = diagnostic.ProductState.ToString(),
            ["startedUtc"] = diagnostic.StartedUtc.ToString("O", CultureInfo.InvariantCulture),
            ["finishedUtc"] = diagnostic.FinishedUtc.ToString("O", CultureInfo.InvariantCulture)
        };
        if (diagnostic.Changed is bool changed)
            data["changed"] = changed.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(diagnostic.ExceptionType))
            data["exceptionType"] = diagnostic.ExceptionType;
        if (!string.IsNullOrWhiteSpace(diagnostic.HResult))
            data["hResult"] = diagnostic.HResult;
        if (diagnostic.ThreadId > 0)
            data["threadId"] = diagnostic.ThreadId.ToString(CultureInfo.InvariantCulture);

        QueueDiagnostic(DiagnosticEventFactory.Create(
            "league.gameflow",
            "FACM.League.Gameflow",
            diagnostic.DurationMs,
            ResolveDiagnosticResult(diagnostic.Event, diagnostic.Outcome),
            diagnostic.Reason,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            CurrentAppVersion(),
            data,
            diagnostic.Event == "started" ? diagnostic.StartedUtc : diagnostic.FinishedUtc));
    }

    private void ReportLeagueAutomationDiagnostic(LeagueAutomationDiagnostic diagnostic)
    {
        var data = new Dictionary<string, string>
        {
            ["correlationId"] = diagnostic.CorrelationId,
            ["feature"] = diagnostic.Feature,
            ["phase"] = diagnostic.Phase,
            ["event"] = diagnostic.Event,
            ["outcome"] = diagnostic.Outcome,
            ["reason"] = diagnostic.Reason,
            ["startedUtc"] = diagnostic.StartedUtc.ToString("O", CultureInfo.InvariantCulture),
            ["finishedUtc"] = diagnostic.FinishedUtc.ToString("O", CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(diagnostic.ExceptionType)) data["exceptionType"] = diagnostic.ExceptionType;
        if (!string.IsNullOrWhiteSpace(diagnostic.HResult)) data["hResult"] = diagnostic.HResult;
        if (diagnostic.ThreadId > 0) data["threadId"] = diagnostic.ThreadId.ToString(CultureInfo.InvariantCulture);

        QueueDiagnostic(DiagnosticEventFactory.Create(
            "league.automation",
            "FACM.League.Automation",
            diagnostic.DurationMs,
            ResolveDiagnosticResult(diagnostic.Event, diagnostic.Outcome),
            diagnostic.Reason,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            CurrentAppVersion(),
            data,
            diagnostic.Event.EndsWith("start", StringComparison.Ordinal) ? diagnostic.StartedUtc : diagnostic.FinishedUtc));
    }

    private static DiagnosticResult ResolveDiagnosticResult(string eventName, string outcome) =>
        eventName == "started"
            ? DiagnosticResult.Success
            : outcome switch
            {
                "success" => DiagnosticResult.Success,
                "cancelled" or "caller-cancelled" => DiagnosticResult.Cancelled,
                "skipped" or "no-session" or "expected-unavailable" => DiagnosticResult.Skipped,
                _ => DiagnosticResult.Failure
            };

    private static DiagnosticResult ResolveSessionDiagnosticResult(LeagueSessionDiscoveryDiagnostic diagnostic) =>
        diagnostic.Event is "discovery-start" or "cache-hit"
            ? DiagnosticResult.Success
            : diagnostic.Outcome is "lockfile-success" or "process-fallback-success" or "success" or "positive-cache"
                ? DiagnosticResult.Success
                : diagnostic.Outcome is "lockfile-empty" or "process-not-found" or "command-line-unavailable" or "negative-cache"
                    ? DiagnosticResult.Skipped
                    : diagnostic.Outcome == "cancelled"
                        ? DiagnosticResult.Cancelled
                        : diagnostic.Event == "invalidate"
                            ? DiagnosticResult.Success
                            : DiagnosticResult.Failure;

    private static string CurrentAppVersion() =>
        typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";

    private IReadOnlyDictionary<string, string> CreateLeagueRuntimeFacts()
    {
        var session = _leagueSessions;
        var gameflow = _gameflow?.Current;
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["league.connectionState"] = session?.State.ToString() ?? "NotRunning",
            ["league.phase"] = gameflow?.Phase ?? string.Empty,
            ["league.productState"] = gameflow?.ProductState.ToString() ?? "NotRunning",
            ["league.http.inFlight"] = (_leagueGateway?.InFlightCount ?? 0).ToString(CultureInfo.InvariantCulture),
            ["league.http.maxInFlight"] = (_leagueGateway?.MaxInFlightObserved ?? 0).ToString(CultureInfo.InvariantCulture)
        };
        if (session?.Current is { } descriptor)
        {
            facts["league.pid"] = descriptor.ProcessId.ToString(CultureInfo.InvariantCulture);
            facts["league.port"] = descriptor.Port.ToString(CultureInfo.InvariantCulture);
            facts["league.sessionSource"] = descriptor.Source;
        }
        return facts;
    }
}

internal static class DiagnosticDictionaryExtensions
{
    public static Dictionary<string, string> AlsoAddIfNotEmpty(
        this Dictionary<string, string> data,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) data[key] = value;
        return data;
    }

    public static Dictionary<string, string> AlsoAddIfPositive(
        this Dictionary<string, string> data,
        string key,
        int value)
    {
        if (value > 0) data[key] = value.ToString(CultureInfo.InvariantCulture);
        return data;
    }
}
