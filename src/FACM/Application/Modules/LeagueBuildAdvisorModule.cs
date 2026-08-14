using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.League;
using FACM.Performance;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeagueBuildAdvisorModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            SettingsModule.ModuleId,
            LeagueClientModule.ModuleId,
            PerformanceModule.ModuleId
        };

        private readonly SettingsModule _settings;
        private readonly LeagueClientModule _leagueClient;
        private readonly PerformanceModule _performance;
        private CachingOpggBuildApi _sharedOpgg;
        private LeagueBuildAdvisorDataService _service;
        private LeagueBuildApplyService _applyService;
        private LeagueItemSetService _itemSetService;
        private LeagueAutoApplyController _autoApply;

        public LeagueBuildAdvisorModule(
            SettingsModule settings,
            LeagueClientModule leagueClient,
            PerformanceModule performance)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        }

        public const string ModuleId = "league-build-advisor";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public void Initialize()
        {
            if (_settings.Settings == null)
                throw new InvalidOperationException("Settings module must initialize before League Build Advisor.");

            _sharedOpgg = new CachingOpggBuildApi(new OpggBuildApiClient(), true);
            _service = new LeagueBuildAdvisorDataService(
                _leagueClient,
                _performance.Budgets,
                _sharedOpgg,
                false);
            _applyService = new LeagueBuildApplyService(_leagueClient, _leagueClient, _performance.Budgets);
            _itemSetService = new LeagueItemSetService(_leagueClient, _performance.Budgets);
            var executor = new LeagueAutoApplyExecutor(
                _applyService,
                _itemSetService,
                _sharedOpgg,
                false);
            _autoApply = new LeagueAutoApplyController(
                _settings.Settings,
                _performance.Budgets,
                _service,
                executor,
                new LeagueAutoApplyCoordinator());
            _autoApply.Start();

            LeagueBuildAdvisorUiBridge.Install(this);
            LeagueBuildApplyUiBridge.Install(this);
            LeagueItemSetUiBridge.Install(this);
        }

        public Form CreateForm(UiTextCatalog ui)
        {
            if (_service == null) throw new InvalidOperationException("League Build Advisor module is not initialized.");
            return new LeagueBuildAdvisorForm(_service, ui);
        }

        public Form CreateApplyForm(UiTextCatalog ui)
        {
            if (_service == null || _applyService == null || _autoApply == null)
                throw new InvalidOperationException("League Build Apply module is not initialized.");
            return new LeagueBuildApplyForm(_service, _applyService, _autoApply, ui);
        }

        public Form CreateItemSetForm(UiTextCatalog ui)
        {
            if (_service == null || _itemSetService == null)
                throw new InvalidOperationException("League item-set module is not initialized.");
            return new LeagueItemSetForm(_service, _itemSetService, ui);
        }

        public void Dispose()
        {
            LeagueItemSetUiBridge.Uninstall();
            LeagueBuildApplyUiBridge.Uninstall();
            LeagueBuildAdvisorUiBridge.Uninstall();

            var autoApply = _autoApply;
            var itemSetService = _itemSetService;
            var applyService = _applyService;
            var service = _service;
            var sharedOpgg = _sharedOpgg;
            _autoApply = null;
            _itemSetService = null;
            _applyService = null;
            _service = null;
            _sharedOpgg = null;

            if (autoApply != null) autoApply.Dispose();
            if (itemSetService != null) itemSetService.Dispose();
            if (applyService != null) applyService.Dispose();
            if (service != null) service.Dispose();
            if (sharedOpgg != null) sharedOpgg.Dispose();
        }
    }
}
