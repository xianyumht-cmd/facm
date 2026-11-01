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
        internal static void Validate()
        {
            ValidateCardFilters();
            ValidateAsync().GetAwaiter().GetResult();
        }

        private static void ValidateCardFilters()
        {
            var a = Sparse(301); a.ChampionId = 22; a.Position = "TOP";
            var b = Sparse(302); b.ChampionId = 22; b.Position = "TOP"; b.WinResult = "loss";
            var c = Sparse(303); c.ChampionId = 55; c.Position = "JUNGLE";
            var foreign = Sparse(304); foreign.PuuId = "someone-else";
            foreign.ChampionId = 99; foreign.ChampionName = "untrusted";
            var matches = new List<LeaguePlayerMatchSummary> { a, b, c, foreign };
            var all = LeagueOngoingHistoryFilter.Select(matches, "target", 0, "");
            Require(all.Count == 3 &&
                LeagueOngoingHistoryFilter.Champions(matches, "target").SequenceEqual(new[] { 22, 55 }) &&
                LeagueOngoingHistoryFilter.Positions(matches, "target").SequenceEqual(new[] { "TOP", "JUNGLE" }),
                "Card filter options must use only verified target rows.");
            var selected = LeagueOngoingHistoryFilter.Select(matches, "target", 22, "TOP");
            Require(selected.Count == 2 && selected.All(m => m.ChampionId == 22 && m.Position == "TOP") &&
                LeagueOngoingHistoryFilter.Select(matches, "target", 55, "TOP").Count == 0 &&
                LeagueOngoingHistoryFilter.Select(matches, "target", 0, "MID").Count == 0,
                "Champion and position selection must intersect without guessing missing roles.");
            b.GameMode = "CHERRY";
            c.GameMode = null;
            Require(LeagueOngoingHistoryFilter.Select(matches, "target", 55, "JUNGLE", "NORMAL").Count == 0,
                "Unknown mode evidence must not enter normal-mode card analysis.");
            c.GameMode = "CLASSIC";
            Require(LeagueOngoingHistoryFilter.Select(matches, "target", 22, "TOP", "CHERRY").Count == 1 &&
                LeagueOngoingHistoryFilter.Select(matches, "target", 22, "TOP", "NORMAL").Count == 1 &&
                LeagueOngoingHistoryFilter.Select(matches, "target", 55, "JUNGLE", "NORMAL").Count == 1 &&
                LeagueOngoingHistoryFilter.Select(matches, "target", 22, "TOP", "unknown").Count == 0,
                "Mode filters must intersect verified champion and position evidence.");
            var breakdown = LeagueOngoingCardBreakdown.Project(matches, "target");
            Require(breakdown.Count(row => row.Kind == "champion") == 2 &&
                breakdown.Count(row => row.Kind == "champion-position") == 2 &&
                breakdown.Count(row => row.Kind == "champion-mode") == 2 &&
                breakdown.Count(row => row.Kind == "position-mode") == 2 &&
                breakdown.Single(row => row.Kind == "champion-position" &&
                    row.ChampionId == 22 && row.Position == "TOP").Analysis.Games == 2 &&
                breakdown.Single(row => row.Kind == "champion-mode" &&
                    row.ChampionId == 22 && row.Mode == "CHERRY").Analysis.Games == 1 &&
                breakdown.Single(row => row.Kind == "position-mode" &&
                    row.Position == "TOP" && row.Mode == "CHERRY").Analysis.Games == 1 &&
                breakdown.Count(row => row.Kind == "position") == 2 &&
                breakdown.Count(row => row.Kind == "mode") == 2 &&
                breakdown.Single(row => row.Kind == "champion" && row.ChampionId == 22)
                    .Analysis.Games == 2 &&
                breakdown.Single(row => row.Kind == "mode" && row.Mode == "CHERRY")
                    .Analysis.Games == 1 &&
                LeagueOngoingCardBreakdown.Project(matches, "bad id!").Count == 0,
                "Card drilldown must use only verified champion, role and mode evidence.");
            Require(LeagueOngoingHistoryFilter.NeedsCollection(matches, "target", 0, "", 40, 0, "CHERRY") &&
                LeagueOngoingHistoryFilter.NeedsCollection(matches, "target", 22, "TOP", 40, 0, "NORMAL") &&
                !LeagueOngoingHistoryFilter.NeedsCollection(matches, "target", 0, "", 40, 0, "bad") &&
                !LeagueOngoingHistoryFilter.NeedsCollection(matches, "target", 0, "", 200, 0, "CHERRY"),
                "Mode-only collection must stay bounded and reject unknown mode requests.");
            matches.Add(Sparse(301));
            Require(LeagueOngoingHistoryFilter.Select(matches, "target", 22, "TOP").Count == 1,
                "Ambiguous duplicate game IDs cannot enter filtered player analysis.");
            Require(LeagueOngoingHistoryFilter.Select(matches, "target", 0, "").Count == 2,
                "An invalid page cannot contaminate other verified card rows.");
            Require(LeagueOngoingHistoryFilter.NeedsCollection(matches, "target", 22, "TOP", 40, 0) &&
                !LeagueOngoingHistoryFilter.NeedsCollection(matches, "target", 0, "", 40, 0) &&
                !LeagueOngoingHistoryFilter.NeedsCollection(matches, "target", 22, "TOP", 200, 0) &&
                !LeagueOngoingHistoryFilter.NeedsCollection(matches, "target", 22, "TOP", 40, 5) &&
                !LeagueOngoingHistoryFilter.NeedsCollection(matches, "target", 22, "MID", 40, 0) &&
                !LeagueOngoingHistoryFilter.NeedsCollection(matches, "not valid!", 22, "TOP", 40, 0),
                "Filtered collection must be explicit, page-bounded and reject unsupported roles.");
            var enough = Enumerable.Range(401, 20).Select(i =>
            {
                var row = Sparse(i); row.ChampionId = 22; row.Position = "TOP"; return row;
            }).ToList();
            Require(!LeagueOngoingHistoryFilter.NeedsCollection(enough, "target", 22, "TOP", 60, 0),
                "Filtered collection must stop after twenty matching verified games.");
        }

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
            var capped = await service.HydrateAnalysisAsync("self", "target", session,
                rows, null, CancellationToken.None, 3).ConfigureAwait(false);
            Require(capped.Attempted.Count == 3 && capped.Updated == 3 && api.Games == 23,
                "Configurable detail prefetch must cap the exact number of source reads.");
            var next = await service.HydrateAnalysisAsync("self", "target", session, result.Matches,
                result.Attempted, CancellationToken.None).ConfigureAwait(false);
            Require(next.Attempted.Count == 2 && api.Games == 25,
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
            Require(await service.VerifyTimelineOwnerAsync("self", session, CancellationToken.None),
                "The final timeline-batch owner check accepts an unchanged account and session.");
            api.Changed = true;
            Require(!await service.VerifyTimelineOwnerAsync("self", session, CancellationToken.None),
                "A same-process account switch invalidates all staged timeline results.");
            api.Changed = false;
            active = new LeagueClientSession("LeagueClientUx", 2, 1001, "fixture", "https", "fixture", "HN10", "TENCENT");
            Require(!await service.VerifyTimelineOwnerAsync("self", session, CancellationToken.None),
                "A changed client session or region invalidates staged timeline results.");
            active = session;
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
