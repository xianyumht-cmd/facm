using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal interface ILeagueMatchmakingClock
    {
        Task Delay(TimeSpan delay, CancellationToken cancellationToken);
    }

    internal sealed class LeagueMatchmakingSystemClock : ILeagueMatchmakingClock
    {
        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }

    internal sealed class LeagueLobbyEligibility
    {
        public bool CanStartActivity { get; set; }
        public bool IsLeader { get; set; }
        public int QueueId { get; set; }
        public int RealMemberCount { get; set; }
        public List<string> MemberIds { get; private set; } = new List<string>();

        public string Fingerprint
        {
            get
            {
                var members = MemberIds.Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                var memberPart = members.Length > 0
                    ? string.Join(",", members)
                    : "count:" + RealMemberCount;
                return "queue:" + QueueId + "|members:" + memberPart;
            }
        }

        public bool IsEligible
        {
            get { return CanStartActivity && IsLeader && RealMemberCount > 0; }
        }

        public string BlockReason
        {
            get
            {
                if (!CanStartActivity) return "cannot-start";
                if (!IsLeader) return "not-leader";
                if (RealMemberCount <= 0) return "no-members";
                return null;
            }
        }
    }

    internal sealed class LeagueReadyCheckState
    {
        public string State { get; set; }
        public string PlayerResponse { get; set; }

        public bool HasFinalLocalResponse
        {
            get
            {
                return string.Equals(PlayerResponse, "Accepted", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(PlayerResponse, "Declined", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    internal sealed class LeagueMatchmakingAutomationController : IDisposable
    {
        internal const string LobbyPath = "/lol-lobby/v2/lobby";
        internal const string SearchStatePath = "/lol-matchmaking/v1/search";
        private static readonly TimeSpan LobbyObserveInterval = TimeSpan.FromSeconds(3);

        private readonly object _sync = new object();
        private readonly ILeagueClientApi _read;
        private readonly ILeagueMatchmakingWriteApi _write;
        private readonly ILeagueMatchmakingClock _clock;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        private CancellationTokenSource _lobbyCancellation;
        private CancellationTokenSource _readyCancellation;
        private string _phase;
        private string _lastSearchFingerprint;
        private string _lastSearchDiagnostic;
        private bool _acceptAttemptedThisReadyCheck;
        private bool _autoSearch;
        private bool _autoAccept;
        private bool _disposed;

        public LeagueMatchmakingAutomationController(ILeagueClientApi read, ILeagueMatchmakingWriteApi write)
            : this(read, write, new LeagueMatchmakingSystemClock())
        {
        }

        internal LeagueMatchmakingAutomationController(
            ILeagueClientApi read,
            ILeagueMatchmakingWriteApi write,
            ILeagueMatchmakingClock clock)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public void Configure(bool autoSearch, bool autoAccept)
        {
            bool restartLobby;
            bool restartReady;
            lock (_sync)
            {
                if (_disposed) return;
                restartLobby = autoSearch && !_autoSearch && IsPhase("Lobby");
                restartReady = autoAccept && !_autoAccept && IsPhase("ReadyCheck");
                if (!autoSearch && _autoSearch)
                {
                    _lastSearchFingerprint = null;
                    _lastSearchDiagnostic = null;
                    CancelLobbyLocked();
                }
                if (!autoAccept && _autoAccept)
                {
                    _acceptAttemptedThisReadyCheck = false;
                    CancelReadyLocked();
                }
                _autoSearch = autoSearch;
                _autoAccept = autoAccept;
            }
            if (restartLobby) StartLobbyObserver();
            if (restartReady) StartReadyObserver();
        }

        public void Observe(LeagueDashboardPhaseState state)
        {
            var nextPhase = state != null && state.Connected ? (state.Phase ?? string.Empty) : string.Empty;
            bool startLobby = false;
            bool startReady = false;
            lock (_sync)
            {
                if (_disposed) return;
                var old = _phase ?? string.Empty;
                _phase = nextPhase;

                if (!string.Equals(nextPhase, "Lobby", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(old, "Lobby", StringComparison.OrdinalIgnoreCase))
                    {
                        _lastSearchFingerprint = null;
                        _lastSearchDiagnostic = null;
                    }
                    CancelLobbyLocked();
                }
                else if (!string.Equals(old, "Lobby", StringComparison.OrdinalIgnoreCase) && _autoSearch)
                {
                    _lastSearchFingerprint = null;
                    _lastSearchDiagnostic = null;
                    startLobby = true;
                }

                if (!string.Equals(nextPhase, "ReadyCheck", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(old, "ReadyCheck", StringComparison.OrdinalIgnoreCase))
                        _acceptAttemptedThisReadyCheck = false;
                    CancelReadyLocked();
                }
                else if (!string.Equals(old, "ReadyCheck", StringComparison.OrdinalIgnoreCase) && _autoAccept)
                {
                    _acceptAttemptedThisReadyCheck = false;
                    startReady = true;
                }
            }
            if (startLobby) StartLobbyObserver();
            if (startReady) StartReadyObserver();
        }

        internal async Task EvaluateLobbyOnceForSmokeTestAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _phase = "Lobby";
                _autoSearch = true;
            }
            await EvaluateLobbyAsync(cancellationToken).ConfigureAwait(false);
        }

        internal async Task EvaluateReadyOnceForSmokeTestAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _phase = "ReadyCheck";
                _autoAccept = true;
            }
            await EvaluateReadyAsync(cancellationToken).ConfigureAwait(false);
        }

        private void StartLobbyObserver()
        {
            CancellationToken token;
            lock (_sync)
            {
                if (_disposed || !_autoSearch || !IsPhase("Lobby")) return;
                if (_lobbyCancellation != null) return;
                _lobbyCancellation = new CancellationTokenSource();
                token = _lobbyCancellation.Token;
            }
            RunLobbyObserverAsync(token).Forget("League auto matchmaking");
        }

        private async Task RunLobbyObserverAsync(CancellationToken cancellationToken)
        {
            // Gameflow already proved that Lobby is active. Evaluate immediately; if the
            // lobby payload has not caught up yet, the existing bounded observer retries.
            while (IsSearchActive())
            {
                var success = await EvaluateLobbyAsync(cancellationToken).ConfigureAwait(false);
                if (success) return;
                await _clock.Delay(LobbyObserveInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<bool> EvaluateLobbyAsync(CancellationToken cancellationToken)
        {
            if (!IsSearchActive()) return false;
            var lobby = ParseLobby(await _read.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false));
            if (lobby == null)
            {
                LogSearchDiagnostic("lobby-unavailable");
                return false;
            }
            if (!lobby.IsEligible)
            {
                LogSearchDiagnostic(lobby.BlockReason ?? "not-eligible");
                return false;
            }

            var fingerprint = lobby.Fingerprint;
            lock (_sync)
            {
                if (string.Equals(_lastSearchFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    LogSearchDiagnosticLocked("already-attempted");
                    return false;
                }
                _lastSearchFingerprint = fingerprint;
                _lastSearchDiagnostic = null;
            }

            if (!IsSearchActive()) return false;
            AppLog.Info("League auto matchmaking: attempt");
            var response = await _write.TrySendAsync(
                "POST",
                LeagueMatchmakingWriteApiClient.SearchPath,
                cancellationToken).ConfigureAwait(false);
            var ok = response != null && response.IsSuccessStatusCode;
            AppLog.Info("League auto matchmaking: " + (ok ? "success" : "failed/status-" + (response == null ? "none" : response.StatusCode.ToString())));
            return ok;
        }

        private void StartReadyObserver()
        {
            CancellationToken token;
            lock (_sync)
            {
                if (_disposed || !_autoAccept || !IsPhase("ReadyCheck")) return;
                if (_readyCancellation != null || _acceptAttemptedThisReadyCheck) return;
                _readyCancellation = new CancellationTokenSource();
                token = _readyCancellation.Token;
            }
            RunReadyObserverAsync(token).Forget("League auto accept");
        }

        private async Task RunReadyObserverAsync(CancellationToken cancellationToken)
        {
            // ReadyCheck is short-lived. Do not add a fixed sleep after Gameflow has
            // already observed it; the per-episode attempted flag prevents duplicates.
            await EvaluateReadyAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> EvaluateReadyAsync(CancellationToken cancellationToken)
        {
            if (!IsAcceptActive()) return false;
            lock (_sync)
            {
                if (_acceptAttemptedThisReadyCheck) return true;
            }

            // Best effort only: Tencent may omit or reshape fields on /lol-matchmaking/v1/search.
            // A readable final local response prevents us from reversing an explicit user action,
            // but missing/partial search data must not block ReadyCheck accept.
            var search = ParseSearch(await _read.TryGetBytesAsync(SearchStatePath, cancellationToken).ConfigureAwait(false));
            if (search != null && search.HasFinalLocalResponse)
            {
                lock (_sync) _acceptAttemptedThisReadyCheck = true;
                AppLog.Info("League auto accept: skip/already-" + search.PlayerResponse.ToLowerInvariant());
                return true;
            }

            lock (_sync)
            {
                if (_acceptAttemptedThisReadyCheck) return true;
                _acceptAttemptedThisReadyCheck = true;
            }
            if (!IsAcceptActive()) return true;

            AppLog.Info("League auto accept: attempt");
            var response = await _write.TrySendAsync(
                "POST",
                LeagueMatchmakingWriteApiClient.AcceptPath,
                cancellationToken).ConfigureAwait(false);
            AppLog.Info("League auto accept: " +
                        (response != null && response.IsSuccessStatusCode
                            ? "success"
                            : "failed/status-" + (response == null ? "none" : response.StatusCode.ToString())));
            return true;
        }

        internal LeagueLobbyEligibility ParseLobby(byte[] bytes)
        {
            var root = ParseObject(bytes);
            if (root == null) return null;
            var local = ReadDictionary(root, "localMember");
            if (local == null) return null;

            var game = ReadDictionary(root, "gameConfig");
            var output = new LeagueLobbyEligibility
            {
                CanStartActivity = ReadBool(root, "canStartActivity"),
                IsLeader = ReadBool(local, "isLeader"),
                QueueId = game == null ? 0 : ReadInt(game, "queueId")
            };

            foreach (var member in EnumerateDictionaries(ReadValue(root, "members")))
            {
                if (ReadBool(member, "isBot") || ReadBool(member, "isSpectator")) continue;
                output.RealMemberCount++;
                var id = ReadString(member, "puuid");
                if (string.IsNullOrWhiteSpace(id)) id = ReadString(member, "summonerId");
                if (!string.IsNullOrWhiteSpace(id)) output.MemberIds.Add(id.Trim());
            }
            return output;
        }

        internal LeagueReadyCheckState ParseSearch(byte[] bytes)
        {
            var root = ParseObject(bytes);
            if (root == null) return null;
            var ready = ReadDictionary(root, "readyCheck");
            return new LeagueReadyCheckState
            {
                State = ReadString(ready, "state"),
                PlayerResponse = ReadString(ready, "playerResponse")
            };
        }

        private void LogSearchDiagnostic(string reason)
        {
            lock (_sync) LogSearchDiagnosticLocked(reason);
        }

        private void LogSearchDiagnosticLocked(string reason)
        {
            if (string.Equals(_lastSearchDiagnostic, reason, StringComparison.Ordinal)) return;
            _lastSearchDiagnostic = reason;
            AppLog.Info("League auto matchmaking: skip/" + reason);
        }

        private bool IsSearchActive()
        {
            lock (_sync) return !_disposed && _autoSearch && IsPhase("Lobby");
        }

        private bool IsAcceptActive()
        {
            lock (_sync) return !_disposed && _autoAccept && IsPhase("ReadyCheck");
        }

        private bool IsPhase(string expected)
        {
            return string.Equals(_phase, expected, StringComparison.OrdinalIgnoreCase);
        }

        private Dictionary<string, object> ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static object ReadValue(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            return ReadValue(source, key) as Dictionary<string, object>;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            return value == null ? null : Convert.ToString(value);
        }

        private static int ReadInt(Dictionary<string, object> source, string key)
        {
            try { return Convert.ToInt32(ReadValue(source, key)); }
            catch { return 0; }
        }

        private static bool ReadBool(Dictionary<string, object> source, string key)
        {
            try { return Convert.ToBoolean(ReadValue(source, key)); }
            catch { return false; }
        }

        private static IEnumerable<Dictionary<string, object>> EnumerateDictionaries(object value)
        {
            var enumerable = value as IEnumerable;
            if (enumerable == null || value is string) yield break;
            foreach (var item in enumerable)
            {
                var row = item as Dictionary<string, object>;
                if (row != null) yield return row;
            }
        }

        private void CancelLobbyLocked()
        {
            var cancellation = _lobbyCancellation;
            _lobbyCancellation = null;
            if (cancellation == null) return;
            try { cancellation.Cancel(); }
            catch { }
            cancellation.Dispose();
        }

        private void CancelReadyLocked()
        {
            var cancellation = _readyCancellation;
            _readyCancellation = null;
            if (cancellation == null) return;
            try { cancellation.Cancel(); }
            catch { }
            cancellation.Dispose();
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                CancelLobbyLocked();
                CancelReadyLocked();
            }
        }
    }
}
