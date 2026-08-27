using FACM.App.ViewModels;
using FACM.Infrastructure.Online;
using FACM.Infrastructure.Settings;
using FACM.Platform.Windows.Runtime;
using Microsoft.UI.Xaml;

namespace FACM.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var executablePaths = new WindowsExecutablePathProvider();
        var executableDirectory = Path.GetDirectoryName(executablePaths.ExecutablePath)
            ?? throw new InvalidOperationException("FACM distribution directory is unavailable.");

        // Keep the 3.5.15 settings location contract: settings.ini lives beside the
        // distributed FACM executable. Never derive persistent paths from AppContext.BaseDirectory,
        // because WinUI single-file extracts there only temporarily.
        var settings = new IniSettingsRepository(Path.Combine(executableDirectory, "settings.ini"));
        var updates = new UnavailableUpdateManifestSource();
        var controlCenter = new ControlCenterViewModel(settings, updates);

        _window = new MainWindow(controlCenter);
        _window.Activate();
    }
}
