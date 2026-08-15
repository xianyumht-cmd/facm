using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.League;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeagueHubModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            LeagueDashboardModule.ModuleId,
            LeaguePlayerModule.ModuleId,
            LeagueLiveModule.ModuleId,
            LeagueBuildAdvisorModule.ModuleId,
            LeagueEfficiencyModule.ModuleId,
            MayhemModule.ModuleId
        };

        private readonly LeagueDashboardModule _dashboard;
        private readonly LeaguePlayerModule _player;
        private readonly LeagueLiveModule _live;
        private readonly LeagueBuildAdvisorModule _advisor;
        private readonly LeagueEfficiencyModule _efficiency;
        private readonly MayhemModule _mayhem;

        public LeagueHubModule(
            LeagueDashboardModule dashboard,
            LeaguePlayerModule player,
            LeagueLiveModule live,
            LeagueBuildAdvisorModule advisor,
            LeagueEfficiencyModule efficiency,
            MayhemModule mayhem)
        {
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _live = live ?? throw new ArgumentNullException(nameof(live));
            _advisor = advisor ?? throw new ArgumentNullException(nameof(advisor));
            _efficiency = efficiency ?? throw new ArgumentNullException(nameof(efficiency));
            _mayhem = mayhem ?? throw new ArgumentNullException(nameof(mayhem));
        }

        public const string ModuleId = "league-hub";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public void Initialize()
        {
            LeagueHubNavigation.ValidateForSmokeTest();
        }

        public Form CreateForm(UiTextCatalog ui)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            return new LeagueHubForm(
                ui,
                CreateDashboard,
                CreatePlayer,
                CreateLive,
                CreateMayhem,
                CreateAdvisor,
                CreateApply,
                CreateItemSet,
                CreateEfficiency);
        }

        private Form CreateDashboard(UiTextCatalog ui) { return _dashboard.CreateDashboardForm(ui); }
        private Form CreatePlayer(UiTextCatalog ui) { return _player.CreatePlayerForm(ui); }
        private Form CreateLive(UiTextCatalog ui) { return _live.CreateLiveForm(ui); }
        private Form CreateMayhem(UiTextCatalog ui) { return _mayhem.CreateLookupForm(); }
        private Form CreateAdvisor(UiTextCatalog ui) { return _advisor.CreateForm(ui); }
        private Form CreateApply(UiTextCatalog ui) { return _advisor.CreateApplyForm(ui); }
        private Form CreateItemSet(UiTextCatalog ui) { return _advisor.CreateItemSetForm(ui); }
        private Form CreateEfficiency(UiTextCatalog ui) { return _efficiency.CreateForm(ui); }

        public void Dispose()
        {
        }
    }
}
