using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal interface ILeagueChallengePreferencesWriteApi
    {
        Task<LeagueClientWriteResponse> TryUpdatePlayerPreferencesAsync(string json, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Dedicated user-directed challenge-preferences writer. It shares the unique LeagueClient
    /// session provider and is hard-fenced to POST /lol-challenges/v1/update-player-preferences/.
    /// It cannot reach chat presence, regalia, matchmaking, Champ Select or arbitrary LCU routes.
    /// </summary>
    internal sealed class LeagueChallengePreferencesWriteApiClient : ILeagueChallengePreferencesWriteApi, IDisposable
    {
        internal const string UpdatePath = "/lol-challenges/v1/update-player-preferences/";

        private readonly LeagueClientSessionProvider _sessions;
        private readonly LeagueSessionHttpClientPool _clients = new LeagueSessionHttpClientPool();
        private bool _disposed;

        public LeagueChallengePreferencesWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<LeagueClientWriteResponse> TryUpdatePlayerPreferencesAsync(
            string json,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _sessions.GetSession();
            if (session == null) return null;

            LeagueSessionHttpClientPool.Lease lease;
            try { lease = _clients.Acquire(session); }
            catch (ObjectDisposedException) { return null; }

            using (lease)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, UpdatePath))
                    {
                        request.Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json");
                        using (var response = await lease.Client.SendAsync(
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
        }

        internal static bool IsAllowedTargetForSmokeTest(string method, string path)
        {
            return string.Equals((method ?? string.Empty).Trim(), "POST", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(NormalizePath(path), UpdatePath, StringComparison.Ordinal);
        }

        private static string NormalizePath(string path)
        {
            var value = (path ?? string.Empty).Trim();
            if (value.Length == 0) return "/";
            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clients.Dispose();
        }
    }
}
