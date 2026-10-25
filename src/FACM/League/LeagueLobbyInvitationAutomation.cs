using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueInvitationPolicies
    {
        internal const string Default = "<DEFAULT>";
        internal const string Ignore = "ignore";
        internal const string Accept = "accept";
        internal const string Decline = "decline";

        internal static readonly string[] GameTypes =
        {
            Default, "RANKED_SOLO_5x5", "RANKED_FLEX_SR", "NORMAL", "ARAM_UNRANKED_5x5",
            "KIWI", "CHERRY", "URF", "NORMAL_TFT", "RANKED_TFT", "RANKED_TFT_TURBO",
            "RANKED_TFT_DOUBLE_UP"
        };

        internal static string NormalizeAction(string value)
        {
            return value == Accept || value == Decline ? value : Ignore;
        }

        internal static Dictionary<string, string> Parse(string json)
        {
            var output = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json) || json.Length > 800) return output;
            try
            {
                var source = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (source == null || source.Count > GameTypes.Length) return output;
                foreach (var row in source)
                {
                    if (!GameTypes.Contains(row.Key, StringComparer.Ordinal) || !(row.Value is string))
                        return new Dictionary<string, string>(StringComparer.Ordinal);
                    var value = (string)row.Value;
                    if (value != Accept && value != Decline && value != Ignore)
                        return new Dictionary<string, string>(StringComparer.Ordinal);
                    output.Add(row.Key, value);
                }
            }
            catch { return new Dictionary<string, string>(StringComparer.Ordinal); }
            return output;
        }

        internal static string Serialize(IDictionary<string, string> policies)
        {
            if (policies == null) return "{}";
            var canonical = GameTypes.Where(policies.ContainsKey)
                .ToDictionary(key => key, key => NormalizeAction(policies[key]), StringComparer.Ordinal);
            return new JavaScriptSerializer().Serialize(canonical);
        }

        internal static string NormalizeJson(string value)
        {
            return Serialize(Parse(value));
        }

        internal static string Resolve(IDictionary<string, string> policies, string gameType)
        {
            string strategy;
            if (!string.IsNullOrEmpty(gameType) && policies.TryGetValue(gameType, out strategy))
                return NormalizeAction(strategy);
            return policies.TryGetValue(Default, out strategy) ? NormalizeAction(strategy) : Ignore;
        }
    }

    internal sealed class LeagueReceivedInvitation
    {
        public string Id { get; set; }
        public string GameType { get; set; }
        public bool CanAccept { get; set; }
    }

    internal sealed class LeagueLobbyLeaderCandidate
    {
        public long SummonerId { get; set; }
        public bool Ready { get; set; }
    }

    internal sealed class LeagueLobbyLeaderSnapshot
    {
        public bool IsLeader { get; set; }
        public long SelfSummonerId { get; set; }
        public string PartyId { get; set; }
        public List<LeagueLobbyLeaderCandidate> Members { get; private set; } = new List<LeagueLobbyLeaderCandidate>();

        public string Fingerprint
        {
            get
            {
                return (PartyId ?? string.Empty) + "|" + SelfSummonerId + "|" +
                    string.Join(",", Members.Select(item => item.SummonerId).OrderBy(value => value));
            }
        }
    }

    internal sealed class LeagueLobbyInvitationController : IDisposable
    {
        internal const string ReceivedInvitationsPath = "/lol-lobby/v2/received-invitations";
        internal const string ChatMePath = "/lol-chat/v1/me";
        internal const string LobbyPath = "/lol-lobby/v2/lobby";
        private static readonly long PollTicks = Math.Max(1,
            (long)(Stopwatch.Frequency * 3.0));

        private readonly object _sync = new object();
        private readonly ILeagueClientApi _read;
        private readonly ILeagueMatchmakingWriteApi _write;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        private readonly HashSet<string> _attemptedInvitations = new HashSet<string>(StringComparer.Ordinal);
        private Dictionary<string, string> _policies = LeagueInvitationPolicies.Parse(null);
        private CancellationTokenSource _active;
        private long _lastTick;
        private string _lastLeaderFingerprint;
        private bool _handleInvitations;
        private bool _skipWhenAway;
        private bool _autoSkipLeader;
        private bool _connected;
        private string _phase;
        private bool _disposed;

        internal LeagueLobbyInvitationController(ILeagueClientApi read, ILeagueMatchmakingWriteApi write)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        internal void Configure(bool handleInvitations, bool skipWhenAway, string policiesJson, bool autoSkipLeader)
        {
            var policies = LeagueInvitationPolicies.Parse(policiesJson);
            lock (_sync)
            {
                if (_disposed) return;
                var changed = _handleInvitations != handleInvitations || _skipWhenAway != skipWhenAway ||
                    _autoSkipLeader != autoSkipLeader ||
                    !string.Equals(LeagueInvitationPolicies.Serialize(_policies),
                        LeagueInvitationPolicies.Serialize(policies), StringComparison.Ordinal);
                _handleInvitations = handleInvitations;
                _skipWhenAway = skipWhenAway;
                _policies = policies;
                _autoSkipLeader = autoSkipLeader;
                if (changed)
                {
                    CancelLocked();
                    _lastTick = 0;
                    _lastLeaderFingerprint = null;
                    _attemptedInvitations.Clear();
                }
            }
        }

        internal void Observe(LeagueDashboardPhaseState state)
        {
            CancellationToken token;
            lock (_sync)
            {
                if (_disposed) return;
                _connected = state != null && state.Connected;
                _phase = state == null ? null : state.Phase;
                if (!_connected || !IsEligiblePhaseLocked() || (!_handleInvitations && !_autoSkipLeader))
                {
                    CancelLocked();
                    _lastTick = 0;
                    _lastLeaderFingerprint = null;
                    _attemptedInvitations.Clear();
                    return;
                }
                if (_active != null) return;
                var now = Stopwatch.GetTimestamp();
                if (_lastTick != 0 && now - _lastTick < PollTicks) return;
                _lastTick = now;
                _active = new CancellationTokenSource();
                token = _active.Token;
            }
            RunAsync(token).Forget("League lobby automation");
        }

        internal async Task EvaluateOnceForSmokeTestAsync(string phase, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _connected = true;
                _phase = phase;
            }
            await EvaluateAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try { await EvaluateAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_sync)
                {
                    if (_active != null && _active.Token == cancellationToken)
                    {
                        _active.Dispose();
                        _active = null;
                    }
                }
            }
        }

        private async Task EvaluateAsync(CancellationToken cancellationToken)
        {
            if (IsInvitationsEnabled()) await HandleReceivedInvitationsAsync(cancellationToken).ConfigureAwait(false);
            if (IsLeaderEnabled()) await SkipLeaderAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task HandleReceivedInvitationsAsync(CancellationToken cancellationToken)
        {
            var invitations = ParseInvitations(await _read.TryGetBytesAsync(ReceivedInvitationsPath, cancellationToken)
                .ConfigureAwait(false));
            if (invitations == null) return;
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
                _attemptedInvitations.IntersectWith(invitations.Select(item => item.Id));

            Dictionary<string, string> policies;
            HashSet<string> attempted;
            bool skipWhenAway;
            lock (_sync)
            {
                policies = new Dictionary<string, string>(_policies, StringComparer.Ordinal);
                attempted = new HashSet<string>(_attemptedInvitations, StringComparer.Ordinal);
                skipWhenAway = _skipWhenAway;
            }
            var candidate = invitations
                .Select(item => new { Item = item, Action = LeagueInvitationPolicies.Resolve(policies, item.GameType) })
                .Where(item => item.Action != LeagueInvitationPolicies.Ignore)
                .Where(item => item.Item.CanAccept)
                .OrderBy(item => item.Action == LeagueInvitationPolicies.Accept ? 0 : 1)
                .FirstOrDefault(item => !attempted.Contains(item.Item.Id));
            if (candidate == null) return;

            if (skipWhenAway)
            {
                var status = ParseObject(await _read.TryGetBytesAsync(ChatMePath, cancellationToken).ConfigureAwait(false));
                if (status == null || string.Equals(ReadString(status, "availability"), "away",
                    StringComparison.OrdinalIgnoreCase)) return;
            }

            var latest = ParseInvitations(await _read.TryGetBytesAsync(ReceivedInvitationsPath, cancellationToken)
                .ConfigureAwait(false));
            cancellationToken.ThrowIfCancellationRequested();
            if (latest == null || !latest.Any(item => item.Id == candidate.Item.Id &&
                item.GameType == candidate.Item.GameType && item.CanAccept)) return;

            lock (_sync)
            {
                if (!IsInvitationsEnabledLocked() || !_attemptedInvitations.Add(candidate.Item.Id)) return;
            }
            var path = ReceivedInvitationsPath + "/" + candidate.Item.Id + "/" + candidate.Action;
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _write.TrySendAsync("POST", path, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            AppLog.Info("League invitation automation: " + candidate.Action + "/" +
                (response != null && response.IsSuccessStatusCode ? "submitted" : "unverified"));
        }

        private async Task SkipLeaderAsync(CancellationToken cancellationToken)
        {
            var lobby = ParseLeader(await _read.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false));
            if (lobby == null || !lobby.IsLeader || lobby.Members.Count == 0) return;
            var target = lobby.Members.FirstOrDefault(item => item.Ready) ?? lobby.Members[0];

            lock (_sync)
            {
                if (!IsLeaderEnabledLocked() || _lastLeaderFingerprint == lobby.Fingerprint) return;
            }

            var current = ParseLeader(await _read.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false));
            cancellationToken.ThrowIfCancellationRequested();
            if (current == null || !current.IsLeader || current.Fingerprint != lobby.Fingerprint ||
                !current.Members.Any(item => item.SummonerId == target.SummonerId && item.Ready == target.Ready)) return;

            lock (_sync)
            {
                if (!IsLeaderEnabledLocked() || _lastLeaderFingerprint == lobby.Fingerprint) return;
                _lastLeaderFingerprint = lobby.Fingerprint;
            }
            var path = LobbyPath + "/members/" + target.SummonerId + "/promote";
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _write.TrySendAsync("POST", path, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            AppLog.Info("League auto skip leader: " +
                (response != null && response.IsSuccessStatusCode ? "submitted" : "unverified"));
        }

        internal List<LeagueReceivedInvitation> ParseInvitations(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 256 * 1024) return null;
            object parsed;
            try { parsed = _json.DeserializeObject(Encoding.UTF8.GetString(bytes)); }
            catch { return null; }
            var rows = parsed as IEnumerable;
            if (rows == null || parsed is string) return null;
            var result = new List<LeagueReceivedInvitation>();
            foreach (var item in rows)
            {
                if (result.Count >= 100) return null;
                var row = item as Dictionary<string, object>;
                if (row == null || ReadString(row, "state") != "Pending") continue;
                var id = ReadString(row, "invitationId");
                if (!LeagueMatchmakingWriteApiClient.IsSafeInvitationId(id)) continue;
                var game = ReadDictionary(row, "gameConfig");
                result.Add(new LeagueReceivedInvitation
                {
                    Id = id,
                    GameType = ReadString(game, "inviteGameType"),
                    CanAccept = ReadBool(row, "canAcceptInvitation")
                });
            }
            return result;
        }

        internal LeagueLobbyLeaderSnapshot ParseLeader(byte[] bytes)
        {
            var root = ParseObject(bytes);
            var local = ReadDictionary(root, "localMember");
            if (local == null) return null;
            var leader = new LeagueLobbyLeaderSnapshot
            {
                IsLeader = ReadBool(local, "isLeader"),
                SelfSummonerId = ReadLong(local, "summonerId"),
                PartyId = ReadString(root, "partyId")
            };
            if (!leader.IsLeader || leader.SelfSummonerId <= 0) return leader;
            foreach (var member in EnumerateDictionaries(ReadValue(root, "members")))
            {
                if (leader.Members.Count >= 25) return null;
                var id = ReadLong(member, "summonerId");
                if (id <= 0 || id == leader.SelfSummonerId || ReadBool(member, "isBot") ||
                    ReadBool(member, "isSpectator")) continue;
                if (leader.Members.Any(existing => existing.SummonerId == id)) continue;
                leader.Members.Add(new LeagueLobbyLeaderCandidate { SummonerId = id, Ready = ReadBool(member, "ready") });
            }
            return leader;
        }

        private bool IsInvitationsEnabled()
        {
            lock (_sync) return IsInvitationsEnabledLocked();
        }

        private bool IsInvitationsEnabledLocked()
        {
            return !_disposed && _connected && _handleInvitations && IsEligiblePhaseLocked();
        }

        private bool IsLeaderEnabled()
        {
            lock (_sync) return IsLeaderEnabledLocked();
        }

        private bool IsLeaderEnabledLocked()
        {
            return !_disposed && _connected && _autoSkipLeader && _phase == "Lobby";
        }

        private bool IsEligiblePhaseLocked()
        {
            return _phase == "Lobby" || _phase == "None";
        }

        private Dictionary<string, object> ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 256 * 1024) return null;
            try { return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> row, string key)
        {
            object value;
            return row != null && row.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static object ReadValue(Dictionary<string, object> row, string key)
        {
            object value;
            return row != null && row.TryGetValue(key, out value) ? value : null;
        }

        private static string ReadString(Dictionary<string, object> row, string key)
        {
            var value = ReadValue(row, key);
            return value == null ? null : Convert.ToString(value);
        }

        private static long ReadLong(Dictionary<string, object> row, string key)
        {
            try { return Convert.ToInt64(ReadValue(row, key)); }
            catch { return 0; }
        }

        private static bool ReadBool(Dictionary<string, object> row, string key)
        {
            try { return Convert.ToBoolean(ReadValue(row, key)); }
            catch { return false; }
        }

        private static IEnumerable<Dictionary<string, object>> EnumerateDictionaries(object value)
        {
            var rows = value as IEnumerable;
            if (rows == null || value is string) yield break;
            foreach (var item in rows)
            {
                var row = item as Dictionary<string, object>;
                if (row != null) yield return row;
            }
        }

        private void CancelLocked()
        {
            var active = _active;
            _active = null;
            if (active != null)
            {
                try { active.Cancel(); }
                catch { }
                active.Dispose();
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                CancelLocked();
                _attemptedInvitations.Clear();
            }
        }
    }
}
