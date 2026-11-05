using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        internal LeagueClientSession UnifiedLookupSession { get { return _leagueClient.CurrentSession; } }

        // Reuse the same lookup and enrichment chain as the native Mayhem form.
        internal async Task<MayhemChampionResult> ReadUnifiedGuideAsync(string champion, CancellationToken token)
        {
            var result = await OpggMayhemService.QueryAsync(champion, token);
            if (result == null || !string.IsNullOrWhiteSpace(result.ErrorMessage)) return result;
            await RiotGameDataService.EnrichAsync(result, _leagueClient, token);
            await MayhemRankedAugmentService.EnrichAsync(result, token);
            await MayhemDecisionLocalizationService.EnrichAsync(result, _leagueClient, token);
            return result;
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
