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
            var api = new FixtureApi("{}", "{}", null);
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
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FixtureApi : ILeagueClientApi
        {
            private readonly byte[] _profile;
            private readonly byte[] _history;
            private readonly Dictionary<long, byte[]> _details = new Dictionary<long, byte[]>();

            public FixtureApi(string profile, string history, Dictionary<long, string> details)
            {
                _profile = Encoding.UTF8.GetBytes(profile ?? string.Empty);
                _history = Encoding.UTF8.GetBytes(history ?? string.Empty);
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
