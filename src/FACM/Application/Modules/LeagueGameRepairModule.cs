using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.League;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeagueGameRepairModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[] { LeagueClientModule.ModuleId };
        private readonly LeagueClientModule _leagueClient;
        private LeagueGameRepairService _service;

        public LeagueGameRepairModule(LeagueClientModule leagueClient)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
        }

        public const string ModuleId = "league-game-repair";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public void Initialize()
        {
            if (_service != null) return;
            _service = new LeagueGameRepairService(
                _leagueClient,
                (ILeaguePostGameWriteApi)_leagueClient,
                (ILeagueClientUxRepairWriteApi)_leagueClient);
            AppLog.Info("League native game repair module initialized; legacy Fix-LCU-Window process is not used.");
        }

        public Form CreateForm(LeagueEfficiencyModule efficiency)
        {
            if (_service == null) throw new InvalidOperationException("League game repair module is not initialized.");
            return new LeagueGameRepairForm(_service, efficiency);
        }

        internal LeagueGameRepairService ServiceForSmokeTest
        {
            get { return _service; }
        }

        public void Dispose()
        {
            var service = _service;
            _service = null;
            if (service != null) service.Dispose();
        }
    }
}
