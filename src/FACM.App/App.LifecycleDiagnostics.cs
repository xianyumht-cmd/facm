using System.Globalization;
using FACM.Core.League;
using FACM.Core.Observability;
using FACM.Core.State;
using Microsoft.UI.Xaml;

namespace FACM.App;

public partial class App
{
    private bool _lifecycleHandlersAttached;

    private void AttachLifecycleHandlers()
    {
        if (_lifecycleHandlersAttached) return;
        _lifecycleHandlersAttached = true;
        UnhandledException += OnApplicationUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private void OnApplicationUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        args.Handled = true;
        QueueLifecycleException("unhandled-ui-exception", "ui-boundary", args.Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        args.SetObserved();
        QueueLifecycleException("unobserved-task-exception", "background-task-boundary", args.Exception);
    }

    private void OnAppDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            QueueLifecycleException("fatal-process-exit", "app-domain-unhandled", exception, DiagnosticResult.Failure);
        else
            QueueLifecycleDiagnostic("fatal-process-exit", "app-domain-unhandled", DiagnosticResult.Failure);
    }

    private void OnProcessExit(object? sender, EventArgs args)
    {
        QueueLifecycleDiagnostic("fatal-process-exit", "process-exit", DiagnosticResult.Success);
        DetachLifecycleHandlers();
    }

    private void DetachLifecycleHandlers()
    {
        if (!_lifecycleHandlersAttached) return;
        _lifecycleHandlersAttached = false;
        UnhandledException -= OnApplicationUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }

    private void QueueLifecycleDiagnostic(
        string eventName,
        string reason,
        DiagnosticResult result = DiagnosticResult.Success,
        IReadOnlyDictionary<string, string>? additionalData = null)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["event"] = eventName,
            ["reason"] = reason,
            ["processId"] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            ["threadId"] = Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture),
            ["mainWindow"] = _window is null ? "closed" : "open",
            ["compactLauncher"] = _compactLauncher is null ? "closed" : "open",
            ["floatingWindow"] = _floatingWindow is null ? "closed" : "open",
            ["morphingSurface"] = _morphingSurfaceExperience ? "enabled" : "disabled",
            ["surfaceMode"] = _window?.SurfaceMode.ToString() ?? string.Empty,
            ["shuttingDown"] = _shuttingDown.ToString(CultureInfo.InvariantCulture),
            ["leaguePhase"] = _gameflow?.Current?.Phase ?? string.Empty
        };
        if (additionalData is not null)
        {
            foreach (var pair in additionalData)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key)) data[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        QueueDiagnostic(DiagnosticEventFactory.Create(
            "app.lifecycle",
            "FACM.App",
            0,
            result,
            reason,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            CurrentAppVersion(),
            data));
    }

    private void QueueLifecycleException(
        string eventName,
        string operation,
        Exception exception,
        DiagnosticResult result = DiagnosticResult.Failure)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["operation"] = operation,
            ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
            ["hResult"] = "0x" + exception.HResult.ToString("X8", CultureInfo.InvariantCulture),
            ["threadId"] = Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture),
            ["lastPhase"] = LeagueDiagnosticContext.Current?.Phase ?? _gameflow?.Current?.Phase ?? string.Empty
        };
        QueueLifecycleDiagnostic(eventName, exception.GetType().Name, result, data);
    }
}
