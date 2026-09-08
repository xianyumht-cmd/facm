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
    /// Callers provide only a champion id plus the already-observed Champ Select route; the
    /// transport still constructs the endpoint itself and cannot be reused for arbitrary writes.
    /// </summary>
    internal interface ILeagueBenchSwapWriteApi
    {
        Task<LeagueClientWriteResponse> TrySwapAsync(
            int championId,
            LeagueBenchSwapRoute route,
            CancellationToken cancellationToken);
    }

    internal sealed class LeagueBenchSwapWriteApiClient : ILeagueBenchSwapWriteApi, IDisposable
    {
        internal const string LegacySwapPathPrefix = "/lol-champ-select/v1/session/bench/swap/";
        internal const string TeamBuilderSwapPathPrefix = "/lol-lobby-team-builder/champ-select/v1/session/bench/swap/";

        private readonly LeagueClientSessionProvider _sessions;
        private readonly LeagueSessionHttpClientPool _clients = new LeagueSessionHttpClientPool();
        private bool _disposed;

        public LeagueBenchSwapWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<LeagueClientWriteResponse> TrySwapAsync(
            int championId,
            LeagueBenchSwapRoute route,
            CancellationToken cancellationToken)
        {
            if (!IsValidChampionIdForSmokeTest(championId))
                throw new ArgumentOutOfRangeException(nameof(championId), "Champion id must be positive.");

            cancellationToken.ThrowIfCancellationRequested();
            var session = _sessions.GetSession();
            if (session == null) return null;

            LeagueSessionHttpClientPool.Lease lease;
            try
            {
                lease = _clients.Acquire(session);
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            using (lease)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, BuildPathForSmokeTest(championId, route)))
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

        internal static bool IsValidChampionIdForSmokeTest(int championId)
        {
            return championId > 0;
        }

        internal static string BuildPathForSmokeTest(int championId, LeagueBenchSwapRoute route)
        {
            if (championId <= 0) return string.Empty;
            return (route == LeagueBenchSwapRoute.TeamBuilder
                ? TeamBuilderSwapPathPrefix
                : LegacySwapPathPrefix) + championId;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clients.Dispose();
        }
    }
}
