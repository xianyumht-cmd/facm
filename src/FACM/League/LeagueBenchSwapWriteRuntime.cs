using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    /// <summary>
    /// Dedicated minimal writer for the ARAM / ARAM Mayhem bench.
    /// Callers provide only a champion id; the transport constructs the one allowed endpoint itself,
    /// so this capability cannot be reused for pick/ban/actions/reroll/dodge/skin writes.
    /// </summary>
    internal interface ILeagueBenchSwapWriteApi
    {
        Task<LeagueClientWriteResponse> TrySwapAsync(int championId, CancellationToken cancellationToken);
    }

    internal sealed class LeagueBenchSwapWriteApiClient : ILeagueBenchSwapWriteApi, IDisposable
    {
        internal const string SwapPathPrefix = "/lol-champ-select/v1/session/bench/swap/";

        private readonly object _sync = new object();
        private readonly LeagueClientSessionProvider _sessions;
        private readonly List<HttpClient> _retiredClients = new List<HttpClient>();
        private LeagueClientSession _clientSession;
        private HttpClient _client;
        private bool _disposed;

        public LeagueBenchSwapWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<LeagueClientWriteResponse> TrySwapAsync(int championId, CancellationToken cancellationToken)
        {
            if (!IsValidChampionIdForSmokeTest(championId))
                throw new ArgumentOutOfRangeException(nameof(championId), "Champion id must be positive.");

            cancellationToken.ThrowIfCancellationRequested();
            var session = _sessions.GetSession();
            if (session == null) return null;

            HttpClient client;
            try
            {
                client = GetOrCreateClient(session);
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, BuildPathForSmokeTest(championId)))
                using (var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized ||
                        response.StatusCode == HttpStatusCode.Forbidden)
                        _sessions.Invalidate(session);

                    return new LeagueClientWriteResponse
                    {
                        StatusCode = (int)response.StatusCode,
                        Body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)
                    };
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                _sessions.Invalidate(session);
                return null;
            }
            catch (HttpRequestException)
            {
                _sessions.Invalidate(session);
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
            catch
            {
                _sessions.Invalidate(session);
                return null;
            }
        }

        internal static bool IsValidChampionIdForSmokeTest(int championId)
        {
            return championId > 0;
        }

        internal static string BuildPathForSmokeTest(int championId)
        {
            if (championId <= 0) return string.Empty;
            return SwapPathPrefix + championId;
        }

        private HttpClient GetOrCreateClient(LeagueClientSession session)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LeagueBenchSwapWriteApiClient));
                if (_client != null && _clientSession != null && _clientSession.Matches(session)) return _client;

                if (_client != null) _retiredClients.Add(_client);
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = delegate { return true; };
                var client = new HttpClient(handler)
                {
                    BaseAddress = session.BaseUri,
                    Timeout = TimeSpan.FromSeconds(2)
                };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    LeagueClientSessionParser.CreateBasicAuthorizationParameter(session));

                _clientSession = session;
                _client = client;
                return client;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                if (_client != null) _client.Dispose();
                _client = null;
                _clientSession = null;
                foreach (var client in _retiredClients) client.Dispose();
                _retiredClients.Clear();
            }
        }
    }
}
