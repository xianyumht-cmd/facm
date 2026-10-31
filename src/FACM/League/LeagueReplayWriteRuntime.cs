using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal interface ILeagueReplayWriteApi
    {
        Task<LeagueClientWriteResponse> TryCreateMetadataAsync(LeagueClientSession session,
            LeaguePlayerMatchSummary match, string gameVersion, CancellationToken token);
        Task<LeagueClientWriteResponse> TryReplayAsync(LeagueClientSession session,
            long gameId, bool watch, CancellationToken token);
    }

    // Only these two explicit replay operations can issue an LCU POST.
    internal sealed class LeagueReplayWriteApiClient : ILeagueReplayWriteApi, IDisposable
    {
        private const string ReplayBody = "{\"componentType\":\"replay-button_match-history\"}";
        private readonly LeagueClientSessionProvider _sessions;
        private readonly LeagueSessionHttpClientPool _clients = new LeagueSessionHttpClientPool();
        private bool _disposed;

        internal LeagueReplayWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public Task<LeagueClientWriteResponse> TryCreateMetadataAsync(LeagueClientSession session,
            LeaguePlayerMatchSummary match, string gameVersion, CancellationToken token)
        {
            if (match == null || match.GameId <= 0 || match.QueueId <= 0 ||
                match.GameDurationSeconds <= 0 || match.GameCreationEpochMs <= 0 ||
                string.IsNullOrWhiteSpace(match.GameType) || string.IsNullOrWhiteSpace(gameVersion) ||
                match.GameCreationEpochMs > long.MaxValue - (long)match.GameDurationSeconds * 1000)
                throw new ArgumentException("Missing validated replay metadata.", nameof(match));
            var body = new JavaScriptSerializer().Serialize(new
            {
                gameVersion,
                gameType = match.GameType,
                queueId = match.QueueId,
                gameEnd = match.GameCreationEpochMs + (long)match.GameDurationSeconds * 1000
            });
            return SendAsync(session, "/lol-replays/v2/metadata/" + match.GameId + "/create", body, token);
        }

        public Task<LeagueClientWriteResponse> TryReplayAsync(LeagueClientSession session,
            long gameId, bool watch, CancellationToken token)
        {
            if (gameId <= 0) throw new ArgumentOutOfRangeException(nameof(gameId));
            return SendAsync(session, "/lol-replays/v1/rofls/" + gameId +
                (watch ? "/watch" : "/download"), ReplayBody, token);
        }

        internal static bool IsAllowedPathForSmokeTest(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var parts = path.Split('/');
            long gameId;
            if (parts.Length != 6 || parts[0] != string.Empty || parts[1] != "lol-replays" ||
                !long.TryParse(parts[4], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out gameId) || gameId <= 0)
                return false;
            return (parts[2] == "v1" && parts[3] == "rofls" &&
                    (parts[5] == "download" || parts[5] == "watch")) ||
                   (parts[2] == "v2" && parts[3] == "metadata" && parts[5] == "create");
        }

        private async Task<LeagueClientWriteResponse> SendAsync(LeagueClientSession expected,
            string path, string body, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_disposed || !IsAllowedPathForSmokeTest(path) || expected == null ||
                !expected.Matches(_sessions.GetSession())) return null;
            LeagueSessionHttpClientPool.Lease lease;
            try { lease = _clients.Acquire(expected); }
            catch (ObjectDisposedException) { return null; }
            using (lease)
            {
                try
                {
                    if (!expected.Matches(_sessions.GetSession())) return null;
                    using (var request = new HttpRequestMessage(HttpMethod.Post, path))
                    {
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                        using (var response = await lease.Client.SendAsync(request,
                            HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                        {
                            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                                response.StatusCode == HttpStatusCode.Forbidden)
                                _sessions.Invalidate(expected);
                            return new LeagueClientWriteResponse { StatusCode = (int)response.StatusCode };
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    _sessions.Invalidate(expected);
                    return null;
                }
                catch (HttpRequestException) { _sessions.Invalidate(expected); return null; }
                catch (ObjectDisposedException) { return null; }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clients.Dispose();
        }
    }
}
