using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// discovery/auth connector. The transport itself hard-fences the only Gate 2 write targets;
    /// forbidden Champ Select actions cannot be reached even if a future caller misuses the API.
    /// </summary>
    internal sealed class LeagueClientWriteApiClient : ILeagueClientWriteApi, IDisposable
    {
        private const string MySelectionPath = "/lol-champ-select/v1/session/my-selection";
        private const string PerkPagesPath = "/lol-perks/v1/pages";
        private const string PerkCreatePath = "/lol-perks/v1/pages/";
        private const string PerkCurrentPagePath = "/lol-perks/v1/currentpage";

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
            var normalizedPath = NormalizePath(path);
            if (!IsAllowedTarget(verb, normalizedPath))
                throw new ArgumentException("LCU write target is not allowed by the Gate 2 transport.", nameof(path));

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
                using (var request = new HttpRequestMessage(new HttpMethod(verb), normalizedPath))
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

        internal static bool IsAllowedTargetForSmokeTest(string method, string path)
        {
            return IsAllowedTarget(
                (method ?? string.Empty).Trim().ToUpperInvariant(),
                NormalizePath(path));
        }

        private static bool IsAllowedTarget(string verb, string normalizedPath)
        {
            if (string.Equals(verb, "PATCH", StringComparison.Ordinal) &&
                string.Equals(normalizedPath, MySelectionPath, StringComparison.Ordinal))
                return true;

            if (string.Equals(verb, "POST", StringComparison.Ordinal) &&
                string.Equals(normalizedPath, PerkCreatePath, StringComparison.Ordinal))
                return true;

            if (string.Equals(verb, "PUT", StringComparison.Ordinal) &&
                string.Equals(normalizedPath, PerkCurrentPagePath, StringComparison.Ordinal))
                return true;

            if (!string.Equals(verb, "PUT", StringComparison.Ordinal) ||
                !normalizedPath.StartsWith(PerkPagesPath + "/", StringComparison.Ordinal))
                return false;

            var suffix = normalizedPath.Substring((PerkPagesPath + "/").Length);
            int pageId;
            return suffix.Length > 0 &&
                   suffix.IndexOfAny(new[] { '/', '?', '#' }) < 0 &&
                   int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out pageId) &&
                   pageId > 0;
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
