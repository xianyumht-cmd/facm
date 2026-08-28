using FACM.App.ViewModels;
using FACM.Core.League;
using FACM.Infrastructure.League;

namespace FACM.App;

public partial class App
{
    private LeaguePostGameAutomationService? _postGameAutomation;
    private bool _postGameProcessExitHooked;

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

    /// <summary>
    /// Presence is user-directed and stateless between clicks, so each shell can hold this light
    /// service while still sharing the one process-wide authenticated League gateway.
    /// </summary>
    internal ILeaguePresenceService CreateLeaguePresenceService()
    {
        var gateway = _leagueGateway
            ?? throw new InvalidOperationException("League gateway is unavailable.");
        return new LeaguePresenceService(gateway, gateway);
    }

    /// <summary>
    /// Returns a per-window settings presenter over one process-wide post-game automation service.
    /// Closing the large shell only disposes the presenter; automatic honor/return remains owned by
    /// the FACM process and continues to consume the shared gameflow heartbeat.
    /// </summary>
    internal LeaguePostGameAutomationSettingsViewModel CreateLeaguePostGameAutomationSettingsViewModel()
    {
        var settings = _settings
            ?? throw new InvalidOperationException("Settings 2.0 repository is unavailable.");
        var gateway = _leagueGateway
            ?? throw new InvalidOperationException("League gateway is unavailable.");
        var gameflow = _gameflow
            ?? throw new InvalidOperationException("League gameflow owner is unavailable.");

        if (_postGameAutomation is null)
        {
            var automation = new LeaguePostGameAutomationService(gateway, gateway, gameflow);
            try
            {
                var loaded = settings.LoadAsync().GetAwaiter().GetResult();
                automation.Configure(
                    loaded.Settings.League.AutoHonorTeammateEnabled,
                    loaded.Settings.League.AutoReturnLobbyEnabled);
            }
            catch
            {
                automation.Configure(false, false);
            }

            _postGameAutomation = automation;
            if (!_postGameProcessExitHooked)
            {
                _postGameProcessExitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += OnLeaguePostGameProcessExit;
            }
        }

        return new LeaguePostGameAutomationSettingsViewModel(settings, _postGameAutomation);
    }

    private void OnLeaguePostGameProcessExit(object? sender, EventArgs args)
    {
        AppDomain.CurrentDomain.ProcessExit -= OnLeaguePostGameProcessExit;
        _postGameProcessExitHooked = false;
        _postGameAutomation?.Dispose();
        _postGameAutomation = null;
    }
}
