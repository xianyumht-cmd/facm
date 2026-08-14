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
            LeagueBuildAdvisorUiBridge.Install(this);
        }

        public Form CreateForm(UiTextCatalog ui)
        {
            if (_service == null) throw new InvalidOperationException("League Build Advisor module is not initialized.");
            return new LeagueBuildAdvisorForm(_service, ui);
        }

        public void Dispose()
        {
            LeagueBuildAdvisorUiBridge.Uninstall();
            var service = _service;
            _service = null;
            if (service != null) service.Dispose();
        }
    }
}
