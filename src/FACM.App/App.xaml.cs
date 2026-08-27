using FACM.App.ViewModels;
using FACM.Core.Desktop;
using FACM.Core.Observability;
using FACM.Core.Runtime;
using FACM.Core.Settings;
using FACM.Core.State;
using FACM.Core.Text;
using FACM.Infrastructure.League;
using FACM.Infrastructure.Observability;
using FACM.Infrastructure.Online;
using FACM.Infrastructure.Settings;
using FACM.Infrastructure.Text;
using FACM.Platform.Windows.Desktop;
using FACM.Platform.Windows.League;
using FACM.Platform.Windows.Runtime;
using Microsoft.UI.Xaml;

namespace FACM.App;

public partial class App : Application
{
    private MainWindow? _window;
    private FloatingWindow? _floatingWindow;
    private ControlCenterViewModel? _controlCenter;
    private IUiTextProvider? _uiText;
    private Settings2Repository? _settings;
    private HttpUpdateManifestSource? _updateManifestSource;
    private LeagueHttpGateway? _leagueGateway;
    private WindowsLeagueTransportSessionSource? _leagueSessions;
    private ProductStateStore? _productState;
    private BoundedJsonLinesDiagnosticSink? _diagnostics;
    private bool _shuttingDown;

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

        _settings = new Settings2Repository(layout.Settings2Path, layout.SettingsPath);
        _uiText = new FileUiTextProvider(layout.UiTextPath);
        _updateManifestSource = new HttpUpdateManifestSource();

        // Exactly one League discovery/auth/session owner for the 4.0 process. Product State,
        // diagnostics, Main Shell and the desktop surface consume facts/contracts only.
        _leagueSessions = new WindowsLeagueTransportSessionSource();
        _leagueGateway = new LeagueHttpGateway(_leagueSessions);

        _controlCenter = new ControlCenterViewModel(_settings, _updateManifestSource, _productState);
        EnsureMainWindow();

        var workAreas = new WindowsDesktopWorkAreaProvider();
        _floatingWindow = new FloatingWindow(workAreas, _uiText, EnsureMainWindow);
        _floatingWindow.Closed += OnFloatingWindowClosed;
        try
        {
            _floatingWindow.ApplyPlacement(null);
        }
        catch (Exception exception)
        {
            QueueDiagnostic(CreateDesktopPlacementDiagnostic("default-placement-failed", exception));
        }
        _floatingWindow.Activate();
        _ = ApplyPreferredFloatingPlacementAsync(_settings, _floatingWindow);

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

    private void EnsureMainWindow()
    {
        if (_shuttingDown) return;
        if (_window is null)
        {
            var controlCenter = _controlCenter ?? throw new InvalidOperationException("Control center is unavailable.");
            var text = _uiText ?? throw new InvalidOperationException("UI text provider is unavailable.");
            _window = new MainWindow(controlCenter, text);
            _window.Closed += OnMainWindowClosed;
        }
        _window.Activate();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (ReferenceEquals(sender, _window)) _window = null;
        // Closing the main shell does not toggle or terminate the desktop entry. Clicking F later
        // recreates the shell and activates it, preserving Ensure Open / Activate semantics.
    }

    private void OnFloatingWindowClosed(object sender, WindowEventArgs args)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _productState?.SetApplication(ApplicationProductState.ShuttingDown, "floating-window-closed");
        _floatingWindow = null;

        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }

        DisposeRuntime();
    }

    private async Task ApplyPreferredFloatingPlacementAsync(
        ISettings2Repository settings,
        FloatingWindow floatingWindow)
    {
        try
        {
            var loaded = await settings.LoadAsync();
            if (!ReferenceEquals(_floatingWindow, floatingWindow) || _shuttingDown) return;

            var pets = loaded.Settings.Pets;
            DesktopPoint? preferred = pets.BallX == int.MinValue || pets.BallY == int.MinValue
                ? null
                : new DesktopPoint(pets.BallX, pets.BallY);
            var placement = floatingWindow.ApplyPlacement(preferred);
            QueueDiagnostic(DiagnosticEventFactory.Create(
                "desktop.place",
                "FACM.Desktop",
                0,
                DiagnosticResult.Success,
                placement.RecoveredOffScreen ? "off-screen-recovered" : "placed",
                _productState?.Current.League ?? LeagueProductState.NotRunning,
                typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
                new Dictionary<string, string>
                {
                    ["monitor"] = placement.WorkArea.Id,
                    ["anchor"] = placement.ResolvedAnchor.ToString()
                }));
        }
        catch (Exception exception)
        {
            QueueDiagnostic(CreateDesktopPlacementDiagnostic("preferred-placement-failed", exception));
        }
    }

    private DiagnosticEvent CreateDesktopPlacementDiagnostic(string reason, Exception exception) =>
        DiagnosticEventFactory.Create(
            "desktop.place",
            "FACM.Desktop",
            0,
            DiagnosticResult.Failure,
            reason + ":" + exception.GetType().Name,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown");

    private void DisposeRuntime()
    {
        _leagueGateway?.Dispose();
        _leagueGateway = null;
        _leagueSessions = null;
        _updateManifestSource?.Dispose();
        _updateManifestSource = null;
        _diagnostics?.Dispose();
        _diagnostics = null;
        _settings = null;
        _controlCenter = null;
        _uiText = null;
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
