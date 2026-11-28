using System;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal sealed class LeagueInsightSnapshot
    {
        public LeagueLeaverBusterStatus Leaver { get; set; }
        public LeagueQueueStatus Queue { get; set; }
        public LeagueFriendActivitySnapshot Friends { get; set; }
        public LeagueRewardsOverview Rewards { get; set; }
        public LeagueRankedSnapshot Ranked { get; set; }
        public LeagueEndOfGameSummary EndOfGame { get; set; }
    }

    /// <summary>
    /// Single entry point for the read-only client insights that the stock client either hides or
    /// surfaces only as a vague status line: leaver-buster state, queue estimate, friend activity,
    /// reward counts, ranked standings and post-game stats.
    /// </summary>
    internal sealed class LeagueInsightService
    {
        private readonly LeagueLeaverBusterService _leaver;
        private readonly LeagueQueueStatusService _queue;
        private readonly LeagueFriendActivityService _friends;
        private readonly LeagueRewardsOverviewService _rewards;
        private readonly LeagueRankedStatusService _ranked;
        private readonly LeagueEndOfGameStatsService _endOfGame;

        public LeagueInsightService(ILeagueClientApi reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            _leaver = new LeagueLeaverBusterService(reader);
            _queue = new LeagueQueueStatusService(reader);
            _friends = new LeagueFriendActivityService(reader);
            _rewards = new LeagueRewardsOverviewService(reader);
            _ranked = new LeagueRankedStatusService(reader);
            _endOfGame = new LeagueEndOfGameStatsService(reader);
        }

        public async Task<LeagueInsightSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            var leaver = _leaver.ReadAsync(cancellationToken);
            var queue = _queue.ReadAsync(cancellationToken);
            var friends = _friends.ReadAsync(cancellationToken);
            var rewards = _rewards.ReadAsync(cancellationToken);
            var ranked = _ranked.ReadRankedAsync(cancellationToken);
            var endOfGame = _endOfGame.ReadAsync(cancellationToken);

            await Task.WhenAll(leaver, queue, friends, rewards, ranked, endOfGame).ConfigureAwait(false);

            return new LeagueInsightSnapshot
            {
                Leaver = leaver.Result,
                Queue = queue.Result,
                Friends = friends.Result,
                Rewards = rewards.Result,
                Ranked = ranked.Result,
                EndOfGame = endOfGame.Result
            };
        }

        public Task<LeagueLpChangeNotice> ReadLpChangeAsync(CancellationToken cancellationToken)
        {
            return _ranked.ReadLpChangeAsync(cancellationToken);
        }
    }
}
