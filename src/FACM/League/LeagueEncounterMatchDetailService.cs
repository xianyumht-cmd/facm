using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;

namespace FACM.League
{
    internal enum LeagueEncounterMatchDetailStatus
    {
        Ready,
        NoClient,
        WrongAccount,
        Unavailable
    }

    internal sealed class LeagueEncounterMatchDetailResult
    {
        internal LeagueEncounterMatchDetailStatus Status;
        internal LeaguePlayerMatchSummary Match;
        internal System.Collections.Generic.List<LeaguePlayerMatchParticipant> Participants;
        internal System.Collections.Generic.List<LeagueMatchTeamScore> Teams;
        internal System.Collections.Generic.Dictionary<int, string> ChampionNames;
        internal LeagueBuildAdvisorCatalog BuildCatalog;
        internal Func<CancellationToken, Task<LeagueMatchTimeline>> LoadTimeline;
        internal Func<CancellationToken, Task<LeagueSgpEogResult>> LoadEog;
        internal LeagueSgpEogResult EogOnly;
        internal Func<bool> IsCurrentSession;
        internal Func<CancellationToken, Task<LeagueDynamicMessageSnapshot>> LoadDryRun;
    }

    // Single user-requested detail read; never observes a game or fetches match history in the background.
    internal sealed class LeagueEncounterMatchDetailService
    {
        private readonly LeaguePlayerDataService _players;
        private readonly Func<LeagueClientSession> _session;

        internal LeagueEncounterMatchDetailService(ILeagueClientApi client,
            PerformanceBudgetProvider budgets, Func<LeagueClientSession> session)
        {
            _players = new LeaguePlayerDataService(client, budgets, null, client as ILeagueSgpMatchHistoryApi, session);
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal LeagueEncounterMatchDetailService(LeaguePlayerDataService players, Func<LeagueClientSession> session)
        {
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal Task<LeagueEncounterMatchDetailResult> ReadHistoryAsync(LeagueClientSession expected,
            string selfPuuid, string targetPuuid, LeaguePlayerMatchSummary selected, CancellationToken token)
        {
            if (!LeagueEncounterReplayService.SessionMatches(expected, _session()))
                return Task.FromResult(Result(LeagueEncounterMatchDetailStatus.NoClient));
            if (selected == null || selected.GameId <= 0)
                return Task.FromResult(Result(LeagueEncounterMatchDetailStatus.Unavailable));
            var record = new LeagueEncounterRecord { SelfPuuid = selfPuuid, Puuid = targetPuuid,
                GameId = selected.GameId, Region = expected.Region, PlatformId = expected.PlatformId };
            return ReadCoreAsync(record, token, selected.SgpServerId,
                selected.DataSource == LeaguePlayerHistorySource.Sgp,
                selected.PreferredSource != LeaguePlayerHistorySource.Lcu);
        }

        internal Task<LeagueEncounterMatchDetailResult> ReadAsync(LeagueEncounterRecord record, CancellationToken token)
        {
            return ReadCoreAsync(record, token, null, false, true);
        }

        private async Task<LeagueEncounterMatchDetailResult> ReadCoreAsync(
            LeagueEncounterRecord record, CancellationToken token, string serverId, bool sgpOnly, bool allowSgpFallback)
        {
            var initial = _session();
            if (initial == null) return Result(LeagueEncounterMatchDetailStatus.NoClient);
            if (record == null || record.GameId <= 0 ||
                !LeagueDynamicMessageService.SafePuuid(record.SelfPuuid) ||
                !LeagueDynamicMessageService.SafePuuid(record.Puuid))
                return Result(LeagueEncounterMatchDetailStatus.Unavailable);
            if (!SameRegion(initial.Region, record.Region) ||
                !SameRegion(initial.PlatformId, record.PlatformId))
                return Result(LeagueEncounterMatchDetailStatus.WrongAccount);

            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    var own = await _players.LoadProfileAsync(true, deadline.Token).ConfigureAwait(false);
                    if (!IsSameSession(initial)) return Result(LeagueEncounterMatchDetailStatus.NoClient);
                    if (own == null || string.IsNullOrEmpty(own.PuuId))
                        return Result(LeagueEncounterMatchDetailStatus.Unavailable);
                    if (!string.Equals(own.PuuId, record.SelfPuuid, StringComparison.OrdinalIgnoreCase))
                        return Result(LeagueEncounterMatchDetailStatus.WrongAccount);

                    var breakdown = await _players.LoadMatchBreakdownAsync(
                        new LeaguePlayerProfile { PuuId = record.Puuid }, record.GameId, deadline.Token, serverId, sgpOnly, allowSgpFallback)
                        .ConfigureAwait(false);
                    if (!IsSameSession(initial)) return Result(LeagueEncounterMatchDetailStatus.NoClient);
                    if (breakdown == null || breakdown.Match == null || breakdown.Match.GameId != record.GameId)
                    {
                        if (CanUseEogOnlyFallback(initial, serverId, allowSgpFallback))
                        {
                            using (var fallbackDeadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                            {
                                fallbackDeadline.CancelAfter(TimeSpan.FromSeconds(8));
                                var eog = await _players.LoadSgpEogAsync(record.SelfPuuid,
                                    record.Puuid, record.GameId, initial, fallbackDeadline.Token).ConfigureAwait(false);
                                if (!IsSameSession(initial)) return Result(LeagueEncounterMatchDetailStatus.NoClient);
                                if (eog != null && eog.GameId == record.GameId && eog.Players != null &&
                                    eog.Players.Count > 0)
                                    return new LeagueEncounterMatchDetailResult
                                    {
                                        Status = LeagueEncounterMatchDetailStatus.Ready,
                                        Match = BuildEogSummary(eog, initial),
                                        EogOnly = eog,
                                        ChampionNames = _players.TryGetCachedChampionNames(),
                                        IsCurrentSession = () => IsSameSession(initial)
                                    };
                            }
                        }
                        return Result(LeagueEncounterMatchDetailStatus.Unavailable);
                    }
                    var verifiedOwner = await _players.LoadProfileAsync(true, deadline.Token).ConfigureAwait(false);
                    if (!IsSameSession(initial)) return Result(LeagueEncounterMatchDetailStatus.NoClient);
                    if (verifiedOwner == null || string.IsNullOrWhiteSpace(verifiedOwner.PuuId))
                        return Result(LeagueEncounterMatchDetailStatus.Unavailable);
                    if (!string.Equals(verifiedOwner.PuuId, record.SelfPuuid, StringComparison.OrdinalIgnoreCase))
                        return Result(LeagueEncounterMatchDetailStatus.WrongAccount);
                    var timelineOwner = record.SelfPuuid;
                    var timelineMatch = new LeaguePlayerMatchSummary { GameId = breakdown.Match.GameId,
                        DataSource = breakdown.Match.DataSource, SgpServerId = breakdown.Match.SgpServerId };
                    return new LeagueEncounterMatchDetailResult
                    {
                        Status = LeagueEncounterMatchDetailStatus.Ready,
                        Match = breakdown.Match, Participants = breakdown.Participants,
                        Teams = breakdown.Teams,
                        ChampionNames = breakdown.ChampionNames,
                        BuildCatalog = breakdown.BuildCatalog,
                        IsCurrentSession = () => IsSameSession(initial),
                        LoadDryRun = LeagueHistoricalMatchDryRun.Build(breakdown.Match, breakdown.Participants,
                            record.Puuid) != null &&
                            (string.IsNullOrEmpty(breakdown.Match.SgpServerId) ||
                             string.Equals(breakdown.Match.SgpServerId,
                                 LeagueSgpMatchHistoryApiClient.CurrentServerId(initial),
                                 StringComparison.OrdinalIgnoreCase))
                            ? (Func<CancellationToken, Task<LeagueDynamicMessageSnapshot>>)
                                (cancellation => LoadHistoricalDryRunAsync(initial, record.SelfPuuid,
                                    record.Puuid, breakdown, cancellation)) : null,
                        LoadTimeline = cancellation => _players.LoadMatchTimelineAsync(
                            timelineOwner, timelineMatch, cancellation, initial),
                        LoadEog = string.IsNullOrEmpty(serverId) ||
                            string.Equals(serverId, LeagueSgpMatchHistoryApiClient.CurrentServerId(initial),
                                StringComparison.Ordinal)
                            ? (Func<CancellationToken, Task<LeagueSgpEogResult>>)
                                (cancellation => _players.LoadSgpEogAsync(record.SelfPuuid,
                                    record.Puuid, record.GameId, initial, cancellation))
                            : null
                    };
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    return Result(LeagueEncounterMatchDetailStatus.Unavailable);
                }
                catch (Exception)
                {
                    return Result(LeagueEncounterMatchDetailStatus.Unavailable);
                }
            }
        }

        private static LeaguePlayerMatchSummary BuildEogSummary(LeagueSgpEogResult eog,
            LeagueClientSession session)
        {
            var match = LeagueSgpEogProjection.ReplayMetadata(eog) ??
                new LeaguePlayerMatchSummary { GameId = eog.GameId };
            match.GameMode = eog.GameMode;
            match.QueueId = eog.QueueId;
            match.DataSource = LeaguePlayerHistorySource.Sgp;
            match.SgpServerId = LeagueSgpMatchHistoryApiClient.CurrentServerId(session);
            return match;
        }

        internal static bool CanUseEogOnlyFallback(LeagueClientSession session,
            string serverId, bool allowSgpFallback)
        {
            if (!allowSgpFallback || session == null) return false;
            var current = LeagueSgpMatchHistoryApiClient.ResolveServer(session);
            return current != null &&
                (string.IsNullOrEmpty(serverId) ||
                    string.Equals(serverId, current.Id, StringComparison.Ordinal));
        }

        private bool IsSameSession(LeagueClientSession expected)
        {
            var current = _session();
            return expected.Matches(current) && SameRegion(expected.Region, current.Region) &&
                SameRegion(expected.PlatformId, current.PlatformId);
        }

        private static bool SameRegion(string left, string right)
        {
            return string.Equals((left ?? "").Trim(), (right ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private async Task<LeagueDynamicMessageSnapshot> LoadHistoricalDryRunAsync(
            LeagueClientSession expected, string owner, string target,
            LeaguePlayerMatchBreakdown breakdown, CancellationToken token)
        {
            var snapshot = LeagueHistoricalMatchDryRun.Build(breakdown.Match, breakdown.Participants, target);
            if (snapshot == null || !IsSameSession(expected) ||
                !string.IsNullOrEmpty(breakdown.Match.SgpServerId) &&
                !string.Equals(breakdown.Match.SgpServerId,
                    LeagueSgpMatchHistoryApiClient.CurrentServerId(expected),
                    StringComparison.OrdinalIgnoreCase)) return null;

            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(38));
                try
                {
                    var before = await _players.LoadProfileAsync(true, deadline.Token).ConfigureAwait(false);
                    if (before == null || !string.Equals(before.PuuId, owner, StringComparison.Ordinal) ||
                        !IsSameSession(expected)) return null;

                    foreach (var player in snapshot.Players)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        if (!IsSameSession(expected)) return null;
                        using (var playerDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
                        {
                            playerDeadline.CancelAfter(TimeSpan.FromSeconds(3));
                            try
                            {
                                var page = await _players.LoadRecentMatchesAsync(
                                    new LeaguePlayerProfile { PuuId = player.Player.PuuId }, 0,
                                    LeaguePlayerDataService.MaximumMatchCount,
                                    false, playerDeadline.Token).ConfigureAwait(false);
                                if (!IsSameSession(expected)) return null;
                                if (page != null && page.Matches.Count <= LeaguePlayerDataService.MaximumMatchCount &&
                                    page.Matches.All(m => m != null &&
                                        (string.IsNullOrEmpty(m.PuuId) ||
                                         string.Equals(m.PuuId, player.Player.PuuId, StringComparison.Ordinal))))
                                    player.History = page;
                            }
                            catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
                        }
                    }

                    var after = await _players.LoadProfileAsync(true, deadline.Token).ConfigureAwait(false);
                    return after != null && string.Equals(after.PuuId, owner, StringComparison.Ordinal) &&
                        IsSameSession(expected) && !token.IsCancellationRequested ? snapshot : null;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
            }
        }

        private static LeagueEncounterMatchDetailResult Result(LeagueEncounterMatchDetailStatus status)
        {
            return new LeagueEncounterMatchDetailResult { Status = status };
        }
    }
}
