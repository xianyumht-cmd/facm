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
            ValidateProfileAndRecentMatchParsing();
            ValidateProgressiveEnrichmentBudget();
            ValidateChampionMetadataAndStats();
            ValidateSourceSimpleFilters();
            ValidateRiotPlayerSearch();
            ValidateRemotePlayerHistory();
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

            var before = api.Paths.Count;
            Require(service.LoadRemotePlayerHistoryAsync(expected, "not/valid", CancellationToken.None)
                .GetAwaiter().GetResult().Status == LeagueRiotPlayerSearchStatus.Unavailable &&
                    api.Paths.Count == before,
                "Untrusted remote player IDs must not become LCU URL paths.");
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
