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
            LeagueClientModule.ModuleId,
            PerformanceModule.ModuleId
        };

        private readonly LeagueClientModule _leagueClient;
        private readonly PerformanceModule _performance;
        private LeagueBuildAdvisorDataService _service;
        private LeagueBuildApplyService _applyService;

        public LeagueBuildAdvisorModule(LeagueClientModule leagueClient, PerformanceModule performance)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        }

        public const string ModuleId = "league-build-advisor";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public void Initialize()
        {
            _service = new LeagueBuildAdvisorDataService(_leagueClient, _performance.Budgets);
            _applyService = new LeagueBuildApplyService(_leagueClient, _leagueClient, _performance.Budgets);
            LeagueBuildAdvisorUiBridge.Install(this);
            LeagueBuildApplyUiBridge.Install(this);
        }

        public Form CreateForm(UiTextCatalog ui)
        {
            if (_service == null) throw new InvalidOperationException("League Build Advisor module is not initialized.");
            return new LeagueBuildAdvisorForm(_service, ui);
        }

        public Form CreateApplyForm(UiTextCatalog ui)
        {
            if (_service == null || _applyService == null)
                throw new InvalidOperationException("League Build Apply module is not initialized.");
            return new LeagueBuildApplyForm(_service, _applyService, ui);
        }

        public void Dispose()
        {
            LeagueBuildApplyUiBridge.Uninstall();
            LeagueBuildAdvisorUiBridge.Uninstall();
            var applyService = _applyService;
            var service = _service;
            _applyService = null;
            _service = null;
            if (applyService != null) applyService.Dispose();
            if (service != null) service.Dispose();
        }
    }
}
