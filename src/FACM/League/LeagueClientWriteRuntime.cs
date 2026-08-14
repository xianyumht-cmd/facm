using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal interface ILeagueClientWriteApi
    {
        Task<LeagueClientWriteResponse> TrySendJsonAsync(
            string method,
            string path,
            string json,
            CancellationToken cancellationToken);
    }

    internal sealed class LeagueClientWriteResponse
    {
        public int StatusCode { get; set; }
        public byte[] Body { get; set; }

        public bool IsSuccessStatusCode
        {
            get { return StatusCode >= 200 && StatusCode <= 299; }
        }
    }

    /// <summary>
    /// Minimal authenticated LCU write transport. It deliberately shares the exact same
    /// LeagueClientSessionProvider as the read client, so Gate 2 does not introduce a second
    /// discovery/auth connector. Only explicit feature code can call this interface.
    /// </summary>
    internal sealed class LeagueClientWriteApiClient : ILeagueClientWriteApi, IDisposable
    {
        private static readonly HashSet<string> AllowedMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "POST",
            "PUT",
            "PATCH"
        };

        private readonly object _sync = new object();
        private readonly LeagueClientSessionProvider _sessions;
        private readonly List<HttpClient> _retiredClients = new List<HttpClient>();
        private LeagueClientSession _clientSession;
        private HttpClient _client;
        private bool _disposed;

        public LeagueClientWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<LeagueClientWriteResponse> TrySendJsonAsync(
            string method,
            string path,
            string json,
            CancellationToken cancellationToken)
        {
            var verb = (method ?? string.Empty).Trim().ToUpperInvariant();
            if (!AllowedMethods.Contains(verb))
                throw new ArgumentException("LCU write method is not allowed.", nameof(method));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("LCU write path is required.", nameof(path));

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
                using (var request = new HttpRequestMessage(new HttpMethod(verb), NormalizePath(path)))
                {
                    request.Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json");
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

        private HttpClient GetOrCreateClient(LeagueClientSession session)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LeagueClientWriteApiClient));
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
