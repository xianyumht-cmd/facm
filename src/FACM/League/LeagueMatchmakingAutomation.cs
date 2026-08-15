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
        public bool AllowedStartActivity { get; set; }
        public bool IsLeader { get; set; }
        public int QueueId { get; set; }
        public string PartyId { get; set; }
        public List<string> MemberPuuids { get; private set; } = new List<string>();
        public bool HasBlockingMetadata { get; set; }

        public string Fingerprint
        {
            get
            {
                if (QueueId <= 0 || string.IsNullOrWhiteSpace(PartyId)) return null;
                var members = MemberPuuids.Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal);
                return PartyId.Trim() + "|" + QueueId + "|" + string.Join(",", members);
            }
        }

        public bool IsEligible
        {
            get
            {
                return CanStartActivity && AllowedStartActivity && IsLeader && QueueId > 0 &&
                       MemberPuuids.Count > 0 && !HasBlockingMetadata && !string.IsNullOrWhiteSpace(Fingerprint);
            }
        }
    }

    internal sealed class LeagueReadyCheckState
    {
        public string LobbyId { get; set; }
        public int QueueId { get; set; }
        public bool IsCurrentlyInQueue { get; set; }
        public string State { get; set; }
        public string PlayerResponse { get; set; }

        public string Fingerprint
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LobbyId) || QueueId <= 0 || string.IsNullOrWhiteSpace(State)) return null;
                return LobbyId.Trim() + "|" + QueueId + "|" + State.Trim();
            }
        }
    }

    internal sealed class LeagueMatchmakingAutomationController : IDisposable
    {
        internal const string LobbyPath = "/lol-lobby/v2/lobby";
        internal const string SearchStatePath = "/lol-matchmaking/v1/search";
        private static readonly TimeSpan LobbyInitialDelay = TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan LobbyObserveInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ReadyInitialDelay = TimeSpan.FromMilliseconds(450);
        private static readonly TimeSpan ReadyRetryDelay = TimeSpan.FromMilliseconds(350);
        private const int ReadyStateAttempts = 4;

        private readonly object _sync = new object();
        private readonly ILeagueClientApi _read;
        private readonly ILeagueMatchmakingWriteApi _write;
        private readonly ILeagueMatchmakingClock _clock;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        private CancellationTokenSource _lobbyCancellation;
        private CancellationTokenSource _readyCancellation;
        private string _phase;
        private string _lastSearchFingerprint;
        private string _lastAcceptFingerprint;
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
                    CancelLobbyLocked();
                }
                if (!autoAccept && _autoAccept)
                {
                    _lastAcceptFingerprint = null;
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
                    if (string.Equals(old, "Lobby", StringComparison.OrdinalIgnoreCase)) _lastSearchFingerprint = null;
                    CancelLobbyLocked();
                }
                else if (!string.Equals(old, "Lobby", StringComparison.OrdinalIgnoreCase) && _autoSearch)
                {
                    startLobby = true;
                }

                if (!string.Equals(nextPhase, "ReadyCheck", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(old, "ReadyCheck", StringComparison.OrdinalIgnoreCase)) _lastAcceptFingerprint = null;
                    CancelReadyLocked();
                }
                else if (!string.Equals(old, "ReadyCheck", StringComparison.OrdinalIgnoreCase) && _autoAccept)
                {
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
            await _clock.Delay(LobbyInitialDelay, cancellationToken).ConfigureAwait(false);
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
            if (lobby == null || !lobby.IsEligible) return false;

            var fingerprint = lobby.Fingerprint;
            lock (_sync)
            {
                if (string.Equals(_lastSearchFingerprint, fingerprint, StringComparison.Ordinal)) return false;
            }

            var search = ParseSearch(await _read.TryGetBytesAsync(SearchStatePath, cancellationToken).ConfigureAwait(false));
            if (search == null || search.IsCurrentlyInQueue) return search != null && search.IsCurrentlyInQueue;
            if (!IsSearchActive()) return false;

            lock (_sync)
            {
                if (string.Equals(_lastSearchFingerprint, fingerprint, StringComparison.Ordinal)) return false;
                _lastSearchFingerprint = fingerprint;
            }

            var response = await _write.TrySendAsync(
                "POST",
                LeagueMatchmakingWriteApiClient.SearchPath,
                cancellationToken).ConfigureAwait(false);
            var ok = response != null && response.IsSuccessStatusCode;
            AppLog.Info("League auto matchmaking: " + (ok ? "success" : "failed"));
            return ok;
        }

        private void StartReadyObserver()
        {
            CancellationToken token;
            lock (_sync)
            {
                if (_disposed || !_autoAccept || !IsPhase("ReadyCheck")) return;
                if (_readyCancellation != null) return;
                _readyCancellation = new CancellationTokenSource();
                token = _readyCancellation.Token;
            }
            RunReadyObserverAsync(token).Forget("League auto accept");
        }

        private async Task RunReadyObserverAsync(CancellationToken cancellationToken)
        {
            await _clock.Delay(ReadyInitialDelay, cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; attempt < ReadyStateAttempts && IsAcceptActive(); attempt++)
            {
                if (await EvaluateReadyAsync(cancellationToken).ConfigureAwait(false)) return;
                if (attempt + 1 < ReadyStateAttempts)
                    await _clock.Delay(ReadyRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<bool> EvaluateReadyAsync(CancellationToken cancellationToken)
        {
            if (!IsAcceptActive()) return false;
            var search = ParseSearch(await _read.TryGetBytesAsync(SearchStatePath, cancellationToken).ConfigureAwait(false));
            if (search == null) return false;
            if (!string.Equals(search.State, "InProgress", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(search.PlayerResponse, "Accepted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(search.PlayerResponse, "Declined", StringComparison.OrdinalIgnoreCase))
                return true;

            var fingerprint = search.Fingerprint;
            if (string.IsNullOrWhiteSpace(fingerprint)) return false;
            lock (_sync)
            {
                if (string.Equals(_lastAcceptFingerprint, fingerprint, StringComparison.Ordinal)) return true;
                _lastAcceptFingerprint = fingerprint;
            }
            if (!IsAcceptActive()) return true;

            var response = await _write.TrySendAsync(
                "POST",
                LeagueMatchmakingWriteApiClient.AcceptPath,
                cancellationToken).ConfigureAwait(false);
            AppLog.Info("League auto accept: " + (response != null && response.IsSuccessStatusCode ? "success" : "failed"));
            return true;
        }

        internal LeagueLobbyEligibility ParseLobby(byte[] bytes)
        {
            var root = ParseObject(bytes);
            if (root == null) return null;
            var local = ReadDictionary(root, "localMember");
            var game = ReadDictionary(root, "gameConfig");
            if (local == null || game == null) return null;
            var output = new LeagueLobbyEligibility
            {
                CanStartActivity = ReadBool(root, "canStartActivity"),
                AllowedStartActivity = ReadBool(local, "allowedStartActivity"),
                IsLeader = ReadBool(local, "isLeader"),
                QueueId = ReadInt(game, "queueId"),
                PartyId = ReadString(root, "partyId"),
                HasBlockingMetadata = HasAny(ReadValue(root, "restrictions")) || HasAny(ReadValue(root, "warnings"))
            };
            foreach (var member in EnumerateDictionaries(ReadValue(root, "members")))
            {
                if (ReadBool(member, "isBot") || ReadBool(member, "isSpectator")) continue;
                var puuid = ReadString(member, "puuid");
                if (!string.IsNullOrWhiteSpace(puuid)) output.MemberPuuids.Add(puuid.Trim());
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
                LobbyId = ReadString(root, "lobbyId"),
                QueueId = ReadInt(root, "queueId"),
                IsCurrentlyInQueue = ReadBool(root, "isCurrentlyInQueue"),
                State = ReadString(ready, "state"),
                PlayerResponse = ReadString(ready, "playerResponse")
            };
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

        private static bool HasAny(object value)
        {
            var enumerable = value as IEnumerable;
            if (enumerable == null || value is string) return false;
            foreach (var ignored in enumerable) return true;
            return false;
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
