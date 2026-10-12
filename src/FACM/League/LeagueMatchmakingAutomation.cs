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
            get { return IsEligibleFor(1); }
        }

        public string BlockReason
        {
            get { return BlockReasonFor(1); }
        }

        public bool IsEligibleFor(int minimumPartySize)
        {
            var minimum = Math.Max(1, Math.Min(5, minimumPartySize));
            return CanStartActivity && IsLeader && RealMemberCount >= minimum;
        }

        public string BlockReasonFor(int minimumPartySize)
        {
            var minimum = Math.Max(1, Math.Min(5, minimumPartySize));
            if (!CanStartActivity) return "cannot-start";
            if (!IsLeader) return "not-leader";
            if (RealMemberCount <= 0) return "no-members";
            if (RealMemberCount < minimum) return "party-below-minimum-" + minimum.ToString();
            return null;
        }
    }

    internal sealed class LeagueReadyCheckState
    {
        public bool IsCurrentlyInQueue { get; set; }
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
        private static readonly TimeSpan ReadyObserveInterval = TimeSpan.FromMilliseconds(500);

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
        private int _minimumPartySize = 1;
        private int _searchStartDelayMs;
        private int _acceptDelayMs;
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
            Configure(autoSearch, autoAccept, 1, 0, 0);
        }

        public void Configure(
            bool autoSearch,
            bool autoAccept,
            int minimumPartySize,
            int searchStartDelayMs,
            int acceptDelayMs)
        {
            minimumPartySize = Math.Max(1, Math.Min(5, minimumPartySize));
            searchStartDelayMs = Math.Max(0, Math.Min(60000, searchStartDelayMs));
            acceptDelayMs = Math.Max(0, Math.Min(15000, acceptDelayMs));

            bool restartLobby;
            bool restartReady;
            lock (_sync)
            {
                if (_disposed) return;
                var wasAutoSearch = _autoSearch;
                var wasAutoAccept = _autoAccept;
                var lobbyPolicyChanged = _minimumPartySize != minimumPartySize || _searchStartDelayMs != searchStartDelayMs;
                var readyPolicyChanged = _acceptDelayMs != acceptDelayMs;

                restartLobby = autoSearch && IsPhase("Lobby") && (!wasAutoSearch || lobbyPolicyChanged);
                restartReady = autoAccept && IsPhase("ReadyCheck") && (!wasAutoAccept || readyPolicyChanged);

                if ((!autoSearch && wasAutoSearch) || (autoSearch && wasAutoSearch && lobbyPolicyChanged))
                {
                    _lastSearchDiagnostic = null;
                    CancelLobbyLocked();
                }
                if ((!autoAccept && wasAutoAccept) || (autoAccept && wasAutoAccept && readyPolicyChanged))
                {
                    CancelReadyLocked();
                }

                _autoSearch = autoSearch;
                _autoAccept = autoAccept;
                _minimumPartySize = minimumPartySize;
                _searchStartDelayMs = searchStartDelayMs;
                _acceptDelayMs = acceptDelayMs;
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
            var initialDelay = GetSearchStartDelay();
            if (initialDelay > TimeSpan.Zero)
            {
                AppLog.Info("League auto matchmaking: wait/start-delay-ms-" + ((int)initialDelay.TotalMilliseconds).ToString());
                await _clock.Delay(initialDelay, cancellationToken).ConfigureAwait(false);
            }
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
            var minimumPartySize = GetMinimumPartySize();
            if (!lobby.IsEligibleFor(minimumPartySize))
            {
                LogSearchDiagnostic(lobby.BlockReasonFor(minimumPartySize) ?? "not-eligible");
                return false;
            }

            var fingerprint = lobby.Fingerprint;
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_disposed || !_autoSearch || !IsPhase("Lobby")) return false;
                if (string.Equals(_lastSearchFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    LogSearchDiagnosticLocked("already-attempted");
                    return false;
                }

                // Claim before the wire side effect. Cancellation can be observed after LCU has
                // already accepted the POST, so an in-flight OFF/ON toggle must retain this claim.
                // A definite live failure releases the claim below so the normal retry still works.
                _lastSearchFingerprint = fingerprint;
                _lastSearchDiagnostic = null;
            }

            AppLog.Info("League auto matchmaking: attempt");
            var response = await _write.TrySendAsync(
                "POST",
                LeagueMatchmakingWriteApiClient.SearchPath,
                cancellationToken).ConfigureAwait(false);
            var ok = response != null && response.IsSuccessStatusCode;
            var reconciled = false;

            // A timeout/connection reset can happen after LCU has already accepted the POST.
            // Only pay for this extra read on a failed/ambiguous write; the normal success path
            // stays immediate. An authoritative queue=true postcondition prevents a duplicate POST,
            // while a missing/false postcondition leaves the existing 3-second retry available.
            if (!ok && IsSearchActive())
            {
                var search = ParseSearch(await _read.TryGetBytesAsync(SearchStatePath, cancellationToken).ConfigureAwait(false));
                if (search != null && search.IsCurrentlyInQueue)
                {
                    ok = true;
                    reconciled = true;
                }
            }

            if (ok)
            {
                lock (_sync)
                {
                    _lastSearchDiagnostic = null;
                }
            }
            else if (IsSearchActive() && !cancellationToken.IsCancellationRequested)
            {
                // The write returned a definite failure while this same Lobby is still live.
                // Release only our own claim so the existing observer can retry after backoff.
                lock (_sync)
                {
                    if (!_disposed && _autoSearch && IsPhase("Lobby") &&
                        string.Equals(_lastSearchFingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        _lastSearchFingerprint = null;
                    }
                }
            }

            AppLog.Info("League auto matchmaking: " +
                        (ok
                            ? (reconciled ? "success/reconciled-queue-state" : "success")
                            : "failed/status-" + (response == null ? "none" : response.StatusCode.ToString())));
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
            var initialDelay = GetAcceptDelay();
            if (initialDelay > TimeSpan.Zero)
            {
                AppLog.Info("League auto accept: wait/delay-ms-" + ((int)initialDelay.TotalMilliseconds).ToString());
                await _clock.Delay(initialDelay, cancellationToken).ConfigureAwait(false);
            }
            while (IsAcceptActive())
            {
                var done = await EvaluateReadyAsync(cancellationToken).ConfigureAwait(false);
                if (done) return;
                await _clock.Delay(ReadyObserveInterval, cancellationToken).ConfigureAwait(false);
            }
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

            // Claim the episode before the POST so Configure/Observe cannot start a duplicate
            // writer while this request is in flight. A true failure releases the claim below.
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
            var ok = response != null && response.IsSuccessStatusCode;
            var reconciled = false;

            // A timeout can be reported after LCU already accepted the ready check. Before
            // releasing the episode claim, reconcile the authoritative local response so an
            // ambiguous success cannot cause a duplicate POST on the 500 ms retry.
            if (!ok && IsAcceptActive())
            {
                var after = ParseSearch(await _read.TryGetBytesAsync(SearchStatePath, cancellationToken).ConfigureAwait(false));
                if (after != null && after.HasFinalLocalResponse)
                {
                    ok = true;
                    reconciled = true;
                }
            }

            if (!ok && IsAcceptActive() && !cancellationToken.IsCancellationRequested)
            {
                lock (_sync)
                {
                    if (!_disposed && _autoAccept && IsPhase("ReadyCheck"))
                        _acceptAttemptedThisReadyCheck = false;
                }
            }

            AppLog.Info("League auto accept: " +
                        (ok
                            ? (reconciled ? "success/reconciled-final-response" : "success")
                            : "failed/status-" + (response == null ? "none" : response.StatusCode.ToString())));
            return ok;
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
                IsCurrentlyInQueue = ReadBool(root, "isCurrentlyInQueue"),
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

        private int GetMinimumPartySize()
        {
            lock (_sync) return _minimumPartySize;
        }

        private TimeSpan GetSearchStartDelay()
        {
            lock (_sync) return TimeSpan.FromMilliseconds(_searchStartDelayMs);
        }

        private TimeSpan GetAcceptDelay()
        {
            lock (_sync) return TimeSpan.FromMilliseconds(_acceptDelayMs);
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
