using FACM.App.Personalization;
using FACM.App.ViewModels;
using FACM.Core.Observability;
using FACM.Core.Personalization;
using FACM.Core.State;
using FACM.Platform.Windows.Personalization;
using Microsoft.UI.Xaml;

namespace FACM.App;

public partial class App
{
    private IFacmThemeRuntime? _facmThemeRuntime;
    private WindowsPetHostBundleStore? _petHostBundleStore;
    private WindowsVPetRuntime? _desktopPetRuntime;
    private DesktopPetPreferenceService? _desktopPetPreferences;
    private bool _desktopPetCloseHookAttached;
    private bool _desktopPetRuntimeStateHookAttached;

    internal PersonalizationViewModel CreatePersonalizationViewModel(ControlCenterViewModel controlCenter)
    {
        ArgumentNullException.ThrowIfNull(controlCenter);
        var settings = _settings ?? throw new InvalidOperationException("Settings 2.0 owner is unavailable.");

        _facmThemeRuntime ??= new WinUiThemeRuntime(Resources);
        var viewModel = controlCenter.CreatePersonalization(_facmThemeRuntime);
        try
        {
            viewModel.InitializeForStartup();
        }
        catch
        {
            // Personalization is optional during product startup. A platform theme-resource failure must
            // never prevent the launcher or the repair/League/settings surfaces from becoming usable.
            // StartupCrashDiagnostics still records first-chance access-denied evidence when relevant.
        }

        _petHostBundleStore ??= new WindowsPetHostBundleStore(
            FACM.Core.Runtime.RuntimePathLayout.From(new FACM.Platform.Windows.Runtime.WindowsExecutablePathProvider()),
            () => typeof(App).Assembly.GetManifestResourceStream(WindowsPetHostBundleStore.ResourceName));
        _desktopPetRuntime ??= new WindowsVPetRuntime(
            _petHostBundleStore,
            FACM.Core.Runtime.RuntimePathLayout.From(new FACM.Platform.Windows.Runtime.WindowsExecutablePathProvider()).PetHostDataDirectory,
            FACM.Core.Runtime.RuntimePathLayout.From(new FACM.Platform.Windows.Runtime.WindowsExecutablePathProvider()).UiTextPath,
            () => RunOnDesktopUi(ToggleCompactLauncher),
            () => RunOnDesktopUi(ToggleCompactLauncher),
            visible => RunOnDesktopUi(() => _floatingWindow?.SetDesktopEntryVisible(visible)),
            ResetFloatingEntryPositionAsync);
        if (!_desktopPetRuntimeStateHookAttached)
        {
            _desktopPetRuntime.StateChanged += OnDesktopPetRuntimeStateChanged;
            _desktopPetRuntimeStateHookAttached = true;
        }
        _desktopPetPreferences ??= new DesktopPetPreferenceService(settings, _desktopPetRuntime);
        viewModel.ConfigureDesktopPetService(_desktopPetPreferences);
        return viewModel;
    }

    internal async Task InitializeDesktopPetAfterLauncherReadyAsync(PersonalizationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        for (var attempt = 0; attempt < 100 && !_shuttingDown; attempt++)
        {
            var floating = _floatingWindow;
            if (floating is not null)
            {
                AttachDesktopPetCloseHook(floating);
                await viewModel.InitializeDesktopPetAsync().ConfigureAwait(false);
                return;
            }
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    internal void ReportPersonalizationAction(string reason, bool success, string petId, string detail = "")
    {
        QueueDiagnostic(DiagnosticEventFactory.Create(
            "personalization.pet",
            "FACM.Personalization",
            0,
            success ? DiagnosticResult.Success : DiagnosticResult.Failure,
            reason,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
            new Dictionary<string, string>
            {
                ["petId"] = petId ?? string.Empty,
                ["detail"] = detail ?? string.Empty
            }));
    }

    internal void ReportPersonalizationState(PersonalizationViewModel viewModel, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var detail = viewModel.Status ?? string.Empty;
        var failed = detail.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                     detail.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                     detail.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                     detail.Contains("unsupported", StringComparison.OrdinalIgnoreCase);
        QueueDiagnostic(DiagnosticEventFactory.Create(
            "personalization.state",
            "FACM.Personalization",
            0,
            failed ? DiagnosticResult.Failure : DiagnosticResult.Success,
            "viewmodel-state",
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
            new Dictionary<string, string>
            {
                ["property"] = propertyName ?? string.Empty,
                ["busy"] = viewModel.IsBusy ? "true" : "false",
                ["petId"] = viewModel.SelectedPet.Id,
                ["petEnabled"] = viewModel.IsPetEnabled ? "true" : "false",
                ["status"] = detail
            }));
    }

    private void OnDesktopPetRuntimeStateChanged(object? sender, DesktopPetRuntimeState state)
    {
        var detail = state.Detail ?? string.Empty;
        var failed = detail.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                     detail.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                     detail.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                     detail.Contains("unsupported", StringComparison.OrdinalIgnoreCase);
        QueueDiagnostic(DiagnosticEventFactory.Create(
            "personalization.pet-runtime",
            "FACM.Personalization",
            0,
            failed ? DiagnosticResult.Failure : DiagnosticResult.Success,
            "runtime-state",
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
            new Dictionary<string, string>
            {
                ["petId"] = state.ActivePetId ?? string.Empty,
                ["startRequested"] = state.StartRequested ? "true" : "false",
                ["petVisible"] = state.PetVisible ? "true" : "false",
                ["detail"] = detail
            }));
    }

    private void AttachDesktopPetCloseHook(FloatingWindow floating)
    {
        if (_desktopPetCloseHookAttached) return;
        _desktopPetCloseHookAttached = true;
        _ = floating.DispatcherQueue.TryEnqueue(() => floating.Closed += OnDesktopPetFloatingWindowClosed);
    }

    private void OnDesktopPetFloatingWindowClosed(object sender, WindowEventArgs args)
    {
        if (sender is FloatingWindow floating)
            floating.Closed -= OnDesktopPetFloatingWindowClosed;
        _desktopPetCloseHookAttached = false;
        if (_desktopPetRuntime is not null && _desktopPetRuntimeStateHookAttached)
        {
            _desktopPetRuntime.StateChanged -= OnDesktopPetRuntimeStateChanged;
            _desktopPetRuntimeStateHookAttached = false;
        }
        _desktopPetRuntime?.Dispose();
        _desktopPetRuntime = null;
        _desktopPetPreferences = null;
        _petHostBundleStore = null;
    }

    private void RunOnDesktopUi(Action action)
    {
        var floating = _floatingWindow;
        if (floating is null || _shuttingDown) return;
        _ = floating.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_shuttingDown) action();
        });
    }

    private Task ResetFloatingEntryPositionAsync()
    {
        var floating = _floatingWindow;
        if (floating is null || _shuttingDown) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!floating.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _ = floating.ApplyPlacement(null);
                    completion.SetResult();
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }))
        {
            completion.SetResult();
        }
        return completion.Task;
    }
}
