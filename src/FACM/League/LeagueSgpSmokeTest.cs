using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal static class LeagueSgpSmokeTest
    {
        internal static void Validate()
        {
            var current = Session("HN10", "TENCENT");
            var local = new TokenApi();
            var handler = new CaptureHandler();
            using (var api = new LeagueSgpMatchHistoryApiClient(() => current, local, handler))
            {
                Require(LeagueSgpMatchHistoryApiClient.AvailableServers(current).Length == 10,
                    "Tencent discovery must include the exact pinned server catalog.");
                var aliases = new Dictionary<string, string> {
                    { "NA", "NA1" }, { "BR", "BR1" }, { "TR", "TR1" },
                    { "LAN", "LA1" }, { "LAS", "LA2" }, { "OCE", "OC1" },
                    { "EUW1", "EUW" }, { "JP1", "JP" } };
                foreach (var alias in aliases)
                {
                    var regionalSession = Session("unused-platform", alias.Key);
                    Require(LeagueSgpMatchHistoryApiClient.CurrentServerId(regionalSession) == alias.Value &&
                        LeagueSgpMatchHistoryApiClient.ResolveServer(regionalSession)?.Id == alias.Value,
                        "Pinned SGP region alias mapping failed for " + alias.Key);
                }
                Require(LeagueSgpMatchHistoryApiClient.ResolveServer(Session("unused-platform", "UNKNOWN")) == null &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(Session("unused-platform", "NA"), "EUW") == null,
                    "Unknown and unrelated region selectors must remain unavailable.");
                var euw = LeagueSgpMatchHistoryApiClient.ResolveServer(Session("EUW1", "EUW"));
                Require(euw != null && euw.PathRegion == "EUW1" &&
                    euw.MatchHistory == "https://euc1-red.pp.sgp.pvp.net" &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(current, "NA1") == null &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(Session("NA1", "NA1"), "EUW") == null,
                    "International path overrides and Tencent-only cross-region capability must remain explicit.");
                api.TryGetGameSummaryAsync(601, CancellationToken.None, "TENCENT_HN1").GetAwaiter().GetResult();
                Require(handler.Url == "https://hn1-k8s-sgp.lol.qq.com:21019/" +
                    "match-history-query/v1/products/lol/HN1_601/SUMMARY" &&
                    handler.Authorization == "Bearer history-token" && local.Paths[0] == LeagueSgpMatchHistoryApiClient.EntitlementsPath,
                    "A selected regional game summary must use that server's path and Entitlements token.");
                api.TryGetGameDetailsAsync(601, CancellationToken.None).GetAwaiter().GetResult();
                Require(handler.Url.EndsWith("/HN10_601/DETAILS", StringComparison.Ordinal),
                    "SGP timeline details must use DETAILS rather than the participant summary payload.");
                api.TryGetSummonerAsync("target-puuid", CancellationToken.None, "TENCENT_TJ100")
                    .GetAwaiter().GetResult();
                Require(handler.Url == "https://tj100-sgp.lol.qq.com:21019/summoner-ledge/v1/regions/TJ100/summoners/puuids" &&
                    handler.Method == HttpMethod.Post && handler.Body == "[\"target-puuid\"]" &&
                    handler.Authorization == "Bearer profile-token" &&
                    local.Paths[local.Paths.Count - 1] == LeagueSgpMatchHistoryApiClient.LeagueSessionTokenPath,
                    "Summoner profiles require the League session token, exact region and bounded JSON POST body.");
                api.TryGetRankedStatsAsync("target-puuid", CancellationToken.None).GetAwaiter().GetResult();
                Require(handler.Url == "https://hn10-k8s-sgp.lol.qq.com:21019/leagues-ledge/v2/rankedStats/puuid/target-puuid" &&
                    handler.Method == HttpMethod.Get && handler.Authorization == "Bearer profile-token",
                    "Same-region SGP ranked fallback must use league session token and pinned leagues-ledge path.");
                var rankedCalls = handler.Calls;
                var rankedTokenReads = local.Paths.Count;
                api.TryGetRankedStatsAsync("target-puuid", CancellationToken.None, "TENCENT_HN1").GetAwaiter().GetResult();
                Require(handler.Calls == rankedCalls && local.Paths.Count == rankedTokenReads,
                    "Source-ranked league stats must never use cross-region Tencent access.");
                api.TryGetChallengesAsync("target-puuid", CancellationToken.None, "TENCENT_HN1").GetAwaiter().GetResult();
                Require(handler.Url == "https://hn1-k8s-sgp.lol.qq.com:21019/challenges-client/v2/all-player-data/?puuid=target-puuid" &&
                    handler.Method == HttpMethod.Post && handler.Body == "[]" &&
                    handler.Authorization == "Bearer profile-token",
                    "Regional challenges must preserve the trailing slash, empty friends array and League-session token.");
                var calls = handler.Calls;
                var reads = local.Paths.Count;
                api.TryGetSummaryAsync("../unsafe", 0, 10, CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetSummaryAsync("target", int.MaxValue, 10, CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetGameSummaryAsync(0, CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetSummonerAsync("target", CancellationToken.None, "https://other.example")
                    .GetAwaiter().GetResult();
                api.TryGetChallengesAsync("../unsafe", CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetChallengesAsync("target", CancellationToken.None, "NA1").GetAwaiter().GetResult();
                api.TryGetRankedStatsAsync("../unsafe", CancellationToken.None).GetAwaiter().GetResult();
                Require(handler.Calls == calls && local.Paths.Count == reads,
                    "Invalid target inputs must fail before credentials or HTTP are requested.");
                handler.AfterRead = () => current = Session("HN1", "TENCENT");
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() == null,
                    "Even an unchanged LCU port/password cannot publish a response after region replacement.");
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel();
                    var cancelled = false;
                    try { api.TryGetGameSummaryAsync(601, cancel.Token).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Require(cancelled, "External cancellation must remain observable by the caller.");
                }
            }
            ValidateRecovery();
            Require(LeagueSgpMatchHistoryApiClient.ReadLeagueSessionToken(Bytes("\"profile-token\"")) == "profile-token" &&
                LeagueSgpMatchHistoryApiClient.ReadLeagueSessionToken(Bytes("{\"token\":\"wrong-shape\"}")) == null &&
                LeagueSgpMatchHistoryApiClient.ReadLeagueSessionToken(Bytes("\"bad\\u007fheader\"")) == null,
                "League session credentials must be a bounded printable JSON string.");
        }

        private static void ValidateRecovery()
        {
            Require(LeagueSgpMatchHistoryApiClient.IsTransientGatewayStatus(HttpStatusCode.BadGateway) &&
                LeagueSgpMatchHistoryApiClient.IsTransientGatewayStatus(HttpStatusCode.ServiceUnavailable) &&
                LeagueSgpMatchHistoryApiClient.IsTransientGatewayStatus(HttpStatusCode.GatewayTimeout) &&
                !LeagueSgpMatchHistoryApiClient.IsTransientGatewayStatus((HttpStatusCode)429),
                "Gateway retries must not include rate-limited responses.");
            var owner = Session("HN10", "TENCENT");
            var local = new TokenApi();
            var handler = new CaptureHandler();
            local.HistoryTokens.Enqueue("expired-token");
            local.HistoryTokens.Enqueue("fresh-token");
            handler.Statuses.Enqueue(HttpStatusCode.Unauthorized);
            handler.Statuses.Enqueue(HttpStatusCode.OK);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, local, handler))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() != null &&
                    handler.Calls == 2 && local.Paths.Count == 2 &&
                    handler.Authorization == "Bearer fresh-token",
                    "An expired entitlement token must be freshly read before one retry.");

            var unchanged = new CaptureHandler();
            unchanged.Statuses.Enqueue(HttpStatusCode.Unauthorized);
            unchanged.Statuses.Enqueue(HttpStatusCode.OK);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), unchanged))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() == null &&
                    unchanged.Calls == 1,
                    "A rejected unchanged token must not trigger another HTTP request.");

            var gateway = new CaptureHandler();
            gateway.Statuses.Enqueue(HttpStatusCode.ServiceUnavailable);
            gateway.Statuses.Enqueue(HttpStatusCode.OK);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), gateway))
                Require(api.TryGetSummonerAsync("target", CancellationToken.None).GetAwaiter().GetResult() != null &&
                    gateway.Calls == 2 && gateway.Bodies.Count == 2 &&
                    gateway.Bodies[0] == "[\"target\"]" && gateway.Bodies[1] == gateway.Bodies[0],
                    "A transient gateway error must retry the same JSON POST body.");

            var profileLocal = new TokenApi();
            profileLocal.ProfileTokens.Enqueue("expired-profile");
            profileLocal.ProfileTokens.Enqueue("fresh-profile");
            var profileHandler = new CaptureHandler();
            profileHandler.Statuses.Enqueue(HttpStatusCode.Unauthorized);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, profileLocal, profileHandler))
                Require(api.TryGetSummonerAsync("target", CancellationToken.None).GetAwaiter().GetResult() != null &&
                    profileHandler.Calls == 2 &&
                    profileHandler.Authorization == "Bearer fresh-profile" &&
                    profileHandler.Bodies.Count == 2 &&
                    profileHandler.Bodies[0] == profileHandler.Bodies[1],
                    "League-session token refresh must retry an identical summoner POST only once.");

            var stillUnavailable = new CaptureHandler();
            stillUnavailable.Statuses.Enqueue(HttpStatusCode.ServiceUnavailable);
            stillUnavailable.Statuses.Enqueue(HttpStatusCode.ServiceUnavailable);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), stillUnavailable))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() == null &&
                    stillUnavailable.Calls == 2, "Repeated gateway errors must not cause unbounded retries.");

            var connection = new CaptureHandler { ThrowOnce = true };
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), connection))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() != null &&
                    connection.Calls == 2, "A single transport failure must recover with a bounded retry.");

            var throttled = new CaptureHandler();
            throttled.Statuses.Enqueue((HttpStatusCode)429);
            throttled.Statuses.Enqueue(HttpStatusCode.OK);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), throttled))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() == null &&
                    throttled.Calls == 1,
                    "Rate-limited SGP responses must never be retried.");

            var replaced = new CaptureHandler();
            replaced.Statuses.Enqueue(HttpStatusCode.Unauthorized);
            replaced.AfterRead = () => owner = Session("HN1", "TENCENT");
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), replaced))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() == null &&
                    replaced.Calls == 1,
                    "Changing the LCU region must prevent credential retry.");
        }

        private static LeagueClientSession Session(string platform, string region)
        {
            return new LeagueClientSession("LeagueClientUx", 1, 12345, "local-fixture", "https", "fixture", platform, region);
        }

        private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class TokenApi : ILeagueClientApi
        {
            internal readonly List<string> Paths = new List<string>();
            internal readonly Queue<string> HistoryTokens = new Queue<string>();
            internal readonly Queue<string> ProfileTokens = new Queue<string>();
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Paths.Add(path);
                var historyToken = HistoryTokens.Count > 0 ? HistoryTokens.Dequeue() : "history-token";
                var profileToken = ProfileTokens.Count > 0 ? ProfileTokens.Dequeue() : "profile-token";
                return Task.FromResult(Bytes(path == LeagueSgpMatchHistoryApiClient.EntitlementsPath
                    ? "{\"accessToken\":\"" + historyToken + "\"}" : "\"" + profileToken + "\""));
            }
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            internal int Calls;
            internal readonly Queue<HttpStatusCode> Statuses = new Queue<HttpStatusCode>();
            internal bool ThrowOnce;
            internal readonly List<string> Bodies = new List<string>();
            internal string Url, Authorization, Body;
            internal HttpMethod Method;
            internal Action AfterRead;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Calls++;
                Url = request.RequestUri.AbsoluteUri;
                Authorization = request.Headers.Authorization.ToString();
                Method = request.Method;
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                Bodies.Add(Body);
                if (ThrowOnce) { ThrowOnce = false; throw new HttpRequestException("synthetic network failure"); }
                AfterRead?.Invoke();
                return new HttpResponseMessage(Statuses.Count == 0 ? HttpStatusCode.OK : Statuses.Dequeue())
                    { Content = new ByteArrayContent(Bytes("{}")) };
            }
        }
    }
}
