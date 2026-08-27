using FACM.App.ViewModels;
using FACM.Core.Observability;
using FACM.Core.Runtime;
using FACM.Core.State;
using FACM.Infrastructure.League;
using FACM.Infrastructure.Observability;
using FACM.Infrastructure.Online;
using FACM.Infrastructure.Settings;
using FACM.Infrastructure.Text;
using FACM.Platform.Windows.League;
using FACM.Platform.Windows.Runtime;
using Microsoft.UI.Xaml;

namespace FACM.App;

public partial class App : Application
{
    private Window? _window;
    private HttpUpdateManifestSource? _updateManifestSource;
    private LeagueHttpGateway? _leagueGateway;
    private WindowsLeagueTransportSessionSource? _leagueSessions;
    private ProductStateStore? _productState;
    private BoundedJsonLinesDiagnosticSink? _diagnostics;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var executablePaths = new WindowsExecutablePathProvider();
        var layout = RuntimePathLayout.From(executablePaths);

        _productState = new ProductStateStore();
        _productState.SetEnvironment(
            new ProductEnvironmentState(layout.DistributionDirectory, null, null),
            "runtime-layout-ready");
        _diagnostics = new BoundedJsonLinesDiagnosticSink(Path.Combine(layout.LogsDirectory, "facm4-events.jsonl"));

        var settings = new Settings2Repository(layout.Settings2Path, layout.SettingsPath);
        var uiText = new FileUiTextProvider(layout.UiTextPath);
        _updateManifestSource = new HttpUpdateManifestSource();

        // Exactly one League discovery/auth/session owner for the 4.0 process. Product State,
        // diagnostics and the Shell consume facts/contracts only; they never create another one.
        _leagueSessions = new WindowsLeagueTransportSessionSource();
        _leagueGateway = new LeagueHttpGateway(_leagueSessions);

        var controlCenter = new ControlCenterViewModel(settings, _updateManifestSource, _productState);
        _window = new MainWindow(controlCenter, uiText);
        _window.Closed += OnMainWindowClosed;
        _window.Activate();

        _productState.SetApplication(ApplicationProductState.Ready, "main-window-activated");
        QueueDiagnostic(DiagnosticEventFactory.Create(
            "app.launch",
            "FACM.App",
            0,
            DiagnosticResult.Success,
            "main-window-activated",
            _productState.Current.League,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown"));
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        _productState?.SetApplication(ApplicationProductState.ShuttingDown, "main-window-closed");
        _leagueGateway?.Dispose();
        _leagueGateway = null;
        _leagueSessions = null;
        _updateManifestSource?.Dispose();
        _updateManifestSource = null;
        _diagnostics?.Dispose();
        _diagnostics = null;
        _productState = null;
    }

    private void QueueDiagnostic(DiagnosticEvent diagnosticEvent)
    {
        var sink = _diagnostics;
        if (sink is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await sink.WriteAsync(diagnosticEvent).ConfigureAwait(false);
            }
            catch
            {
                // Diagnostics are best-effort and may never make the product fail to launch.
            }
        });
    }
}
