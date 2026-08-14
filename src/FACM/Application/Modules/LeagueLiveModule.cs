using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.League;
using FACM.Performance;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeagueLiveModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[] { LeagueClientModule.ModuleId, PerformanceModule.ModuleId };
        private readonly LeagueClientModule _leagueClient;
        private readonly PerformanceModule _performance;
        private LeagueLiveDataService _service;

        public LeagueLiveModule(LeagueClientModule leagueClient, PerformanceModule performance)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        }

        public const string ModuleId = "league-live";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public void Initialize()
        {
            _service = new LeagueLiveDataService(_leagueClient, _performance.Budgets);
            LeagueLiveUiBridge.Install(this);
        }

        public Form CreateLiveForm(UiTextCatalog ui)
        {
            if (_service == null) throw new InvalidOperationException("League Live module is not initialized.");
            return new LeagueLiveForm(_service, ui);
        }

        public void Dispose()
        {
            LeagueLiveUiBridge.Uninstall();
            _service = null;
        }
    }
}
