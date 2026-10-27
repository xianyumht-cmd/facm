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
        internal LeagueBuildAdvisorCatalog BuildCatalog;
    }

    // Single user-requested detail read; never observes a game or fetches match history in the background.
    internal sealed class LeagueEncounterMatchDetailService
    {
        private readonly LeaguePlayerDataService _players;
        private readonly Func<LeagueClientSession> _session;

        internal LeagueEncounterMatchDetailService(ILeagueClientApi client,
            PerformanceBudgetProvider budgets, Func<LeagueClientSession> session)
        {
            _players = new LeaguePlayerDataService(client, budgets);
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal async Task<LeagueEncounterMatchDetailResult> ReadAsync(
            LeagueEncounterRecord record, CancellationToken token)
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
                        new LeaguePlayerProfile { PuuId = record.Puuid }, record.GameId, deadline.Token)
                        .ConfigureAwait(false);
                    if (!IsSameSession(initial)) return Result(LeagueEncounterMatchDetailStatus.NoClient);
                    if (breakdown == null || breakdown.Match == null || breakdown.Match.GameId != record.GameId)
                        return Result(LeagueEncounterMatchDetailStatus.Unavailable);
                    var verifiedOwner = await _players.LoadProfileAsync(true, deadline.Token).ConfigureAwait(false);
                    if (!IsSameSession(initial)) return Result(LeagueEncounterMatchDetailStatus.NoClient);
                    if (verifiedOwner == null || string.IsNullOrWhiteSpace(verifiedOwner.PuuId))
                        return Result(LeagueEncounterMatchDetailStatus.Unavailable);
                    if (!string.Equals(verifiedOwner.PuuId, record.SelfPuuid, StringComparison.OrdinalIgnoreCase))
                        return Result(LeagueEncounterMatchDetailStatus.WrongAccount);
                    return new LeagueEncounterMatchDetailResult
                    {
                        Status = LeagueEncounterMatchDetailStatus.Ready,
                        Match = breakdown.Match, Participants = breakdown.Participants,
                        BuildCatalog = breakdown.BuildCatalog
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
