using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Performance;

namespace FACM.League
{
    internal static class LeaguePlayerHistoryHydrationSmokeTest
    {
        internal static void Validate() { ValidateAsync().GetAwaiter().GetResult(); }

        private static async Task ValidateAsync()
        {
            var session = new LeagueClientSession("LeagueClientUx", 1, 1000, "fixture", "https", "fixture", "HN1", "TENCENT");
            var active = session;
            var api = new Api();
            var sgp = new Sgp();
            var service = new LeaguePlayerDataService(api, new PerformanceBudgetProvider(), null, sgp, () => active);
            var rows = Enumerable.Range(1, 22).Select(i => Sparse(i)).ToList();
            rows.Insert(1, Sparse(1));
            var result = await service.HydrateAnalysisAsync("self", "target", session, rows, null,
                CancellationToken.None).ConfigureAwait(false);
            Require(result.Attempted.Count == 20 && result.Updated == 20 && api.Games == 20 &&
                !result.AccountChanged && rows.All(m => m.ParticipantRoster == null) &&
                result.Matches[0].ParticipantRoster != null && result.Matches[0].PuuId == "target" &&
                service.TryGetCachedPage() == null && service.TryGetCachedProfile() == null && sgp.Calls == 0,
                "Analysis hydration must cap unique reads, preserve target, detach snapshots and leave self caches alone.");
            var next = await service.HydrateAnalysisAsync("self", "target", session, result.Matches,
                result.Attempted, CancellationToken.None).ConfigureAwait(false);
            Require(next.Attempted.Count == 2 && api.Games == 22,
                "Continuing hydration must skip attempted duplicates and reach the remainder.");
            var complete = result.Matches[0];
            Require(!LeaguePlayerDataService.NeedsAnalysisDetail(complete) &&
                LeagueHistoryAkariScore.Calculate(new[] { complete }) != null,
                "Source-complete hydrated statistics must become scoreable without another read.");
            var original = Sparse(50); original.GoldEarned = 99;
            var missing = Sparse(50);
            Require(!LeaguePlayerDataService.CanReplaceAnalysisMatch(original, missing, "target"),
                "A detail replacement cannot erase an already known analysis metric.");

            api.WrongGame = true;
            var bad = await service.HydrateAnalysisAsync("self", "target", session,
                new[] { Sparse(70) }, null, CancellationToken.None).ConfigureAwait(false);
            Require(bad.Updated == 0, "Wrong-game responses cannot enrich a selected game.");
            api.WrongGame = false; api.WrongTarget = true;
            bad = await service.HydrateAnalysisAsync("self", "target", session,
                new[] { Sparse(71) }, null, CancellationToken.None).ConfigureAwait(false);
            Require(bad.Updated == 0, "Wrong-target details cannot replace a selected participant.");
            api.WrongTarget = false; api.SwitchAccount = true;
            bad = await service.HydrateAnalysisAsync("self", "target", session,
                new[] { Sparse(72), Sparse(73) }, null, CancellationToken.None).ConfigureAwait(false);
            Require(bad.AccountChanged && bad.Matches.Count == 0 && bad.Updated == 0,
                "Same-process account changes must discard the entire result.");
            api.SwitchAccount = false; api.Changed = false;
            active = new LeagueClientSession("LeagueClientUx", 2, 1001, "fixture", "https", "fixture", "HN10", "TENCENT");
            var reads = api.Games;
            bad = await service.HydrateAnalysisAsync("self", "target", session,
                new[] { Sparse(74) }, null, CancellationToken.None).ConfigureAwait(false);
            Require(bad.AccountChanged && api.Games == reads, "Replaced sessions must reject detail reads.");
            active = session;

            api.TimeoutGame = 80;
            var partial = await service.HydrateAnalysisAsync("self", "target", session,
                new[] { Sparse(80), Sparse(81) }, null, CancellationToken.None).ConfigureAwait(false);
            Require(partial.TimedOut && partial.Attempted.Count == 2 && partial.Updated == 1 &&
                partial.Matches[0].ParticipantRoster == null && partial.Matches[1].ParticipantRoster != null,
                "One bounded failure must preserve its sparse row and allow the next explicit candidate.");
            using (var cancellation = new CancellationTokenSource())
            {
                api.Cancel = cancellation.Cancel;
                bool cancelled = false;
                try
                {
                    await service.HydrateAnalysisAsync("self", "target", session,
                        new[] { Sparse(90) }, null, cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { cancelled = true; }
                Require(cancelled, "User cancellation must propagate and discard the uncommitted batch.");
                api.Cancel = null;
            }

            var regional = Sparse(100);
            regional.DataSource = LeaguePlayerHistorySource.Sgp;
            regional.SgpServerId = "HN10";
            reads = api.Games;
            var remote = await service.HydrateAnalysisAsync("self", "target", session,
                new[] { regional }, null, CancellationToken.None).ConfigureAwait(false);
            Require(remote.Updated == 1 && remote.Matches[0].SgpServerId == "HN10" &&
                sgp.Server == "HN10" && sgp.Calls == 1 && api.Games == reads,
                "Regional enrichment must use the selected SUMMARY endpoint without an LCU game lookup.");
            var noWork = await service.HydrateAnalysisAsync("self", "target", session,
                new[] { complete }, null, CancellationToken.None).ConfigureAwait(false);
            Require(noWork.Attempted.Count == 0 && api.Games == reads,
                "Complete records must not trigger game-detail traffic.");
        }

        private static LeaguePlayerMatchSummary Sparse(long id)
        {
            return new LeaguePlayerMatchSummary { GameId = id, PuuId = "target",
                DataSource = LeaguePlayerHistorySource.Lcu, GameType = "MATCHED_GAME",
                GameMode = "CLASSIC", GameDurationSeconds = 1200, ChampionId = 22, QueueId = 420,
                ParticipantResolved = true, StatisticsAvailable = true, WinResult = "win",
                ParticipantId = 1, TeamId = 100 };
        }

        private static byte[] Game(long id, string target)
        {
            var stats = new { win = true, kills = 4, deaths = 2, assists = 8,
                totalMinionsKilled = 120, neutralMinionsKilled = 0, totalDamageDealtToChampions = 10000,
                totalDamageTaken = 9000, goldEarned = 10000, visionScore = 10, totalHeal = 1000 };
            return Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new {
                gameId = id, gameType = "MATCHED_GAME", gameMode = "CLASSIC", gameDuration = 1200,
                queueId = 420, participants = new[] { new { participantId = 1, puuid = target,
                    teamId = 100, championId = 22, stats,
                    win = true, kills = 4, deaths = 2, assists = 8, totalMinionsKilled = 120,
                    neutralMinionsKilled = 0, totalDamageDealtToChampions = 10000,
                    totalDamageTaken = 9000, goldEarned = 10000, visionScore = 10, totalHeal = 1000 } },
                participantIdentities = new[] { new { participantId = 1, player = new { puuid = target } } }
            }));
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private sealed class Api : ILeagueClientApi
        {
            internal int Games;
            internal long TimeoutGame;
            internal bool WrongGame, WrongTarget, SwitchAccount, Changed;
            internal Action Cancel;
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (path == LeagueDashboardDetailsService.SummonerPath)
                    return Task.FromResult(Encoding.UTF8.GetBytes(Changed ? "{\"puuid\":\"other\"}" : "{\"puuid\":\"self\"}"));
                if (!path.StartsWith("/lol-match-history/v1/games/", StringComparison.Ordinal))
                    return Task.FromResult<byte[]>(null);
                Games++;
                var id = long.Parse(path.Substring(path.LastIndexOf('/') + 1),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (SwitchAccount) Changed = true;
                Cancel?.Invoke(); token.ThrowIfCancellationRequested();
                if (id == TimeoutGame) throw new OperationCanceledException();
                return Task.FromResult(Game(WrongGame ? id + 1 : id, WrongTarget ? "other" : "target"));
            }
        }

        private sealed class Sgp : ILeagueSgpMatchHistoryApi
        {
            internal int Calls;
            internal string Server;
            public Task<byte[]> TryGetGameSummaryAsync(long id, CancellationToken token, string serverId = null)
            {
                Calls++; Server = serverId;
                return Task.FromResult(Encoding.UTF8.GetBytes("{\"json\":" +
                    Encoding.UTF8.GetString(Game(id, "target")) + "}"));
            }
            public Task<byte[]> TryGetSummaryAsync(string puuid, int start, int count, CancellationToken token, string serverId = null)
            { return Task.FromResult<byte[]>(null); }
            public Task<byte[]> TryGetGameDetailsAsync(long id, CancellationToken token, string serverId = null)
            { throw new InvalidOperationException("SUMMARY hydration must not request timeline DETAILS."); }
            public Task<byte[]> TryGetSummonerAsync(string puuid, CancellationToken token, string serverId = null)
            { return Task.FromResult<byte[]>(null); }
        }
    }
}
