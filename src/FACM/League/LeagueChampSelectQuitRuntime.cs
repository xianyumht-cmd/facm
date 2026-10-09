using System;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    /// <summary>
    /// Narrow writer for the League Client's team-builder Champion Select quit action.
    /// This owner can issue exactly one POST target and cannot leave/delete the lobby itself.
    /// </summary>
    internal interface ILeagueChampSelectQuitWriteApi
    {
        Task<LeagueClientWriteResponse> TryQuitAsync(CancellationToken cancellationToken);
        Task<LeagueClientWriteResponse> TryRequestLobbyAsync(CancellationToken cancellationToken);
    }

    internal sealed class LeagueChampSelectQuitWriteApiClient : ILeagueChampSelectQuitWriteApi, IDisposable
    {
        internal const string QuitPath = "/lol-lobby-team-builder/champ-select/v1/session/quit";
        internal const string RequestLobbyPath = "/lol-gameflow/v1/session/request-lobby";

        private readonly LeagueClientSessionProvider _sessions;
        private readonly LeagueSessionHttpClientPool _clients = new LeagueSessionHttpClientPool();
        private bool _disposed;

        public LeagueChampSelectQuitWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public Task<LeagueClientWriteResponse> TryQuitAsync(CancellationToken cancellationToken)
        {
            return SendAsync(QuitPath, cancellationToken);
        }

        public Task<LeagueClientWriteResponse> TryRequestLobbyAsync(CancellationToken cancellationToken)
        {
            return SendAsync(RequestLobbyPath, cancellationToken);
        }

        private async Task<LeagueClientWriteResponse> SendAsync(string path, CancellationToken cancellationToken)
        {
            if (!IsAllowedTargetForSmokeTest("POST", path))
                throw new ArgumentException("Champ Select quit target is not allowed.", nameof(path));
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
                    using (var request = new HttpRequestMessage(HttpMethod.Post, path))
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
                if (!string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) ||
                    session == null || session.Length == 0)
                    return Result(LeagueChampSelectQuitStatus.NotInChampSelect, 0, phase, false);

                var before = ReadLobbyIdentity(
                    await _reader.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false));
                var response = await _writer.TryQuitAsync(cancellationToken).ConfigureAwait(false);
                if (response == null)
                    return Result(LeagueChampSelectQuitStatus.SessionUnavailable, 0, phase, false);

                var usedFallback = false;
                if (!response.IsSuccessStatusCode)
                {
                    AppLog.Info("League quit Champion Select: primary-rejected; http=" + response.StatusCode +
                                "; errorCode=" + ReadErrorCode(response.Body) +
                                "; originalLobbyKnown=" + (before != null).ToString().ToLowerInvariant());

                    // An uncertain transport result is never retried. The fallback requires
                    // a locally observed party identity before any further write is attempted.
                    if (response.StatusCode != 400 || before == null)
                        return Result(LeagueChampSelectQuitStatus.WriteRejected, response.StatusCode, phase, false);

                    // Do not change gameflow after the initial rejection if the user
                    // has already left Champion Select through another action.
                    if (!string.Equals(await ReadPhaseAsync(cancellationToken).ConfigureAwait(false),
                                       "ChampSelect", StringComparison.OrdinalIgnoreCase))
                        return Result(LeagueChampSelectQuitStatus.VerificationFailed, response.StatusCode, phase, false);

                    usedFallback = true;
                    response = await _writer.TryRequestLobbyAsync(cancellationToken).ConfigureAwait(false);
                    if (response == null)
                        return Result(LeagueChampSelectQuitStatus.SessionUnavailable, 0, phase, false);
                    if (!response.IsSuccessStatusCode || !IsAcceptedLobbyRequest(response.Body))
                    {
                        AppLog.Info("League quit Champion Select: request-lobby-rejected; http=" + response.StatusCode +
                                    "; errorCode=" + ReadErrorCode(response.Body));
                        return Result(LeagueChampSelectQuitStatus.WriteRejected, response.StatusCode, phase, false);
                    }
                }

                string settledPhase = phase;
                var lobbyPreserved = false;
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    if (attempt > 0) await Task.Delay(180, cancellationToken).ConfigureAwait(false);
                    settledPhase = await ReadPhaseAsync(cancellationToken).ConfigureAwait(false);
                    var lobby = await _reader.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false);
                    var after = ReadLobbyIdentity(lobby);
                    lobbyPreserved = before == null
                        ? !usedFallback && lobby != null && lobby.Length > 0
                        : MatchesOriginalLobby(before, after);
                    if (string.Equals(settledPhase, "Lobby", StringComparison.OrdinalIgnoreCase) && lobbyPreserved)
                    {
                        var remainingSession = await _reader.TryGetBytesAsync(
                            ChampSelectSessionPath, cancellationToken).ConfigureAwait(false);
                        if (remainingSession == null || remainingSession.Length == 0)
                        {
                            AppLog.Info("League quit Champion Select: success; route=" +
                                        (usedFallback ? "request-lobby" : "quit") +
                                        "; phase=Lobby; lobbyPreserved=true");
                            return Result(LeagueChampSelectQuitStatus.Success, response.StatusCode, settledPhase, true);
                        }
                    }
                }

                AppLog.Info("League quit Champion Select: verification-failed; route=" +
                            (usedFallback ? "request-lobby" : "quit") +
                            "; http=" + response.StatusCode +
                            "; phase=" + Safe(settledPhase) +
                            "; lobbyPreserved=" + lobbyPreserved.ToString().ToLowerInvariant());
                return Result(LeagueChampSelectQuitStatus.VerificationFailed, response.StatusCode, settledPhase, lobbyPreserved);
            }
            finally
            {
                _gate.Release();
            }
        }

        // A lobby ID proves that the returned party is the original one; merely
        // seeing some lobby or the same local player is not sufficient.
        private sealed class LobbyIdentity
        {
            public string Id;
            public string[] Members;
        }

        private static LobbyIdentity ReadLobbyIdentity(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var lobby = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes))
                    as Dictionary<string, object>;
                if (lobby == null) return null;

                var id = Field(lobby, "partyId") ?? Field(lobby, "chatRoomId") ?? Field(lobby, "chatRoomKey");
                var members = lobby.ContainsKey("members") ? lobby["members"] as object[] : null;
                if (string.IsNullOrWhiteSpace(id) || members == null || members.Length == 0) return null;

                var identifiers = new List<string>();
                foreach (var item in members)
                {
                    var member = item as Dictionary<string, object>;
                    if (member == null) return null;
                    var key = Field(member, "puuid") ?? Field(member, "summonerId");
                    if (string.IsNullOrWhiteSpace(key)) return null;
                    identifiers.Add(key.Trim());
                }

                identifiers.Sort(StringComparer.Ordinal);
                return new LobbyIdentity { Id = id.Trim(), Members = identifiers.ToArray() };
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static bool MatchesOriginalLobby(LobbyIdentity before, LobbyIdentity after)
        {
            return before != null && after != null &&
                   string.Equals(before.Id, after.Id, StringComparison.Ordinal) &&
                   before.Members.SequenceEqual(after.Members, StringComparer.Ordinal);
        }

        private static string Field(Dictionary<string, object> value, string key)
        {
            object entry;
            return value != null && value.TryGetValue(key, out entry) && entry != null
                ? Convert.ToString(entry, CultureInfo.InvariantCulture)
                : null;
        }

        private static bool IsAcceptedLobbyRequest(byte[] body)
        {
            return body != null && string.Equals(
                Encoding.UTF8.GetString(body).Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadErrorCode(byte[] body)
        {
            if (body == null || body.Length == 0 || body.Length > 4096) return "-";
            try
            {
                var error = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(body))
                    as Dictionary<string, object>;
                var code = Field(error, "errorCode");
                if (string.IsNullOrWhiteSpace(code)) return "-";
                var safe = new string(code.Take(64).Where(ch =>
                    char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
                return safe.Length == 0 ? "-" : safe;
            }
            catch (ArgumentException) { return "-"; }
            catch (InvalidOperationException) { return "-"; }
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
