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
        private static async Task ValidateAsync()
        {
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
        private sealed class Api : ILeagueClientApi
        {
            internal readonly List<string> Paths = new List<string>();
            internal bool SwitchOnRanked, Changed, TimeoutRanked;
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
