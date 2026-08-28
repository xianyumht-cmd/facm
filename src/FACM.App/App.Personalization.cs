using FACM.App.Personalization;
using FACM.App.ViewModels;
using FACM.Core.Personalization;
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
