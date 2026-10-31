using System;
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
                deadline.CancelAfter(TimeSpan.FromSeconds(allowSgpFallback ? 16 : 8));
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
                            var eog = await _players.LoadSgpEogAsync(record.SelfPuuid,
                                record.Puuid, record.GameId, initial, deadline.Token).ConfigureAwait(false);
                            if (!IsSameSession(initial)) return Result(LeagueEncounterMatchDetailStatus.NoClient);
                            if (eog != null && eog.GameId == record.GameId && eog.Players != null &&
                                eog.Players.Count > 0)
                                return new LeagueEncounterMatchDetailResult
                                {
                                    Status = LeagueEncounterMatchDetailStatus.Ready,
                                    Match = new LeaguePlayerMatchSummary { GameId = record.GameId,
                                        GameMode = eog.GameMode, QueueId = eog.QueueId,
                                        DataSource = LeaguePlayerHistorySource.Sgp,
                                        SgpServerId = LeagueSgpMatchHistoryApiClient.CurrentServerId(initial) },
                                    EogOnly = eog,
                                    IsCurrentSession = () => IsSameSession(initial)
                                };
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

        private static LeagueEncounterMatchDetailResult Result(LeagueEncounterMatchDetailStatus status)
        {
            return new LeagueEncounterMatchDetailResult { Status = status };
        }
    }
}
