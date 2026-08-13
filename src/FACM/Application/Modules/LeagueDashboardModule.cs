using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.League;
using FACM.Performance;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeagueDashboardModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[] { LeagueClientModule.ModuleId, PerformanceModule.ModuleId };
        private readonly LeagueClientModule _leagueClient;
        private readonly PerformanceModule _performance;
        public LeagueDashboardModule(LeagueClientModule leagueClient, PerformanceModule performance)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        }
        public const string ModuleId = "league-dashboard";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }
        public void Initialize() { }
        public Form CreateDashboardForm(UiTextCatalog ui) { return new LeagueDashboardForm(_leagueClient, _performance.Budgets, ui); }
        public void Dispose() { }
    }
}
