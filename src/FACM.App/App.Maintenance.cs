using FACM.App.ViewModels;
using FACM.Core.Maintenance;
using FACM.Core.Runtime;
using FACM.Infrastructure.Online;
using FACM.Platform.Windows.Runtime;
using Microsoft.UI.Dispatching;

namespace FACM.App;

public partial class App
{
    private WindowsSingleInstanceGate? _singleInstanceGate;
    private DispatcherQueue? _mainDispatcher;
    private MaintenanceViewModel? _maintenanceCenter;
    private HttpAnnouncementSource? _httpAnnouncementSource;

    private bool TryEnterSingleInstance(IExecutablePathProvider executablePaths)
    {
        ArgumentNullException.ThrowIfNull(executablePaths);
        _mainDispatcher = DispatcherQueue.GetForCurrentThread();
        var cleanupLaunch = Environment.GetCommandLineArgs().Any(argument =>
            string.Equals(argument, "--cleanup", StringComparison.OrdinalIgnoreCase));
        if (cleanupLaunch) return true;

        var gate = new WindowsSingleInstanceGate();
        var disposition = gate.EnterNormal(
            OnExternalActivationRequested,
            WindowsSingleInstanceGate.DefaultSignalTimeout);
        if (disposition == SingleInstanceDisposition.Primary)
        {
            _singleInstanceGate = gate;
            return true;
        }

        gate.Dispose();
        // ExistingSignaled and ExistingUnresponsive both fail closed: a second process never creates
        // settings, League, desktop or diagnostics owners and never takes over/kills the first process.
        Exit();
        return false;
    }

    private void ComposeMaintenance(RuntimePathLayout layout, string appVersion)
    {
        var settings = _settings ?? throw new InvalidOperationException("Settings owner is unavailable.");
        var updateSource = _updateManifestSource ?? throw new InvalidOperationException("Update manifest source is unavailable.");
        var currentVersion = Version.TryParse(appVersion, out var parsed) ? parsed : new Version(4, 0, 0);
        _httpAnnouncementSource = new HttpAnnouncementSource();
        var service = new FACM.Core.Online.MaintenanceApplicationService(
            settings,
            updateSource,
            _httpAnnouncementSource,
            currentVersion);
        var executablePaths = new WindowsExecutablePathProvider();
        var launcher = new WindowsUpdateReplacementLauncher(layout, executablePaths);
        var identityVerifier = new WindowsUpdatePackageIdentityVerifier(executablePaths);
        var installer = new HttpPreparedUpdateInstaller(layout, launcher, identityVerifier);
        var logOpener = new WindowsLogFileOpener(layout);
        _maintenanceCenter = new MaintenanceViewModel(service, installer, logOpener);
    }

    private async Task InitializeMaintenanceAsync()
    {
        var maintenance = _maintenanceCenter;
        if (maintenance is null || _shuttingDown) return;
        try
        {
            await maintenance.InitializeAsync();
            await maintenance.RefreshAnnouncementAsync();
            if (maintenance.IsAnnouncementNew && maintenance.Announcement is { } announcement)
            {
                ShowTrayNotificationOnce(
                    "announcement:" + announcement.Id,
                    announcement.Title,
                    announcement.Body);
                await maintenance.MarkAnnouncementSeenAsync();
            }
            if (maintenance.AutoUpdateEnabled)
            {
                await maintenance.ManualCheckAsync();
                if (maintenance.UpdateAvailable && maintenance.LatestVersion.Length > 0)
                    ShowTrayNotificationOnce(
                        "update:" + maintenance.LatestVersion,
                        "FACM",
                        "发现可用更新：" + maintenance.LatestVersion);
                if (maintenance.ForceUpdateRequired && !_shuttingDown)
                    OpenMainWindowSection("settings");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Startup maintenance is fail-soft. Manual check remains available in More Settings.
        }
    }

    private void ConfigureMaintenanceWindow(MainWindow window)
    {
        var maintenance = _maintenanceCenter;
        if (maintenance is null) return;
        window.ConfigureMaintenance(maintenance, RequestApplicationShutdown);
    }

    private void OnExternalActivationRequested()
    {
        var dispatcher = _mainDispatcher;
        if (dispatcher is null || _shuttingDown) return;
        _ = dispatcher.TryEnqueue(() =>
        {
            if (_shuttingDown) return;
            OpenOrActivateCompactLauncher();
        });
    }

    private void RequestApplicationShutdown()
    {
        if (_shuttingDown) return;
        QueueLifecycleDiagnostic("shutdown-requested", "tray-or-maintenance");
        var floating = _floatingWindow;
        if (floating is not null)
        {
            floating.Close();
            return;
        }

        _shuttingDown = true;
        QueueLifecycleDiagnostic("shutdown-start", "tray-or-maintenance");
        try { _compactLauncher?.Close(); } catch { }
        try { _window?.Close(); } catch { }
        DisposeRuntime();
        Exit();
    }

    private void DisposeMaintenanceRuntime()
    {
        // App.DisposeRuntime enters through this hook before shared League/gameflow/gateway teardown.
        // Explicitly stop process-scoped product features and PetHost first instead of relying on
        // ProcessExit or a WinUI Closed callback whose ordering can vary during shutdown.
        DisposePersonalizationRuntime();
        DisposeLeagueProductizationRuntime();

        _maintenanceCenter?.Dispose();
        _maintenanceCenter = null;
        _httpAnnouncementSource?.Dispose();
        _httpAnnouncementSource = null;
        _singleInstanceGate?.Dispose();
        _singleInstanceGate = null;
        _mainDispatcher = null;
    }
}
