using System;
using System.Net;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal interface ILeagueBlockedPlayersWriteApi
    {
        Task<LeagueClientWriteResponse> TrySetBlockedAsync(string playerId, bool blocked, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Narrow writer for the block list. The stock client only exposes it through the friend
    /// context menu; there is no bulk view and no way to lift a block without finding the player again.
    /// Messaging is deliberately not handled here: LeagueMatchmakingWriteRuntime already owns it.
    /// </summary>
    internal sealed class LeagueBlockedPlayersWriteApiClient : ILeagueBlockedPlayersWriteApi, IDisposable
    {
        internal const string BlockedPlayersPath = "/lol-chat/v1/blocked-players";

        private readonly LeagueClientSessionProvider _sessions;
        private readonly LeagueSessionHttpClientPool _clients = new LeagueSessionHttpClientPool();
        private bool _disposed;

        public LeagueBlockedPlayersWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<LeagueClientWriteResponse> TrySetBlockedAsync(
            string playerId, bool blocked, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("playerId", nameof(playerId));
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
                    using (var request = new HttpRequestMessage(blocked ? HttpMethod.Post : HttpMethod.Delete,
                               BlockedPlayersPath))
                    {
                        request.Content = new StringContent(
                            "{\"id\":\"" + playerId.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}",
                            Encoding.UTF8, "application/json");
                        using (var response = await lease.Client.SendAsync(
                            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
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
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clients.Dispose();
        }
    }

    internal sealed class LeagueBlockedPlayersService
    {
        internal const string BlockedPlayersPath = "/lol-chat/v1/blocked-players";

        private readonly ILeagueClientApi _reader;
        private readonly ILeagueBlockedPlayersWriteApi _writer;

        public LeagueBlockedPlayersService(ILeagueClientApi reader, ILeagueBlockedPlayersWriteApi writer)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public Task<byte[]> ReadAsync(CancellationToken cancellationToken)
        {
            return _reader.TryGetBytesAsync(BlockedPlayersPath, cancellationToken);
        }

        internal static int? ParseCount(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var items = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes)) as object[];
                if (items == null) return null;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in items)
                {
                    var entry = item as Dictionary<string, object>;
                    object raw;
                    if (entry == null || !entry.TryGetValue("id", out raw)) return null;
                    var id = raw as string;
                    if (string.IsNullOrWhiteSpace(id) || id.Length > 256) return null;
                    ids.Add(id);
                }
                return ids.Count;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        public async Task<bool> SetBlockedAsync(string playerId, bool blocked, CancellationToken cancellationToken)
        {
            var response = await _writer.TrySetBlockedAsync(playerId, blocked, cancellationToken).ConfigureAwait(false);
            var ok = response != null && response.IsSuccessStatusCode;
            AppLog.Info("Blocked players update: blocked=" + blocked + "; ok=" + ok);
            return ok;
        }
    }
}
