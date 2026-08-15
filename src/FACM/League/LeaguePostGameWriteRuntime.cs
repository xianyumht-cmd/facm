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
    internal interface ILeaguePostGameWriteApi
    {
        Task<LeagueClientWriteResponse> TrySendAsync(
            string method,
            string path,
            string json,
            CancellationToken cancellationToken);
    }

    internal sealed class LeaguePostGameWriteApiClient : ILeaguePostGameWriteApi, IDisposable
    {
        internal const string HonorPath = "/lol-honor/v1/honor";
        internal const string HonorBallotSubmitPath = "/lol-honor/v1/ballot";
        internal const string PlayAgainPath = "/lol-lobby/v2/play-again";

        private readonly object _sync = new object();
        private readonly LeagueClientSessionProvider _sessions;
        private readonly List<HttpClient> _retiredClients = new List<HttpClient>();
        private LeagueClientSession _clientSession;
        private HttpClient _client;
        private bool _disposed;

        public LeaguePostGameWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<LeagueClientWriteResponse> TrySendAsync(
            string method,
            string path,
            string json,
            CancellationToken cancellationToken)
        {
            var verb = (method ?? string.Empty).Trim().ToUpperInvariant();
            var normalizedPath = NormalizePath(path);
            if (!IsAllowedTarget(verb, normalizedPath))
                throw new ArgumentException("LCU write target is not allowed by the Gate 6 transport.", nameof(path));

            cancellationToken.ThrowIfCancellationRequested();
            var session = _sessions.GetSession();
            if (session == null) return null;

            HttpClient client;
            try { client = GetOrCreateClient(session); }
            catch (ObjectDisposedException) { return null; }

            try
            {
                using (var request = new HttpRequestMessage(new HttpMethod(verb), normalizedPath))
                {
                    if (json != null)
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
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
            return IsAllowedTarget((method ?? string.Empty).Trim().ToUpperInvariant(), NormalizePath(path));
        }

        private static bool IsAllowedTarget(string verb, string path)
        {
            if (!string.Equals(verb, "POST", StringComparison.Ordinal)) return false;
            return string.Equals(path, HonorPath, StringComparison.Ordinal) ||
                   string.Equals(path, HonorBallotSubmitPath, StringComparison.Ordinal) ||
                   string.Equals(path, PlayAgainPath, StringComparison.Ordinal);
        }

        private HttpClient GetOrCreateClient(LeagueClientSession session)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LeaguePostGameWriteApiClient));
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
