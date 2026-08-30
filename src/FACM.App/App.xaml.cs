using FACM.App.ViewModels;
using FACM.Core.Cleanup;
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
using FACM.Platform.Windows.Cleanup;
using FACM.Platform.Windows.Desktop;
using FACM.Platform.Windows.League;
using FACM.Platform.Windows.Repair;
using FACM.Platform.Windows.Runtime;
using Microsoft.UI.Xaml;

namespace FACM.App;

public partial class App : Application
{
    private readonly SemaphoreSlim _floatingPlacementSaveGate = new(1, 1);

    private MainWindow? _window;
    private CompactLauncherWindow? _compactLauncher;
    private FloatingWindow? _floatingWindow;
    private IDesktopWorkAreaProvider? _desktopWorkAreas;
    private ControlCenterViewModel? _controlCenter;
    private CleanupViewModel? _cleanupCenter;
    private WindowsCleanupEnvironment? _cleanupEnvironment;
    private LeagueWorkbenchViewModel? _leagueWorkbench;
    private WindowsLeagueGameRepairService? _leagueGameRepairService;
    private DiagnosticsCenterViewModel? _diagnosticsCenter;
    private IUiTextProvider? _uiText;
    private ISettings2Repository? _settings;
    private HttpUpdateManifestSource? _httpUpdateManifestSource;
    private IUpdateManifestSource? _updateManifestSource;
    private LeagueHttpGateway? _leagueGateway;
    private WindowsLeagueTransportSessionSource? _leagueSessions;
    private LeagueGameflowMonitor? _gameflow;
    private LeagueMatchmakingAutomationService? _matchmakingAutomation;
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
        if (!TryEnterSingleInstance(executablePaths)) return;
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
        InitializeTrayHost();
        _httpUpdateManifestSource = new HttpUpdateManifestSource();
        _updateManifestSource = new FeatureGatedUpdateManifestSource(_httpUpdateManifestSource, _featurePolicy);
        ComposeMaintenance(layout, appVersion);

        _cleanupEnvironment = new WindowsCleanupEnvironment();
        var cleanupEngine = new WindowsCleanupEngine(_cleanupEnvironment);
        var cleanupExecutor = new FeatureGatedCleanupExecutor(cleanupEngine, _featurePolicy);
        var cleanupService = new CleanupApplicationService(cleanupEngine, cleanupExecutor);
        _cleanupCenter = new CleanupViewModel(_settings, cleanupService, _cleanupEnvironment);

        // Exactly one League discovery/auth/session owner and one Gameflow loop for the 4.0 process.
        // Read/write transport, Product State, performance, repair, automation and the Workbench all
        // consume the same facts and gateway.
        _leagueSessions = new WindowsLeagueTransportSessionSource(
            diagnosticReporter: ReportLeagueSessionDiagnostic);
        _leagueGateway = new LeagueHttpGateway(
            _leagueSessions,
            diagnosticReporter: ReportLeagueHttpDiagnostic);
        _leagueGameRepairService = new WindowsLeagueGameRepairService(_leagueGateway, _leagueGateway);
        _gameflow = new LeagueGameflowMonitor(
            _leagueGateway,
            _leagueSessions,
            _productState,
            _performance,
            diagnosticReporter: ReportLeagueGameflowDiagnostic);
        _matchmakingAutomation = new LeagueMatchmakingAutomationService(
            _leagueGateway,
            _leagueGateway,
            _gameflow);
        ConfigureLeagueAutomationFromSettings();

        _controlCenter = new ControlCenterViewModel(_settings, _updateManifestSource, _productState);
        _gameflow.Start();

        // Keep MainWindow XAML constructed during startup so Win10 resource regressions still fail fast,
        // but do not activate the large shell. FACM 3.5's proven default UX is launcher-first.
        PrepareMainWindow();
        _ = InitializeMaintenanceAsync();

        _desktopWorkAreas = new WindowsDesktopWorkAreaProvider();
        var floatingPlatform = new WindowsFloatingSurfacePlatform();
        _floatingWindow = new FloatingWindow(
            _desktopWorkAreas,
            floatingPlatform,
            _uiText,
            ToggleCompactLauncher,
            ShowTrayContextMenuAtCursor,
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

        _productState.SetApplication(ApplicationProductState.Ready, "desktop-launcher-ready");
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
            "desktop-launcher-ready",
            _productState.Current.League,
            appVersion));

        if (Environment.GetCommandLineArgs().Any(argument =>
                string.Equals(argument, "--cleanup", StringComparison.OrdinalIgnoreCase)))
        {
            OpenMainWindowSection("repair");
            QueueCleanupDiagnostic("elevated-cleanup-entry");
        }
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

    private void ConfigureLeagueAutomationFromSettings()
    {
        var settings = _settings;
        var automation = _matchmakingAutomation;
        if (settings is null || automation is null) return;

        try
        {
            var loaded = settings.LoadAsync().GetAwaiter().GetResult();
            automation.Configure(
                loaded.Settings.League.AutoMatchmakingEnabled,
                loaded.Settings.League.AutoAcceptEnabled);
        }
        catch
        {
            // Automation is optional and fail-soft. Corrupt/unavailable settings must not prevent the
            // shell from starting; RecoveringSettings2Repository remains the owner of settings repair.
            automation.Configure(false, false);
        }
    }

    private MainWindow GetOrCreateMainWindow()
    {
        if (_shuttingDown) throw new InvalidOperationException("FACM is shutting down.");
        if (_window is not null) return _window;

        var controlCenter = _controlCenter ?? throw new InvalidOperationException("Control center is unavailable.");
        var cleanupCenter = _cleanupCenter ?? throw new InvalidOperationException("Cleanup center is unavailable.");
        var productState = _productState ?? throw new InvalidOperationException("Product State is unavailable.");
        var performance = _performance ?? throw new InvalidOperationException("Performance budget provider is unavailable.");
        var leagueGateway = _leagueGateway ?? throw new InvalidOperationException("League read gateway is unavailable.");
        var gameflow = _gameflow ?? throw new InvalidOperationException("League gameflow owner is unavailable.");
        var gameRepairService = _leagueGameRepairService ?? throw new InvalidOperationException("League game repair is unavailable.");
        var diagnosticsSource = _diagnosticsSource ?? throw new InvalidOperationException("Diagnostics source is unavailable.");
        var diagnosticsExporter = _diagnosticsExporter ?? throw new InvalidOperationException("Diagnostics exporter is unavailable.");
        var text = _uiText ?? throw new InvalidOperationException("UI text provider is unavailable.");
        var repairTools = new RepairToolsViewModel(new WindowsRepairToolService());
        var gameRepair = new LeagueGameRepairViewModel(gameRepairService);
        var workbenchData = new LeagueWorkbenchDataSource(leagueGateway, gameflow);
        _leagueWorkbench = new LeagueWorkbenchViewModel(
            productState,
            performance,
            workbenchData,
            ReportLeagueWorkbenchDiagnostic);
        _diagnosticsCenter = new DiagnosticsCenterViewModel(diagnosticsSource, diagnosticsExporter);
        _window = new MainWindow(controlCenter, cleanupCenter, repairTools, _leagueWorkbench, _diagnosticsCenter, text);
        _window.ConfigureGameRepair(gameRepair);
        ConfigureMaintenanceWindow(_window);
        _window.Closed += OnMainWindowClosed;
        return _window;
    }

    private void PrepareMainWindow() => _ = GetOrCreateMainWindow();

    private void EnsureMainWindow() => OpenMainWindowSection("repair");

    private void OpenMainWindowSection(string section)
    {
        if (_shuttingDown) return;
        try
        {
            var window = GetOrCreateMainWindow();
            window.NavigateToSection(section);
            window.Activate();
            QueueLauncherDiagnostic("main-shell-opened");
        }
        catch (Exception exception)
        {
            QueueLauncherDiagnostic("main-shell-open-failed:" + exception.GetType().Name, DiagnosticResult.Failure);
        }
    }

    private void ToggleCompactLauncher()
    {
        if (_shuttingDown) return;
        if (_compactLauncher is not null)
        {
            _compactLauncher.Close();
            return;
        }

        var floating = _floatingWindow;
        var workAreas = _desktopWorkAreas;
        var text = _uiText;
        if (floating is null || workAreas is null || text is null) return;

        CompactLauncherWindow? launcher = null;
        try
        {
            launcher = new CompactLauncherWindow(workAreas, text, OpenMainWindowSection);
            _compactLauncher = launcher;
            launcher.Closed += OnCompactLauncherClosed;
            launcher.ShowNextTo(floating.GetCurrentBounds());
            QueueLauncherDiagnostic("compact-opened");
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_compactLauncher, launcher)) _compactLauncher = null;
            try { launcher?.Close(); } catch { }
            QueueLauncherDiagnostic("compact-open-failed:" + exception.GetType().Name, DiagnosticResult.Failure);
        }
    }

    private void OnCompactLauncherClosed(object sender, WindowEventArgs args)
    {
        if (!ReferenceEquals(sender, _compactLauncher)) return;
        _compactLauncher = null;
        QueueLauncherDiagnostic("compact-closed");
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (!ReferenceEquals(sender, _window)) return;
        _window = null;
        _leagueWorkbench?.Dispose();
        _leagueWorkbench = null;
        _diagnosticsCenter = null;
        // Closing the detailed shell does not toggle or terminate the desktop entry. Clicking F later
        // still opens the compact launcher; detailed state-consuming ViewModels can be recreated safely.
    }

    private void OnFloatingWindowClosed(object sender, WindowEventArgs args)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _productState?.SetApplication(ApplicationProductState.ShuttingDown, "floating-window-closed");
        _floatingWindow = null;

        if (_compactLauncher is not null)
        {
            _compactLauncher.Close();
            _compactLauncher = null;
        }

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
            var x = ToPersistedCoordinate(topLeft.X);
            var y = ToPersistedCoordinate(topLeft.Y);
            var updated = await settings.UpdateAsync(
                document =>
                {
                    document.Pets.BallX = x;
                    document.Pets.BallY = y;
                },
                allowRecoveryRebuild: false);
            if (!updated.Persisted)
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

    private void QueueLauncherDiagnostic(string reason, DiagnosticResult result = DiagnosticResult.Success)
    {
        QueueDiagnostic(DiagnosticEventFactory.Create(
            "desktop.launcher",
            "FACM.Desktop",
            0,
            result,
            reason,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown"));
    }

    private void QueueCleanupDiagnostic(string reason, DiagnosticResult result = DiagnosticResult.Success)
    {
        QueueDiagnostic(DiagnosticEventFactory.Create(
            "cleanup.flow",
            "FACM.Cleanup",
            0,
            result,
            reason,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown"));
    }

    private void DisposeRuntime()
    {
        DisposeMaintenanceRuntime();
        DisposeTrayHost();
        _matchmakingAutomation?.Dispose();
        _matchmakingAutomation = null;
        _gameflow?.Dispose();
        _gameflow = null;
        _leagueWorkbench?.Dispose();
        _leagueWorkbench = null;
        _leagueGameRepairService?.Dispose();
        _leagueGameRepairService = null;
        _diagnosticsCenter = null;
        _cleanupCenter = null;
        _cleanupEnvironment = null;
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
        _desktopWorkAreas = null;
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
