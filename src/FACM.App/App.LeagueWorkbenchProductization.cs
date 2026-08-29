using FACM.App.ViewModels;
using FACM.Core.League;
using FACM.Infrastructure.League;
using FACM.Platform.Windows.League;

namespace FACM.App;

public partial class App
{
    private LeaguePostGameAutomationService? _postGameAutomation;
    private bool _postGameProcessExitHooked;
    private LeagueRecommendedAutoApplyService? _recommendedAutoApply;
    private bool _recommendedAutoApplyProcessExitHooked;
    private LeagueEfficiencyRuntime? _leagueEfficiencyRuntime;
    private bool _leagueEfficiencyProcessExitHooked;

    /// <summary>
    /// Completes the per-shell League Workbench composition without creating another League runtime.
    /// The ViewModel already owns the one Workbench data source created over the process-wide gateway
    /// and gameflow owner; Build Advisor / Item Sets and automation controls reuse those facts.
    /// </summary>
    internal void ConfigureLeagueWorkbenchProductization(LeagueWorkbenchViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var dataSource = viewModel.DataSource
            ?? throw new InvalidOperationException("League Workbench data source is unavailable.");
        var gateway = _leagueGateway
            ?? throw new InvalidOperationException("League read gateway is unavailable.");
        var performance = _performance
            ?? throw new InvalidOperationException("Performance budget provider is unavailable.");
        var settings = _settings
            ?? throw new InvalidOperationException("Settings 2.0 repository is unavailable.");
        var gameflow = _gameflow
            ?? throw new InvalidOperationException("League gameflow owner is unavailable.");

        if (!viewModel.HasProductServices)
        {
            var advisor = new LeagueBuildAdvisorService(dataSource, gateway, performance);
            var itemSets = new LeagueItemSetService(dataSource, gateway);
            viewModel.ConfigureProductServices(advisor, itemSets, ownsServices: true);
        }

        if (!viewModel.HasMatchmakingAutomation)
        {
            var automation = _matchmakingAutomation
                ?? throw new InvalidOperationException("League matchmaking automation is unavailable.");
            viewModel.ConfigureMatchmakingAutomation(settings, automation);
        }

        EnsureLeagueRecommendedAutoApply(dataSource, gateway, gateway, performance, settings, gameflow);
        EnsureLeagueEfficiencyRuntime(settings);
    }

    internal ILeagueBuildLoadoutService CreateLeagueBuildLoadoutService(ILeagueWorkbenchDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var gateway = _leagueGateway
            ?? throw new InvalidOperationException("League gateway is unavailable.");
        return new LeagueBuildLoadoutService(dataSource, gateway, gateway);
    }

    internal LeagueRecommendedAutoApplySettingsViewModel CreateLeagueRecommendedAutoApplySettingsViewModel()
    {
        var settings = _settings
            ?? throw new InvalidOperationException("Settings 2.0 repository is unavailable.");
        var automation = _recommendedAutoApply
            ?? throw new InvalidOperationException("League recommended auto apply is unavailable.");
        return new LeagueRecommendedAutoApplySettingsViewModel(settings, automation);
    }

    internal ILeagueEfficiencyRuntime? GetLeagueEfficiencyRuntime() => _leagueEfficiencyRuntime;

    private void EnsureLeagueRecommendedAutoApply(
        ILeagueWorkbenchDataSource dataSource,
        ILeagueReadGateway readGateway,
        ILeagueWriteGateway writeGateway,
        FACM.Core.Performance.PerformanceBudgetProvider performance,
        FACM.Core.Settings.ISettings2Repository settings,
        ILeagueGameflowObservationSource gameflow)
    {
        if (_recommendedAutoApply is not null) return;

        // Auto-apply is process scoped so it keeps working when the detailed shell is closed. It
        // consumes the same shared gameflow heartbeat and gateway; no second phase loop is created.
        var autoAdvisor = new LeagueBuildAdvisorService(dataSource, readGateway, performance);
        var autoItemSets = new LeagueItemSetService(dataSource, readGateway);
        var autoLoadout = new LeagueBuildLoadoutService(dataSource, readGateway, writeGateway);
        var autoApply = new LeagueRecommendedAutoApplyService(
            autoAdvisor,
            autoLoadout,
            autoItemSets,
            gameflow);
        try
        {
            var loaded = settings.LoadAsync().GetAwaiter().GetResult();
            autoApply.Configure(loaded.Settings.League.AutoApplyRecommended);
        }
        catch
        {
            autoApply.Configure(false);
        }

        _recommendedAutoApply = autoApply;
        if (!_recommendedAutoApplyProcessExitHooked)
        {
            _recommendedAutoApplyProcessExitHooked = true;
            AppDomain.CurrentDomain.ProcessExit += OnLeagueRecommendedAutoApplyProcessExit;
        }
    }

    private void EnsureLeagueEfficiencyRuntime(FACM.Core.Settings.ISettings2Repository settings)
    {
        if (_leagueEfficiencyRuntime is not null) return;

        LeagueEfficiencyRuntime? runtime = null;
        try
        {
            runtime = new LeagueEfficiencyRuntime(
                settings,
                new WindowsLeagueEfficiencyActionService(),
                new WindowsLeagueGlobalHotkeyService());
            runtime.InitializeAsync().GetAwaiter().GetResult();
            _leagueEfficiencyRuntime = runtime;
            runtime = null;

            if (!_leagueEfficiencyProcessExitHooked)
            {
                _leagueEfficiencyProcessExitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += OnLeagueEfficiencyProcessExit;
            }
        }
        catch
        {
            runtime?.Dispose();
            _leagueEfficiencyRuntime = null;
            // Global hotkeys are an optional efficiency feature. A Win32 registration/thread failure
            // must not prevent the League Workbench or the FACM desktop entry from starting.
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

    /// <summary>
    /// Normal FACM shutdown owns these process-scoped feature services explicitly. ProcessExit stays
    /// only as a last-resort fallback; runtime teardown must not depend on CLR event ordering after the
    /// shared gameflow/gateway have already been disposed.
    /// </summary>
    internal void DisposeLeagueProductizationRuntime()
    {
        if (_recommendedAutoApplyProcessExitHooked)
        {
            AppDomain.CurrentDomain.ProcessExit -= OnLeagueRecommendedAutoApplyProcessExit;
            _recommendedAutoApplyProcessExitHooked = false;
        }
        if (_leagueEfficiencyProcessExitHooked)
        {
            AppDomain.CurrentDomain.ProcessExit -= OnLeagueEfficiencyProcessExit;
            _leagueEfficiencyProcessExitHooked = false;
        }
        if (_postGameProcessExitHooked)
        {
            AppDomain.CurrentDomain.ProcessExit -= OnLeaguePostGameProcessExit;
            _postGameProcessExitHooked = false;
        }

        _recommendedAutoApply?.Dispose();
        _recommendedAutoApply = null;
        _leagueEfficiencyRuntime?.Dispose();
        _leagueEfficiencyRuntime = null;
        _postGameAutomation?.Dispose();
        _postGameAutomation = null;
    }

    private void OnLeagueRecommendedAutoApplyProcessExit(object? sender, EventArgs args)
    {
        AppDomain.CurrentDomain.ProcessExit -= OnLeagueRecommendedAutoApplyProcessExit;
        _recommendedAutoApplyProcessExitHooked = false;
        _recommendedAutoApply?.Dispose();
        _recommendedAutoApply = null;
    }

    private void OnLeagueEfficiencyProcessExit(object? sender, EventArgs args)
    {
        AppDomain.CurrentDomain.ProcessExit -= OnLeagueEfficiencyProcessExit;
        _leagueEfficiencyProcessExitHooked = false;
        _leagueEfficiencyRuntime?.Dispose();
        _leagueEfficiencyRuntime = null;
    }

    private void OnLeaguePostGameProcessExit(object? sender, EventArgs args)
    {
        AppDomain.CurrentDomain.ProcessExit -= OnLeaguePostGameProcessExit;
        _postGameProcessExitHooked = false;
        _postGameAutomation?.Dispose();
        _postGameAutomation = null;
    }
}
