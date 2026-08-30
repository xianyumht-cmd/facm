using System.Globalization;
using FACM.Core.League;
using FACM.Core.Observability;
using FACM.Core.State;

namespace FACM.App;

public partial class App
{
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
            },
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

    private static DiagnosticResult ResolveDiagnosticResult(string eventName, string outcome) =>
        eventName == "started"
            ? DiagnosticResult.Success
            : outcome switch
            {
                "success" => DiagnosticResult.Success,
                "cancelled" or "caller-cancelled" => DiagnosticResult.Cancelled,
                "skipped" or "no-session" => DiagnosticResult.Skipped,
                _ => DiagnosticResult.Failure
            };

    private static string CurrentAppVersion() =>
        typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
}
