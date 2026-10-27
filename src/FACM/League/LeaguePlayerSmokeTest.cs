using System;
using System.Collections.Generic;
using System.Linq;
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
            var completeReplay = new LeaguePlayerMatchSummary { GameId = 12, QueueId = 450,
                GameType = "MATCHED_GAME", GameDurationSeconds = 1234,
                GameCreationEpochMs = 1720000000000 };
            Require(!FACM.AppHost.Modules.LeaguePlayerModule.RequiresReplayDetail(completeReplay) &&
                FACM.AppHost.Modules.LeaguePlayerModule.RequiresReplayDetail(new LeaguePlayerMatchSummary
                    { GameId = 12, QueueId = 450, GameType = "MATCHED_GAME", GameDurationSeconds = 1234 }) &&
                FACM.AppHost.Modules.LeaguePlayerModule.RequiresReplayDetail(null),
                "Sparse history must hydrate only the selected game before creating replay metadata.");
            ValidateProfileAndRecentMatchParsing();
            ValidateProgressiveEnrichmentBudget();
            ValidateChampionMetadataAndStats();
            ValidateSourceSimpleFilters();
            ValidateAdvancedCombinatorFilters();
            ValidateAdvancedParticipantEquipmentFilters();
            ValidateAdvancedRelativeMetricFilters();
            ValidateAdvancedParticipantScopes();
            ValidateAdvancedGameResults();
            ValidateRiotPlayerSearch();
            ValidateRemotePlayerHistory();
            ValidateMatchHistoryCollection();
            ValidatePaginationBoundary();
            ValidateCancellation();
            ValidatePlayerPageLayout();
            if (!LeaguePlayerUiBridge.HasTrayAccessForSmokeTest())
                throw new InvalidOperationException("Player tray bridge lost MainForm tray access.");
        }

        private static void ValidatePlayerPageLayout()
        {
            foreach (var width in new[] { 540, 680, 960 })
            {
                var columns = LeagueRemotePlayerHistoryForm.ResolveColumnWidthsForSmokeTest(width);
                Require(columns.Length == 5 && columns.All(value => value > 0) && columns.Sum() == width,
                    "Remote player history columns must scale without clipping at compact native widths.");
            }
            int start;
            Require(LeagueRemotePlayerHistoryForm.TryResolvePageStartForSmokeTest(1, 10, out start) && start == 0 &&
                    LeagueRemotePlayerHistoryForm.TryResolvePageStartForSmokeTest(8, 200, out start) && start == 1400 &&
                    !LeagueRemotePlayerHistoryForm.TryResolvePageStartForSmokeTest(0, 10, out start) &&
                    !LeagueRemotePlayerHistoryForm.TryResolvePageStartForSmokeTest(int.MaxValue, 200, out start),
                "Remote history direct-page navigation must match source offsets without integer overflow.");
            Require(LeaguePlayerForm.FilterOptionMatchesForSmokeTest("卡莎 (#145)", "145", "卡莎") &&
                    LeaguePlayerForm.FilterOptionMatchesForSmokeTest("卡莎 (#145)", "145", " 145 ") &&
                    LeaguePlayerForm.FilterOptionMatchesForSmokeTest("Opponent#CN1", "puuid-Example-ZZ", "example"),
                "Native hero/player filter option searches must accept localized names, IDs and PUUID substrings.");
            Require(!LeaguePlayerForm.FilterOptionMatchesForSmokeTest("艾希", "22", "卡莎") &&
                    !LeaguePlayerForm.FilterOptionMatchesForSmokeTest("Opponent#CN1", "puuid-Example-ZZ", "Other"),
                "Filter search must not show unrelated champion or summoner options.");
            Require(LeaguePlayerForm.FilterOptionMatchesForSmokeTest("Some Name", "uid", "   "),
                "Clearing an option search must restore the full available catalog.");

            var minimum = LeaguePlayerForm.ResolveMatchesBoundsForSmokeTest(680, 540);
            var defaultSize = LeaguePlayerForm.ResolveMatchesBoundsForSmokeTest(860, 720);
            var expanded = LeaguePlayerForm.ResolveMatchesBoundsForSmokeTest(1180, 850);

            Require(minimum.Left >= 0 && minimum.Top > 0 && minimum.Right <= 680 &&
                    minimum.Bottom <= 540 - 58 && minimum.Height >= 96,
                "Embedded Player page must preserve a visible list and bottom action row.");
            Require(defaultSize.Width > minimum.Width && expanded.Width > defaultSize.Width,
                "Player match list must grow with the workbench content width.");
            Require(expanded.Height > defaultSize.Height && defaultSize.Height > minimum.Height,
                "Player match list must grow vertically when the workbench is resized.");

            foreach (var width in new[] { 580, 680, 800, 1040 })
            {
                var columns = LeaguePlayerForm.ResolveMatchColumnWidthsForSmokeTest(width);
                Require(columns.Length == 7 && columns.All(value => value > 0),
                    "Player match table lost a column or generated an invalid width.");
                if (width >= 630)
                    Require(columns.Sum() <= width - 10,
                        "Player match table should fit inside its content area without horizontal scrolling.");
            }
        }

        private static void ValidateMatchHistoryCollection()
        {
            var pages = new Dictionary<int, string>
            {
                { 0, CollectionHistory(CollectionGame(100, 22), CollectionGame(101, 99)) },
                { 2, CollectionHistory(CollectionGame(101, 99), CollectionGame(102, 99)) },
                { 4, CollectionHistory() }
            };
            var profile = new LeaguePlayerProfile { PuuId = "self" };
            var settings = new LeagueMatchCollectionSettings
            {
                CountPerIteration = 2, ExpectedCount = 2, MaxIterations = 3
            };
            var filter = new LeagueSimpleMatchHistoryFilter { ChampionIds = new[] { 99 } };
            var api = new CollectionFixtureApi(pages);
            var service = new LeaguePlayerDataService(api, new PerformanceBudgetProvider());
            LeagueMatchCollectionProgress last = null;
            var result = service.CollectMatchesAsync(profile, settings, filter, null,
                new InlineProgress<LeagueMatchCollectionProgress>(value => last = value),
                CancellationToken.None).GetAwaiter().GetResult();
            Require(result.Status == LeagueMatchCollectionStatus.Completed && result.ScannedCount == 4 &&
                    result.Iterations == 2 && result.Page.Matches.Select(m => m.GameId).SequenceEqual(new long[] { 101, 102 }) &&
                    last != null && last.CollectedCount == 2 && last.ScannedCount == 4 && last.Iterations == 2 &&
                    service.TryGetCachedPage() == null && service.TryGetCachedProfile() == null,
                "Collection must match, deduplicate and stop at its exact target without replacing regular caches.");
            Require(api.Paths.Count(p => p.Contains("/matches?")) == 2 &&
                    api.Paths.Contains("/lol-match-history/v1/products/lol/self/matches?begIndex=0&endIndex=1") &&
                    api.Paths.Contains("/lol-match-history/v1/products/lol/self/matches?begIndex=2&endIndex=3") &&
                    api.ProfileReads == 4,
                "Each collected page must use source-inclusive offsets between account identity fences.");

            var stoppedApi = new CollectionFixtureApi(pages);
            var stoppedService = new LeaguePlayerDataService(stoppedApi, new PerformanceBudgetProvider());
            using (var cancel = new CancellationTokenSource())
            {
                var stopped = stoppedService.CollectMatchesAsync(profile, settings, filter, null,
                    new InlineProgress<LeagueMatchCollectionProgress>(value => cancel.Cancel()), cancel.Token)
                    .GetAwaiter().GetResult();
                Require(stopped.Status == LeagueMatchCollectionStatus.Stopped && stopped.Iterations == 1 &&
                        stopped.Page.Matches.Count == 1 && stopped.Page.Matches[0].GameId == 101 &&
                        stoppedApi.Paths.Count(p => p.Contains("/matches?")) == 1,
                    "Explicit stop must retain the last complete collection page without fetching another.");
            }

            var switchedApi = new CollectionFixtureApi(pages) { SwitchAccountAtRead = 3 };
            var switched = new LeaguePlayerDataService(switchedApi, new PerformanceBudgetProvider())
                .CollectMatchesAsync(profile, settings, filter, null, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(switched.Status == LeagueMatchCollectionStatus.AccountChanged &&
                    switched.Page.Matches.Count == 0 && switchedApi.Paths.Count(p => p.Contains("/matches?")) == 1,
                "An account change between pages must discard all collected identity-bound rows.");

            var emptyApi = new CollectionFixtureApi(new Dictionary<int, string> { { 0, CollectionHistory() } });
            var empty = new LeaguePlayerDataService(emptyApi, new PerformanceBudgetProvider())
                .CollectMatchesAsync(profile, settings, filter, null, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(empty.Status == LeagueMatchCollectionStatus.Completed && empty.Iterations == 1 &&
                    empty.Page.Matches.Count == 0 && emptyApi.Paths.Count(p => p.Contains("/matches?")) == 1,
                "Empty history must terminate the collection without consuming the full iteration budget.");

            LeagueAdvancedMatchHistoryFilter advanced;
            string error;
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                    Predicate(LeagueAdvancedMatchKind.IsLoss)), out advanced, out error),
                "The collection fixture must use a valid source-shaped advanced predicate.");
            var advancedApi = new CollectionFixtureApi(pages);
            var advancedResult = new LeaguePlayerDataService(advancedApi, new PerformanceBudgetProvider())
                .CollectMatchesAsync(profile, settings, filter, advanced, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(advancedResult.Status == LeagueMatchCollectionStatus.Completed &&
                    advancedResult.Page.Matches.Count == 0 && advancedResult.Iterations == 3,
                "Collection must intersect simple and advanced predicates across every scanned window.");

            var unavailableApi = new CollectionFixtureApi(new Dictionary<int, string> { { 0, pages[0] } });
            var unavailable = new LeaguePlayerDataService(unavailableApi, new PerformanceBudgetProvider())
                .CollectMatchesAsync(profile, settings, filter, null, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(unavailable.Status == LeagueMatchCollectionStatus.Unavailable && unavailable.Iterations == 1 &&
                    unavailable.Page.Matches.Count == 1 && unavailable.Page.Matches[0].GameId == 101,
                "A failed later read must preserve completed matching pages and report partial availability.");

            var invalidApi = new CollectionFixtureApi(pages);
            var invalid = new LeaguePlayerDataService(invalidApi, new PerformanceBudgetProvider())
                .CollectMatchesAsync(profile, new LeagueMatchCollectionSettings { CountPerIteration = 201 },
                    filter, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Require(invalid.Status == LeagueMatchCollectionStatus.Unavailable && invalidApi.Paths.Count == 0 &&
                    new LeagueMatchCollectionSettings { CountPerIteration = 200, ExpectedCount = 1000,
                        MaxIterations = 100 }.IsValid,
                "Collection limits must reject over-budget requests before any LCU call.");
        }

        private static string CollectionGame(long id, int champion)
        {
            return "{\"gameId\":" + id + ",\"gameMode\":\"ARAM\",\"queueId\":450," +
                "\"participantIdentities\":[{\"participantId\":1,\"player\":{\"puuid\":\"self\"}}]," +
                "\"participants\":[{\"participantId\":1,\"championId\":" + champion +
                ",\"stats\":{\"win\":true,\"kills\":3,\"deaths\":1,\"assists\":8}}]}";
        }

        private static string CollectionHistory(params string[] games)
        {
            return "{\"games\":{\"games\":[" + string.Join(",", games) + "]}}";
        }

        private static void ValidateProfileAndRecentMatchParsing()
        {
            var profileJson = "{\"puuid\":\"puuid-current\",\"summonerId\":9001,\"accountId\":8001,\"gameName\":\"Player\",\"tagLine\":\"CQ100\",\"displayName\":\"Player\",\"summonerLevel\":321,\"profileIconId\":12}";
            var historyJson = "{\"accountId\":8001,\"platformId\":\"CQ100\",\"games\":{\"gameCount\":42,\"gameIndexBegin\":0,\"gameIndexEnd\":1,\"games\":[" +
                "{\"gameId\":10001,\"gameCreation\":1700000000000,\"gameDuration\":1260,\"gameMode\":\"ARAM\",\"queueId\":450,\"participantIdentities\":[{\"participantId\":3,\"player\":{\"puuid\":\"puuid-current\",\"summonerId\":9001,\"gameName\":\"Self\",\"tagLine\":\"CQ100\"}},{\"participantId\":4,\"player\":{\"puuid\":\"puuid-opponent\",\"gameName\":\"Opponent\",\"tagLine\":\"TEST\"}}],\"participants\":[{\"participantId\":3,\"championId\":145,\"stats\":{\"kills\":8,\"deaths\":4,\"assists\":12,\"totalMinionsKilled\":44,\"neutralMinionsKilled\":3,\"win\":true}},{\"participantId\":4,\"championId\":99}]} ," +
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
            Require(first.MatchChampionIds != null && first.MatchChampionIds.SequenceEqual(new[] { 145, 99 }),
                "Champion filter must read all known champions from the one existing history response.");
            Require(first.MatchPlayers != null && first.MatchPlayers.Length == 2 &&
                    first.MatchPlayers[0].PuuId == "puuid-current" && first.MatchPlayers[0].PlayerName == "Self#CQ100" &&
                    first.MatchPlayers[1].PuuId == "puuid-opponent",
                "Participant filtering must correlate verified identities across the complete game roster.");
            var second = page.Matches[1];
            Require(second.ParticipantResolved && !second.Win && second.QueueId == 420, "Summoner-ID participant fallback changed unexpectedly.");
            Require(second.MatchChampionIds != null && second.MatchChampionIds.SequenceEqual(new[] { 22 }),
                "Different-match champion-roster filters must stay isolated.");
            Require(second.MatchPlayers == null, "A participant with no PUUID is not positive filter evidence.");
            var cached = service.TryGetCachedPage();
            Require(cached != null && cached.Matches[0].MatchChampionIds.SequenceEqual(new[] { 145, 99 }),
                "The Player cache must retain match-wide champion evidence.");
            cached.Matches[0].MatchChampionIds[0] = 999;
            cached.Matches[0].MatchPlayers[0].PuuId = "mutated";
            Require(service.TryGetCachedPage().Matches[0].MatchChampionIds[0] == 145,
                "Filtered match champion arrays must not mutate the shared Player cache.");
            Require(service.TryGetCachedPage().Matches[0].MatchPlayers[0].PuuId == "puuid-current",
                "Filtered match participant objects must not mutate the shared Player cache.");

            var duplicateIds = "{\"games\":{\"games\":[{\"participants\":[{\"participantId\":1}]," +
                "\"participantIdentities\":[{\"participantId\":1,\"player\":{\"puuid\":\"unique\"}},{\"participantId\":1,\"player\":{\"puuid\":\"ambiguous\"}}]}]}}";
            var invalidPage = service.ParseMatchPage(Encoding.UTF8.GetBytes(duplicateIds), profile, 0, 10);
            Require(invalidPage.Matches[0].MatchPlayers == null,
                "Conflicting participant identities must not create guessed player membership.");
            var duplicatePuuids = "{\"games\":{\"games\":[{\"participants\":[{\"participantId\":1},{\"participantId\":2}]," +
                "\"participantIdentities\":[{\"participantId\":1,\"player\":{\"puuid\":\"same\"}},{\"participantId\":2,\"player\":{\"puuid\":\"same\"}}]}]}}";
            Require(service.ParseMatchPage(Encoding.UTF8.GetBytes(duplicatePuuids), profile, 0, 10)
                .Matches[0].MatchPlayers == null, "One PUUID cannot prove membership for two different participants.");
        }

        private static void ValidateSourceSimpleFilters()
        {
            var now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
            var matches = new[]
            {
                new LeaguePlayerMatchSummary { GameId = 1, Win = true, StatisticsAvailable = true,
                    ParticipantResolved = true, GameCreationLocal = now.AddHours(-2), MatchChampionIds = new[] { 22, 99 },
                    MatchPlayers = new[] { new LeaguePlayerMatchIdentity { PuuId = "self" },
                        new LeaguePlayerMatchIdentity { PuuId = "other" } } },
                new LeaguePlayerMatchSummary { GameId = 2, Win = false, StatisticsAvailable = true,
                    ParticipantResolved = true, GameCreationLocal = now.AddHours(-10), MatchChampionIds = new[] { 22 },
                    MatchPlayers = new[] { new LeaguePlayerMatchIdentity { PuuId = "self" } } },
                new LeaguePlayerMatchSummary { GameId = 3, Win = true, StatisticsAvailable = true,
                    ParticipantResolved = true, GameCreationLocal = now.AddDays(-8), MatchChampionIds = null },
                new LeaguePlayerMatchSummary { GameId = 4, Win = false, StatisticsAvailable = false,
                    ParticipantResolved = true, GameCreationLocal = now.AddHours(-1), MatchChampionIds = new[] { 99 } },
                new LeaguePlayerMatchSummary { GameId = 5, Win = true, StatisticsAvailable = true,
                    ParticipantResolved = true, GameCreationLocal = now.AddMinutes(1), MatchChampionIds = new[] { 22, 99 } }
            };
            var filter = new LeagueSimpleMatchHistoryFilter
            {
                WinLoss = LeagueMatchWinLossFilter.Win,
                TimeRange = LeagueMatchTimeRangeFilter.Last3Hours,
                ChampionIds = new[] { 22, 99 }
            };
            Require(LeagueSimpleMatchHistoryFilter.Apply(matches, filter, now).Select(m => m.GameId)
                .SequenceEqual(new long[] { 1 }),
                "Source simple filters must intersect win, inclusive time range and all selected champion IDs.");
            filter.PlayerPuuids = new[] { "self", "other" };
            Require(LeagueSimpleMatchHistoryFilter.Apply(matches, filter, now).Select(m => m.GameId)
                .SequenceEqual(new long[] { 1 }),
                "All selected PUUIDs must match across the game roster, combined with every champion/result/time condition.");
            filter.PlayerPuuids = new[] { "SELF" };
            Require(LeagueSimpleMatchHistoryFilter.Apply(matches, filter, now).Count == 0,
                "Akari PUUID identity predicates use exact case-sensitive membership.");
            filter.PlayerPuuids = new[] { "self", "missing" };
            Require(LeagueSimpleMatchHistoryFilter.Apply(matches, filter, now).Count == 0,
                "Unknown player membership must not satisfy a positive player condition.");
            filter.PlayerPuuids = new[] { "" };
            Require(LeagueSimpleMatchHistoryFilter.Apply(matches, filter, now).Count == 0,
                "An invalid selected PUUID must fail closed.");
            filter.PlayerPuuids = null;
            filter.WinLoss = LeagueMatchWinLossFilter.Loss;
            filter.TimeRange = LeagueMatchTimeRangeFilter.Last12Hours;
            filter.ChampionIds = new[] { 22 };
            Require(LeagueSimpleMatchHistoryFilter.Apply(matches, filter, now).Select(m => m.GameId)
                .SequenceEqual(new long[] { 2 }),
                "Loss filters must require verified results and include only matching games.");
            filter.WinLoss = LeagueMatchWinLossFilter.All;
            filter.TimeRange = LeagueMatchTimeRangeFilter.All;
            filter.ChampionIds = new[] { 99 };
            Require(LeagueSimpleMatchHistoryFilter.Apply(matches, filter, now).Select(m => m.GameId)
                .SequenceEqual(new long[] { 1, 4, 5 }),
                "Anyone-champion filters must match other participants without requiring own champion identity.");
            filter.ChampionIds = null;
            filter.TimeRange = LeagueMatchTimeRangeFilter.Last7Days;
            Require(LeagueSimpleMatchHistoryFilter.Apply(matches, filter, now).Select(m => m.GameId)
                .SequenceEqual(new long[] { 1, 2, 4 }),
                "Time-range filtering must exclude future games and games older than the selected range.");
            Require(LeagueSimpleMatchHistoryFilter.Apply(matches, null, now).Count == 5,
                "The default empty filter must not change the loaded-match list.");
            filter.TimeRange = LeagueMatchTimeRangeFilter.Last3Hours;
            var edge = new LeaguePlayerMatchSummary { GameId = 6, GameCreationLocal = now.AddHours(-3) };
            Require(filter.Matches(edge, now), "Three-hour history boundary must be inclusive.");
            edge.GameCreationLocal = DateTime.MinValue;
            Require(!filter.Matches(edge, now), "Unknown historical timestamps cannot pass a dated filter.");
        }

        private static LeagueAdvancedMatchNode Predicate(LeagueAdvancedMatchKind kind, string value = null)
        {
            return new LeagueAdvancedMatchNode { Kind = kind, Value = value };
        }

        private static LeagueAdvancedMatchNode Group(LeagueAdvancedMatchKind kind, params LeagueAdvancedMatchNode[] children)
        {
            var node = Predicate(kind);
            node.Children.AddRange(children);
            return node;
        }

        private static LeagueAdvancedMatchNode Scoped(LeagueAdvancedMatchKind kind, string player,
            LeagueAdvancedMatchNode child)
        {
            var node = Group(kind, child);
            node.Value = player;
            return node;
        }

        private static void ValidateAdvancedCombinatorFilters()
        {
            // Pinned Akari 1.5.1 combinator-nodes.ts: game(AND/OR/NOT) with queue,
            // map, mode, duration, results and match-wide player/champion predicates.
            var root = Group(LeagueAdvancedMatchKind.And,
                Group(LeagueAdvancedMatchKind.Or,
                    Predicate(LeagueAdvancedMatchKind.IsQueue, "420"),
                    Predicate(LeagueAdvancedMatchKind.IsGameMode, "ARAM")),
                Group(LeagueAdvancedMatchKind.Not, Predicate(LeagueAdvancedMatchKind.HasChampion, "99")),
                Predicate(LeagueAdvancedMatchKind.DurationBetween, "600,1800"),
                Predicate(LeagueAdvancedMatchKind.KillsBetween, "5,10"));
            LeagueAdvancedMatchHistoryFilter filter;
            string error;
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(root, out filter, out error),
                "Valid recursive AND/OR/NOT source-shaped history filters must compile: " + error);

            var matches = new[]
            {
                new LeaguePlayerMatchSummary { GameId = 1, GameMode = "ARAM", QueueId = 450,
                    GameDurationSeconds = 600, MatchChampionIds = new[] { 22 }, Kills = 5,
                    ParticipantResolved = true, StatisticsAvailable = true },
                new LeaguePlayerMatchSummary { GameId = 2, GameMode = "CLASSIC", QueueId = 420,
                    GameDurationSeconds = 1800, MatchChampionIds = new[] { 22 }, Kills = 10,
                    ParticipantResolved = true, StatisticsAvailable = true },
                new LeaguePlayerMatchSummary { GameId = 3, GameMode = "CLASSIC", QueueId = 420,
                    GameDurationSeconds = 1200, MatchChampionIds = new[] { 99 }, Kills = 7,
                    ParticipantResolved = true, StatisticsAvailable = true },
                new LeaguePlayerMatchSummary { GameId = 4, GameMode = "ARAM", QueueId = 450,
                    GameDurationSeconds = 1200, MatchChampionIds = null, Kills = 7,
                    ParticipantResolved = true, StatisticsAvailable = true },
                new LeaguePlayerMatchSummary { GameId = 5, GameMode = "ARAM", QueueId = 450,
                    GameDurationSeconds = 1200, MatchChampionIds = new[] { 22 }, Kills = 0,
                    ParticipantResolved = false, StatisticsAvailable = false },
                new LeaguePlayerMatchSummary { GameId = 6, GameMode = "KIWI", QueueId = 3270,
                    GameDurationSeconds = 1200, MatchChampionIds = new[] { 22 }, Kills = 7,
                    ParticipantResolved = true, StatisticsAvailable = true },
                new LeaguePlayerMatchSummary { GameId = 7, GameMode = "ARAM", QueueId = 450,
                    GameDurationSeconds = 0, MatchChampionIds = new[] { 22 }, Kills = 7,
                    ParticipantResolved = true, StatisticsAvailable = true }
            };
            Require(LeagueAdvancedMatchHistoryFilter.Apply(matches, filter).Select(m => m.GameId)
                .SequenceEqual(new long[] { 1, 2 }),
                "Nested advanced groups must preserve AND/OR/NOT semantics, inclusive ranges and missing-evidence gates.");
            root.Children.Clear();
            Require(filter.Matches(matches[0]) && !filter.Matches(matches[3]),
                "Applying a filter must use a defensive snapshot, not the mutable editor nodes.");

            var winResult = Group(LeagueAdvancedMatchKind.Or,
                Predicate(LeagueAdvancedMatchKind.IsWin),
                Group(LeagueAdvancedMatchKind.Not, Predicate(LeagueAdvancedMatchKind.IsLoss)));
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(winResult, out filter, out error),
                "Win/loss OR/NOT tree should compile.");
            Require(!filter.Matches(new LeaguePlayerMatchSummary { Win = false, StatisticsAvailable = false,
                ParticipantResolved = true }), "NOT must not turn an unknown outcome into a verified victory.");
            Require(filter.Matches(new LeaguePlayerMatchSummary { Win = true, StatisticsAvailable = true,
                ParticipantResolved = true }), "Nested result combinations must include verified wins.");

            var playerTree = Group(LeagueAdvancedMatchKind.And,
                Predicate(LeagueAdvancedMatchKind.HasPlayer, "target"),
                Predicate(LeagueAdvancedMatchKind.HasChampion, "22"),
                Predicate(LeagueAdvancedMatchKind.IsMap, "12"),
                Predicate(LeagueAdvancedMatchKind.AssistsBetween, "3,8"));
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(playerTree, out filter, out error),
                "Player/champion/map/assists conditions must compile.");
            var complete = new LeaguePlayerMatchSummary
            {
                MatchPlayers = new[] { new LeaguePlayerMatchIdentity { PuuId = "target" } },
                MatchChampionIds = new[] { 22 }, MapId = 12, Assists = 8,
                StatisticsAvailable = true, ParticipantResolved = true
            };
            Require(filter.Matches(complete), "Verified match-wide PUUID and champion evidence must combine with own stats.");
            complete.MatchPlayers[0].PuuId = "TARGET";
            Require(!filter.Matches(complete), "Akari PUUID identity filtering must remain case-sensitive.");

            foreach (var invalid in new[]
            {
                Group(LeagueAdvancedMatchKind.And, Predicate(LeagueAdvancedMatchKind.IsQueue, "0")),
                Group(LeagueAdvancedMatchKind.And, Predicate(LeagueAdvancedMatchKind.DurationBetween, "12,11")),
                Group(LeagueAdvancedMatchKind.And, Predicate(LeagueAdvancedMatchKind.HasPlayer, "  ")),
                Group(LeagueAdvancedMatchKind.And, Group(LeagueAdvancedMatchKind.Not)),
                Group(LeagueAdvancedMatchKind.And, Group(LeagueAdvancedMatchKind.Or)),
                Group(LeagueAdvancedMatchKind.And, Group(LeagueAdvancedMatchKind.Not,
                    Predicate(LeagueAdvancedMatchKind.IsWin), Predicate(LeagueAdvancedMatchKind.IsLoss)))
            })
                Require(!LeagueAdvancedMatchHistoryFilter.TryCreate(invalid, out filter, out error),
                    "Invalid or incomplete advanced match filters must fail closed before application.");

            var cycle = Group(LeagueAdvancedMatchKind.And);
            cycle.Children.Add(cycle);
            Require(!LeagueAdvancedMatchHistoryFilter.TryCreate(cycle, out filter, out error),
                "Cyclic filter input must not recurse indefinitely.");
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(
                Group(LeagueAdvancedMatchKind.Or), out filter, out error) &&
                LeagueAdvancedMatchHistoryFilter.Apply(matches, filter).Count == matches.Length,
                "An empty advanced OR root must not hide the entire loaded page.");
        }

        private static void ValidateAdvancedParticipantEquipmentFilters()
        {
            var matchJson = "{\"games\":{\"games\":[{\"gameId\":9001,\"gameMode\":\"ARAM\"," +
                "\"participants\":[{\"participantId\":1,\"teamId\":100,\"championId\":145,\"spell1Id\":11,\"spell2Id\":4," +
                "\"stats\":{\"win\":true,\"kills\":8,\"deaths\":2,\"assists\":12," +
                "\"teamPosition\":\"MIDDLE\",\"champLevel\":18,\"goldEarned\":12000,\"goldSpent\":10000," +
                "\"totalDamageDealtToChampions\":18000,\"physicalDamageDealtToChampions\":12000," +
                "\"magicDamageDealtToChampions\":5000,\"trueDamageDealtToChampions\":1000," +
                "\"totalDamageTaken\":11000,\"physicalDamageTaken\":6000,\"magicDamageTaken\":4000," +
                "\"trueDamageTaken\":1000,\"damageDealtToTurrets\":1000,\"totalHeal\":750," +
                "\"timeCCingOthers\":45,\"visionScore\":24,\"doubleKills\":1,\"tripleKills\":0," +
                "\"quadraKills\":0,\"pentaKills\":0,\"challenges\":{\"soloKills\":2}," +
                "\"item0\":1055,\"item1\":3006,\"item2\":0,\"item3\":0,\"item4\":0,\"item5\":0,\"item6\":3364," +
                "\"roleBoundItem\":3340," +
                "\"perk0\":8010,\"perk1\":9111,\"perk2\":9104,\"perk3\":8014,\"perk4\":8304,\"perk5\":8345," +
                "\"perkPrimaryStyle\":8000,\"perkSubStyle\":8300," +
                "\"playerAugment1\":1234,\"playerAugment2\":5678,\"playerAugment3\":0," +
                "\"playerAugment4\":0,\"playerAugment5\":0,\"playerAugment6\":0}}]," +
                "\"participantIdentities\":[{\"participantId\":1,\"player\":{\"puuid\":\"self\"}}]}]}}";
            var service = new LeaguePlayerDataService(new FixtureApi("{\"puuid\":\"self\"}", matchJson, null),
                new PerformanceBudgetProvider());
            var profile = service.LoadProfileAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            var loaded = service.LoadRecentMatchesAsync(profile, 0, 10, true, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(loaded != null && loaded.Matches.Count == 1, "One self-participant equipment fixture should parse.");
            var match = loaded.Matches[0];
            Require(match.ItemIds.SequenceEqual(new[] { 1055, 3006, 0, 0, 0, 0, 3364 }) &&
                    match.RoleBoundItemId == 3340 && match.SpellIds.SequenceEqual(new[] { 11, 4 }) &&
                    match.PerkIds.SequenceEqual(new[] { 8010, 9111, 9104, 8014, 8304, 8345 }) &&
                    match.PerkStyleIds.SequenceEqual(new[] { 8000, 8300 }) &&
                    match.AugmentIds.SequenceEqual(new[] { 1234, 5678, 0, 0, 0, 0 }),
                "Akari-style LCU equipment, spells, rune styles and augment slots must preserve original order.");

            var root = Group(LeagueAdvancedMatchKind.And,
                Predicate(LeagueAdvancedMatchKind.HasItem, "1055@0"),
                Predicate(LeagueAdvancedMatchKind.HasItem, "3340@6"),
                Predicate(LeagueAdvancedMatchKind.HasSpell, "4@1"),
                Predicate(LeagueAdvancedMatchKind.HasPerk, "8010@0"),
                Predicate(LeagueAdvancedMatchKind.HasPerkStyle, "8300@1"),
                Predicate(LeagueAdvancedMatchKind.HasAugment, "5678@1"));
            LeagueAdvancedMatchHistoryFilter filter;
            string error;
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(root, out filter, out error) && filter.Matches(match),
                "Indexed participant predicates and Akari role-bound item exception must match verified fixture slots.");
            root.Children.Clear();
            Require(filter.Matches(match), "Compiled participant slot conditions must be detached from the editor tree.");
            var cached = service.TryGetCachedPage();
            Require(cached != null && cached.Matches.Count == 1, "Loaded participant evidence should have a cache snapshot.");
            cached.Matches[0].ItemIds[0] = 9999;
            cached.Matches[0].SpellIds[0] = 9999;
            cached.Matches[0].PerkIds[0] = 9999;
            cached.Matches[0].PerkStyleIds[0] = 9999;
            cached.Matches[0].AugmentIds[0] = 9999;
            Require(service.TryGetCachedPage().Matches[0].ItemIds[0] == 1055 &&
                    service.TryGetCachedPage().Matches[0].SpellIds[0] == 11 &&
                    service.TryGetCachedPage().Matches[0].PerkIds[0] == 8010 &&
                    service.TryGetCachedPage().Matches[0].PerkStyleIds[0] == 8000 &&
                    service.TryGetCachedPage().Matches[0].AugmentIds[0] == 1234,
                "Mutating a Player cache snapshot must not alter verified participant slots.");
            ValidateAdvancedStatisticsFilters(match, service);

            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Predicate(LeagueAdvancedMatchKind.HasSpell, "11@1")), out filter, out error) && !filter.Matches(match),
                "A spell in the wrong occupied slot must fail an indexed predicate.");
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Group(LeagueAdvancedMatchKind.Not, Predicate(LeagueAdvancedMatchKind.HasPerk, "9999"))),
                out filter, out error) && filter.Matches(match),
                "NOT on a known-complete rune set must accept a verified absence.");

            var partialJson = "{\"games\":{\"games\":[{\"gameId\":9002," +
                "\"participants\":[{\"participantId\":1,\"spell1Id\":4," +
                "\"stats\":{\"item0\":1055,\"item1\":\"invalid\",\"playerAugment1\":1234}}]," +
                "\"participantIdentities\":[{\"participantId\":1,\"player\":{\"puuid\":\"self\"}}]}]}}";
            var partialPage = service.ParseMatchPage(Encoding.UTF8.GetBytes(partialJson), profile, 0, 10);
            Require(partialPage != null && partialPage.Matches.Count == 1,
                "Incomplete LCU participant-slot fixture must remain readable.");
            var incomplete = partialPage.Matches[0];
            Require(incomplete.ItemIds.SequenceEqual(new[] { 1055, -1, -1, -1, -1, -1, -1 }) &&
                    incomplete.SpellIds.SequenceEqual(new[] { 4, -1 }) &&
                    incomplete.AugmentIds.SequenceEqual(new[] { 1234, -1, -1, -1, -1, -1 }) &&
                    !incomplete.RoleBoundItemId.HasValue,
                "Missing or malformed slots must remain unknown instead of becoming an empty zero slot.");
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Predicate(LeagueAdvancedMatchKind.HasAugment, "1234")), out filter, out error) &&
                filter.Matches(incomplete), "Known positive slots must remain usable when sibling slots are absent.");
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Group(LeagueAdvancedMatchKind.Not, Predicate(LeagueAdvancedMatchKind.HasAugment, "9999"))),
                out filter, out error) && !filter.Matches(incomplete),
                "A partial augment list must not be interpreted as proof a different augment is absent.");
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Group(LeagueAdvancedMatchKind.Not, Predicate(LeagueAdvancedMatchKind.HasItem, "9999"))),
                out filter, out error) && !filter.Matches(incomplete),
                "Missing item slots or role-bound item cannot satisfy negative equipment predicates.");

            foreach (var invalid in new[]
            {
                Predicate(LeagueAdvancedMatchKind.HasItem, "1038@7"),
                Predicate(LeagueAdvancedMatchKind.HasSpell, "4@2"),
                Predicate(LeagueAdvancedMatchKind.HasPerkStyle, "8000@-1"),
                Predicate(LeagueAdvancedMatchKind.HasAugment, "0"),
                Predicate(LeagueAdvancedMatchKind.HasPerk, "8010@0@1")
            })
                Require(!LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And, invalid),
                    out filter, out error), "Invalid participant ID or slot bounds must fail before application.");
        }

        private static void ValidateAdvancedStatisticsFilters(LeaguePlayerMatchSummary match, LeaguePlayerDataService service)
        {
            Require(match.Position == "MIDDLE" && match.Level == 18 && match.GoldEarned == 12000 &&
                    match.PhysicalDamageToChampions == 12000 && match.MagicDamageTaken == 4000 &&
                    match.DamageToTowers == 1000 && match.TimeCCingOthers == 45 &&
                    match.SoloKills == 2 && match.PentaKills == 0,
                "Self-participant numeric statistics must preserve their LCU source values.");
            var cases = new Dictionary<LeagueAdvancedMatchKind, string>
            {
                { LeagueAdvancedMatchKind.IsPosition, "MIDDLE" },
                { LeagueAdvancedMatchKind.KdaBetween, "10,10" },
                { LeagueAdvancedMatchKind.LevelBetween, "18,18" },
                { LeagueAdvancedMatchKind.GoldBetween, "12000,12000" },
                { LeagueAdvancedMatchKind.GoldSpentBetween, "10000,10000" },
                { LeagueAdvancedMatchKind.DamageToChampionsBetween, "18000,18000" },
                { LeagueAdvancedMatchKind.PhysicalDamageToChampionsBetween, "12000,12000" },
                { LeagueAdvancedMatchKind.MagicDamageToChampionsBetween, "5000,5000" },
                { LeagueAdvancedMatchKind.TrueDamageToChampionsBetween, "1000,1000" },
                { LeagueAdvancedMatchKind.DamageTakenBetween, "11000,11000" },
                { LeagueAdvancedMatchKind.PhysicalDamageTakenBetween, "6000,6000" },
                { LeagueAdvancedMatchKind.MagicDamageTakenBetween, "4000,4000" },
                { LeagueAdvancedMatchKind.TrueDamageTakenBetween, "1000,1000" },
                { LeagueAdvancedMatchKind.DamageToTowersBetween, "1000,1000" },
                { LeagueAdvancedMatchKind.HealBetween, "750,750" },
                { LeagueAdvancedMatchKind.VisionScoreBetween, "24,24" },
                { LeagueAdvancedMatchKind.TimeCCingOthersBetween, "45,45" },
                { LeagueAdvancedMatchKind.SoloKillsBetween, "2,2" },
                { LeagueAdvancedMatchKind.KillParticipationBetween, "250,250" },
                { LeagueAdvancedMatchKind.DamageGoldEfficiencyBetween, "150,150" },
                { LeagueAdvancedMatchKind.ChampionDamageShareBetween, "100,100" },
                { LeagueAdvancedMatchKind.DamageTakenShareBetween, "100,100" },
                { LeagueAdvancedMatchKind.GoldShareBetween, "100,100" },
                { LeagueAdvancedMatchKind.DoubleKillsBetween, "1,1" },
                { LeagueAdvancedMatchKind.TripleKillsBetween, "0,0" },
                { LeagueAdvancedMatchKind.QuadraKillsBetween, "0,0" },
                { LeagueAdvancedMatchKind.PentaKillsBetween, "0,0" }
            };
            LeagueAdvancedMatchHistoryFilter filter;
            string error;
            foreach (var expected in cases)
            {
                Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                    Predicate(expected.Key, expected.Value)), out filter, out error) && filter.Matches(match),
                    "Advanced numeric/position predicate must match LCU fixture: " + expected.Key);
            }
            Require(service.TryGetCachedPage().Matches[0].GoldSpent == 10000 &&
                    service.TryGetCachedPage().Matches[0].PhysicalDamageTaken == 6000,
                "A cached match snapshot must retain numeric participant statistics.");
            var noEvidence = new LeaguePlayerMatchSummary { ParticipantResolved = true, StatisticsAvailable = true };
            foreach (var kind in new[] { LeagueAdvancedMatchKind.GoldBetween,
                LeagueAdvancedMatchKind.SoloKillsBetween, LeagueAdvancedMatchKind.ChampionDamageShareBetween })
            {
                Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                    Group(LeagueAdvancedMatchKind.Not, Predicate(kind, "0,999999"))), out filter, out error) &&
                    !filter.Matches(noEvidence), "NOT must not turn unavailable metrics into verified absence: " + kind);
            }
            foreach (var invalid in new[] { "NaN,1", "Infinity,10", "-1,2", "10,2", "1", "1,2,3" })
                Require(!LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                    Predicate(LeagueAdvancedMatchKind.KdaBetween, invalid)), out filter, out error),
                    "Invalid floating ranges must be rejected before evaluating a match.");
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Predicate(LeagueAdvancedMatchKind.KdaBetween, "10.01,11")), out filter, out error) &&
                !filter.Matches(match), "Decimal bounds must exclude genuine out-of-range KDA values.");
        }

        private static void ValidateAdvancedRelativeMetricFilters()
        {
            var matchJson = "{\"games\":{\"games\":[{\"gameId\":9011,\"gameMode\":\"CLASSIC\"," +
                "\"participants\":[" +
                "{\"participantId\":1,\"teamId\":100,\"championId\":145,\"stats\":{" +
                "\"win\":true,\"kills\":8,\"deaths\":2,\"assists\":12," +
                "\"totalMinionsKilled\":100,\"neutralMinionsKilled\":20," +
                "\"goldEarned\":12000,\"totalDamageDealtToChampions\":18000}}," +
                "{\"participantId\":2,\"teamId\":100,\"championId\":22,\"stats\":{" +
                "\"win\":true,\"kills\":4,\"deaths\":4,\"assists\":6," +
                "\"totalMinionsKilled\":80,\"neutralMinionsKilled\":10," +
                "\"goldEarned\":6000,\"totalDamageDealtToChampions\":6000}}," +
                "{\"participantId\":3,\"teamId\":200,\"championId\":99,\"stats\":{" +
                "\"win\":false,\"kills\":10,\"deaths\":3,\"assists\":2," +
                "\"totalMinionsKilled\":120,\"neutralMinionsKilled\":20," +
                "\"goldEarned\":10000,\"totalDamageDealtToChampions\":12000}}]," +
                "\"participantIdentities\":[{\"participantId\":1,\"player\":{\"puuid\":\"self\"}}]}]}}";
            var service = new LeaguePlayerDataService(new FixtureApi("{\"puuid\":\"self\"}", matchJson, null),
                new PerformanceBudgetProvider());
            var profile = service.LoadProfileAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            var loaded = service.LoadRecentMatchesAsync(profile, 0, 10, true, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(loaded != null && loaded.Matches.Count == 1, "A complete three-player numeric fixture should parse.");
            var match = loaded.Matches[0];
            LeagueAdvancedMatchHistoryFilter filter;
            string error;
            foreach (var node in new[]
            {
                Predicate(LeagueAdvancedMatchKind.KillsBetween, "teamShare:66,67"),
                Predicate(LeagueAdvancedMatchKind.KillsBetween, "teamMaxShare:100,100"),
                Predicate(LeagueAdvancedMatchKind.KillsBetween, "gameShare:36,37"),
                Predicate(LeagueAdvancedMatchKind.KillsBetween, "gameMaxShare:80,80"),
                Predicate(LeagueAdvancedMatchKind.GoldBetween, "teamShare:66,67"),
                Predicate(LeagueAdvancedMatchKind.GoldBetween, "gameShare:42,43"),
                Predicate(LeagueAdvancedMatchKind.DamageToChampionsBetween, "gameMaxShare:100,100"),
                Predicate(LeagueAdvancedMatchKind.CsBetween, "teamShare:57,58"),
                Predicate(LeagueAdvancedMatchKind.KillsBetween, "8,8"),
                Predicate(LeagueAdvancedMatchKind.KillsBetween, "7.5,8.5")
            })
                Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And, node),
                    out filter, out error) && filter.Matches(match),
                    "Relative numeric measure must match verified team/game baselines: " + node.Value);
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Predicate(LeagueAdvancedMatchKind.KillsBetween, "teamShare:67,90")), out filter, out error) &&
                !filter.Matches(match), "Relative percent bounds must reject values outside the interval.");
            var snapshot = service.TryGetCachedPage();
            Require(snapshot.Matches[0].MetricBaselines != null, "Cached pages must preserve participant baselines.");
            snapshot.Matches[0].MetricBaselines["kills"][0] = 999;
            Require(service.TryGetCachedPage().Matches[0].MetricBaselines["kills"][0] == 12,
                "Metric baseline arrays must be defensively cloned across cache snapshots.");
            var noRoster = new LeaguePlayerMatchSummary
            {
                ParticipantResolved = true, StatisticsAvailable = true, Kills = 8
            };
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Group(LeagueAdvancedMatchKind.Not,
                    Predicate(LeagueAdvancedMatchKind.KillsBetween, "gameShare:0,100"))), out filter, out error) &&
                !filter.Matches(noRoster), "NOT must not convert absent team/game baselines into verified evidence.");
            foreach (var invalid in new[]
            {
                "teamShare:-1,100", "teamMaxShare:100,20", "gameShare:NaN,50",
                "gameMaxShare:1", "teamFoo:0,100", "gameShare:1,2,3", "gameShare:0,1:2"
            })
                Require(!LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                    Predicate(LeagueAdvancedMatchKind.GoldBetween, invalid)), out filter, out error),
                    "Malformed relative modes must be rejected at compile time.");
            Require(!LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Predicate(LeagueAdvancedMatchKind.KdaBetween, "teamShare:0,100")), out filter, out error),
                "KDA must retain Akari's absolute-only comparator.");
        }

        private static void ValidateAdvancedParticipantScopes()
        {
            var history = "{\"games\":{\"games\":[{\"gameId\":9021,\"gameMode\":\"CLASSIC\"," +
                "\"participants\":[" +
                "{\"participantId\":1,\"teamId\":100,\"championId\":145,\"stats\":{" +
                "\"win\":true,\"kills\":8,\"deaths\":2,\"assists\":12,\"item0\":1055}}," +
                "{\"participantId\":2,\"teamId\":100,\"championId\":22,\"stats\":{" +
                "\"win\":true,\"kills\":4,\"deaths\":4,\"assists\":6,\"item0\":3006}}," +
                "{\"participantId\":3,\"teamId\":200,\"championId\":99,\"stats\":{" +
                "\"win\":false,\"kills\":10,\"deaths\":3,\"assists\":2,\"item0\":3020}}]," +
                "\"participantIdentities\":[" +
                "{\"participantId\":1,\"player\":{\"puuid\":\"self\"}}," +
                "{\"participantId\":2,\"player\":{\"puuid\":\"ally\"}}," +
                "{\"participantId\":3,\"player\":{\"puuid\":\"enemy\"}}]}]}}";
            var service = new LeaguePlayerDataService(new FixtureApi("{\"puuid\":\"self\"}", history, null),
                new PerformanceBudgetProvider());
            var profile = service.LoadProfileAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            var page = service.LoadRecentMatchesAsync(profile, 0, 10, true, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(page != null && page.Matches.Count == 1 &&
                    page.Matches[0].ParticipantRoster.Length == 3 &&
                    page.Matches[0].RosterIdentitiesComplete,
                "Bounded LCU participant scopes must correlate all three unique participant identities.");
            var match = page.Matches[0];
            var pass = new[]
            {
                Scoped(LeagueAdvancedMatchKind.All, null,
                    Scoped(LeagueAdvancedMatchKind.Anyone, null,
                        Predicate(LeagueAdvancedMatchKind.HasChampion, "22"))),
                Scoped(LeagueAdvancedMatchKind.All, null,
                    Scoped(LeagueAdvancedMatchKind.Everyone, null,
                        Predicate(LeagueAdvancedMatchKind.KillsBetween, "0,20"))),
                Scoped(LeagueAdvancedMatchKind.Player, "ally", Group(LeagueAdvancedMatchKind.And,
                    Predicate(LeagueAdvancedMatchKind.HasChampion, "22"),
                    Predicate(LeagueAdvancedMatchKind.KillsBetween, "4,4"),
                    Predicate(LeagueAdvancedMatchKind.HasItem, "3006@0"))),
                Scoped(LeagueAdvancedMatchKind.Allies, "self",
                    Scoped(LeagueAdvancedMatchKind.Anyone, null,
                        Predicate(LeagueAdvancedMatchKind.KillsBetween, "4,4"))),
                Scoped(LeagueAdvancedMatchKind.Enemies, "self",
                    Scoped(LeagueAdvancedMatchKind.Everyone, null,
                        Predicate(LeagueAdvancedMatchKind.KillsBetween, "8,20"))),
                Scoped(LeagueAdvancedMatchKind.Allies, "ally",
                    Scoped(LeagueAdvancedMatchKind.Everyone, null,
                        Predicate(LeagueAdvancedMatchKind.IsWin))),
                Scoped(LeagueAdvancedMatchKind.Enemies, "ally",
                    Scoped(LeagueAdvancedMatchKind.Anyone, null,
                        Predicate(LeagueAdvancedMatchKind.HasItem, "3020")))
            };
            LeagueAdvancedMatchHistoryFilter filter;
            string error;
            foreach (var node in pass)
                Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And, node),
                    out filter, out error) && filter.Matches(match),
                    "Akari participant/group quantifier must retain selected-player context: " + node.Kind);
            foreach (var node in new[]
            {
                Scoped(LeagueAdvancedMatchKind.All, null,
                    Scoped(LeagueAdvancedMatchKind.Everyone, null,
                        Predicate(LeagueAdvancedMatchKind.IsWin))),
                Scoped(LeagueAdvancedMatchKind.Player, "ally",
                    Predicate(LeagueAdvancedMatchKind.KillsBetween, "8,8")),
                Scoped(LeagueAdvancedMatchKind.Enemies, "self",
                    Scoped(LeagueAdvancedMatchKind.Anyone, null,
                        Predicate(LeagueAdvancedMatchKind.HasChampion, "22")))
            })
                Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And, node),
                    out filter, out error) && !filter.Matches(match),
                    "Cross-participant predicates must reject another player's nonmatching statistics.");
            var cached = service.TryGetCachedPage();
            cached.Matches[0].ParticipantRoster[1].ItemIds[0] = 9999;
            cached.Matches[0].ParticipantRoster[1].PuuId = "modified";
            Require(service.TryGetCachedPage().Matches[0].ParticipantRoster[1].ItemIds[0] == 3006 &&
                    service.TryGetCachedPage().Matches[0].ParticipantRoster[1].PuuId == "ally",
                "Nested participant evidence must be fully detached from cached pages.");
            var incomplete = new LeaguePlayerMatchSummary
            {
                ParticipantResolved = true, ParticipantId = 1,
                ParticipantRoster = new[]
                {
                    new LeaguePlayerMatchSummary { ParticipantId = 1, PuuId = "self", TeamId = 100,
                        ParticipantResolved = true, StatisticsAvailable = true, Kills = 8 },
                    new LeaguePlayerMatchSummary { ParticipantId = 2, TeamId = 200,
                        ParticipantResolved = true, StatisticsAvailable = true, Kills = 4 }
                }
            };
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Group(LeagueAdvancedMatchKind.Not,
                    Scoped(LeagueAdvancedMatchKind.Player, "unknown",
                        Predicate(LeagueAdvancedMatchKind.KillsBetween, "0,20")))), out filter, out error) &&
                !filter.Matches(incomplete), "Missing participant PUUIDs must stay unknown under NOT.");
            Require(!LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Scoped(LeagueAdvancedMatchKind.Player, "", Predicate(LeagueAdvancedMatchKind.IsWin))),
                out filter, out error), "A targeted scope must reject an empty PUUID before application.");
            Require(!LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                Group(LeagueAdvancedMatchKind.Anyone,
                    Predicate(LeagueAdvancedMatchKind.IsWin), Predicate(LeagueAdvancedMatchKind.IsLoss))),
                out filter, out error), "Quantifier scopes must accept exactly one child expression.");
        }

        private static void ValidateAdvancedGameResults()
        {
            // Pinned Akari 1.5.1 win-result.ts: abort > remake > early surrender > win > loss.
            // LCU statistics nest participant flags under stats; other providers put them on the participant.
            var games = "{\"games\":{\"games\":[" +
                OutcomeGame(9101, "MATCHED_GAME", 420, "\"win\":true", null) + "," +
                OutcomeGame(9102, "CUSTOM_GAME", 450,
                    "\"win\":false,\"gameEndedInSurrender\":false", null) + "," +
                OutcomeGame(9103, "MATCHED_GAME", 420,
                    "\"win\":false,\"gameEndedInSurrender\":true,\"teamEarlySurrendered\":true", null) + "," +
                OutcomeGame(9104, "MATCHED_GAME", 420,
                    "\"win\":false,\"gameEndedInEarlySurrender\":true", null) + "," +
                OutcomeGame(9105, "MATCHED_GAME", 420, "\"win\":true",
                    "\"endOfGameResult\":\"Abort_Afk\"") + "," +
                OutcomeGame(9106, null, 0, "\"kills\":2", null) + "," +
                OutcomeGame(9107, "MATCHED_GAME", 830, "\"win\":true", null) + "," +
                OutcomeGame(9108, "MATCHED_GAME", 420, "\"win\":false", null) + "]}}";
            var service = new LeaguePlayerDataService(new FixtureApi("{\"puuid\":\"self\"}", games, null),
                new PerformanceBudgetProvider());
            var profile = service.LoadProfileAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            var page = service.LoadRecentMatchesAsync(profile, 0, 10, true, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(page != null && page.Matches.Count == 8,
                "All source-shaped game-result fixtures must parse in one bounded LCU page.");
            var byId = page.Matches.ToDictionary(m => m.GameId);
            Require(byId[9101].WinResult == "win" && byId[9102].WinResult == "loss" &&
                    byId[9102].IsSurrender == false && byId[9103].IsSurrender == true &&
                    byId[9104].WinResult == "remake" && byId[9105].WinResult == "abort" &&
                    byId[9105].IsSurrender == false && byId[9106].WinResult == null &&
                    !byId[9108].IsSurrender.HasValue,
                "Outcome classification and missing surrender evidence must respect the source precedence.");

            LeagueAdvancedMatchHistoryFilter filter;
            string error;
            var cases = new[]
            {
                new { Kind = LeagueAdvancedMatchKind.IsWin, Ids = new long[] { 9101, 9107 } },
                new { Kind = LeagueAdvancedMatchKind.IsLoss, Ids = new long[] { 9102, 9103, 9108 } },
                new { Kind = LeagueAdvancedMatchKind.IsNormalLoss, Ids = new long[] { 9102 } },
                new { Kind = LeagueAdvancedMatchKind.IsSurrenderLoss, Ids = new long[] { 9103 } },
                new { Kind = LeagueAdvancedMatchKind.IsRemake, Ids = new long[] { 9104 } },
                new { Kind = LeagueAdvancedMatchKind.IsAbort, Ids = new long[] { 9105 } },
                new { Kind = LeagueAdvancedMatchKind.IsMatchedGame,
                    Ids = new long[] { 9101, 9103, 9104, 9105, 9107, 9108 } },
                new { Kind = LeagueAdvancedMatchKind.IsPveGame, Ids = new long[] { 9107 } }
            };
            foreach (var item in cases)
            {
                Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                        Predicate(item.Kind)), out filter, out error),
                    "Result condition should compile without user input: " + item.Kind);
                Require(LeagueAdvancedMatchHistoryFilter.Apply(page.Matches, filter)
                        .Select(m => m.GameId).OrderBy(id => id).SequenceEqual(item.Ids),
                    "Source outcome/game-type/queue classification diverged: " + item.Kind);
            }
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                    Group(LeagueAdvancedMatchKind.Not,
                        Predicate(LeagueAdvancedMatchKind.IsNormalLoss))), out filter, out error) &&
                    !filter.Matches(byId[9108]) && filter.Matches(byId[9103]),
                "NOT cannot infer a normal loss from absent surrender data.");
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(Group(LeagueAdvancedMatchKind.And,
                    Group(LeagueAdvancedMatchKind.Not,
                        Predicate(LeagueAdvancedMatchKind.IsRemake))), out filter, out error) &&
                    !filter.Matches(byId[9106]),
                "NOT must preserve unknown outcome evidence.");
            var cached = service.TryGetCachedPage();
            Require(cached.Matches.Any(m => m.GameId == 9104 && m.WinResult == "remake") &&
                    cached.Matches.Any(m => m.GameId == 9103 && m.IsSurrender == true),
                "The page cache clone must retain source result and surrender classification.");
            var group = Group(LeagueAdvancedMatchKind.And,
                Scoped(LeagueAdvancedMatchKind.Anyone, null,
                    Predicate(LeagueAdvancedMatchKind.IsSurrenderLoss)));
            Require(LeagueAdvancedMatchHistoryFilter.TryCreate(group, out filter, out error) &&
                    filter.Matches(byId[9103]) && !filter.Matches(byId[9101]),
                "Result predicates must operate on the selected participant in quantified groups.");
        }

        private static string OutcomeGame(long id, string type, int queue, string stats, string extra)
        {
            return "{\"gameId\":" + id + ",\"queueId\":" + queue +
                (type == null ? "" : ",\"gameType\":\"" + type + "\"") +
                (extra == null ? "" : "," + extra) +
                ",\"participants\":[{\"participantId\":1,\"teamId\":100,\"championId\":22," +
                "\"stats\":{" + stats + "}}],\"participantIdentities\":[{" +
                "\"participantId\":1,\"player\":{\"puuid\":\"self\"}}]}";
        }

        private static void ValidateRiotPlayerSearch()
        {
            string name;
            string tag;
            Require(LeaguePlayerDataService.TryParseRiotId("  Example Name # CN1  ", out name, out tag) &&
                    name == "Example Name" && tag == "CN1",
                "Riot ID lookup must require and trim both exact name and tag components.");
            Require(!LeaguePlayerDataService.TryParseRiotId("MissingTag", out name, out tag) &&
                    !LeaguePlayerDataService.TryParseRiotId("Name##Tag", out name, out tag) &&
                    !LeaguePlayerDataService.TryParseRiotId("Name#", out name, out tag) &&
                    !LeaguePlayerDataService.TryParseRiotId("Bad\nName#Tag", out name, out tag),
                "Invalid Riot IDs must never be sent to the local Riot Client.");

            const string current = "{\"puuid\":\"current-account\"}";
            const string alias = "[{\"puuid\":\"target-puuid\",\"alias\":{\"game_name\":\"Example Name\",\"tag_line\":\"CN1\"}}]";
            var details = new Dictionary<string, string> { { "target-puuid", "{\"puuid\":\"target-puuid\"}" } };
            var api = new FixtureApi(current, "{}", null, null, details);
            var riot = new FakeRiotAliases(alias);
            var service = new LeaguePlayerDataService(api, new PerformanceBudgetProvider(), riot);
            Require(service.ParseRiotAliasPuuid(Encoding.UTF8.GetBytes(alias), "Example Name", "CN1") == "target-puuid",
                "Exact Riot Client alias-to-PUUID parsing changed unexpectedly.");
            Require(service.ParseRiotAliasPuuid(Encoding.UTF8.GetBytes(alias), "Other", "CN1") == null &&
                    service.ParseRiotAliasPuuid(Encoding.UTF8.GetBytes("not-json"), "Example Name", "CN1") == null &&
                    service.ParseRiotAliasPuuid(Encoding.UTF8.GetBytes("[{\"puuid\":\"bad/identifier\",\"alias\":{\"game_name\":\"Example Name\",\"tag_line\":\"CN1\"}}]"), "Example Name", "CN1") == null,
                "Unrelated, malformed or unsafe aliases must not pass the identity gate.");
            var conflicting = "[{\"puuid\":\"target-puuid\",\"alias\":{\"game_name\":\"Example Name\",\"tag_line\":\"CN1\"}}," +
                "{\"puuid\":\"other-puuid\",\"alias\":{\"game_name\":\"Example Name\",\"tag_line\":\"CN1\"}}]";
            Require(service.ParseRiotAliasPuuid(Encoding.UTF8.GetBytes(conflicting), "Example Name", "CN1") == null,
                "Ambiguous duplicate Riot aliases must never guess the target PUUID.");

            var expected = new LeaguePlayerProfile { PuuId = "current-account" };
            var result = service.SearchRiotIdAsync(expected, " Example Name #CN1 ", CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(result.Status == LeagueRiotPlayerSearchStatus.Found && result.Player != null &&
                    result.Player.PuuId == "target-puuid" && result.Player.PlayerName == "Example Name#CN1" &&
                    riot.Calls == 1 && riot.LastName == "Example Name" && riot.LastTag == "CN1" &&
                    CountPath(api.Paths, LeagueDashboardDetailsService.SummonerPath) == 2 &&
                    CountPath(api.Paths, "/lol-summoner/v2/summoners/puuid/target-puuid") == 1,
                "Explicit Riot ID lookup must validate alias, LCU PUUID detail and unchanged signed-in account.");

            var beforeInvalid = riot.Calls;
            Require(service.SearchRiotIdAsync(expected, "NameWithoutTag", CancellationToken.None)
                .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.InvalidQuery &&
                    riot.Calls == beforeInvalid,
                "Malformed player search must not dispatch an authenticated Riot request.");
            riot.SetAlias("[]");
            Require(service.SearchRiotIdAsync(expected, "Example Name#CN1", CancellationToken.None)
                .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.NotFound,
                "Riot Client empty alias lists must be distinguished from unavailable transport.");
            riot.SetAlias(null);
            Require(service.SearchRiotIdAsync(expected, "Example Name#CN1", CancellationToken.None)
                .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.Unavailable,
                "Disconnected Riot Client must fail without creating an identity.");

            var staleApi = new FixtureApi("{\"puuid\":\"other-account\"}", "{}", null, null, details);
            var staleRiot = new FakeRiotAliases(alias);
            var stale = new LeaguePlayerDataService(staleApi, new PerformanceBudgetProvider(), staleRiot);
            Require(stale.SearchRiotIdAsync(expected, "Example Name#CN1", CancellationToken.None)
                .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.AccountChanged && staleRiot.Calls == 0,
                "An account changed before lookup must not send a Riot Client request.");
            var mismatchApi = new FixtureApi(current, "{}", null, null,
                new Dictionary<string, string> { { "target-puuid", "{\"puuid\":\"unrelated\"}" } });
            var mismatch = new LeaguePlayerDataService(mismatchApi, new PerformanceBudgetProvider(), new FakeRiotAliases(alias));
            Require(mismatch.SearchRiotIdAsync(expected, "Example Name#CN1", CancellationToken.None)
                .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.NotFound,
                "LCU detail for a different PUUID must not become a selectable Riot player.");

            var changedApi = new FixtureApi(current, "{}", null, null, details)
            {
                SecondProfile = "{\"puuid\":\"other-account\"}"
            };
            var changed = new LeaguePlayerDataService(changedApi, new PerformanceBudgetProvider(), new FakeRiotAliases(alias));
            Require(changed.SearchRiotIdAsync(expected, "Example Name#CN1", CancellationToken.None)
                .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.AccountChanged,
                "A completed Riot lookup must not publish data after account replacement.");
        }

        private static void ValidateRemotePlayerHistory()
        {
            const string current = "{\"puuid\":\"current-account\"}";
            const string target = "{\"puuid\":\"target-puuid\",\"gameName\":\"Opponent\",\"tagLine\":\"CN1\",\"summonerLevel\":128}";
            const string history = "{\"games\":{\"games\":[{\"gameId\":54321,\"gameMode\":\"ARAM\",\"participantIdentities\":[{\"participantId\":2,\"player\":{\"puuid\":\"target-puuid\"}}],\"participants\":[{\"participantId\":2,\"championId\":99,\"stats\":{\"win\":true,\"kills\":12,\"deaths\":3,\"assists\":18}}]}]}}";
            var profiles = new Dictionary<string, string> { { "target-puuid", target } };
            var histories = new Dictionary<string, string> { { "target-puuid", history } };
            var api = new FixtureApi(current, "{}", null, null, profiles, histories);
            var service = new LeaguePlayerDataService(api, new PerformanceBudgetProvider());
            var expected = new LeaguePlayerProfile { PuuId = "current-account" };
            var result = service.LoadRemotePlayerHistoryAsync(expected, "target-puuid", CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(result.Status == LeagueRiotPlayerSearchStatus.Found && result.Profile.AccountName == "Opponent#CN1" &&
                    result.Profile.SummonerLevel == 128 && result.Page.Matches.Count == 1 &&
                    result.Page.Matches[0].GameId == 54321 && result.Page.Matches[0].ParticipantResolved &&
                    result.Page.Matches[0].Kills == 12 && result.Page.Matches[0].ChampionId == 99 &&
                    service.TryGetCachedPage() == null && service.TryGetCachedProfile() == null,
                "Remote profile/history must parse target records without poisoning own-account caches.");
            Require(CountPath(api.Paths, LeagueDashboardDetailsService.SummonerPath) == 2 &&
                    CountPath(api.Paths, "/lol-summoner/v2/summoners/puuid/target-puuid") == 1 &&
                    CountPath(api.Paths, "/lol-match-history/v1/products/lol/target-puuid/matches?begIndex=0&endIndex=9") == 1,
                "One explicit remote view must use the bounded target LCU route between two self-profile fences.");

            var secondPage = service.LoadRemotePlayerHistoryAsync(expected, "target-puuid", CancellationToken.None, 10, 20)
                .GetAwaiter().GetResult();
            Require(secondPage.Status == LeagueRiotPlayerSearchStatus.Found &&
                    secondPage.Page.StartIndex == 10 && secondPage.Page.RequestedCount == 20 &&
                    CountPath(api.Paths, "/lol-match-history/v1/products/lol/target-puuid/matches?begIndex=10&endIndex=29") == 1 &&
                    service.TryGetCachedPage() == null,
                "Remote history page changes must use exact inclusive source indexes without changing own caches.");

            var before = api.Paths.Count;
            Require(service.LoadRemotePlayerHistoryAsync(expected, "not/valid", CancellationToken.None)
                .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.Unavailable &&
                    service.LoadRemotePlayerHistoryAsync(expected, "target-puuid", CancellationToken.None, 0, 201)
                        .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.Unavailable &&
                    service.LoadRemotePlayerHistoryAsync(expected, "target-puuid", CancellationToken.None, int.MaxValue, 10)
                        .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.Unavailable &&
                    api.Paths.Count == before,
                "Untrusted IDs, oversized page requests and integer-overflow offsets must never dispatch LCU requests.");

            var oversizedPage = "{\"games\":{\"games\":[{\"gameId\":1},{\"gameId\":2}]}}";
            var oversized = new LeaguePlayerDataService(new FixtureApi(current, "{}", null, null, profiles,
                new Dictionary<string, string> { { "target-puuid", oversizedPage } }),
                new PerformanceBudgetProvider());
            var truncated = oversized.LoadRemotePlayerHistoryAsync(expected, "target-puuid", CancellationToken.None, 0, 1)
                .GetAwaiter().GetResult();
            Require(truncated.Status == LeagueRiotPlayerSearchStatus.Found && truncated.Page.Matches.Count == 1 &&
                    truncated.Page.HasMore,
                "Unexpected LCU over-return must remain bounded by the requested remote page size.");
            var wrongAccount = new FixtureApi("{\"puuid\":\"another-account\"}", "{}", null, null, profiles, histories);
            var preflight = new LeaguePlayerDataService(wrongAccount, new PerformanceBudgetProvider());
            Require(preflight.LoadRemotePlayerHistoryAsync(expected, "target-puuid", CancellationToken.None)
                .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.AccountChanged &&
                    CountPath(wrongAccount.Paths, "/lol-summoner/v2/summoners/puuid/target-puuid") == 0,
                "Remote history must stop before target reads when the signed-in account has changed.");
            var changedApi = new FixtureApi(current, "{}", null, null, profiles, histories)
            {
                SecondProfile = "{\"puuid\":\"another-account\"}"
            };
            var changed = new LeaguePlayerDataService(changedApi, new PerformanceBudgetProvider());
            var switched = changed.LoadRemotePlayerHistoryAsync(expected, "target-puuid", CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(switched.Status == LeagueRiotPlayerSearchStatus.AccountChanged && switched.Profile == null &&
                    switched.Page == null && changed.TryGetCachedPage() == null,
                "Completed remote history cannot escape an account switch or enter the local player cache.");
            var missing = new LeaguePlayerDataService(new FixtureApi(current, "{}", null, null,
                new Dictionary<string, string> { { "target-puuid", "{\"puuid\":\"different\"}" } }, histories),
                new PerformanceBudgetProvider());
            Require(missing.LoadRemotePlayerHistoryAsync(expected, "target-puuid", CancellationToken.None)
                .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.NotFound,
                "An LCU target detail mismatch cannot be used for remote history.");
            var unavailable = new LeaguePlayerDataService(new FixtureApi(current, "{}", null, null, profiles),
                new PerformanceBudgetProvider());
            Require(unavailable.LoadRemotePlayerHistoryAsync(expected, "target-puuid", CancellationToken.None)
                .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.Unavailable,
                "Missing remote history must report unavailable instead of an empty successful page.");
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
            const string championSummary = "[{\"id\":-1,\"name\":\"None\"},{\"id\":145,\"name\":\"卡莎\"},{\"id\":22,\"name\":\"艾希\"},{\"id\":147,\"name\":\"萨勒芬妮\"}]";
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
            var entireCatalog = service.TryGetCachedChampionNames();
            Require(entireCatalog != null && entireCatalog[147] == "萨勒芬妮" && !entireCatalog.ContainsKey(-1),
                "Full champion selection must use verified game-data heroes, including those absent from the loaded matches.");
            entireCatalog[147] = "changed";
            Require(service.TryGetCachedChampionNames()[147] == "萨勒芬妮",
                "Full champion selector must receive an isolated metadata snapshot.");
            Require(CountPath(api.Paths, LeaguePlayerDataService.ChampionSummaryPath) == 1, "Champion metadata must use one bounded summary request.");
            Require(CountPrefix(api.Paths, "/lol-match-history/") == 0, "Champion metadata must not add match-history fan-out.");

            var cachedAgain = service.EnrichChampionNamesAsync(profile, page, CancellationToken.None).GetAwaiter().GetResult();
            Require(cachedAgain.Matches[0].ChampionName == "卡莎", "Champion metadata cache did not remain usable.");
            Require(CountPath(api.Paths, LeaguePlayerDataService.ChampionSummaryPath) == 1, "Fresh champion metadata cache unexpectedly refetched.");

            var emptyHistoryApi = new FixtureApi("{}", "{}", null, championSummary);
            var emptyHistoryService = new LeaguePlayerDataService(emptyHistoryApi, new PerformanceBudgetProvider());
            Require(emptyHistoryService.TryGetCachedChampionNames() == null,
                "A new Player service must not invent champion metadata before an explicit request.");
            var allOptions = emptyHistoryService.LoadChampionOptionsAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(allOptions != null && allOptions.ContainsKey(147) &&
                    CountPath(emptyHistoryApi.Paths, LeaguePlayerDataService.ChampionSummaryPath) == 1 &&
                    CountPrefix(emptyHistoryApi.Paths, "/lol-match-history/") == 0,
                "Explicit champion-option opening should fill the one existing cache even without loaded matches.");
            emptyHistoryService.LoadChampionOptionsAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(CountPath(emptyHistoryApi.Paths, LeaguePlayerDataService.ChampionSummaryPath) == 1,
                "Reopening hero options within cache TTL must not request metadata again.");

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
            Require(inGameService.LoadChampionOptionsAsync(CancellationToken.None).GetAwaiter().GetResult() == null &&
                    CountPath(inGameApi.Paths, LeaguePlayerDataService.ChampionSummaryPath) == 0,
                "Explicit hero-option opening in game must respect the existing maintenance budget.");
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
            private readonly Dictionary<string, byte[]> _summoners = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            private readonly Dictionary<string, byte[]> _remoteHistories = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            private int _profileReads;

            public FixtureApi(string profile, string history, Dictionary<long, string> details, string championSummary = null,
                Dictionary<string, string> summoners = null, Dictionary<string, string> remoteHistories = null)
            {
                _profile = Encoding.UTF8.GetBytes(profile ?? string.Empty);
                _history = Encoding.UTF8.GetBytes(history ?? string.Empty);
                _championSummary = Encoding.UTF8.GetBytes(championSummary ?? string.Empty);
                if (details != null)
                {
                    foreach (var pair in details) _details[pair.Key] = Encoding.UTF8.GetBytes(pair.Value ?? string.Empty);
                }
                if (summoners != null)
                    foreach (var entry in summoners) _summoners[entry.Key] = Encoding.UTF8.GetBytes(entry.Value);
                if (remoteHistories != null)
                    foreach (var entry in remoteHistories) _remoteHistories[entry.Key] = Encoding.UTF8.GetBytes(entry.Value);
                Paths = new List<string>();
            }

            public List<string> Paths { get; private set; }
            public string SecondProfile { get; set; }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                if (string.Equals(path, LeagueDashboardDetailsService.SummonerPath, StringComparison.Ordinal))
                    return Task.FromResult(++_profileReads > 1 && SecondProfile != null
                        ? Encoding.UTF8.GetBytes(SecondProfile) : _profile);
                if (string.Equals(path, LeaguePlayerDataService.ChampionSummaryPath, StringComparison.Ordinal))
                    return Task.FromResult(_championSummary);
                const string summonerPrefix = "/lol-summoner/v2/summoners/puuid/";
                if (path != null && path.StartsWith(summonerPrefix, StringComparison.Ordinal))
                {
                    byte[] summoner;
                    return Task.FromResult(_summoners.TryGetValue(path.Substring(summonerPrefix.Length), out summoner)
                        ? summoner : null);
                }
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
                {
                    const string remotePrefix = "/lol-match-history/v1/products/lol/";
                    if (path.StartsWith(remotePrefix, StringComparison.Ordinal))
                    {
                        var end = path.IndexOf("/matches?", remotePrefix.Length, StringComparison.Ordinal);
                        if (end >= 0)
                        {
                            byte[] remote;
                            if (_remoteHistories.TryGetValue(path.Substring(remotePrefix.Length,
                                end - remotePrefix.Length), out remote)) return Task.FromResult(remote);
                        }
                    }
                    return Task.FromResult(_history);
                }
                return Task.FromResult<byte[]>(null);
            }
        }

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;
            internal InlineProgress(Action<T> callback) { _callback = callback; }
            public void Report(T value) { _callback(value); }
        }

        private sealed class CollectionFixtureApi : ILeagueClientApi
        {
            private readonly Dictionary<int, string> _pages;
            internal readonly List<string> Paths = new List<string>();
            internal int ProfileReads;
            internal int SwitchAccountAtRead = int.MaxValue;

            internal CollectionFixtureApi(Dictionary<int, string> pages) { _pages = pages; }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                if (path == LeagueDashboardDetailsService.SummonerPath)
                {
                    var account = ++ProfileReads >= SwitchAccountAtRead ? "other" : "self";
                    return Task.FromResult(Encoding.UTF8.GetBytes("{\"puuid\":\"" + account + "\"}"));
                }
                const string prefix = "/lol-match-history/v1/products/lol/self/matches?begIndex=";
                if (path != null && path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var value = path.Substring(prefix.Length).Split('&')[0];
                    int start;
                    string json;
                    if (int.TryParse(value, out start) && _pages.TryGetValue(start, out json))
                        return Task.FromResult(Encoding.UTF8.GetBytes(json));
                }
                return Task.FromResult<byte[]>(null);
            }
        }

        private sealed class FakeRiotAliases : ILeagueRiotAliasLookupApi
        {
            private byte[] _response;
            internal int Calls;
            internal string LastName;
            internal string LastTag;

            internal FakeRiotAliases(string json) { SetAlias(json); }
            internal void SetAlias(string json) { _response = json == null ? null : Encoding.UTF8.GetBytes(json); }

            public Task<byte[]> TryLookupAsync(string gameName, string tagLine, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                LastName = gameName;
                LastTag = tagLine;
                return Task.FromResult(_response);
            }
        }
    }
}
