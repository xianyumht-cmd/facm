using FACM.Core.League;
using FACM.Infrastructure.League;

namespace FACM.App;

public partial class App
{
    private ILeagueBenchQuickPickService? _leagueBenchQuickPick;
    private LeagueBenchRuntimeObserver? _leagueBenchRuntime;

    /// <summary>
    /// Bench quick-pick is one process-wide service. The detailed Workbench presenter and the
    /// compact surface share it, while the runtime observer reuses the existing Gameflow heartbeat.
    /// </summary>
    internal ILeagueBenchQuickPickService CreateLeagueBenchQuickPickService()
    {
        if (_leagueBenchQuickPick is not null) return _leagueBenchQuickPick;
        var gateway = _leagueGateway
            ?? throw new InvalidOperationException("League gateway is unavailable.");
        return _leagueBenchQuickPick = new LeagueBenchQuickPickService(gateway, gateway);
    }

    internal ILeagueBenchRuntimeState? LeagueBenchRuntime => _leagueBenchRuntime;

    private void InitializeLeagueBenchRuntime()
    {
        if (_leagueBenchRuntime is not null) return;
        var gameflow = _gameflow
            ?? throw new InvalidOperationException("League gameflow owner is unavailable.");
        _leagueBenchRuntime = new LeagueBenchRuntimeObserver(gameflow, CreateLeagueBenchQuickPickService());
    }
}
