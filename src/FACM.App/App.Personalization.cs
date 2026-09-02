using FACM.App.Personalization;
using FACM.App.ViewModels;
using FACM.Core.Desktop;
using FACM.Core.Observability;
using FACM.Core.Personalization;
using FACM.Core.Runtime;
using FACM.Core.State;
using FACM.Platform.Windows.Personalization;
using FACM.Platform.Windows.Runtime;
using Microsoft.UI.Xaml;

namespace FACM.App;

public partial class App
{
    private IFacmThemeRuntime? _facmThemeRuntime;
    private WindowsPetHostBundleStore? _petHostBundleStore;
    private WindowsFlyingHostBundleStore? _flyingHostBundleStore;
    private WindowsVPetRuntime? _vpetRuntime;
    private WindowsFlyingPetRuntime? _flyingPetRuntime;
    private WindowsDesktopPetRuntimeRouter? _desktopPetRuntime;
    private DesktopPetPreferenceService? _desktopPetPreferences;
    private FloatingWindow? _desktopPetCloseHookTarget;
    private bool _desktopPetCloseHookAttached;
    private bool _desktopPetRuntimeStateHookAttached;
    private IComponentAvailability? _componentAvailability;

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

        var layout = FACM.Core.Runtime.RuntimePathLayout.From(new FACM.Platform.Windows.Runtime.WindowsExecutablePathProvider());
        _componentAvailability ??= new WindowsComponentAvailability(
            new Dictionary<string, Func<bool>>(StringComparer.OrdinalIgnoreCase)
            {
                [FacmComponentIds.PetHostWinX64] = () => typeof(App).Assembly.GetManifestResourceInfo(WindowsPetHostBundleStore.ResourceName) is not null,
                [FacmComponentIds.FlyingHostWinX64] = () => typeof(App).Assembly.GetManifestResourceInfo(WindowsFlyingHostBundleStore.ResourceName) is not null
            });
        _petHostBundleStore ??= new WindowsPetHostBundleStore(
            layout,
            () => typeof(App).Assembly.GetManifestResourceStream(WindowsPetHostBundleStore.ResourceName),
            ReadPetHostBundleSha256(),
            ReportPetHostBundleStage);
        _flyingHostBundleStore ??= new WindowsFlyingHostBundleStore(
            layout,
            () => typeof(App).Assembly.GetManifestResourceStream(WindowsFlyingHostBundleStore.ResourceName),
            ReadFlyingHostBundleSha256(),
            ReportFlyingHostBundleStage);
        _vpetRuntime ??= new WindowsVPetRuntime(
            _petHostBundleStore,
            layout.PetHostDataDirectory,
            layout.UiTextPath,
            () => RequestDesktopEntryAction(DesktopEntryGesture.LeftClick),
            () => RequestDesktopEntryAction(DesktopEntryGesture.RightClick),
            visible => RunOnDesktopUi(() => SetDesktopEntryVisible(visible)),
            ResetFloatingEntryPositionAsync,
            ReportPetHostRuntimeStage,
            componentAvailability: _componentAvailability);
        _flyingPetRuntime ??= new WindowsFlyingPetRuntime(
            _flyingHostBundleStore,
            layout.UiTextPath,
            () => RequestDesktopEntryAction(DesktopEntryGesture.LeftClick),
            () => RequestDesktopEntryAction(DesktopEntryGesture.RightClick),
            visible => RunOnDesktopUi(() => SetDesktopEntryVisible(visible)),
            ResetFloatingEntryPositionAsync,
            ReportFlyingHostRuntimeStage,
            componentAvailability: _componentAvailability);
        _desktopPetRuntime ??= new WindowsDesktopPetRuntimeRouter(_flyingPetRuntime, _vpetRuntime);
        if (!_desktopPetRuntimeStateHookAttached)
        {
            _desktopPetRuntime.StateChanged += OnDesktopPetRuntimeStateChanged;
            _desktopPetRuntimeStateHookAttached = true;
        }
        _desktopPetPreferences ??= new DesktopPetPreferenceService(settings, _desktopPetRuntime, _componentAvailability);
        viewModel.ConfigureDesktopPetService(_desktopPetPreferences);
        return viewModel;
    }

    internal async Task InitializeDesktopPetAfterLauncherReadyAsync(PersonalizationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        for (var attempt = 0; attempt < 100 && !_shuttingDown; attempt++)
        {
            var floating = _floatingWindow;
            var surface = _window;
            if (floating is not null || (_morphingSurfaceExperience && surface is not null))
            {
                if (floating is not null) AttachDesktopPetCloseHook(floating);
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
        if (detail.StartsWith("component-unavailable;", StringComparison.OrdinalIgnoreCase))
        {
            var fields = ParseDesktopPetRuntimeStage(detail);
            QueueDiagnostic(DiagnosticEventFactory.Create(
                "desktop.pet.component-unavailable",
                "FACM.Personalization",
                0,
                DiagnosticResult.Failure,
                "optional-component-missing",
                _productState?.Current.League ?? LeagueProductState.NotRunning,
                typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
                new Dictionary<string, string>
                {
                    ["requestedStyleId"] = fields.GetValueOrDefault("requestedStyleId", string.Empty),
                    ["requiredComponent"] = fields.GetValueOrDefault("requiredComponent", string.Empty)
                }));
        }
    }

    private void ReportPetHostBundleStage(string stage) =>
        ReportDesktopPetStage("personalization.pet-bundle", "vpet-preparation-stage", stage);

    private void ReportFlyingHostBundleStage(string stage) =>
        ReportDesktopPetStage("personalization.flying-bundle", "flying-preparation-stage", stage);

    private void ReportPetHostRuntimeStage(string stage) =>
        ReportDesktopPetRuntimeStage("personalization.pet-host", "vpet-startup-stage", stage);

    private void ReportFlyingHostRuntimeStage(string stage) =>
        ReportDesktopPetRuntimeStage("personalization.flying-host", "flying-startup-stage", stage);

    private void ReportDesktopPetStage(string eventName, string reason, string stage)
    {
        QueueDiagnostic(DiagnosticEventFactory.Create(
            eventName,
            "FACM.Personalization",
            0,
            DiagnosticResult.Success,
            reason,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
            new Dictionary<string, string>
            {
                ["stage"] = stage ?? string.Empty
            }));
    }

    private void ReportDesktopPetRuntimeStage(string eventName, string reason, string stage)
    {
        var diagnosticFields = ParseDesktopPetRuntimeStage(stage);
        var normalizedStage = diagnosticFields["stage"];
        var failed = normalizedStage.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                     normalizedStage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                     normalizedStage.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
                     normalizedStage.Contains("rejected", StringComparison.OrdinalIgnoreCase);
        diagnosticFields["rawStage"] = stage ?? string.Empty;
        QueueDiagnostic(DiagnosticEventFactory.Create(
            eventName,
            "FACM.Personalization",
            0,
            failed ? DiagnosticResult.Failure : DiagnosticResult.Success,
            reason,
            _productState?.Current.League ?? LeagueProductState.NotRunning,
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
            diagnosticFields));
    }

    private static Dictionary<string, string> ParseDesktopPetRuntimeStage(string? stage)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["stage"] = string.Empty
        };
        if (string.IsNullOrWhiteSpace(stage)) return fields;

        var parts = stage.Split(';', StringSplitOptions.RemoveEmptyEntries);
        fields["stage"] = parts[0];
        for (var index = 1; index < parts.Length; index++)
        {
            var separator = parts[index].IndexOf('=');
            if (separator <= 0 || separator == parts[index].Length - 1) continue;
            fields[parts[index][..separator]] = parts[index][(separator + 1)..];
        }
        return fields;
    }

    private void AttachDesktopPetCloseHook(FloatingWindow floating)
    {
        if (_desktopPetCloseHookAttached || _shuttingDown) return;

        void Attach()
        {
            if (_desktopPetCloseHookAttached || _shuttingDown) return;
            floating.Closed += OnDesktopPetFloatingWindowClosed;
            _desktopPetCloseHookTarget = floating;
            _desktopPetCloseHookAttached = true;
        }

        if (floating.DispatcherQueue.HasThreadAccess)
        {
            Attach();
            return;
        }

        _ = floating.DispatcherQueue.TryEnqueue(Attach);
    }

    private void OnDesktopPetFloatingWindowClosed(object sender, WindowEventArgs args)
    {
        if (sender is FloatingWindow floating)
            floating.Closed -= OnDesktopPetFloatingWindowClosed;
        _desktopPetCloseHookAttached = false;
        _desktopPetCloseHookTarget = null;
        DisposePersonalizationRuntime();
    }

    internal void DisposePersonalizationRuntime()
    {
        var hookTarget = _desktopPetCloseHookTarget;
        if (hookTarget is not null && _desktopPetCloseHookAttached)
        {
            try
            {
                if (hookTarget.DispatcherQueue.HasThreadAccess)
                    hookTarget.Closed -= OnDesktopPetFloatingWindowClosed;
                else
                    _ = hookTarget.DispatcherQueue.TryEnqueue(() => hookTarget.Closed -= OnDesktopPetFloatingWindowClosed);
            }
            catch
            {
            }
        }
        _desktopPetCloseHookAttached = false;
        _desktopPetCloseHookTarget = null;

        var runtime = _desktopPetRuntime;
        if (runtime is not null && _desktopPetRuntimeStateHookAttached)
        {
            runtime.StateChanged -= OnDesktopPetRuntimeStateChanged;
            _desktopPetRuntimeStateHookAttached = false;
        }
        runtime?.Dispose();
        _desktopPetRuntime = null;
        _vpetRuntime = null;
        _flyingPetRuntime = null;
        _desktopPetPreferences = null;
        _petHostBundleStore = null;
        _flyingHostBundleStore = null;
        _componentAvailability = null;
    }

    private static string? ReadPetHostBundleSha256() =>
        ReadEmbeddedBundleSha256(WindowsPetHostBundleStore.HashResourceName);

    private static string? ReadFlyingHostBundleSha256() =>
        ReadEmbeddedBundleSha256(WindowsFlyingHostBundleStore.HashResourceName);

    private static string? ReadEmbeddedBundleSha256(string resourceName)
    {
        try
        {
            using var stream = typeof(App).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Trim();
        }
        catch
        {
            // Local/lightweight developer builds may omit the identity resource. Each bundle store
            // retains a safe hash-on-demand fallback for that case.
            return null;
        }
    }

    private void RunOnDesktopUi(Action action)
    {
        var dispatcher = _morphingSurfaceExperience
            ? _window?.DispatcherQueue
            : _floatingWindow?.DispatcherQueue;
        if (dispatcher is null || _shuttingDown) return;
        _ = dispatcher.TryEnqueue(() =>
        {
            if (!_shuttingDown) action();
        });
    }

    private void SetDesktopEntryVisible(bool visible)
    {
        if (_morphingSurfaceExperience)
            _window?.SetDesktopEntryVisible(visible);
        else
            _floatingWindow?.SetDesktopEntryVisible(visible);
    }

    private void RequestDesktopEntryAction(DesktopEntryGesture gesture) =>
        RunOnDesktopUi(() =>
        {
            if (DesktopEntryInteractionPolicy.Resolve(gesture) == DesktopEntryAction.ShowTrayContextMenu)
                ShowTrayContextMenuAtCursor();
            else
                ToggleCompactLauncher();
        });

    private Task ResetFloatingEntryPositionAsync()
    {
        if (_morphingSurfaceExperience)
            return _window?.ResetMorphingSurfacePositionAsync() ?? Task.CompletedTask;

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
