using FACM.Core.Desktop;
using FACM.Core.Observability;
using FACM.Core.State;

namespace FACM.App;

public partial class App
{
    private WindowsTrayHost? _tray;
    private string _lastTrayNotificationKey = string.Empty;

    private void InitializeTrayHost()
    {
        if (_tray is not null) return;
        var text = _uiText ?? throw new InvalidOperationException("UI text provider is unavailable.");
        _tray = new WindowsTrayHost(text, new Dictionary<TrayCommand, Action>
        {
            [TrayCommand.OpenCompactLauncher] = OpenOrActivateCompactLauncher,
            [TrayCommand.OpenCleanup] = () => OpenMainWindowSection("repair"),
            [TrayCommand.OpenLeague] = () => OpenMainWindowSection("league"),
            [TrayCommand.OpenPersonalization] = () => OpenMainWindowSection("personalization"),
            [TrayCommand.OpenDesktopPetSettings] = () => OpenMainWindowSection("personalization"),
            [TrayCommand.RestoreDefaultLauncher] = () => _ = RunTrayDesktopPetActionAsync(restoreLauncher: true),
            [TrayCommand.ResetDesktopPosition] = () => _ = RunTrayDesktopPetActionAsync(restoreLauncher: false),
            [TrayCommand.CheckForUpdates] = () => _ = CheckForUpdatesFromTrayAsync(),
            [TrayCommand.OpenLog] = () => _ = OpenLogFromTrayAsync(),
            [TrayCommand.Exit] = RequestApplicationShutdown
        });
    }

    private void OpenOrActivateCompactLauncher()
    {
        if (_shuttingDown) return;
        if (_compactLauncher is not null)
        {
            try { _compactLauncher.Activate(); } catch { }
            QueueLauncherDiagnostic("compact-activated");
            return;
        }

        ToggleCompactLauncher();
    }

    private void ShowTrayContextMenuAtCursor()
    {
        if (_shuttingDown) return;
        try { _tray?.ShowContextMenuAtCursor(); } catch { }
    }

    private async Task RunTrayDesktopPetActionAsync(bool restoreLauncher)
    {
        if (_shuttingDown) return;
        var service = _desktopPetPreferences;
        if (service is null)
        {
            OpenMainWindowSection("personalization");
            return;
        }

        try
        {
            if (restoreLauncher)
                _ = await service.RestoreDefaultLauncherAsync().ConfigureAwait(false);
            else
                await service.ResetPositionAsync().ConfigureAwait(false);
            _ = _window?.DispatcherQueue.TryEnqueue(() => _window.RefreshPersonalizationSurfaceFromRuntime());
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            QueueDiagnostic(DiagnosticEventFactory.Create(
                "tray.personalization",
                "FACM.Tray",
                0,
                DiagnosticResult.Failure,
                exception.GetType().Name,
                _productState?.Current.League ?? LeagueProductState.NotRunning,
                typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown"));
        }
    }

    private async Task CheckForUpdatesFromTrayAsync()
    {
        if (_shuttingDown) return;
        OpenMainWindowSection("settings");
        var maintenance = _maintenanceCenter;
        if (maintenance is null) return;
        try
        {
            if (!maintenance.IsInitialized) await maintenance.InitializeAsync().ConfigureAwait(false);
            var decision = await maintenance.ManualCheckAsync().ConfigureAwait(false);
            if (decision.UpdateAvailable)
                ShowTrayNotificationOnce("update:" + decision.LatestVersion, "GGman", "发现可用更新：" + decision.LatestVersion);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private async Task OpenLogFromTrayAsync()
    {
        if (_shuttingDown) return;
        OpenMainWindowSection("settings");
        _window?.OpenStructuredLogSurface();
        await Task.CompletedTask;
    }

    private void ShowTrayNotificationOnce(string key, string title, string message)
    {
        if (_shuttingDown || string.Equals(_lastTrayNotificationKey, key, StringComparison.Ordinal)) return;
        _lastTrayNotificationKey = key;
        _tray?.ShowBalloonTip(title, message);
    }

    private void DisposeTrayHost()
    {
        _tray?.Dispose();
        _tray = null;
        _lastTrayNotificationKey = string.Empty;
    }
}
