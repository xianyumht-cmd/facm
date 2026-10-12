using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.Mayhem;
using FACM.Theming;

namespace FACM.AppHost.Modules
{
    internal sealed class MayhemModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            LeagueClientModule.ModuleId
        };
        private readonly LeagueClientModule _leagueClient;

        public MayhemModule(LeagueClientModule leagueClient)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
        }

        public const string ModuleId = "mayhem";

        public string Id
        {
            get { return ModuleId; }
        }

        public IReadOnlyList<string> Dependencies
        {
            get { return ModuleDependencies; }
        }

        public void Initialize()
        {
        }

        public Form CreateLookupForm()
        {
            var form = new MayhemLookupForm(_leagueClient);
            FacmWindowChrome.Prepare(form);
            return form;
        }

        public void Dispose()
        {
        }
    }
}
