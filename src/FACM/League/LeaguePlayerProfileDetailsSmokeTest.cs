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
            { Connected = true, Phase = name };
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
            using (var dialog = new LeaguePlayerProfileDialog(service, UiTextCatalog.Load(), "self", "target", "fixture",
                session, () => session))
            using (var bitmap = new Bitmap(700, 490))
            { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); }
        }
        private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private sealed class ChallengesApi : ILeagueSgpMatchHistoryApi, ILeagueSgpPlayerChallengesApi
        {
            internal string Server, Target;
            internal Action AfterRead;
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
                    return Task.FromResult(Bytes("{\"puuid\":\"target\",\"gameName\":\"Fixture\",\"summonerLevel\":42}"));
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
