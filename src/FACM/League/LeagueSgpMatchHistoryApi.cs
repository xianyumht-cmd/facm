using System;
using System.Collections.Generic;
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
            CancellationToken cancellationToken);
    }

    internal sealed class LeagueSgpMatchHistoryApiClient : ILeagueSgpMatchHistoryApi, IDisposable
    {
        internal const string EntitlementsPath = "/entitlements/v1/token";
        private static readonly IReadOnlyDictionary<string, string> TencentHosts =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "HN1", "hn1-k8s-sgp.lol.qq.com" },
                { "HN10", "hn10-k8s-sgp.lol.qq.com" },
                { "TJ100", "tj100-sgp.lol.qq.com" },
                { "TJ101", "tj101-sgp.lol.qq.com" },
                { "NJ100", "nj100-sgp.lol.qq.com" },
                { "GZ100", "gz100-sgp.lol.qq.com" },
                { "CQ100", "cq100-sgp.lol.qq.com" },
                { "BGP2", "bgp2-k8s-sgp.lol.qq.com" },
                { "PBE", "pbe-sgp.lol.qq.com" },
                { "PREPBE", "prepbe-sgp.lol.qq.com" }
            };

        private readonly LeagueClientSessionProvider _sessions;
        private readonly ILeagueClientApi _local;
        private readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        }) { Timeout = TimeSpan.FromSeconds(8) };

        internal LeagueSgpMatchHistoryApiClient(LeagueClientSessionProvider sessions, ILeagueClientApi local)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _local = local ?? throw new ArgumentNullException(nameof(local));
        }

        internal static bool TryResolveTencentEndpoint(LeagueClientSession session,
            string targetPuuid, int startIndex, int count, out Uri endpoint)
        {
            endpoint = null;
            if (session == null || (!string.IsNullOrWhiteSpace(session.Region) &&
                !string.Equals(session.Region, "TENCENT", StringComparison.OrdinalIgnoreCase)) ||
                string.IsNullOrEmpty(session.PlatformId) || string.IsNullOrWhiteSpace(targetPuuid) ||
                targetPuuid.Length > 128 || startIndex < 0 || count < 1 || count > 200 ||
                startIndex > int.MaxValue - count) return false;
            foreach (var character in targetPuuid)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') && character != '-' && character != '_') return false;
            string host;
            if (!TencentHosts.TryGetValue(session.PlatformId, out host)) return false;
            var path = "/match-history-query/v1/products/lol/player/" +
                Uri.EscapeDataString(targetPuuid) + "/SUMMARY?startIndex=" + startIndex + "&count=" + count;
            endpoint = new Uri("https://" + host + ":21019" + path, UriKind.Absolute);
            return true;
        }

        public async Task<byte[]> TryGetSummaryAsync(string targetPuuid, int startIndex, int count,
            CancellationToken cancellationToken)
        {
            var session = _sessions.GetSession();
            Uri endpoint;
            if (!TryResolveTencentEndpoint(session, targetPuuid, startIndex, count, out endpoint)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var credentials = await _local.TryGetBytesAsync(EntitlementsPath, cancellationToken)
                    .ConfigureAwait(false);
                var token = ReadEntitlementsToken(credentials);
                if (token == null || _sessions.GetSession()?.Matches(session) != true) return null;

                using (var request = new HttpRequestMessage(HttpMethod.Get, endpoint))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using (var response = await _http.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) return null;
                        var bytes = await CancelableHttpContentReader.ReadBytesAsync(
                            response.Content, cancellationToken, 16 * 1024 * 1024).ConfigureAwait(false);
                        return _sessions.GetSession()?.Matches(session) == true ? bytes : null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                return null;
            }
            catch (Exception) { return null; }
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
                return !string.IsNullOrWhiteSpace(token) && token.Length <= 8192 &&
                    token.IndexOfAny(new[] { '\r', '\n', '\t', ' ' }) < 0 ? token : null;
            }
            catch (Exception) { return null; }
        }

        public void Dispose() { _http.Dispose(); }
    }
}
