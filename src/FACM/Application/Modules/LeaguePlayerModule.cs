using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.League;
using FACM.Performance;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeaguePlayerModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[] { LeagueClientModule.ModuleId, PerformanceModule.ModuleId };
        private readonly LeagueClientModule _leagueClient;
        private readonly PerformanceModule _performance;
        private LeaguePlayerDataService _service;

        public LeaguePlayerModule(LeagueClientModule leagueClient, PerformanceModule performance)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        }

        public const string ModuleId = "league-player";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public void Initialize()
        {
            _service = new LeaguePlayerDataService(_leagueClient, _performance.Budgets);
            LeaguePlayerUiBridge.Install(this);
        }

        public Form CreatePlayerForm(UiTextCatalog ui)
        {
            if (_service == null) throw new InvalidOperationException("League Player module is not initialized.");
            return new LeaguePlayerForm(_service, ui);
        }

        public void Dispose()
        {
            LeaguePlayerUiBridge.Uninstall();
            _service = null;
        }
    }
}
