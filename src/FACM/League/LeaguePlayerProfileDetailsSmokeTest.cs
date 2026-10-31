using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal static class LeaguePlayerProfileDetailsSmokeTest
    {
        internal static void Validate()
        {
            ValidateAsync().GetAwaiter().GetResult();
        }
        private static void ValidatePostgameRefresh()
        {
            var detector = new LeaguePlayerPostgameRefreshDetector();
            Func<string, LeagueDashboardPhaseState> phase = name => new LeagueDashboardPhaseState
            { Connected = true, Phase = name, IsRankedQueue = true };
            Require(!detector.Observe(phase("Lobby")) &&
                    !detector.Observe(phase("EndOfGame")),
                "Ranked refresh must not fire on initial postgame or an idle lobby.");
            Require(!detector.Observe(phase("InProgress")) &&
                    !detector.Observe(phase("WaitingForStats")) &&
                    detector.Observe(phase("PreEndOfGame")) &&
                    !detector.Observe(phase("EndOfGame")) &&
                    !detector.Observe(phase("EndOfGame")),
                "Only one postgame refresh notification may be emitted per observed game.");
            Require(!detector.Observe(phase("Lobby")) &&
                    !detector.Observe(phase("Reconnect")) &&
                    detector.Observe(phase("EndOfGame")),
                "A subsequent game must independently trigger a refresh.");
            Require(!detector.Observe(phase("InProgress")) &&
                    !detector.Observe(new LeagueDashboardPhaseState { Connected = false }) &&
                    !detector.Observe(phase("EndOfGame")),
                "A disconnected session must not carry stale postgame refresh ownership.");
            detector.Reset();
            Require(!detector.Observe(phase("PreEndOfGame")),
                "Opening on PreEndOfGame must not be mistaken for an observed completed game.");
            detector.Reset();
            var unranked = phase("EndOfGame"); unranked.IsRankedQueue = false;
            Require(!detector.Observe(phase("InProgress")) && !detector.Observe(unranked),
                "Unranked matches must not refresh ranked stats.");
            detector.Reset();
            var unknown = phase("EndOfGame"); unknown.IsRankedQueue = null;
            Require(!detector.Observe(phase("InProgress")) && !detector.Observe(unknown),
                "Unknown match queue data must not trigger ranked refresh.");
            detector.Reset();
            Require(!detector.Observe(phase("EndOfGame")),
                "Disposing the dashboard must clear its pending postgame signal.");
        }

        private static async Task ValidateAsync()
        {
            ValidatePostgameRefresh();
            Require(LeaguePlayerProfileDialog.ShouldRetryPostgameRanked("same", "same", true) &&
                    !LeaguePlayerProfileDialog.ShouldRetryPostgameRanked("before", "after", true) &&
                    !LeaguePlayerProfileDialog.ShouldRetryPostgameRanked("", "", false),
                "The one-shot postgame retry must require a loaded and unchanged ranked snapshot.");

            var session = new LeagueClientSession("LeagueClientUx", 1, 1000, "fixture", "https", "fixture", "HN1", "TENCENT");
            var active = session;
            var api = new Api();
            var service = new LeaguePlayerDataService(api, new PerformanceBudgetProvider(), null, null, () => active);
            var result = await service.LoadPlayerDetailsAsync("self", "target", session, null,
                LeaguePlayerHistorySource.Automatic, CancellationToken.None);
            Require(result.Status == LeaguePlayerDetailsStatus.Ready && result.Profile.PuuId == "target" &&
                result.Profile.ProfileIconId == 42 &&
                api.Paths.SequenceEqual(new[] { LeagueDashboardDetailsService.SummonerPath,
                    "/lol-summoner/v2/summoners/puuid/target", "/lol-ranked/v1/ranked-stats/target",
                    "/lol-summoner/v1/summoner-profile?puuid=target", LeagueDashboardDetailsService.SummonerPath }) &&
                service.TryGetCachedProfile() == null && service.TryGetCachedPage() == null,
                "Selected-player details must use the pinned local paths between account fences without touching self caches.");
            var solo = LeaguePlayerDataService.RankedEntry(result.Ranked, "RANKED_SOLO_5x5");
            var flex = LeaguePlayerDataService.RankedEntry(result.Ranked, "RANKED_FLEX_SR");
            Require(LeagueMatchTimelineParser.Number(solo, "leaguePoints") == 0 &&
                LeaguePlayerProfileDialog.Winrate(solo) == "50%" &&
                LeagueMatchTimelineParser.Number(flex, "losses") == null &&
                LeaguePlayerProfileDialog.Winrate(flex) == "—",
                "Ranked queueMap precedence and queues fallback must retain zero and incomplete values.");
            Require(LeaguePlayerProfileDialog.RankTierIndex("IRON") == 0 &&
                LeaguePlayerProfileDialog.RankTierIndex("emerald") == 5 &&
                LeaguePlayerProfileDialog.RankTierIndex("CHALLENGER") == 9 &&
                LeaguePlayerProfileDialog.RankTierIndex("NA") == -1 &&
                LeaguePlayerProfileDialog.RankTierIndex(null) == -1,
                "Only known source-ranked tiers may receive a native rank emblem.");
            using (var medal = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(medal))
            {
                graphics.Clear(Color.Transparent);
                LeaguePlayerProfileDialog.DrawRankMedal(graphics, new Rectangle(1, 1, 30, 30), "EMERALD");
                Require(medal.GetPixel(16, 16).A > 0 && medal.GetPixel(0, 0).A == 0,
                    "Native ranked emblems must draw inside their own bounded cell.");
            }
            var rankedCard = LeaguePlayerProfileDialog.RankCardRecord(UiTextCatalog.Load(), solo);
            Require(rankedCard.Contains("0") && rankedCard.Contains("50%") &&
                LeaguePlayerProfileDialog.RankCardRecord(UiTextCatalog.Load(), flex).Contains("—"),
                "Ranked cards must retain true zero LP and unavailable fallback records.");
            var unrankedWithWins = new Dictionary<string, object> {
                { "tier", "NA" }, { "wins", 1 }, { "losses", 0 }, { "leaguePoints", 0 } };
            Require(!LeaguePlayerProfileDialog.RankCardRecord(UiTextCatalog.Load(), unrankedWithWins)
                    .Contains("100%"),
                "An unranked entry must not show a fabricated ranked win-rate badge.");
            var rankedMap = new Dictionary<string, object>
            {
                { "RANKED_SOLO_5x5", new Dictionary<string, object> {
                    { "tier", "GOLD" }, { "division", "II" }, { "leaguePoints", 0 },
                    { "previousSeasonHighestTier", "PLATINUM" }, { "previousSeasonHighestDivision", "IV" } } },
                { "RANKED_TFT", new Dictionary<string, object> {
                    { "tier", "SILVER" }, { "leaguePoints", 10 } } },
                { "RANKED_INVALID", new Dictionary<string, object> { { "queueType", "RANKED_SOLO_5x5" } } }
            };
            var rankedHistory = new Dictionary<string, object>
            {
                { "queueMap", rankedMap },
                { "queues", new object[] {
                    new Dictionary<string, object> { { "queueType", "RANKED_SOLO_5x5" }, { "leaguePoints", 99 } },
                    new Dictionary<string, object> { { "queueType", "RANKED_FLEX_SR" }, { "tier", "NA" }, { "wins", 0 } },
                    new Dictionary<string, object> { { "queueType", "RANKED_TFT_TURBO" }, { "tier", "SILVER" },
                        { "wins", 0 }, { "losses", 0 }, { "leaguePoints", 0 } } } }
            };
            var queueRows = LeaguePlayerDataService.RankedQueueEntries(rankedHistory);
            Require(queueRows.Select(entry => entry.Key).SequenceEqual(new[] {
                    "RANKED_SOLO_5x5", "RANKED_FLEX_SR", "RANKED_TFT", "RANKED_TFT_TURBO" }) &&
                LeagueMatchTimelineParser.Number(queueRows[0].Value, "leaguePoints") == 0 &&
                LeagueMatchTimelineParser.Text(queueRows[0].Value, "previousSeasonHighestTier") == "PLATINUM" &&
                LeaguePlayerProfileDialog.RankedNumber(queueRows[0].Value, "leaguePoints") == "0" &&
                LeaguePlayerProfileDialog.RankedNumber(queueRows[1].Value, "wins") == "—" &&
                LeaguePlayerProfileDialog.RankedNumber(queueRows[3].Value, "wins") == "0",
                "All ranked queues must preserve queueMap precedence, prior peak, missing/unranked and zero values.");
            var duplicateQueues = new Dictionary<string, object> { { "queues", new object[] {
                new Dictionary<string, object> { { "queueType", "RANKED_TFT" } },
                new Dictionary<string, object> { { "queueType", "RANKED_TFT" } } } } };
            Require(LeaguePlayerDataService.RankedQueueEntries(duplicateQueues).Count == 0 &&
                LeaguePlayerDataService.RankedQueueEntries(null).Count == 0,
                "Duplicate or missing queue evidence must not be accepted as a ranked entry.");
            var masteryFixtures = new[]
            {
                new Dictionary<string, object> { { "championId", 2 }, { "championPoints", 100L },
                    { "championLevel", 7 }, { "lastPlayTime", 2000L } },
                new Dictionary<string, object> { { "championId", 1 }, { "championPoints", 100L },
                    { "championLevel", 5 }, { "lastPlayTime", 3000L } },
                new Dictionary<string, object> { { "championId", 3 }, { "championPoints", 0L },
                    { "championLevel", 1 } },
                new Dictionary<string, object> { { "championId", 0 }, { "championPoints", 999L } }
            };
            var masteryNames = new Dictionary<int, string> { { 1, "Ahri" }, { 2, "Annie" }, { 3, "Garen" } };
            Func<IEnumerable<Dictionary<string, object>>, int[]> ids = rows => rows
                .Select(row => (int)LeagueMatchTimelineParser.Number(row, "championId").Value).ToArray();
            Require(ids(LeaguePlayerProfileDialog.FilterAndSortMasteries(masteryFixtures,
                    masteryNames, "", 2, true)).SequenceEqual(new[] { 1, 2, 3 }) &&
                ids(LeaguePlayerProfileDialog.FilterAndSortMasteries(masteryFixtures,
                    masteryNames, "", 1, true)).SequenceEqual(new[] { 2, 1, 3 }) &&
                ids(LeaguePlayerProfileDialog.FilterAndSortMasteries(masteryFixtures,
                    masteryNames, "", 3, false)).SequenceEqual(new[] { 2, 1, 3 }) &&
                ids(LeaguePlayerProfileDialog.FilterAndSortMasteries(masteryFixtures,
                    masteryNames, "aH", 0, false)).SequenceEqual(new[] { 1 }) &&
                ids(LeaguePlayerProfileDialog.FilterAndSortMasteries(masteryFixtures,
                    masteryNames, "2", 2, true)).SequenceEqual(new[] { 2 }) &&
                LeaguePlayerProfileDialog.FilterAndSortMasteries(null, masteryNames, "", 2, true).Count == 0,
                "Mastery list must support stable column sorting, name/ID filtering and missing-time ordering.");
            Require(LeaguePlayerProfileDialog.ProfileIconReference(42) ==
                    "lcu:/lol-game-data/assets/v1/profile-icons/42.jpg" &&
                LeaguePlayerProfileDialog.ProfileIconReference(0) == null &&
                LeaguePlayerProfileDialog.ProfileIconReference(-1) == null &&
                FACM.AppHost.Modules.LeagueEfficiencyModule.IsSafeEncounterAssetReference(
                    LeaguePlayerProfileDialog.ProfileIconReference(42)),
                "Profile avatar must use the pinned LCU profile icon path with invalid-ID fallback.");
            Require(LeaguePlayerProfileDialog.MasteryPortraitReference(1) ==
                    "lcu:/lol-game-data/assets/v1/champion-icons/1.png" &&
                LeaguePlayerProfileDialog.MasteryPortraitReference(0) == null &&
                LeaguePlayerProfileDialog.MasteryPortraitReference(-1) == null &&
                LeaguePlayerProfileDialog.MasteryPortraitReference(100001) == null &&
                FACM.AppHost.Modules.LeagueEfficiencyModule.IsSafeEncounterAssetReference(
                    LeaguePlayerProfileDialog.MasteryPortraitReference(266)),
                "Mastery portraits must use the pinned local champion icon path and reject invalid IDs.");
            var requestedPortraits = new HashSet<int> { 2 };
            Require(LeaguePlayerProfileDialog.SelectMasteryPortraitIds(
                    new[] { -1, 0, 2, 1, 1, 3, 4 }, requestedPortraits, 2)
                    .SequenceEqual(new[] { 1, 3 }) &&
                LeaguePlayerProfileDialog.SelectMasteryPortraitIds(
                    new[] { 1, 3 }, requestedPortraits, 0).Count == 0 &&
                LeaguePlayerProfileDialog.SelectMasteryPortraitIds(
                    null, requestedPortraits, 10).Count == 0,
                "Visible mastery portraits must be deduplicated and bounded across filter/scroll changes.");
            var tooltip = LeaguePlayerProfileDialog.MasteryDetailText(UiTextCatalog.Load(),
                new Dictionary<string, object> { { "championPointsSinceLastLevel", 0 },
                    { "championPointsUntilNextLevel", 1200 }, { "tokensEarned", 0 },
                    { "markRequiredForNextLevel", 1 }, { "highestGrade", "S+" } });
            Require(tooltip.Contains("S+") && tooltip.Contains("1,200") &&
                !tooltip.Contains("System.Collections"),
                "Mastery row inspection must preserve grade, progress and true zero without selecting a row.");
            var mastery = await service.LoadPlayerMasteriesAsync("self", "target", session, null, CancellationToken.None);
            Require(mastery.Status == LeaguePlayerDetailsStatus.Ready && mastery.Masteries.Count == 2 &&
                LeagueMatchTimelineParser.Number(mastery.Masteries[0], "championId") == 2 &&
                LeagueMatchTimelineParser.Number(mastery.Masteries[1], "championPoints") == 0,
                "Full mastery must sort by points and preserve valid zero-point heroes.");
            Require(LeaguePlayerDataService.ParsePlayerMasteries(Bytes("[{\"championId\":1,\"puuid\":\"other\"}]"), "target") == null &&
                LeaguePlayerDataService.ParsePlayerMasteries(Bytes("[{\"championId\":1},{\"championId\":1}]"), "target") == null &&
                LeaguePlayerDataService.ParsePlayerMasteries(Bytes("[]"), "target").Count == 0 &&
                LeaguePlayerDataService.ParsePlayerMasteries(Bytes("{}"), "target") == null,
                "Wrong target, duplicate heroes and malformed mastery roots cannot produce an accepted snapshot.");
            var top = await service.LoadPlayerMasteriesAsync("self", "target", session, null, CancellationToken.None, true);
            Require(top.Status == LeaguePlayerDetailsStatus.Ready && top.Score == 99 && top.Masteries.Count == 1 && api.TopReads == 1,
                "The mastery overview must consume the source top-eight envelope and score instead of inventing an aggregate from full rows.");
            var reads = api.Paths.Count;
            mastery = await service.LoadPlayerMasteriesAsync("self", "target", session, "TENCENT_HN10", CancellationToken.None);
            Require(mastery.Status == LeaguePlayerDetailsStatus.UnsupportedRegion && api.Paths.Count == reads,
                "Cross-region mastery must be explicitly unavailable without submitting the target to local endpoints.");
            active = new LeagueClientSession("LeagueClientUx", 2, 1001, "other", "https", "fixture", "HN10", "TENCENT");
            result = await service.LoadPlayerDetailsAsync("self", "target", session, null, LeaguePlayerHistorySource.Automatic, CancellationToken.None);
            Require(result.Status == LeaguePlayerDetailsStatus.AccountChanged && api.Paths.Count == reads,
                "A replaced session must reject profile requests before any reads.");
            active = session; api.SwitchOnRanked = true;
            result = await service.LoadPlayerDetailsAsync("self", "target", session, null, LeaguePlayerHistorySource.Automatic, CancellationToken.None);
            Require(result.Status == LeaguePlayerDetailsStatus.AccountChanged && result.Profile == null && result.Ranked == null,
                "A same-session account change during the response must discard all selected-player fields.");
            api.SwitchOnRanked = false; api.Changed = false;
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                var threw = false;
                try { await service.LoadPlayerMasteriesAsync("self", "target", session, null, cancelled.Token); }
                catch (OperationCanceledException) { threw = true; }
                Require(threw, "Closing a profile must propagate cancellation through the existing Player request gate.");
            }
            var challengesApi = new ChallengesApi();
            var challengesService = new LeaguePlayerDataService(api, new PerformanceBudgetProvider(), null, challengesApi, () => active);
            var challenges = await challengesService.LoadPlayerChallengesAsync("self", "target", session, "TENCENT_HN10", CancellationToken.None);
            Require(challenges.Status == LeaguePlayerDetailsStatus.Ready && challengesApi.Server == "TENCENT_HN10" &&
                challengesApi.Target == "target" && challengesService.TryGetCachedProfile() == null,
                "Regional challenge data must retain target/server identity without entering local profile caches.");
            challengesApi.AfterRead = () => api.Changed = true;
            challenges = await challengesService.LoadPlayerChallengesAsync("self", "target", session, "TENCENT_HN10", CancellationToken.None);
            Require(challenges.Status == LeaguePlayerDetailsStatus.AccountChanged && challenges.Data == null,
                "Changing the signed-in owner during a challenge read must discard its regional payload.");
            api.Changed = false;
            Require(!LeaguePlayerDataService.ValidatePlayerChallenges(LeaguePlayerDataService.ParseDetailsObject(
                    Bytes("{\"puuid\":\"other\",\"playerChallenges\":[]}")), "target") &&
                !LeaguePlayerDataService.ValidatePlayerChallenges(LeaguePlayerDataService.ParseDetailsObject(
                    Bytes("{\"puuid\":\"target\",\"playerChallenges\":[{\"id\":1},{\"id\":1}]}")), "target") &&
                LeaguePlayerProfileDialog.MetricText(new Dictionary<string, object> { { "v", 0 } }, "v") == "0" &&
                LeaguePlayerProfileDialog.MetricText(new Dictionary<string, object>(), "v") == "—",
                "Challenge identity/duplicate records must be rejected and missing collection values must differ from zero.");
            api.TimeoutRanked = true;
            result = await service.LoadPlayerDetailsAsync("self", "target", session, null, LeaguePlayerHistorySource.Automatic, CancellationToken.None);
            Require(result.Status == LeaguePlayerDetailsStatus.Partial && result.Ranked == null && result.Cosmetics != null,
                "One local endpoint timeout must not poison the subsequent optional profile request.");
            var remoteRankedApi = new ChallengesApi
            {
                RankedPayload = Bytes("{\\"queues\\":[{\\"queueType\\":\\"RANKED_SOLO_5x5\\",\\"tier\\":\\"DIAMOND\\",\\"rank\\":\\"II\\",\\"leaguePoints\\":0,\\"wins\\":4,\\"losses\\":4}]}") 
            };
            var rankedService = new LeaguePlayerDataService(api, new PerformanceBudgetProvider(),
                null, remoteRankedApi, () => active);
            result = await rankedService.LoadPlayerDetailsAsync("self", "target", session, null,
                LeaguePlayerHistorySource.Automatic, CancellationToken.None);
            Require(result.Status == LeaguePlayerDetailsStatus.Ready &&
                remoteRankedApi.RankedReads == 1 && remoteRankedApi.Target == "target" &&
                LeagueMatchTimelineParser.Text(LeaguePlayerDataService.RankedEntry(
                    result.Ranked, "RANKED_SOLO_5x5"), "tier") == "DIAMOND" &&
                LeagueMatchTimelineParser.Number(LeaguePlayerDataService.RankedEntry(
                    result.Ranked, "RANKED_SOLO_5x5"), "leaguePoints") == 0,
                "A missing local ranked response must use authenticated same-region SGP queues and preserve zero LP.");

            var priorReads = remoteRankedApi.RankedReads;
            result = await rankedService.LoadPlayerDetailsAsync("self", "target", session, null,
                LeaguePlayerHistorySource.Lcu, CancellationToken.None);
            Require(result.Status == LeaguePlayerDetailsStatus.Partial &&
                remoteRankedApi.RankedReads == priorReads,
                "LCU-only source must not silently use SGP-ranked data.");

            remoteRankedApi.RankedPayload = Bytes("{\\"queues\\":[{\\"queueType\\":\\"RANKED_SOLO_5x5\\",\\"wins\\":4},{\\"queueType\\":\\"RANKED_SOLO_5x5\\",\\"wins\\":5}]}");
            result = await rankedService.LoadPlayerDetailsAsync("self", "target", session, null,
                LeaguePlayerHistorySource.Automatic, CancellationToken.None);
            Require(result.Status == LeaguePlayerDetailsStatus.Partial && result.Ranked == null,
                "Conflicting SGP ranked queue records must not replace absent local rank data.");

            remoteRankedApi.RankedPayload = Bytes("{\\"queues\\":[{\\"queueType\\":\\"RANKED_SOLO_5x5\\",\\"wins\\":-1}]}");
            result = await rankedService.LoadPlayerDetailsAsync("self", "target", session, null,
                LeaguePlayerHistorySource.Automatic, CancellationToken.None);
            Require(result.Status == LeaguePlayerDetailsStatus.Partial && result.Ranked == null,
                "Negative ranked counters must fail closed.");

            remoteRankedApi.RankedPayload = Bytes("{\\"queues\\":[]}");
            remoteRankedApi.AfterRead = () => api.Changed = true;
            result = await rankedService.LoadPlayerDetailsAsync("self", "target", session, null,
                LeaguePlayerHistorySource.Automatic, CancellationToken.None);
            Require(result.Status == LeaguePlayerDetailsStatus.AccountChanged &&
                result.Profile == null && result.Ranked == null,
                "An account change during SGP ranked fallback must not expose stale player data.");
            api.Changed = false;
            Require(LeaguePlayerDataService.ValidateSgpRankedStats(
                    LeaguePlayerDataService.ParseDetailsObject(Bytes("{\\"queues\\":[]}"))) &&
                !LeaguePlayerDataService.ValidateSgpRankedStats(
                    LeaguePlayerDataService.ParseDetailsObject(Bytes("{\\"queueMap\\":{}}"))),
                "Unranked source data differs from an incompatible ranked schema.");
            using (var dialog = new LeaguePlayerProfileDialog(service, UiTextCatalog.Load(), "self", "target", "fixture",
                session, () => session))
            using (var bitmap = new Bitmap(700, 490))
            { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); }
        }
        private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private sealed class ChallengesApi : ILeagueSgpMatchHistoryApi, ILeagueSgpPlayerChallengesApi, ILeagueSgpRankedStatsApi
        {
            internal string Server, Target;
            internal Action AfterRead;
            internal int RankedReads;
            internal byte[] RankedPayload;
            public Task<byte[]> TryGetRankedStatsAsync(string target, CancellationToken token, string serverId = null)
            {
                token.ThrowIfCancellationRequested();
                Target = target; Server = serverId; RankedReads++;
                AfterRead?.Invoke();
                return Task.FromResult(RankedPayload);
            }
            public Task<byte[]> TryGetChallengesAsync(string target, CancellationToken token, string serverId = null)
            {
                Server = serverId; Target = target; AfterRead?.Invoke();
                return Task.FromResult(Bytes("{\"puuid\":\"target\",\"totalPoints\":{\"current\":0},\"playerChallenges\":[{\"id\":505001,\"currentValue\":0}]}"));
            }
            public Task<byte[]> TryGetSummaryAsync(string target, int start, int count, CancellationToken token, string serverId = null)
            { return Task.FromResult<byte[]>(null); }
            public Task<byte[]> TryGetGameSummaryAsync(long id, CancellationToken token, string serverId = null)
            { return Task.FromResult<byte[]>(null); }
            public Task<byte[]> TryGetGameDetailsAsync(long id, CancellationToken token, string serverId = null)
            { return Task.FromResult<byte[]>(null); }
            public Task<byte[]> TryGetSummonerAsync(string target, CancellationToken token, string serverId = null)
            { return Task.FromResult<byte[]>(null); }
        }
        private sealed class Api : ILeagueClientApi, ILeagueMasteryReadApi
        {
            internal readonly List<string> Paths = new List<string>();
            internal bool SwitchOnRanked, Changed, TimeoutRanked;
            internal int TopReads;
            public Task<byte[]> TryGetMasteryTopAsync(LeagueClientSession expected, string target, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); TopReads++;
                return Task.FromResult(Bytes("{\"puuid\":\"target\",\"score\":99,\"masteries\":[{\"championId\":2,\"championPoints\":200}]}"));
            }
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); Paths.Add(path);
                if (path == LeagueDashboardDetailsService.SummonerPath)
                    return Task.FromResult(Bytes(Changed ? "{\"puuid\":\"other\"}" : "{\"puuid\":\"self\"}"));
                if (path == "/lol-summoner/v2/summoners/puuid/target")
                    return Task.FromResult(Bytes("{\"puuid\":\"target\",\"gameName\":\"Fixture\",\"summonerLevel\":42,\"profileIconId\":42}"));
                if (path == "/lol-ranked/v1/ranked-stats/target")
                {
                    if (TimeoutRanked) throw new OperationCanceledException();
                    if (SwitchOnRanked) Changed = true;
                    return Task.FromResult(Bytes("{\"queueMap\":{\"RANKED_SOLO_5x5\":{\"tier\":\"GOLD\",\"division\":\"II\",\"leaguePoints\":0,\"wins\":5,\"losses\":5}},\"queues\":[{\"queueType\":\"RANKED_SOLO_5x5\",\"leaguePoints\":99},{\"queueType\":\"RANKED_FLEX_SR\",\"tier\":\"NA\"}]}"));
                }
                if (path == "/lol-summoner/v1/summoner-profile?puuid=target") return Task.FromResult(Bytes("{\"backgroundSkinId\":0}"));
                if (path == "/lol-champion-mastery/v1/target/champion-mastery")
                    return Task.FromResult(Bytes("[{\"championId\":1,\"puuid\":\"target\",\"championPoints\":0},{\"championId\":2,\"puuid\":\"target\",\"championPoints\":200}]"));
                return Task.FromResult<byte[]>(null);
            }
        }
    }
}
