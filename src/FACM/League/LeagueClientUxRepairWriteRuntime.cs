using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal interface ILeagueClientUxRepairWriteApi
    {
        Task<LeagueClientWriteResponse> TryRestartUxAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Dedicated repair writer. It shares LeagueClientModule's single session provider and exposes
    /// no arbitrary path API: the only possible write is Riot's client UX restart endpoint.
    /// </summary>
    internal sealed class LeagueClientUxRepairWriteApiClient : ILeagueClientUxRepairWriteApi, IDisposable
    {
        internal const string RestartUxPath = "/riotclient/kill-and-restart-ux";

        private readonly object _sync = new object();
        private readonly LeagueClientSessionProvider _sessions;
        private readonly List<HttpClient> _retiredClients = new List<HttpClient>();
        private LeagueClientSession _clientSession;
        private HttpClient _client;
        private bool _disposed;

        public LeagueClientUxRepairWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<LeagueClientWriteResponse> TryRestartUxAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _sessions.GetSession();
            if (session == null) return null;

            HttpClient client;
            try { client = GetOrCreateClient(session); }
            catch (ObjectDisposedException) { return null; }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, RestartUxPath))
                using (var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
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
            catch (ObjectDisposedException) { return null; }
            catch
            {
                _sessions.Invalidate(session);
                return null;
            }
        }

        internal static bool IsAllowedTargetForSmokeTest(string method, string path)
        {
            return string.Equals((method ?? string.Empty).Trim(), "POST", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(NormalizePath(path), RestartUxPath, StringComparison.Ordinal);
        }

        private HttpClient GetOrCreateClient(LeagueClientSession session)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LeagueClientUxRepairWriteApiClient));
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

        private static string NormalizePath(string path)
        {
            var value = (path ?? string.Empty).Trim();
            if (value.Length == 0) return "/";
            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
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
