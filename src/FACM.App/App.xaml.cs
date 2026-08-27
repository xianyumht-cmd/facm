using FACM.App.ViewModels;
using FACM.Core.Runtime;
using FACM.Infrastructure.League;
using FACM.Infrastructure.Online;
using FACM.Infrastructure.Settings;
using FACM.Platform.Windows.League;
using FACM.Platform.Windows.Runtime;
using Microsoft.UI.Xaml;

namespace FACM.App;

public partial class App : Application
{
    private Window? _window;
    private HttpUpdateManifestSource? _updateManifestSource;
    private LeagueHttpGateway? _leagueGateway;
    private WindowsLeagueTransportSessionSource? _leagueSessions;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var executablePaths = new WindowsExecutablePathProvider();
        var layout = RuntimePathLayout.From(executablePaths);

        // Settings 2.0 is stored beside the distribution executable. The legacy settings.ini is
        // migration input only and is deliberately preserved so FACM 3.5.15 remains rollback-safe.
        var settings = new Settings2Repository(layout.Settings2Path, layout.SettingsPath);
        _updateManifestSource = new HttpUpdateManifestSource();

        // Gate 3 establishes exactly one real League discovery/auth/session owner for the 4.0
        // process. Read and write gateway capabilities share this same source instance.
        _leagueSessions = new WindowsLeagueTransportSessionSource();
        _leagueGateway = new LeagueHttpGateway(_leagueSessions);

        var controlCenter = new ControlCenterViewModel(settings, _updateManifestSource);
        _window = new MainWindow(controlCenter);
        _window.Closed += OnMainWindowClosed;
        _window.Activate();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        _leagueGateway?.Dispose();
        _leagueGateway = null;
        _leagueSessions = null;
        _updateManifestSource?.Dispose();
        _updateManifestSource = null;
    }
}
