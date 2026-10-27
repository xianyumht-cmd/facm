using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        private LeagueEncounterReplayService _replay;

        public LeaguePlayerModule(LeagueClientModule leagueClient, PerformanceModule performance)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        }

        public const string ModuleId = "league-player";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        internal LeaguePlayerDataService RuntimeCompanionReadService
        {
            get
            {
                if (_service == null) throw new InvalidOperationException("League Player module is not initialized.");
                return _service;
            }
        }

        public void Initialize()
        {
            _service = new LeaguePlayerDataService(_leagueClient, _performance.Budgets,
                _leagueClient, _leagueClient, () => _leagueClient.CurrentSession);
            _replay = new LeagueEncounterReplayService(_leagueClient,
                (ILeagueReplayWriteApi)_leagueClient, () => _leagueClient.CurrentSession);
        }

        public Form CreatePlayerForm(UiTextCatalog ui)
        {
            if (_service == null) throw new InvalidOperationException("League Player module is not initialized.");
            return new LeaguePlayerForm(_service, ui, () => _leagueClient.CurrentSession,
                (expected, self, target, match, token) => ExecuteHistoryReplayAsync(
                    expected, self, target, match, token));
        }

        private async Task<LeagueEncounterReplayResult> ExecuteHistoryReplayAsync(
            LeagueClientSession expected, string selfPuuid, string targetPuuid,
            LeaguePlayerMatchSummary selected, CancellationToken token)
        {
            if (_service == null || _replay == null || selected == null || selected.GameId <= 0)
                return new LeagueEncounterReplayResult { Status = LeagueEncounterReplayStatus.Unavailable };
            if (!LeagueEncounterReplayService.SessionMatches(expected, _leagueClient.CurrentSession))
                return new LeagueEncounterReplayResult { Status = LeagueEncounterReplayStatus.NoClient };
            var match = selected;
            if (RequiresReplayDetail(selected))
            {
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(6));
                    try
                    {
                        match = await _service.LoadMatchDetailAsync(
                            new LeaguePlayerProfile { PuuId = targetPuuid }, selected.GameId,
                            deadline.Token, selected.SgpServerId,
                            selected.DataSource == LeaguePlayerHistorySource.Sgp).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        if (token.IsCancellationRequested) throw;
                        match = null;
                    }
                    catch (Exception) { match = null; }
                }
                if (!LeagueEncounterReplayService.SessionMatches(expected, _leagueClient.CurrentSession))
                    return new LeagueEncounterReplayResult { Status = LeagueEncounterReplayStatus.NoClient };
                if (match == null || match.GameId != selected.GameId)
                    return new LeagueEncounterReplayResult { Status = LeagueEncounterReplayStatus.Unavailable };
            }
            return await _replay.ExecuteForHistoryAsync(expected, selfPuuid, targetPuuid, match, token)
                .ConfigureAwait(false);
        }

        internal static bool RequiresReplayDetail(LeaguePlayerMatchSummary match)
        {
            return match == null || match.GameCreationEpochMs <= 0 || match.GameDurationSeconds <= 0 ||
                match.QueueId <= 0 || string.IsNullOrWhiteSpace(match.GameType);
        }

        public void Dispose()
        {
            _service = null;
            _replay = null;
        }
    }
}
