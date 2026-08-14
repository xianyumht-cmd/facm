using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;

namespace FACM.League
{
    internal static class LeaguePlayerSmokeTest
    {
        public static void Validate()
        {
            ValidateProfileAndRecentMatchParsing();
            ValidateProgressiveEnrichmentBudget();
            ValidateChampionMetadataAndStats();
            ValidatePaginationBoundary();
            ValidateCancellation();
            if (!LeaguePlayerUiBridge.HasTrayAccessForSmokeTest())
                throw new InvalidOperationException("Player tray bridge lost MainForm tray access.");
        }

        private static void ValidateProfileAndRecentMatchParsing()
        {
            var profileJson = "{\"puuid\":\"puuid-current\",\"summonerId\":9001,\"accountId\":8001,\"gameName\":\"Player\",\"tagLine\":\"CQ100\",\"displayName\":\"Player\",\"summonerLevel\":321,\"profileIconId\":12}";
            var historyJson = "{\"accountId\":8001,\"platformId\":\"CQ100\",\"games\":{\"gameCount\":42,\"gameIndexBegin\":0,\"gameIndexEnd\":1,\"games\":[" +
                "{\"gameId\":10001,\"gameCreation\":1700000000000,\"gameDuration\":1260,\"gameMode\":\"ARAM\",\"queueId\":450,\"participantIdentities\":[{\"participantId\":3,\"player\":{\"puuid\":\"puuid-current\",\"summonerId\":9001}}],\"participants\":[{\"participantId\":3,\"championId\":145,\"stats\":{\"kills\":8,\"deaths\":4,\"assists\":12,\"totalMinionsKilled\":44,\"neutralMinionsKilled\":3,\"win\":true}}]}," +
                "{\"gameId\":10002,\"gameCreation\":1700001000000,\"gameDuration\":1800,\"gameMode\":\"CLASSIC\",\"queueId\":420,\"participantIdentities\":[{\"participantId\":7,\"player\":{\"summonerId\":9001}}],\"participants\":[{\"participantId\":7,\"championId\":22,\"stats\":{\"kills\":2,\"deaths\":7,\"assists\":5,\"totalMinionsKilled\":151,\"neutralMinionsKilled\":0,\"win\":false}}]}" +
                "]}}";

            var api = new FixtureApi(profileJson, historyJson, null);
            var service = new LeaguePlayerDataService(api, new PerformanceBudgetProvider());
            var profile = service.LoadProfileAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            Require(profile != null, "Player profile did not parse.");
            Require(profile.PuuId == "puuid-current", "Player PUUID changed unexpectedly.");
            Require(profile.AccountName == "Player#CQ100", "Player Riot ID display changed unexpectedly.");
            Require(profile.SummonerLevel == 321, "Player level did not parse.");

            var page = service.LoadRecentMatchesAsync(profile, 0, 10, true, CancellationToken.None).GetAwaiter().GetResult();
            Require(page != null && page.Matches.Count == 2, "Recent match page did not parse expected rows.");
            Require(page.ReportedGameCount == 42 && !page.HasMore, "Partial fixture must not claim another page solely from gameCount.");
            Require(api.Paths.Contains("/lol-match-history/v1/products/lol/puuid-current/matches?begIndex=0&endIndex=9"), "Player history request must use bounded 0..9 Gate 1 page.");

            var first = page.Matches[0];
            Require(first.ParticipantResolved && first.ChampionId == 145, "Current participant was not resolved by PUUID.");
            Require(first.Kills == 8 && first.Deaths == 4 && first.Assists == 12, "KDA parsing changed unexpectedly.");
            Require(first.CreepScore == 47 && first.Win, "CS/result parsing changed unexpectedly.");
            var second = page.Matches[1];
            Require(second.ParticipantResolved && !second.Win && second.QueueId == 420, "Summoner-ID participant fallback changed unexpectedly.");
        }

        private static void ValidateProgressiveEnrichmentBudget()
        {
            var profile = new LeaguePlayerProfile { PuuId = "puuid-current", SummonerId = 9001 };
            var page = new LeaguePlayerMatchPage { StartIndex = 0, RequestedCount = 10, ReportedGameCount = 1 };
            page.Matches.Add(new LeaguePlayerMatchSummary { GameId = 20001, GameMode = "ARAM", ParticipantResolved = false });
            var detail = "{\"gameId\":20001,\"gameCreation\":1700002000000,\"gameDuration\":900,\"gameMode\":\"ARAM\",\"queueId\":450,\"participantIdentities\":[{\"participantId\":4,\"player\":{\"puuid\":\"puuid-current\",\"summonerId\":9001}}],\"participants\":[{\"participantId\":4,\"championId\":99,\"stats\":{\"kills\":11,\"deaths\":3,\"assists\":18,\"totalMinionsKilled\":55,\"neutralMinionsKilled\":2,\"win\":true}}]}";
            var details = new Dictionary<long, string> { { 20001, detail } };
            var provider = new PerformanceBudgetProvider();
            var api = new FixtureApi("{}", "{}", details);
            var service = new LeaguePlayerDataService(api, provider);

            var enriched = service.EnrichIncompleteMatchesAsync(profile, page, CancellationToken.None).GetAwaiter().GetResult();
            Require(enriched != null && enriched.Matches[0].ParticipantResolved, "Visible Player page did not enrich an incomplete history summary.");
            Require(enriched.Matches[0].ChampionId == 99 && enriched.Matches[0].Kills == 11, "Player detail enrichment did not apply full-game stats.");
            Require(api.Paths.Contains("/lol-match-history/v1/games/20001"), "Player detail enrichment did not use the verified LCU game-detail endpoint.");

            provider.UpdateLeagueActivity(LeagueActivityLevel.InGame);
            var inGamePage = new LeaguePlayerMatchPage { StartIndex = 0, RequestedCount = 10, ReportedGameCount = 1 };
            inGamePage.Matches.Add(new LeaguePlayerMatchSummary { GameId = 20002, ParticipantResolved = false });
            var before = api.Paths.Count;
            var suppressed = service.EnrichIncompleteMatchesAsync(profile, inGamePage, CancellationToken.None).GetAwaiter().GetResult();
            Require(suppressed != null && !suppressed.Matches[0].ParticipantResolved, "In-game Player enrichment unexpectedly fabricated detail state.");
            Require(api.Paths.Count == before, "In-game Player page must not perform automatic per-game detail prefetch.");
        }

        private static void ValidateChampionMetadataAndStats()
        {
            const string championSummary = "[{\"id\":-1,\"name\":\"None\"},{\"id\":145,\"name\":\"卡莎\"},{\"id\":22,\"name\":\"艾希\"}]";
            var profile = new LeaguePlayerProfile { PuuId = "puuid-current", SummonerId = 9001 };
            var page = new LeaguePlayerMatchPage { StartIndex = 0, RequestedCount = 10, ReportedGameCount = 3 };
            page.Matches.Add(new LeaguePlayerMatchSummary { GameId = 30001, ChampionId = 145, Kills = 8, Deaths = 4, Assists = 12, Win = true, ParticipantResolved = true });
            page.Matches.Add(new LeaguePlayerMatchSummary { GameId = 30002, ChampionId = 145, Kills = 4, Deaths = 6, Assists = 8, Win = false, ParticipantResolved = true });
            page.Matches.Add(new LeaguePlayerMatchSummary { GameId = 30003, ChampionId = 22, Kills = 2, Deaths = 3, Assists = 9, Win = true, ParticipantResolved = true });

            var provider = new PerformanceBudgetProvider();
            var api = new FixtureApi("{}", "{}", null, championSummary);
            var service = new LeaguePlayerDataService(api, provider);
            var named = service.EnrichChampionNamesAsync(profile, page, CancellationToken.None).GetAwaiter().GetResult();
            Require(named != null && named.Matches[0].ChampionName == "卡莎" && named.Matches[2].ChampionName == "艾希", "Champion-summary ID mapping failed.");
            Require(CountPath(api.Paths, LeaguePlayerDataService.ChampionSummaryPath) == 1, "Champion metadata must use one bounded summary request.");
            Require(CountPrefix(api.Paths, "/lol-match-history/") == 0, "Champion metadata must not add match-history fan-out.");

            var cachedAgain = service.EnrichChampionNamesAsync(profile, page, CancellationToken.None).GetAwaiter().GetResult();
            Require(cachedAgain.Matches[0].ChampionName == "卡莎", "Champion metadata cache did not remain usable.");
            Require(CountPath(api.Paths, LeaguePlayerDataService.ChampionSummaryPath) == 1, "Fresh champion metadata cache unexpectedly refetched.");

            var stats = service.BuildChampionStats(named);
            Require(stats.Count == 2, "Loaded-match champion stats did not group expected champions.");
            Require(stats[0].ChampionId == 145 && stats[0].Games == 2 && stats[0].Wins == 1, "Champion stats games/wins changed unexpectedly.");
            Require(Math.Abs(stats[0].WinRate - 50d) < 0.001d, "Champion stats win rate changed unexpectedly.");
            Require(Math.Abs(stats[0].AverageKills - 6d) < 0.001d && Math.Abs(stats[0].AverageDeaths - 5d) < 0.001d && Math.Abs(stats[0].AverageAssists - 10d) < 0.001d,
                "Champion stats average K/D/A changed unexpectedly.");
            Require(CountPrefix(api.Paths, "/lol-match-history/") == 0, "Pure champion aggregation unexpectedly performed network work.");

            provider.UpdateLeagueActivity(LeagueActivityLevel.InGame);
            var cachedInGame = new LeaguePlayerMatchPage { StartIndex = 0, RequestedCount = 10, ReportedGameCount = 1 };
            cachedInGame.Matches.Add(new LeaguePlayerMatchSummary { GameId = 30004, ChampionId = 145, ParticipantResolved = true });
            var beforeCachedInGame = api.Paths.Count;
            var cachedSensitive = service.EnrichChampionNamesAsync(profile, cachedInGame, CancellationToken.None).GetAwaiter().GetResult();
            Require(cachedSensitive.Matches[0].ChampionName == "卡莎", "Sensitive phase should still use an existing champion-name cache.");
            Require(api.Paths.Count == beforeCachedInGame, "Sensitive phase unexpectedly refreshed champion metadata despite a cache.");

            var inGameProvider = new PerformanceBudgetProvider();
            inGameProvider.UpdateLeagueActivity(LeagueActivityLevel.InGame);
            var inGameApi = new FixtureApi("{}", "{}", null, championSummary);
            var inGameService = new LeaguePlayerDataService(inGameApi, inGameProvider);
            var noCachePage = new LeaguePlayerMatchPage { StartIndex = 0, RequestedCount = 10, ReportedGameCount = 1 };
            noCachePage.Matches.Add(new LeaguePlayerMatchSummary { GameId = 30005, ChampionId = 145, ParticipantResolved = true });
            var suppressed = inGameService.EnrichChampionNamesAsync(profile, noCachePage, CancellationToken.None).GetAwaiter().GetResult();
            Require(string.IsNullOrWhiteSpace(suppressed.Matches[0].ChampionName), "In-game metadata suppression unexpectedly fabricated a champion name.");
            Require(CountPath(inGameApi.Paths, LeaguePlayerDataService.ChampionSummaryPath) == 0, "In-game Player page must not start nonessential champion metadata requests.");
        }

        private static void ValidatePaginationBoundary()
        {
            Require(LeaguePlayerDataService.InitialMatchCount == 10, "Player Gate 1 must initially request only 10 matches.");
            Require(LeaguePlayerDataService.MaximumMatchCount == 20, "Player Gate 1 must cap the visible page at 20 matches.");

            var fullWindow = new LeaguePlayerMatchPage { RequestedCount = 10, ReportedGameCount = 10 };
            for (var index = 0; index < 10; index++) fullWindow.Matches.Add(new LeaguePlayerMatchSummary { GameId = index + 1 });
            Require(fullWindow.HasMore, "A full 10-match LCU window must keep explicit load-more available.");

            var shortWindow = new LeaguePlayerMatchPage { RequestedCount = 10, ReportedGameCount = 9 };
            for (var index = 0; index < 9; index++) shortWindow.Matches.Add(new LeaguePlayerMatchSummary { GameId = index + 1 });
            Require(!shortWindow.HasMore, "A short LCU window must stop explicit load-more.");
        }

        private static void ValidateCancellation()
        {
            var api = new FixtureApi("{}", "{}", null, "[{\"id\":145,\"name\":\"卡莎\"}]");
            var service = new LeaguePlayerDataService(api, new PerformanceBudgetProvider());
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try
                {
                    service.LoadProfileAsync(true, cancellation.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Canceled Player request unexpectedly completed.");
                }
                catch (OperationCanceledException)
                {
                    // Expected: page-close cancellation must stop queued Player work.
                }
            }

            var profile = new LeaguePlayerProfile { PuuId = "puuid-current" };
            var page = new LeaguePlayerMatchPage { RequestedCount = 10 };
            page.Matches.Add(new LeaguePlayerMatchSummary { ChampionId = 145, ParticipantResolved = true });
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try
                {
                    service.EnrichChampionNamesAsync(profile, page, cancellation.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Canceled champion metadata request unexpectedly completed.");
                }
                catch (OperationCanceledException)
                {
                    // Expected: page-close cancellation must also stop optional metadata work.
                }
            }
        }

        private static int CountPath(List<string> paths, string expected)
        {
            var count = 0;
            foreach (var path in paths)
                if (string.Equals(path, expected, StringComparison.Ordinal)) count++;
            return count;
        }

        private static int CountPrefix(List<string> paths, string prefix)
        {
            var count = 0;
            foreach (var path in paths)
                if (path != null && path.StartsWith(prefix, StringComparison.Ordinal)) count++;
            return count;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FixtureApi : ILeagueClientApi
        {
            private readonly byte[] _profile;
            private readonly byte[] _history;
            private readonly byte[] _championSummary;
            private readonly Dictionary<long, byte[]> _details = new Dictionary<long, byte[]>();

            public FixtureApi(string profile, string history, Dictionary<long, string> details, string championSummary = null)
            {
                _profile = Encoding.UTF8.GetBytes(profile ?? string.Empty);
                _history = Encoding.UTF8.GetBytes(history ?? string.Empty);
                _championSummary = Encoding.UTF8.GetBytes(championSummary ?? string.Empty);
                if (details != null)
                {
                    foreach (var pair in details) _details[pair.Key] = Encoding.UTF8.GetBytes(pair.Value ?? string.Empty);
                }
                Paths = new List<string>();
            }

            public List<string> Paths { get; private set; }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                if (string.Equals(path, LeagueDashboardDetailsService.SummonerPath, StringComparison.Ordinal))
                    return Task.FromResult(_profile);
                if (string.Equals(path, LeaguePlayerDataService.ChampionSummaryPath, StringComparison.Ordinal))
                    return Task.FromResult(_championSummary);
                const string detailPrefix = "/lol-match-history/v1/games/";
                if (path != null && path.StartsWith(detailPrefix, StringComparison.Ordinal))
                {
                    long gameId;
                    byte[] bytes;
                    return long.TryParse(path.Substring(detailPrefix.Length), out gameId) && _details.TryGetValue(gameId, out bytes)
                        ? Task.FromResult(bytes)
                        : Task.FromResult<byte[]>(null);
                }
                if (path != null && path.StartsWith("/lol-match-history/", StringComparison.Ordinal))
                    return Task.FromResult(_history);
                return Task.FromResult<byte[]>(null);
            }
        }
    }
}
