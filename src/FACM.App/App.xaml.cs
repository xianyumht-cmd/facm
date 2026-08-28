using FACM.App.ViewModels;
using FACM.Core.Desktop;
using FACM.Core.Observability;
using FACM.Core.Online;
using FACM.Core.Performance;
using FACM.Core.Recovery;
using FACM.Core.Runtime;
using FACM.Core.Settings;
using FACM.Core.State;
using FACM.Core.Text;
using FACM.Infrastructure.League;
using FACM.Infrastructure.Observability;
using FACM.Infrastructure.Online;
using FACM.Infrastructure.Recovery;
using FACM.Infrastructure.Settings;
using FACM.Infrastructure.Text;
using FACM.Infrastructure.Time;
using FACM.Platform.Windows.Desktop;
using FACM.Platform.Windows.League;
using FACM.Platform.Windows.Runtime;
using Microsoft.UI.Xaml;

namespace FACM.App;

public partial class App : Application
{
    private readonly SemaphoreSlim _floatingPlacementSaveGate = new(1, 1);

    private MainWindow? _window;
    private FloatingWindow? _floatingWindow;
    private ControlCenterViewModel? _controlCenter;
    private LeagueWorkbenchViewModel? _leagueWorkbench;
    private DiagnosticsCenterViewModel? _diagnosticsCenter;
    private IUiTextProvider? _uiText;
    private ISettings2Repository? _settings;
    private HttpUpdateManifestSource? _httpUpdateManifestSource;
    private IUpdateManifestSource? _updateManifestSource;
    private LeagueHttpGateway? _leagueGateway;
    private WindowsLeagueTransportSessionSource? _leagueSessions;
    private LeagueGameflowMonitor? _gameflow;
    private PerformanceBudgetProvider? _performance;
    private ProductStateStore? _productState;
    private BoundedJsonLinesDiagnosticSink? _diagnostics;
    private IDiagnosticsSnapshotSource? _diagnosticsSource;
    private IDiagnosticsBundleExporter? _diagnosticsExporter;
    private IFeaturePolicy? _featurePolicy;
    private RecoveryCoordinator? _recovery;
    private bool _shuttingDown;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var executablePaths = new WindowsExecutablePathProvider();
        var layout = RuntimePathLayout.From(executablePaths);
        var appVersion = typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
        var clock = new SystemClock();
        _recovery = TryBeginRecovery(layout, appVersion, clock);

        try
        {
            LaunchCore(layout, appVersion);
            TryMarkRecoveryRunning();
        }
        catch (Exception exception)
        {
            TryMarkRecoveryFailed("launch-" + exception.GetType().Name);
            throw;
        }
    }

    private void LaunchCore(RuntimePathLayout layout, string appVersion)
    {
        var diagnosticLogPath = Path.Combine(layout.LogsDirectory, "facm4-events.jsonl");
        var diagnosticsPolicy = DiagnosticsExportPolicy.Default;
        var killSwitchLoad = new FeatureKillSwitchFileSource(layout.FeatureKillSwitchPath)
            .LoadAsync()
            .GetAwaiter()
            .GetResult();
        _featurePolicy = FeaturePolicyEvaluator.Evaluate(
            FeatureBaseline.GetApprovedCapabilities(),
            killSwitchLoad.KillSwitch);

        _productState = new ProductStateStore();
        _productState.SetEnvironment(
            new ProductEnvironmentState(layout.DistributionDirectory, null, null),
            "runtime-layout-ready");
        _performance = new PerformanceBudgetProvider();
        _diagnostics = new BoundedJsonLinesDiagnosticSink(diagnosticLogPath);
        _diagnosticsSource = new FileDiagnosticsSnapshotSource(
            _productState,
            diagnosticLogPath,
            appVersion,
            diagnosticsPolicy);
        var rawDiagnosticsExporter = new DiagnosticsBundleExporter(
            Path.Combine(layout.RuntimeDirectory, "diagnostics"),
            diagnosticsPolicy);
        _diagnosticsExporter = new FeatureGatedDiagnosticsBundleExporter(rawDiagnosticsExporter, _featurePolicy);

        var strictSettings = new Settings2Repository(layout.Settings2Path, layout.SettingsPath);
        var settingsRecovery = new JsonSettings2RecoveryStore(layout.Settings2LastKnownGoodPath);
        _settings = new RecoveringSettings2Repository(strictSettings, settingsRecovery);
        _uiText = new FileUiTextProvider(layout.UiTextPath);
        _httpUpdateManifestSource = new HttpUpdateManifestSource();
        _updateManifestSource = new FeatureGatedUpdateManifestSource(_httpUpdateManifestSource, _featurePolicy);

        // Exactly one League discovery/auth/session owner and one Gameflow loop for the 4.0 process.
        // Read/write transport, Product State, performance and the Workbench all consume the same facts.
        _leagueSessions = new WindowsLeagueTransportSessionSource();
        _leagueGateway = new LeagueHttpGateway(_leagueSessions);
        _gameflow = new LeagueGameflowMonitor(
            _leagueGateway,
            _leagueSessions,
            _productState,
            _performance);

        _controlCenter = new ControlCenterViewModel(_settings, _updateManifestSource, _productState);
        _gameflow.Start();
        EnsureMainWindow();

        var workAreas = new WindowsDesktopWorkAreaProvider();
        var floatingPlatform = new WindowsFloatingSurfacePlatform();
        _floatingWindow = new FloatingWindow(
            workAreas,
            floatingPlatform,
            _uiText,
            EnsureMainWindow,
            PersistFloatingPlacementAsync);
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
            "feature.policy",
            "FACM.Recovery",
            0,
            killSwitchLoad.Origin == FeatureKillSwitchLoadOrigin.FailClosed
                ? DiagnosticResult.Failure
                : DiagnosticResult.Success,
            killSwitchLoad.Reason,
            _productState.Current.League,
            appVersion,
            new Dictionary<string, string>
            {
                ["enabledCount"] = _featurePolicy.EnabledCapabilitiesCount().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["origin"] = killSwitchLoad.Origin.ToString()
            }));
        QueueDiagnostic(DiagnosticEventFactory.Create(
            "app.launch",
            "FACM.App",
            0,
            DiagnosticResult.Success,
            "main-window-activated",
            _productState.Current.League,
            appVersion));
    }

    private static RecoveryCoordinator? TryBeginRecovery(
        RuntimePathLayout layout,
        string appVersion,
        SystemClock clock)
    {
        try
        {
            var coordinator = new RecoveryCoordinator(
                new JsonRecoveryStateStore(layout.RecoveryStatePath, clock),
                clock);
            _ = coordinator.BeginStartAsync(appVersion).GetAwaiter().GetResult();
            return coordinator;
        }
        catch
        {
            // Recovery metadata is defense-in-depth and must never prevent the stable app from starting.
            return null;
        }
    }

    private void TryMarkRecoveryRunning()
    {
        var recovery = _recovery;
        if (recovery is null) return;
        try
        {
            _ = recovery.MarkRunningAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // A recovery metadata write failure must not turn a successful product launch into a crash.
        }
    }

    private void TryMarkRecoveryFailed(string reason)
    {
        var recovery = _recovery;
        if (recovery is null) return;
        try
        {
            _ = recovery.MarkFailedAsync(reason).GetAwaiter().GetResult();
        }
        catch
        {
            // Preserve the original launch failure; recovery logging may never mask it.
        }
    }

    private void EnsureMainWindow()
    {
        if (_shuttingDown) return;
        if (_window is null)
        {
            var controlCenter = _controlCenter ?? throw new InvalidOperationException("Control center is unavailable.");
            var productState = _productState ?? throw new InvalidOperationException("Product State is unavailable.");
            var performance = _performance ?? throw new InvalidOperationException("Performance budget provider is unavailable.");
            var diagnosticsSource = _diagnosticsSource ?? throw new InvalidOperationException("Diagnostics source is unavailable.");
            var diagnosticsExporter = _diagnosticsExporter ?? throw new InvalidOperationException("Diagnostics exporter is unavailable.");
            var text = _uiText ?? throw new InvalidOperationException("UI text provider is unavailable.");
            _leagueWorkbench = new LeagueWorkbenchViewModel(productState, performance);
            _diagnosticsCenter = new DiagnosticsCenterViewModel(diagnosticsSource, diagnosticsExporter);
            _window = new MainWindow(controlCenter, _leagueWorkbench, _diagnosticsCenter, text);
            _window.Closed += OnMainWindowClosed;
        }
        _window.Activate();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (!ReferenceEquals(sender, _window)) return;
        _window = null;
        _leagueWorkbench?.Dispose();
        _leagueWorkbench = null;
        _diagnosticsCenter = null;
        // Closing the main shell does not toggle or terminate the desktop entry. Clicking F later
        // recreates only state-consuming ViewModels/window; process-wide runtime owners remain shared.
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

    private async Task PersistFloatingPlacementAsync(DesktopPoint topLeft)
    {
        var settings = _settings;
        if (settings is null || _shuttingDown || !topLeft.IsFinite) return;

        await _floatingPlacementSaveGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(settings, _settings) || _shuttingDown) return;
            var loaded = await settings.LoadAsync();
            if (loaded.Origin is SettingsLoadOrigin.RecoveredLastKnownGood or SettingsLoadOrigin.RecoveryDefaults)
            {
                QueueDiagnostic(DiagnosticEventFactory.Create(
                    "desktop.place",
                    "FACM.Desktop",
                    0,
                    DiagnosticResult.Success,
                    "drag-position-not-persisted-recovery",
                    _productState?.Current.League ?? LeagueProductState.NotRunning,
                    typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown"));
                return;
            }

            loaded.Settings.Pets.BallX = ToPersistedCoordinate(topLeft.X);
            loaded.Settings.Pets.BallY = ToPersistedCoordinate(topLeft.Y);
            await settings.SaveAsync(loaded.Settings);
            QueueDiagnostic(DiagnosticEventFactory.Create(
                "desktop.place",
                "FACM.Desktop",
                0,
                DiagnosticResult.Success,
                "drag-position-saved",
                _productState?.Current.League ?? LeagueProductState.NotRunning,
                typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown"));
        }
        catch (Exception exception)
        {
            QueueDiagnostic(CreateDesktopPlacementDiagnostic("drag-position-save-failed", exception));
        }
        finally
        {
            _floatingPlacementSaveGate.Release();
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
        _gameflow?.Dispose();
        _gameflow = null;
        _leagueWorkbench?.Dispose();
        _leagueWorkbench = null;
        _diagnosticsCenter = null;
        _leagueGateway?.Dispose();
        _leagueGateway = null;
        _leagueSessions = null;
        _performance = null;
        _httpUpdateManifestSource?.Dispose();
        _httpUpdateManifestSource = null;
        _updateManifestSource = null;
        _diagnostics?.Dispose();
        _diagnostics = null;
        _diagnosticsSource = null;
        _diagnosticsExporter = null;
        _settings = null;
        _controlCenter = null;
        _uiText = null;
        _featurePolicy = null;
        _recovery = null;
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

    private static int ToPersistedCoordinate(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(rounded, int.MinValue, int.MaxValue);
    }
}

internal static class FeaturePolicyExtensions
{
    public static int EnabledCapabilitiesCount(this IFeaturePolicy policy) =>
        FeatureBaseline.GetApprovedCapabilities().Count(policy.IsEnabled);
}
