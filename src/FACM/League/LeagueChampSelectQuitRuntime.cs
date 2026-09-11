using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    /// <summary>
    /// Narrow writer for the League Client's team-builder Champion Select quit action.
    /// This owner can issue exactly one POST target and cannot leave/delete the lobby itself.
    /// </summary>
    internal interface ILeagueChampSelectQuitWriteApi
    {
        Task<LeagueClientWriteResponse> TryQuitAsync(CancellationToken cancellationToken);
    }

    internal sealed class LeagueChampSelectQuitWriteApiClient : ILeagueChampSelectQuitWriteApi, IDisposable
    {
        internal const string QuitPath = "/lol-lobby-team-builder/champ-select/v1/session/quit";

        private readonly LeagueClientSessionProvider _sessions;
        private readonly LeagueSessionHttpClientPool _clients = new LeagueSessionHttpClientPool();
        private bool _disposed;

        public LeagueChampSelectQuitWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<LeagueClientWriteResponse> TryQuitAsync(CancellationToken cancellationToken)
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
                    using (var request = new HttpRequestMessage(HttpMethod.Post, QuitPath))
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
                catch (ObjectDisposedException) { return null; }
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
                   string.Equals((path ?? string.Empty).Trim(), QuitPath, StringComparison.Ordinal);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clients.Dispose();
        }
    }

    internal enum LeagueChampSelectQuitStatus
    {
        Success,
        NotInChampSelect,
        SessionUnavailable,
        WriteRejected,
        VerificationFailed
    }

    internal sealed class LeagueChampSelectQuitResult
    {
        public LeagueChampSelectQuitStatus Status { get; set; }
        public int StatusCode { get; set; }
        public string PhaseAfter { get; set; }
        public bool LobbyPreserved { get; set; }

        public bool Success
        {
            get { return Status == LeagueChampSelectQuitStatus.Success; }
        }
    }

    /// <summary>
    /// Explicit one-click transaction for leaving only the current Champion Select episode.
    /// It performs a read-only preflight, exactly one POST, then bounded read-back proving both
    /// that ChampSelect ended and that the lobby still exists. It never calls DELETE /lol-lobby/v2/lobby.
    /// </summary>
    internal sealed class LeagueChampSelectQuitService
    {
        internal const string GameflowPhasePath = "/lol-gameflow/v1/gameflow-phase";
        internal const string ChampSelectSessionPath = "/lol-champ-select/v1/session";
        internal const string LobbyPath = "/lol-lobby/v2/lobby";

        private readonly ILeagueClientApi _reader;
        private readonly ILeagueChampSelectQuitWriteApi _writer;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public LeagueChampSelectQuitService(ILeagueClientApi reader, ILeagueChampSelectQuitWriteApi writer)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public async Task<LeagueChampSelectQuitResult> QuitAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var phase = await ReadPhaseAsync(cancellationToken).ConfigureAwait(false);
                var session = await _reader.TryGetBytesAsync(ChampSelectSessionPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) || session == null || session.Length == 0)
                    return Result(LeagueChampSelectQuitStatus.NotInChampSelect, 0, phase, false);

                var response = await _writer.TryQuitAsync(cancellationToken).ConfigureAwait(false);
                if (response == null)
                    return Result(LeagueChampSelectQuitStatus.SessionUnavailable, 0, phase, false);
                if (!response.IsSuccessStatusCode)
                    return Result(LeagueChampSelectQuitStatus.WriteRejected, response.StatusCode, phase, false);

                string settledPhase = phase;
                var lobbyPreserved = false;
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    if (attempt > 0) await Task.Delay(180, cancellationToken).ConfigureAwait(false);
                    settledPhase = await ReadPhaseAsync(cancellationToken).ConfigureAwait(false);
                    var lobby = await _reader.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false);
                    lobbyPreserved = lobby != null && lobby.Length > 0;
                    if (!string.Equals(settledPhase, "ChampSelect", StringComparison.OrdinalIgnoreCase) && lobbyPreserved)
                    {
                        AppLog.Info("League quit Champion Select: success; phase=" + Safe(settledPhase) + "; lobbyPreserved=true");
                        return Result(LeagueChampSelectQuitStatus.Success, response.StatusCode, settledPhase, true);
                    }
                }

                AppLog.Info(
                    "League quit Champion Select: verification-failed; http=" + response.StatusCode +
                    "; phase=" + Safe(settledPhase) +
                    "; lobbyPreserved=" + lobbyPreserved.ToString().ToLowerInvariant());
                return Result(LeagueChampSelectQuitStatus.VerificationFailed, response.StatusCode, settledPhase, lobbyPreserved);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<string> ReadPhaseAsync(CancellationToken cancellationToken)
        {
            var bytes = await _reader.TryGetBytesAsync(GameflowPhasePath, cancellationToken).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0) return null;
            var value = Encoding.UTF8.GetString(bytes).Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = value.Substring(1, value.Length - 2);
            return value.Trim();
        }

        private static LeagueChampSelectQuitResult Result(
            LeagueChampSelectQuitStatus status,
            int statusCode,
            string phase,
            bool lobbyPreserved)
        {
            return new LeagueChampSelectQuitResult
            {
                Status = status,
                StatusCode = statusCode,
                PhaseAfter = phase,
                LobbyPreserved = lobbyPreserved
            };
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
