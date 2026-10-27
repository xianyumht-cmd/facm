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
                var calls = handler.Calls;
                var reads = local.Paths.Count;
                api.TryGetSummaryAsync("../unsafe", 0, 10, CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetSummaryAsync("target", int.MaxValue, 10, CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetGameSummaryAsync(0, CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetSummonerAsync("target", CancellationToken.None, "https://other.example")
                    .GetAwaiter().GetResult();
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
            Require(LeagueSgpMatchHistoryApiClient.ReadLeagueSessionToken(Bytes("\"profile-token\"")) == "profile-token" &&
                LeagueSgpMatchHistoryApiClient.ReadLeagueSessionToken(Bytes("{\"token\":\"wrong-shape\"}")) == null &&
                LeagueSgpMatchHistoryApiClient.ReadLeagueSessionToken(Bytes("\"bad\\u007fheader\"")) == null,
                "League session credentials must be a bounded printable JSON string.");
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
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Paths.Add(path);
                return Task.FromResult(Bytes(path == LeagueSgpMatchHistoryApiClient.EntitlementsPath
                    ? "{\"accessToken\":\"history-token\"}" : "\"profile-token\""));
            }
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            internal int Calls;
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
                AfterRead?.Invoke();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes("{}")) };
            }
        }
    }
}
