using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Mayhem;

namespace FACM.League
{
    internal interface ILeagueSgpMatchHistoryApi
    {
        Task<byte[]> TryGetSummaryAsync(string targetPuuid, int startIndex, int count,
            CancellationToken cancellationToken, string serverId = null);
        Task<byte[]> TryGetGameSummaryAsync(long gameId, CancellationToken cancellationToken, string serverId = null);
        Task<byte[]> TryGetGameDetailsAsync(long gameId, CancellationToken cancellationToken, string serverId = null);
        Task<byte[]> TryGetSummonerAsync(string targetPuuid, CancellationToken cancellationToken, string serverId = null);
    }

    internal interface ILeagueSgpPlayerChallengesApi
    {
        Task<byte[]> TryGetChallengesAsync(string targetPuuid, CancellationToken token, string serverId = null);
    }

    internal sealed class LeagueSgpServer
    {
        internal string Id;
        internal string Name;
        internal string MatchHistory;
        internal string Common;
        internal string PathRegion;
        internal bool IsTencent;
        public override string ToString() { return Name; }
    }

    internal sealed class LeagueSgpMatchHistoryApiClient : ILeagueSgpMatchHistoryApi, ILeagueSgpPlayerChallengesApi, IDisposable
    {
        internal const string EntitlementsPath = "/entitlements/v1/token";
        internal const string LeagueSessionTokenPath = "/lol-league-session/v1/league-session-token";
        private static readonly LeagueSgpServer[] Servers =
        {
            new LeagueSgpServer { Id = "TENCENT_HN1", Name = "艾欧尼亚", PathRegion = "HN1", IsTencent = true,
                MatchHistory = "https://hn1-k8s-sgp.lol.qq.com:21019", Common = "https://hn1-k8s-sgp.lol.qq.com:21019" },
            new LeagueSgpServer { Id = "TENCENT_HN10", Name = "黑色玫瑰", PathRegion = "HN10", IsTencent = true,
                MatchHistory = "https://hn10-k8s-sgp.lol.qq.com:21019", Common = "https://hn10-k8s-sgp.lol.qq.com:21019" },
            new LeagueSgpServer { Id = "TENCENT_TJ100", Name = "联盟一区", PathRegion = "TJ100", IsTencent = true,
                MatchHistory = "https://tj100-sgp.lol.qq.com:21019", Common = "https://tj100-sgp.lol.qq.com:21019" },
            new LeagueSgpServer { Id = "TENCENT_TJ101", Name = "联盟二区", PathRegion = "TJ101", IsTencent = true,
                MatchHistory = "https://tj101-sgp.lol.qq.com:21019", Common = "https://tj101-sgp.lol.qq.com:21019" },
            new LeagueSgpServer { Id = "TENCENT_NJ100", Name = "联盟三区", PathRegion = "NJ100", IsTencent = true,
                MatchHistory = "https://nj100-sgp.lol.qq.com:21019", Common = "https://nj100-sgp.lol.qq.com:21019" },
            new LeagueSgpServer { Id = "TENCENT_GZ100", Name = "联盟四区", PathRegion = "GZ100", IsTencent = true,
                MatchHistory = "https://gz100-sgp.lol.qq.com:21019", Common = "https://gz100-sgp.lol.qq.com:21019" },
            new LeagueSgpServer { Id = "TENCENT_CQ100", Name = "联盟五区", PathRegion = "CQ100", IsTencent = true,
                MatchHistory = "https://cq100-sgp.lol.qq.com:21019", Common = "https://cq100-sgp.lol.qq.com:21019" },
            new LeagueSgpServer { Id = "TENCENT_BGP2", Name = "男爵领域", PathRegion = "BGP2", IsTencent = true,
                MatchHistory = "https://bgp2-k8s-sgp.lol.qq.com:21019", Common = "https://bgp2-k8s-sgp.lol.qq.com:21019" },
            new LeagueSgpServer { Id = "TENCENT_PBE", Name = "体验服", PathRegion = "PBE", IsTencent = true,
                MatchHistory = "https://pbe-sgp.lol.qq.com:21019", Common = "https://pbe-sgp.lol.qq.com:21019" },
            new LeagueSgpServer { Id = "TENCENT_PREPBE", Name = "预备体验服", PathRegion = "PREPBE", IsTencent = true,
                MatchHistory = "https://prepbe-sgp.lol.qq.com:21019", Common = "https://prepbe-sgp.lol.qq.com:21019" },
            new LeagueSgpServer { Id = "TW2", Name = "TW2", PathRegion = "TW2", IsTencent = false,
                MatchHistory = "https://apse1-red.pp.sgp.pvp.net", Common = "https://tw2-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "SG2", Name = "SG2", PathRegion = "SG2", IsTencent = false,
                MatchHistory = "https://apse1-red.pp.sgp.pvp.net", Common = "https://sg2-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "PH2", Name = "PH2", PathRegion = "PH2", IsTencent = false,
                MatchHistory = "https://apse1-red.pp.sgp.pvp.net", Common = "https://ph2-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "VN2", Name = "VN2", PathRegion = "VN2", IsTencent = false,
                MatchHistory = "https://apse1-red.pp.sgp.pvp.net", Common = "https://vn2-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "PBE", Name = "PBE", PathRegion = "PBE1", IsTencent = false,
                MatchHistory = "https://usw2-red.pp.sgp.pvp.net", Common = "https://pbe-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "EUW", Name = "EUW", PathRegion = "EUW1", IsTencent = false,
                MatchHistory = "https://euc1-red.pp.sgp.pvp.net", Common = "https://euw-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "JP", Name = "JP", PathRegion = "JP1", IsTencent = false,
                MatchHistory = "https://apne1-red.pp.sgp.pvp.net", Common = "https://jp-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "RU", Name = "RU", PathRegion = "RU", IsTencent = false,
                MatchHistory = "https://euc1-red.pp.sgp.pvp.net", Common = "https://ru-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "BR1", Name = "BR1", PathRegion = "BR1", IsTencent = false,
                MatchHistory = "https://usw2-red.pp.sgp.pvp.net", Common = "https://br-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "OC1", Name = "OC1", PathRegion = "OC1", IsTencent = false,
                MatchHistory = "https://apse1-red.pp.sgp.pvp.net", Common = "https://oce-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "TR1", Name = "TR1", PathRegion = "TR1", IsTencent = false,
                MatchHistory = "https://euc1-red.pp.sgp.pvp.net", Common = "https://tr-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "LA1", Name = "LA1", PathRegion = "LA1", IsTencent = false,
                MatchHistory = "https://usw2-red.pp.sgp.pvp.net", Common = "https://lan-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "LA2", Name = "LA2", PathRegion = "LA2", IsTencent = false,
                MatchHistory = "https://usw2-red.pp.sgp.pvp.net", Common = "https://las-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "NA1", Name = "NA1", PathRegion = "NA1", IsTencent = false,
                MatchHistory = "https://usw2-red.pp.sgp.pvp.net", Common = "https://na-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "TH2", Name = "TH2", PathRegion = "TH2", IsTencent = false,
                MatchHistory = "https://apse1-red.pp.sgp.pvp.net", Common = "https://th2-red.lol.sgp.pvp.net" },
            new LeagueSgpServer { Id = "KR", Name = "KR", PathRegion = "KR", IsTencent = false,
                MatchHistory = "https://apne1-red.pp.sgp.pvp.net", Common = "https://kr-red.lol.sgp.pvp.net" }
        };

        private readonly Func<LeagueClientSession> _session;
        private readonly ILeagueClientApi _local;
        private readonly HttpClient _http;

        internal LeagueSgpMatchHistoryApiClient(LeagueClientSessionProvider sessions, ILeagueClientApi local)
            : this(() => sessions.GetSession(), local, new HttpClientHandler
                { AllowAutoRedirect = false, UseCookies = false })
        {
            if (sessions == null) throw new ArgumentNullException(nameof(sessions));
        }

        internal LeagueSgpMatchHistoryApiClient(Func<LeagueClientSession> session,
            ILeagueClientApi local, HttpMessageHandler handler)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _local = local ?? throw new ArgumentNullException(nameof(local));
            _http = new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler)))
                { Timeout = Timeout.InfiniteTimeSpan };
        }

        internal static string CurrentServerId(LeagueClientSession session)
        {
            if (session == null) return null;
            var platform = (session.PlatformId ?? "").ToUpperInvariant();
            var region = (session.Region ?? "").ToUpperInvariant();
            if (region == "TENCENT" || (region.Length == 0 &&
                Servers.Any(server => server.IsTencent && server.PathRegion == platform)))
                return "TENCENT_" + platform;
            switch (region)
            {
                case "NA": return "NA1";
                case "BR": return "BR1";
                case "TR": return "TR1";
                case "LAN": return "LA1";
                case "LAS": return "LA2";
                case "OCE": return "OC1";
                case "EUW1": return "EUW";
                case "JP1": return "JP";
                default: return region.Length > 0 ? region : platform;
            }
        }

        internal static LeagueSgpServer[] AvailableServers(LeagueClientSession session)
        {
            var current = Servers.FirstOrDefault(server => server.Id == CurrentServerId(session));
            return current == null ? Array.Empty<LeagueSgpServer>() : Servers
                .Where(server => current.IsTencent ? server.IsTencent : server.Id == current.Id)
                .Select(server => new LeagueSgpServer { Id = server.Id, Name = server.Name,
                    PathRegion = server.PathRegion, IsTencent = server.IsTencent,
                    MatchHistory = server.MatchHistory, Common = server.Common }).ToArray();
        }

        internal static LeagueSgpServer ResolveServer(LeagueClientSession session, string serverId = null)
        {
            var current = Servers.FirstOrDefault(server => server.Id == CurrentServerId(session));
            if (current == null) return null;
            var target = string.IsNullOrEmpty(serverId) ? current :
                Servers.FirstOrDefault(server => string.Equals(server.Id, serverId, StringComparison.OrdinalIgnoreCase));
            return target != null && (target.Id == current.Id || (current.IsTencent && target.IsTencent))
                ? target : null;
        }

        internal static bool SafePuuid(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length <= 128 && value.All(character =>
                (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') ||
                (character >= '0' && character <= '9') || character == '-' || character == '_');
        }

        internal static bool TryResolveTencentEndpoint(LeagueClientSession session,
            string targetPuuid, int startIndex, int count, out Uri endpoint)
        {
            var server = ResolveServer(session);
            endpoint = server != null && server.IsTencent
                ? SummaryEndpoint(server, targetPuuid, startIndex, count) : null;
            return endpoint != null;
        }

        internal static Uri SummaryEndpoint(LeagueSgpServer server, string targetPuuid, int startIndex, int count)
        {
            if (server == null || !SafePuuid(targetPuuid) || startIndex < 0 || count < 1 || count > 200 ||
                startIndex > int.MaxValue - count) return null;
            return new Uri(server.MatchHistory + "/match-history-query/v1/products/lol/player/" +
                Uri.EscapeDataString(targetPuuid) + "/SUMMARY?startIndex=" +
                startIndex.ToString(CultureInfo.InvariantCulture) + "&count=" + count.ToString(CultureInfo.InvariantCulture));
        }

        public Task<byte[]> TryGetSummaryAsync(string targetPuuid, int startIndex, int count,
            CancellationToken cancellationToken, string serverId = null)
        {
            var session = _session();
            return SendAsync(session, SummaryEndpoint(ResolveServer(session, serverId),
                targetPuuid, startIndex, count), null, false, cancellationToken);
        }

        public Task<byte[]> TryGetGameSummaryAsync(long gameId, CancellationToken cancellationToken,
            string serverId = null)
        {
            return GetGameAsync(gameId, "SUMMARY", serverId, cancellationToken);
        }

        public Task<byte[]> TryGetGameDetailsAsync(long gameId, CancellationToken cancellationToken,
            string serverId = null)
        {
            return GetGameAsync(gameId, "DETAILS", serverId, cancellationToken);
        }

        private Task<byte[]> GetGameAsync(long gameId, string kind, string serverId, CancellationToken token)
        {
            var session = _session();
            var server = ResolveServer(session, serverId);
            var endpoint = server == null || gameId <= 0 ? null : new Uri(server.MatchHistory +
                "/match-history-query/v1/products/lol/" + server.PathRegion + "_" +
                gameId.ToString(CultureInfo.InvariantCulture) + "/" + kind);
            return SendAsync(session, endpoint, null, false, token);
        }

        public Task<byte[]> TryGetSummonerAsync(string targetPuuid, CancellationToken cancellationToken,
            string serverId = null)
        {
            var session = _session();
            var server = ResolveServer(session, serverId);
            var endpoint = server == null || !SafePuuid(targetPuuid) ? null : new Uri(server.Common +
                "/summoner-ledge/v1/regions/" + server.PathRegion + "/summoners/puuids");
            var body = endpoint == null ? null : new JavaScriptSerializer().Serialize(new[] { targetPuuid });
            return SendAsync(session, endpoint, body, true, cancellationToken);
        }

        public Task<byte[]> TryGetChallengesAsync(string targetPuuid, CancellationToken token, string serverId = null)
        {
            var session = _session();
            var server = ResolveServer(session, serverId);
            var endpoint = server == null || !SafePuuid(targetPuuid) ? null : new Uri(server.Common +
                "/challenges-client/v2/all-player-data/?puuid=" + Uri.EscapeDataString(targetPuuid));
            return SendAsync(session, endpoint, "[]", true, token);
        }

        private bool SameSession(LeagueClientSession expected)
        {
            var current = _session();
            return expected != null && expected.Matches(current) &&
                string.Equals(CurrentServerId(expected), CurrentServerId(current), StringComparison.Ordinal);
        }

        private async Task<byte[]> SendAsync(LeagueClientSession session, Uri endpoint, string body,
            bool leagueToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session == null || endpoint == null) return null;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    string rejectedToken = null;
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        if (!SameSession(session)) return null;
                        var credentials = await _local.TryGetBytesAsync(
                            leagueToken ? LeagueSessionTokenPath : EntitlementsPath, deadline.Token).ConfigureAwait(false);
                        var token = leagueToken ? ReadLeagueSessionToken(credentials) : ReadEntitlementsToken(credentials);
                        if (token == null || !SameSession(session) ||
                            (rejectedToken != null && string.Equals(token, rejectedToken, StringComparison.Ordinal)))
                            return null;
                        using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, endpoint))
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                            try
                            {
                                using (var response = await _http.SendAsync(request,
                                    HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                                {
                                    if (!SameSession(session)) return null;
                                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && attempt == 0)
                                    {
                                        rejectedToken = token;
                                        continue;
                                    }
                                    if (IsTransientGatewayStatus(response.StatusCode) && attempt == 0)
                                    {
                                        await Task.Delay(200, deadline.Token).ConfigureAwait(false);
                                        continue;
                                    }
                                    if (!response.IsSuccessStatusCode) return null;
                                    var bytes = await CancelableHttpContentReader.ReadBytesAsync(
                                        response.Content, deadline.Token, 16 * 1024 * 1024).ConfigureAwait(false);
                                    return SameSession(session) ? bytes : null;
                                }
                            }
                            catch (HttpRequestException) when (attempt == 0 && SameSession(session))
                            {
                                await Task.Delay(200, deadline.Token).ConfigureAwait(false);
                            }
                        }
                    }
                    return null;
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    return null;
                }
                catch (HttpRequestException) { return null; }
                catch (System.IO.IOException) { return null; }
                catch (ObjectDisposedException) { return null; }
            }
        }

        internal static bool IsTransientGatewayStatus(System.Net.HttpStatusCode status)
        {
            return status == System.Net.HttpStatusCode.BadGateway ||
                status == System.Net.HttpStatusCode.ServiceUnavailable ||
                status == System.Net.HttpStatusCode.GatewayTimeout;
        }

        internal static string ReadLeagueSessionToken(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > 64 * 1024) return null;
            try
            {
                return SafeToken(new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(data)) as string);
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static string SafeToken(string token)
        {
            return !string.IsNullOrWhiteSpace(token) && token.Length <= 8192 &&
                token.All(character => character > 32 && character < 127) ? token : null;
        }

        internal static string ReadEntitlementsToken(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > 64 * 1024) return null;
            try
            {
                var parser = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 };
                var root = parser.DeserializeObject(Encoding.UTF8.GetString(data)) as Dictionary<string, object>;
                object value;
                if (root == null || !root.TryGetValue("accessToken", out value)) return null;
                var token = value as string;
                return SafeToken(token);
            }
            catch (Exception) { return null; }
        }

        public void Dispose() { _http.Dispose(); }
    }
}
