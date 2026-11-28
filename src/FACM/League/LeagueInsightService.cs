using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

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

    internal sealed class LeagueInsightService
    {
        private readonly ILeagueClientApi _reader;
        private readonly LeagueFriendActivityService _friends;
        private readonly LeagueRewardsOverviewService _rewards;
        private readonly LeagueRankedStatusService _ranked;
        private readonly LeagueEndOfGameStatsService _endOfGame;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public LeagueInsightService(ILeagueClientApi reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _friends = new LeagueFriendActivityService(reader);
            _rewards = new LeagueRewardsOverviewService(reader);
            _ranked = new LeagueRankedStatusService(reader);
            _endOfGame = new LeagueEndOfGameStatsService(reader);
        }

        public async Task<LeagueInsightSnapshot> ReadAsync(string phase, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = new LeagueInsightSnapshot();
                var lobby = phase == "Lobby" || phase == "None";
                var queue = phase == "Matchmaking" || phase == "ReadyCheck";
                var postgame = phase == "WaitingForStats" || phase == "PreEndOfGame" || phase == "EndOfGame";
                if (lobby || queue)
                {
                    // Both projections consume the same matchmaking response.
                    var bytes = await ReadGroupAsync(t => _reader.TryGetBytesAsync(LeagueQueueStatusService.SearchPath, t),
                        cancellationToken).ConfigureAwait(false);
                    result.Leaver = LeagueLeaverBusterService.ParseSearch(bytes);
                    result.Queue = LeagueQueueStatusService.ParseSearch(bytes);
                }
                if (lobby)
                {
                    result.Friends = await ReadGroupAsync(_friends.ReadAsync, cancellationToken).ConfigureAwait(false);
                    result.Ranked = await ReadGroupAsync(_ranked.ReadRankedAsync, cancellationToken).ConfigureAwait(false);
                    result.Rewards = await ReadGroupAsync(_rewards.ReadAsync, cancellationToken).ConfigureAwait(false);
                }
                if (postgame)
                {
                    var before = await ReadGroupAsync(t => _reader.TryGetBytesAsync(LeagueLiveDataService.GameflowSessionPath, t),
                        cancellationToken).ConfigureAwait(false);
                    var gameId = GameId(before);
                    if (gameId > 0)
                    {
                        var stats = await ReadGroupAsync(_endOfGame.ReadAsync, cancellationToken).ConfigureAwait(false);
                        var after = await ReadGroupAsync(t => _reader.TryGetBytesAsync(LeagueLiveDataService.GameflowSessionPath, t),
                            cancellationToken).ConfigureAwait(false);
                        if (stats != null && stats.Available && stats.GameId == gameId && GameId(after) == gameId)
                            result.EndOfGame = stats;
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            finally { _gate.Release(); }
        }

        private static long GameId(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return 0;
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                object raw, id;
                var game = root != null && root.TryGetValue("gameData", out raw) ? raw as Dictionary<string, object> : null;
                long value;
                return game != null && game.TryGetValue("gameId", out id) &&
                    long.TryParse(Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture), out value) ? value : 0;
            }
            catch (ArgumentException) { return 0; }
            catch (InvalidOperationException) { return 0; }
        }

        private static async Task<T> ReadGroupAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken token)
            where T : class
        {
            token.ThrowIfCancellationRequested();
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                try { return await read(deadline.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    return null;
                }
                catch (Exception e)
                {
                    AppLog.Info("League insight group unavailable: " + e.GetType().Name);
                    return null;
                }
            }
        }

        public Task<LeagueLpChangeNotice> ReadLpChangeAsync(CancellationToken cancellationToken)
        {
            return _ranked.ReadLpChangeAsync(cancellationToken);
        }
    }
}
