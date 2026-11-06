using System;
using System.Collections.Generic;
using System.Drawing;
using FACM.Mayhem;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.League;
using FACM.Performance;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeagueUnifiedHistorySnapshot
    {
        internal LeaguePlayerProfile Profile;
        internal LeaguePlayerMatchPage Page;
    }

    internal sealed class LeaguePlayerModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[] { LeagueClientModule.ModuleId, PerformanceModule.ModuleId };
        private readonly LeagueClientModule _leagueClient;
        private readonly PerformanceModule _performance;
        private LeaguePlayerDataService _service;
        private LeagueEncounterReplayService _replay;
        private LeagueEncounterMatchDetailService _details;

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
            _details = new LeagueEncounterMatchDetailService(_service, () => _leagueClient.CurrentSession);
            _replay = new LeagueEncounterReplayService(_leagueClient,
                (ILeagueReplayWriteApi)_leagueClient, () => _leagueClient.CurrentSession);
        }

        internal LeagueClientSession UnifiedHistorySession { get { return _leagueClient.CurrentSession; } }

        internal async Task<LeagueUnifiedHistorySnapshot> ReadUnifiedHistoryAsync(
            int count, bool force, CancellationToken cancellationToken)
        {
            if (_service == null || (count != 10 && count != 20)) return null;
            var initial = _leagueClient.CurrentSession;
            if (initial == null) return null;
            var profile = await _service.LoadProfileAsync(force, cancellationToken);
            if (profile == null || string.IsNullOrWhiteSpace(profile.PuuId) ||
                !LeagueEncounterReplayService.SessionMatches(initial, _leagueClient.CurrentSession)) return null;
            var page = await _service.LoadRecentMatchesAsync(profile, 0, count, force, cancellationToken);
            if (page != null && !cancellationToken.IsCancellationRequested)
            {
                var enriched = await _service.EnrichIncompleteMatchesAsync(profile, page, cancellationToken);
                if (enriched != null) page = enriched;
                var named = await _service.EnrichChampionNamesAsync(profile, page, cancellationToken);
                if (named != null) page = named;
            }
            return !cancellationToken.IsCancellationRequested &&
                LeagueEncounterReplayService.SessionMatches(initial, _leagueClient.CurrentSession)
                ? new LeagueUnifiedHistorySnapshot { Profile = profile, Page = page } : null;
        }

        // Match details share the existing collector and SGP fallback; callers must
        // select an actual match belonging to the currently loaded account.
        internal Task<LeagueEncounterMatchDetailResult> ReadUnifiedMatchDetailAsync(
            LeagueClientSession expected, string selfPuuid, LeaguePlayerMatchSummary selected,
            CancellationToken cancellationToken)
        {
            if (_details == null || selected == null)
                return Task.FromResult(new LeagueEncounterMatchDetailResult
                {
                    Status = LeagueEncounterMatchDetailStatus.Unavailable
                });
            return _details.ReadHistoryAsync(expected, selfPuuid, selfPuuid, selected, cancellationToken);
        }

        internal Task<LeagueEncounterReplayResult> ExecuteUnifiedHistoryReplayAsync(
            LeagueClientSession expected, string selfPuuid,
            LeaguePlayerMatchSummary selected, CancellationToken cancellationToken)
        {
            return ExecuteHistoryReplayAsync(expected, selfPuuid, selfPuuid,
                selected, cancellationToken);
        }

        internal Task<LeagueReplayDirectoryResult> ReadUnifiedReplayDirectoryAsync(
            LeagueClientSession expected, string selfPuuid, CancellationToken token)
        {
            if (_replay == null) return Task.FromResult(
                new LeagueReplayDirectoryResult { Status = LeagueEncounterReplayStatus.Unavailable });
            return _replay.ReadDirectoryAsync(expected, selfPuuid, token);
        }

        public Form CreatePlayerForm(UiTextCatalog ui)
        {
            if (_service == null) throw new InvalidOperationException("League Player module is not initialized.");
            return new LeaguePlayerForm(_service, ui, () => _leagueClient.CurrentSession,
                (expected, self, target, match, token) => ExecuteHistoryReplayAsync(
                    expected, self, target, match, token),
                (expected, self, target, match, token) => _details.ReadHistoryAsync(expected, self, target, match, token),
                LoadMatchAssetAsync,
                (expected, self, token) => _replay.ReadDirectoryAsync(expected, self, token));
        }

        private Task<Bitmap> LoadMatchAssetAsync(string reference, CancellationToken token)
        {
            return LeagueEfficiencyModule.IsSafeEncounterAssetReference(reference)
                ? MayhemImageCache.GetAsync(reference, _leagueClient, token) : Task.FromResult<Bitmap>(null);
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
                            selected.DataSource == LeaguePlayerHistorySource.Sgp,
                            selected.PreferredSource != LeaguePlayerHistorySource.Lcu).ConfigureAwait(false);
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
            _details = null;
        }
    }
}
