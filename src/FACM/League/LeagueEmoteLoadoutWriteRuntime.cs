using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal interface ILeagueEmoteLoadoutWriteApi
    {
        Task<LeagueClientWriteResponse> TryPatchEmotesAsync(
            string loadoutId,
            string json,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Dedicated user-directed emote-loadout writer. It shares the unique LeagueClient session
    /// provider and can PATCH only one validated /lol-loadouts/v4/loadouts/{id} segment supplied by
    /// the emote service. It cannot reach inventory, chat, matchmaking, ChampSelect or arbitrary LCU routes.
    /// </summary>
    internal sealed class LeagueEmoteLoadoutWriteApiClient : ILeagueEmoteLoadoutWriteApi, IDisposable
    {
        internal const string LoadoutPathPrefix = "/lol-loadouts/v4/loadouts/";

        private readonly LeagueClientSessionProvider _sessions;
        private readonly LeagueSessionHttpClientPool _clients = new LeagueSessionHttpClientPool();
        private bool _disposed;

        public LeagueEmoteLoadoutWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<LeagueClientWriteResponse> TryPatchEmotesAsync(
            string loadoutId,
            string json,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSafeLoadoutId(loadoutId)) return null;

            var session = _sessions.GetSession();
            if (session == null) return null;

            LeagueSessionHttpClientPool.Lease lease;
            try { lease = _clients.Acquire(session); }
            catch (ObjectDisposedException) { return null; }

            using (lease)
            {
                try
                {
                    var path = LoadoutPathPrefix + loadoutId;
                    using (var request = new HttpRequestMessage(new HttpMethod("PATCH"), path))
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

        internal static bool IsAllowedTargetForSmokeTest(string method, string loadoutId)
        {
            return string.Equals((method ?? string.Empty).Trim(), "PATCH", StringComparison.OrdinalIgnoreCase) &&
                   IsSafeLoadoutId(loadoutId);
        }

        internal static bool IsSafeLoadoutId(string loadoutId)
        {
            var value = (loadoutId ?? string.Empty).Trim();
            if (value.Length == 0 || value.Length > 128) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var c = value[index];
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')) return false;
            }
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clients.Dispose();
        }
    }
}
