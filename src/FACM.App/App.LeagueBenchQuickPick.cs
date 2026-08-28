using FACM.Core.League;
using FACM.Infrastructure.League;

namespace FACM.App;

public partial class App
{
    /// <summary>
    /// Bench quick-pick is a light per-shell presenter/service. It reuses the one process-wide
    /// authenticated League gateway for both reads and the two strictly allowlisted swap writes.
    /// </summary>
    internal ILeagueBenchQuickPickService CreateLeagueBenchQuickPickService()
    {
        var gateway = _leagueGateway
            ?? throw new InvalidOperationException("League gateway is unavailable.");
        return new LeagueBenchQuickPickService(gateway, gateway);
    }
}
