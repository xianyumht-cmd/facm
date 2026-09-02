using FACM.App.ViewModels;
using FACM.Core.Cleanup;
using FACM.Core.Desktop;
using FACM.Core.League;
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
    private readonly bool _morphingSurfaceExperience =
        !string.Equals(
            Environment.GetEnvironmentVariable("FACM_SHELL_EXPERIENCE"),
            "legacy",
            StringComparison.OrdinalIgnoreCase);

    private MainWindow? _window;
    private CompactLauncherWindow? _compactLauncher;
    private FloatingWindow? _floatingWindow;
    private IDesktopWorkAreaProvider? _desktopWorkAreas;
    private WindowsFloatingSurfacePlatform? _floatingSurfacePlatform;
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
    private string _diagnosticLogPath = string.Empty;
    private IFeaturePolicy? _featurePolicy;
    private RecoveryCoordinator? _recovery;
    private bool _shuttingDown;

    public App()
    {
        InitializeComponent();
        AttachLifecycleHandlers();
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
        _diagnosticLogPath = diagnosticLogPath;
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
        QueueLifecycleDiagnostic("startup", "diagnostics-ready");
        var bootstrapCorrelationId = Environment.GetEnvironmentVariable("FACM_BOOTSTRAP_CORRELATION_ID");
        if (!string.IsNullOrWhiteSpace(bootstrapCorrelationId))
        {
            QueueDiagnostic(DiagnosticEventFactory.Create(
                "app.bootstrap-launch",
                "FACM.Bootstrapper",
                0,
                DiagnosticResult.Success,
                "core-process-started",
                _productState.Current.League,
                appVersion,
                new Dictionary<string, string>
                {
                    ["correlationId"] = bootstrapCorrelationId.Trim(),
                    ["rootMode"] = layout.IsModular ? "modular" : "legacy",
                    ["distributionDirectory"] = layout.DistributionDirectory,
                    ["dataRootDirectory"] = layout.DataRootDirectory
                }));
        }
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
        _httpUpdateManifestSource = new HttpUpdateManifestSource(
            manifestUri: layout.IsModular
                ? HttpUpdateManifestSource.ModularProductionManifestUri
                : HttpUpdateManifestSource.ProductionManifestUri);
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
            discovery: new ProcessLockfileLeagueSessionDiscovery(
                new WindowsLeagueProcessSnapshotProvider(WindowsAppLeagueCommandLineReader.TryRead)),
            diagnosticReporter: ReportLeagueSessionDiagnostic);
        _leagueGateway = new LeagueHttpGateway(
            _leagueSessions,
            diagnosticReporter: ReportLeagueHttpDiagnostic,
            gameflowProvider: () => _gameflow?.Current);
        _leagueGameRepairService = new WindowsLeagueGameRepairService(_leagueGateway, _leagueGateway);
        _gameflow = new LeagueGameflowMonitor(
            _leagueGateway,
            _leagueSessions,
            _productState,
            _performance,
            diagnosticReporter: ReportLeagueGameflowDiagnostic);
        InitializeLeagueBenchRuntime();
        _matchmakingAutomation = new LeagueMatchmakingAutomationService(
            _leagueGateway,
            _leagueGateway,
            _gameflow,
            ReportLeagueAutomationDiagnostic);
        ConfigureLeagueAutomationFromSettings();
        InitializeLeaguePostGameAutomationFromSettings();

        _desktopWorkAreas = new WindowsDesktopWorkAreaProvider();
        _floatingSurfacePlatform = new WindowsFloatingSurfacePlatform();
        _controlCenter = new ControlCenterViewModel(
            _settings,
            _updateManifestSource,
            _productState,
            ResolveCurrentVersion(layout, appVersion));
        _gameflow.Start();

        // The default candidate is one persistent MainWindow surface. The legacy FloatingWindow /
        // CompactLauncher path remains available through FACM_SHELL_EXPERIENCE=legacy for rollback.
        PrepareMainWindow();
        _ = InitializeMaintenanceAsync();

        if (_morphingSurfaceExperience)
        {
            _window?.InitializeMorphingSurface(null);
            _ = ApplyPreferredMorphingPlacementAsync(_settings, _window);
        }
        else
        {
            _floatingWindow = new FloatingWindow(
                _desktopWorkAreas,
                _floatingSurfacePlatform!,
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
        }
        _gameflow.Changed += OnLeagueGameflowChanged;
        ApplyDesktopGameflowStatus(_gameflow.Current);

        _productState.SetApplication(ApplicationProductState.Ready, "desktop-launcher-ready");
        QueueLifecycleDiagnostic("startup", "desktop-launcher-ready");
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
                loaded.Settings.League.AutoAcceptEnabled,
                "startup-settings:" + loaded.Origin);
        }
        catch
        {
            // Automation is optional and fail-soft. Corrupt/unavailable settings must not prevent the
            // shell from starting; RecoveringSettings2Repository remains the owner of settings repair.
            automation.Configure(false, false, "startup-settings-fallback");
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
        _diagnosticsCenter = new DiagnosticsCenterViewModel(
            diagnosticsSource,
            diagnosticsExporter,
            CreateLeagueRuntimeFacts,
            _diagnosticLogPath);
        _window = new MainWindow(controlCenter, cleanupCenter,
            repairTools,
            _leagueWorkbench,
            _diagnosticsCenter,
            text,
            _morphingSurfaceExperience,
            _desktopWorkAreas,
            _floatingSurfacePlatform ?? throw new InvalidOperationException("Desktop surface platform is unavailable."),
            PersistFloatingPlacementAsync,
            ShowTrayContextMenuAtCursor,
            ReportSurfaceTransitionDiagnostic,
            ReportSurfacePresentationFailureDiagnostic,
            _floatingSurfacePlatform!.TryEnableSmallSurfaceWindow,
            CreateLeagueBenchQuickPickService(),
            _leagueBenchRuntime,
            ReportLeagueBenchSurfaceEvaluation,
            CreateLeagueGuideAssetService(),
            leagueGateway);
        _window.ConfigureGameRepair(gameRepair);
        ConfigureMaintenanceWindow(_window);
        _window.Closed += OnMainWindowClosed;
        QueueLifecycleDiagnostic("main-window-created", "constructed");
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
            if (_morphingSurfaceExperience)
            {
                window.ShowMorphingSurface(
                    string.Equals(section, "league", StringComparison.Ordinal)
                        ? FacmSurfaceMode.LeagueSurface
                        : FacmSurfaceMode.FeatureSurface,
                    "tray-or-feature:" + section,
                    true);
            }
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
        if (_morphingSurfaceExperience)
        {
            var mode = _window?.SurfaceMode;
            var target = mode is FacmSurfaceMode.Orb or FacmSurfaceMode.HiddenInGame
                ? FacmSurfaceMode.ControlMatrix
                : FacmSurfaceMode.Orb;
            _window?.ShowMorphingSurface(
                target,
                target == FacmSurfaceMode.Orb ? "desktop-entry-toggle-collapse" : "desktop-entry-left-click",
                true);
            return;
        }
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
            QueueLifecycleDiagnostic("compact-opened", "shown");
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
        QueueLifecycleDiagnostic("compact-closed", "closed");
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (!ReferenceEquals(sender, _window)) return;
        if (_morphingSurfaceExperience && !_shuttingDown)
        {
            _shuttingDown = true;
            QueueLifecycleDiagnostic("shutdown-requested", "surface-window-closed");
            QueueLifecycleDiagnostic("shutdown-start", "surface-window-closed");
            _productState?.SetApplication(ApplicationProductState.ShuttingDown, "surface-window-closed");
            _window = null;
            _leagueWorkbench?.Dispose();
            _leagueWorkbench = null;
            _diagnosticsCenter = null;
            DisposeRuntime();
            Exit();
            return;
        }
        _window = null;
        _leagueWorkbench?.Dispose();
        _leagueWorkbench = null;
        _diagnosticsCenter = null;
        QueueLifecycleDiagnostic("main-window-closed", "closed");
        // Closing the detailed shell does not toggle or terminate the desktop entry. Clicking F later
        // still opens the compact launcher; detailed state-consuming ViewModels can be recreated safely.
    }

    private void OnFloatingWindowClosed(object sender, WindowEventArgs args)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        QueueLifecycleDiagnostic("shutdown-requested", "floating-window-closed");
        QueueLifecycleDiagnostic("shutdown-start", "floating-window-closed");
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

    private void OnLeagueGameflowChanged(object? sender, LeagueGameflowChangedEventArgs args)
    {
        var dispatcher = _mainDispatcher;
        if (dispatcher is null) return;
        if (dispatcher.HasThreadAccess)
        {
            ApplyDesktopGameflowStatus(args.Current);
            return;
        }

        _ = dispatcher.TryEnqueue(() => ApplyDesktopGameflowStatus(args.Current));
    }

    private void ApplyDesktopGameflowStatus(LeagueGameflowSnapshot? snapshot)
    {
        _tray?.SetLeagueConnectionState(snapshot?.ConnectionState ?? LeagueConnectionState.NotRunning);

        if (_morphingSurfaceExperience)
        {
            _window?.ApplyGameflowSurfaceMode(snapshot);
            ApplySurfaceRuntimeStatus(snapshot);
            return;
        }

        var floating = _floatingWindow;
        if (floating is null || _shuttingDown) return;

        if (snapshot is null)
        {
            floating.SetRuntimeStatus("!", "GGman · LCU · 等待连接", problem: true);
            return;
        }

        var problem = snapshot.ConnectionState != LeagueConnectionState.Connected;
        var badge = problem
            ? "!"
            : snapshot.ProductState == LeagueProductState.ReadyCheck
                ? "✓"
                : snapshot.ProductState is LeagueProductState.Matchmaking or LeagueProductState.InGame
                    ? "•"
                    : "·";
        var phase = string.IsNullOrWhiteSpace(snapshot.Phase) ? snapshot.ProductState.ToString() : snapshot.Phase;
        floating.SetRuntimeStatus(
            badge,
            "GGman · LCU " + snapshot.ConnectionState + " · " + phase,
            problem);
    }

    private void ApplySurfaceRuntimeStatus(LeagueGameflowSnapshot? snapshot)
    {
        var window = _window;
        if (window is null || _shuttingDown) return;
        if (snapshot is null)
        {
            window.SetRuntimeStatus("!", "GGman · LCU · 等待连接", problem: true);
            return;
        }

        var problem = snapshot.ConnectionState != LeagueConnectionState.Connected;
        var badge = problem
            ? "!"
            : snapshot.ProductState == LeagueProductState.ReadyCheck
                ? "✓"
                : snapshot.ProductState is LeagueProductState.Matchmaking or LeagueProductState.InGame
                    ? "•"
                    : "·";
        var phase = string.IsNullOrWhiteSpace(snapshot.Phase) ? snapshot.ProductState.ToString() : snapshot.Phase;
        window.SetRuntimeStatus(badge, "GGman · LCU " + snapshot.ConnectionState + " · " + phase, problem);
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

    private async Task ApplyPreferredMorphingPlacementAsync(
        ISettings2Repository? settings,
        MainWindow? surfaceWindow)
    {
        if (settings is null || surfaceWindow is null || !_morphingSurfaceExperience) return;
        try
        {
            var loaded = await settings.LoadAsync();
            if (!ReferenceEquals(_window, surfaceWindow) || _shuttingDown) return;

            var pets = loaded.Settings.Pets;
            DesktopPoint? preferred = pets.BallX == int.MinValue || pets.BallY == int.MinValue
                ? null
                : new DesktopPoint(pets.BallX, pets.BallY);
            surfaceWindow.ApplyMorphingPlacement(preferred);
        }
        catch (Exception exception)
        {
            QueueDiagnostic(CreateDesktopPlacementDiagnostic("preferred-surface-placement-failed", exception));
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

    private void ReportSurfaceTransitionDiagnostic(FacmSurfaceTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var failed = transition.Reason.StartsWith("transition-failed:", StringComparison.Ordinal);
        QueueDiagnostic(DiagnosticEventFactory.Create(
            failed ? "facm.surface.transition-failed" : "facm.surface.transition",
            "FACM.Surface",
            transition.DurationMs,
            failed ? DiagnosticResult.Failure : DiagnosticResult.Success,
            transition.Reason,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
            new Dictionary<string, string>
            {
                ["from"] = transition.From.ToString(),
                ["to"] = transition.To.ToString(),
                ["durationMs"] = transition.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["correlationId"] = transition.CorrelationId,
                ["isUserInitiated"] = transition.IsUserInitiated ? "true" : "false",
                ["phase"] = transition.Phase ?? string.Empty
            }));
    }

    private void ReportSurfacePresentationFailureDiagnostic(FacmSurfacePresentationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        QueueDiagnostic(DiagnosticEventFactory.Create(
            failure.EventName,
            "FACM.Surface",
            0,
            DiagnosticResult.Failure,
            failure.Operation,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
            new Dictionary<string, string>
            {
                ["requestedMode"] = failure.RequestedMode.ToString(),
                ["previousMode"] = failure.PreviousMode.ToString(),
                ["currentMode"] = failure.CurrentMode.ToString(),
                ["reason"] = failure.Reason,
                ["operation"] = failure.Operation,
                ["exceptionType"] = failure.ExceptionType,
                ["exceptionMessage"] = failure.ExceptionMessage,
                ["stackSignature"] = failure.StackSignature,
                ["hResult"] = "0x" + failure.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                ["threadId"] = failure.ThreadId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["hasThreadAccess"] = failure.HasThreadAccess ? "true" : "false",
                ["dispatcherQueueAvailable"] = failure.DispatcherQueueAvailable ? "true" : "false",
                ["windowHandle"] = failure.WindowHandle,
                ["appWindowId"] = failure.AppWindowId,
                ["windowVisible"] = failure.WindowVisible ? "true" : "false",
                ["presenterKind"] = failure.PresenterKind,
                ["bounds"] = failure.Bounds.ToString(),
                ["actualWindowX"] = FormatCoordinate(failure.ActualBounds?.Left),
                ["actualWindowY"] = FormatCoordinate(failure.ActualBounds?.Top),
                ["actualWindowWidth"] = FormatCoordinate(failure.ActualBounds?.Width),
                ["actualWindowHeight"] = FormatCoordinate(failure.ActualBounds?.Height),
                ["targetX"] = FormatCoordinate(failure.TargetBounds?.Left),
                ["targetY"] = FormatCoordinate(failure.TargetBounds?.Top),
                ["targetWidth"] = FormatCoordinate(failure.TargetBounds?.Width),
                ["targetHeight"] = FormatCoordinate(failure.TargetBounds?.Height),
                ["orbSurfaceVisibility"] = failure.OrbVisibility,
                ["transientRailVisibility"] = failure.TransientRailVisibility,
                ["compactChromeVisibility"] = failure.CompactChromeVisibility,
                ["featureContentVisibility"] = failure.FeatureContentVisibility,
                ["currentPresentationGeneration"] = failure.CurrentPresentationGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["requestedPresentationGeneration"] = failure.RequestedPresentationGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["correlationId"] = failure.CorrelationId,
                ["phase"] = failure.Phase ?? string.Empty,
                ["isUserInitiated"] = failure.IsUserInitiated ? "true" : "false"
            }));
    }

    private static string FormatCoordinate(double? value) =>
        value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

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
        if (_gameflow is not null)
            _gameflow.Changed -= OnLeagueGameflowChanged;
        _leagueBenchRuntime?.Dispose();
        _leagueBenchRuntime = null;
        if (_leagueBenchQuickPick is IDisposable leagueBenchQuickPick)
            leagueBenchQuickPick.Dispose();
        _leagueBenchQuickPick = null;
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
        QueueLifecycleDiagnostic("shutdown-complete", "runtime-disposed");
        _diagnostics?.Dispose();
        _diagnostics = null;
        _diagnosticsSource = null;
        _diagnosticsExporter = null;
        _diagnosticLogPath = string.Empty;
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
        _ = WriteDiagnosticAsync(sink, diagnosticEvent);
    }

    private static async Task WriteDiagnosticAsync(
        BoundedJsonLinesDiagnosticSink sink,
        DiagnosticEvent diagnosticEvent)
    {
        try
        {
            await sink.WriteAsync(diagnosticEvent).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics are best-effort and may never make the product fail to launch.
        }
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
