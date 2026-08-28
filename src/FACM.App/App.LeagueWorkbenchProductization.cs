using FACM.App.ViewModels;
using FACM.Infrastructure.League;

namespace FACM.App;

public partial class App
{
    /// <summary>
    /// Completes the per-shell League Workbench composition without creating another League runtime.
    /// The ViewModel already owns the one Workbench data source created over the process-wide gateway
    /// and gameflow owner; Build Advisor / Item Sets and automation controls reuse those process owners.
    /// </summary>
    internal void ConfigureLeagueWorkbenchProductization(LeagueWorkbenchViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (!viewModel.HasProductServices)
        {
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

        if (!viewModel.HasMatchmakingAutomation)
        {
            var settings = _settings
                ?? throw new InvalidOperationException("Settings 2.0 repository is unavailable.");
            var automation = _matchmakingAutomation
                ?? throw new InvalidOperationException("League matchmaking automation is unavailable.");
            viewModel.ConfigureMatchmakingAutomation(settings, automation);
        }
    }
}
