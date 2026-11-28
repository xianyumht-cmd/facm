using System;
using System.Collections.Generic;
using System.Linq;
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

    internal static class LeagueInsightPresentation
    {
        internal static Dictionary<string, string> Project(LeagueInsightSnapshot data, string phase)
        {
            var lobby = phase == "Lobby" || phase == "None";
            var queue = phase == "Matchmaking" || phase == "ReadyCheck";
            var postgame = phase == "WaitingForStats" || phase == "PreEndOfGame" || phase == "EndOfGame";
            var other = string.IsNullOrEmpty(phase) ? "等待客户端连接" : "当前阶段不适用";
            var ranked = data == null ? null : data.Ranked;
            var rankedText = "段位数据未知";
            if (ranked != null && ranked.Available && ranked.Queues != null)
            {
                var rows = ranked.Queues.Where(x => x != null &&
                    (x.QueueType == "RANKED_SOLO_5x5" || x.QueueType == "RANKED_FLEX_SR")).Take(2)
                    .Select(x => x.Display.Replace("RANKED_SOLO_5x5", "单双排").Replace("RANKED_FLEX_SR", "灵活组排")).ToArray();
                rankedText = rows.Length == 0 ? "未报告单双排或灵活段位" : string.Join(" · ", rows);
            }
            var game = data == null ? null : data.EndOfGame;
            var gameText = "本局统计未确认";
            if (game != null && game.Available && game.Players != null)
            {
                var local = game.Players.Where(x => x.IsLocalPlayer).Take(2).ToArray();
                if (local.Length == 1)
                {
                    var p = local[0];
                    var fields = new List<string>();
                    if (p.KdaKnown) fields.Add(p.Kills + "/" + p.Deaths + "/" + p.Assists);
                    if (p.DamageKnown) fields.Add("伤害 " + p.DamageToChampions);
                    if (p.GoldKnown) fields.Add("经济 " + p.GoldEarned);
                    if (p.MinionsKnown) fields.Add("补刀 " + p.MinionsKilled);
                    if (fields.Count > 0) gameText = string.Join(" · ", fields);
                }
            }
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "penalty", lobby || queue ? (data?.Leaver?.Summary ?? "排队限制状态未知") : other },
                { "queue", lobby || queue ? (data?.Queue?.Summary ?? "队列状态未知") : other },
                { "friends", lobby ? (data?.Friends?.Summary ?? "好友状态未知") : other },
                { "rewards", lobby ? (data?.Rewards?.Summary ?? "奖励数据未知") : other },
                { "ranked", lobby ? rankedText : other },
                { "postgame", postgame ? gameText : other }
            };
        }
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

        internal static bool IsApplicablePhase(string phase)
        {
            return phase == "Lobby" || phase == "None" || phase == "Matchmaking" || phase == "ReadyCheck" ||
                phase == "WaitingForStats" || phase == "PreEndOfGame" || phase == "EndOfGame";
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
                    result.Rewards = await _rewards.ReadAsync(cancellationToken).ConfigureAwait(false);
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

        internal static async Task<T> ReadGroupAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken token)
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
