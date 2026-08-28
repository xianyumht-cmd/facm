using FACM.App.ViewModels;
using FACM.Infrastructure.League;

namespace FACM.App;

public partial class App
{
    /// <summary>
    /// Completes the per-shell League Workbench composition without creating another League runtime.
    /// The ViewModel already owns the one Workbench data source created over the process-wide gateway
    /// and gameflow owner; Build Advisor / Item Sets reuse that exact read source and gateway.
    /// </summary>
    internal void ConfigureLeagueWorkbenchProductization(LeagueWorkbenchViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (viewModel.HasProductServices) return;

        var dataSource = viewModel.DataSource
            ?? throw new InvalidOperationException("League Workbench data source is unavailable.");
        var gateway = _leagueGateway
            ?? throw new InvalidOperationException("League read gateway is unavailable.");
        var performance = _performance
            ?? throw new InvalidOperationException("Performance budget provider is unavailable.");

        var advisor = new LeagueBuildAdvisorService(dataSource, gateway, performance);
        var itemSets = new LeagueItemSetService(dataSource, gateway);
        viewModel.ConfigureProductServices(advisor, itemSets, ownsServices: true);
    }
}
