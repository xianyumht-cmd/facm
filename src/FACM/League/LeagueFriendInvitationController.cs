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
    internal sealed class LeagueFriendInvitationChoice
    {
        public string Puuid { get; set; }
        public string Name { get; set; }
        public string Availability { get; set; }
        public long SummonerId { get; set; }
        public bool Selected { get; set; }
        public bool Attempted { get; set; }
    }

    internal sealed class LeagueFriendInvitationCatalog
    {
        public string LobbyKey { get; set; }
        public List<LeagueFriendInvitationChoice> Friends { get; set; }
    }

    internal sealed class LeagueFriendInvitationController : IDisposable
    {
        internal const string FriendsPath = "/lol-chat/v1/friends";
        internal const string LobbyPath = "/lol-lobby/v2/lobby";
        private static readonly long PollTicks = Math.Max(1, (long)(Stopwatch.Frequency * 3.0));

        private readonly object _sync = new object();
        private readonly ILeagueClientApi _read;
        private readonly ILeagueLobbyInviteWriteApi _write;
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _attempted = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _catalogPuuids = new HashSet<string>(StringComparer.Ordinal);
        private CancellationTokenSource _active;
        private string _lobbyKey;
        private string _catalogLobbyKey;
        private long _lastTick;
        private bool _inLobby;
        private bool _disposed;

        internal LeagueFriendInvitationController(ILeagueClientApi read, ILeagueLobbyInviteWriteApi write)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        internal void Observe(LeagueDashboardPhaseState state)
        {
            CancellationToken token;
            lock (_sync)
            {
                if (_disposed) return;
                // The LCU lobby remains alive while matchmaking or a ready check is underway.
                _inLobby = state != null && state.Connected &&
                    (state.Phase == "Lobby" || state.Phase == "Matchmaking" || state.Phase == "ReadyCheck");
                if (!_inLobby)
                {
                    ClearLocked();
                    return;
                }
                if (_pending.Count == 0 || _active != null) return;
                var now = Stopwatch.GetTimestamp();
                if (_lastTick != 0 && now - _lastTick < PollTicks) return;
                _lastTick = now;
                _active = new CancellationTokenSource();
                token = _active.Token;
            }
            RunAsync(token).Forget("League scheduled friend invitation");
        }

        internal async Task<LeagueFriendInvitationCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
                if (_disposed || !_inLobby) return null;
            var lobby = ParseLobby(await _read.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false));
            if (lobby == null) return null;
            var friends = ParseFriends(await _read.TryGetBytesAsync(FriendsPath, cancellationToken).ConfigureAwait(false));
            if (friends == null) return null;
            var fresh = ParseLobby(await _read.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false));
            cancellationToken.ThrowIfCancellationRequested();
            if (fresh == null || fresh.Key != lobby.Key) return null;

            lock (_sync)
            {
                if (_disposed || !_inLobby) return null;
                if (_lobbyKey != null && _lobbyKey != lobby.Key) ClearLocked();
                _catalogLobbyKey = lobby.Key;
                _catalogPuuids.Clear();
                foreach (var friend in friends)
                {
                    _catalogPuuids.Add(friend.Puuid);
                    friend.Selected = _lobbyKey == lobby.Key && _pending.Contains(friend.Puuid);
                    friend.Attempted = _lobbyKey == lobby.Key && _attempted.Contains(friend.Puuid);
                }
            }
            return new LeagueFriendInvitationCatalog { LobbyKey = lobby.Key, Friends = friends };
        }

        internal bool SetScheduled(string lobbyKey, string puuid, bool selected)
        {
            lock (_sync)
            {
                if (_disposed || !_inLobby || lobbyKey != _catalogLobbyKey || !IsSafePuuid(puuid) ||
                    !_catalogPuuids.Contains(puuid)) return false;
                if (_lobbyKey != null && _lobbyKey != lobbyKey) return false;
                if (!selected)
                {
                    _pending.Remove(puuid);
                    if (_pending.Count == 0) CancelActiveLocked();
                    return true;
                }
                if (_attempted.Contains(puuid)) return false;
                if (_pending.Count >= 5 && !_pending.Contains(puuid)) return false;
                _lobbyKey = lobbyKey;
                _pending.Add(puuid);
                _lastTick = 0;
                return true;
            }
        }

        internal int PendingCount
        {
            get { lock (_sync) return _pending.Count; }
        }

        internal async Task EvaluateOnceForSmokeTestAsync(CancellationToken cancellationToken)
        {
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
            string key;
            lock (_sync)
            {
                if (_disposed || !_inLobby || _pending.Count == 0) return;
                key = _lobbyKey;
            }
            var lobby = ParseLobby(await _read.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false));
            cancellationToken.ThrowIfCancellationRequested();
            if (lobby == null) return;
            if (lobby.Key != key)
            {
                lock (_sync) if (_lobbyKey == key) ClearLocked();
                return;
            }
            if (!lobby.CanInvite) return;
            var friends = ParseFriends(await _read.TryGetBytesAsync(FriendsPath, cancellationToken).ConfigureAwait(false));
            if (friends == null) return;
            cancellationToken.ThrowIfCancellationRequested();
            LeagueFriendInvitationChoice candidate = null;
            lock (_sync)
            {
                if (_disposed || !_inLobby || _lobbyKey != key) return;
                var found = new HashSet<string>(friends.Select(friend => friend.Puuid), StringComparer.Ordinal);
                _pending.IntersectWith(found);
                candidate = friends.FirstOrDefault(friend => _pending.Contains(friend.Puuid) &&
                    friend.Availability == "chat" && friend.SummonerId > 0 &&
                    !lobby.MemberPuuids.Contains(friend.Puuid));
            }
            if (candidate == null) return;

            var latestLobby = ParseLobby(await _read.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false));
            var latestFriends = ParseFriends(await _read.TryGetBytesAsync(FriendsPath, cancellationToken).ConfigureAwait(false));
            cancellationToken.ThrowIfCancellationRequested();
            if (latestLobby == null || latestFriends == null || latestLobby.Key != key || !latestLobby.CanInvite ||
                latestLobby.MemberPuuids.Contains(candidate.Puuid) ||
                !latestFriends.Any(friend => friend.Puuid == candidate.Puuid &&
                    friend.SummonerId == candidate.SummonerId && friend.Availability == "chat")) return;

            lock (_sync)
            {
                if (_disposed || !_inLobby || _lobbyKey != key || !_pending.Remove(candidate.Puuid)) return;
                _attempted.Add(candidate.Puuid);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _write.TryInviteAsync(candidate.SummonerId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            AppLog.Info("League scheduled friend invitation: " +
                (response != null && response.IsSuccessStatusCode ? "submitted" : "unverified"));
        }

        private LeagueFriendLobbySnapshot ParseLobby(byte[] bytes)
        {
            var root = ParseObject(bytes);
            var local = ReadDictionary(root, "localMember");
            var partyId = ReadString(root, "partyId");
            var selfId = ReadLong(local, "summonerId");
            if (string.IsNullOrEmpty(partyId) || partyId.Length > 128 || selfId <= 0) return null;
            var snapshot = new LeagueFriendLobbySnapshot
            {
                Key = partyId + "|" + selfId,
                CanInvite = ReadBool(local, "allowedInviteOthers")
            };
            foreach (var member in EnumerateDictionaries(ReadValue(root, "members")))
            {
                if (snapshot.MemberPuuids.Count > 24) return null;
                var puuid = ReadString(member, "puuid");
                if (IsSafePuuid(puuid)) snapshot.MemberPuuids.Add(puuid);
            }
            return snapshot;
        }

        private List<LeagueFriendInvitationChoice> ParseFriends(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 1024 * 1024) return null;
            object parsed;
            try { parsed = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }
                .DeserializeObject(Encoding.UTF8.GetString(bytes)); }
            catch { return null; }
            var rows = parsed as IEnumerable;
            if (rows == null || parsed is string) return null;
            var result = new List<LeagueFriendInvitationChoice>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in rows)
            {
                if (result.Count >= 3000) return null;
                var friend = item as Dictionary<string, object>;
                var puuid = ReadString(friend, "puuid");
                if (!IsSafePuuid(puuid) || !seen.Add(puuid)) continue;
                var name = ReadString(friend, "gameName") ?? ReadString(friend, "name") ?? "";
                var tag = ReadString(friend, "gameTag");
                name = CleanName(name, 48);
                tag = CleanName(tag, 12);
                result.Add(new LeagueFriendInvitationChoice
                {
                    Puuid = puuid,
                    Name = string.IsNullOrEmpty(tag) ? name : name + " #" + tag,
                    Availability = ReadString(friend, "availability"),
                    SummonerId = ReadLong(friend, "summonerId")
                });
            }
            return result.OrderBy(friend => friend.Availability == "chat" ? 1 :
                friend.Availability == "away" || friend.Availability == "dnd" ? 0 : 2)
                .ThenBy(friend => friend.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private Dictionary<string, object> ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 256 * 1024) return null;
            try { return new JavaScriptSerializer { MaxJsonLength = 256 * 1024 }
                .DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        internal static bool IsSafePuuid(string puuid)
        {
            if (string.IsNullOrEmpty(puuid) || puuid.Length < 8 || puuid.Length > 90) return false;
            foreach (var value in puuid)
                if (!(value >= '0' && value <= '9') && !(value >= 'a' && value <= 'z') &&
                    !(value >= 'A' && value <= 'Z') && value != '-') return false;
            return true;
        }

        private static string CleanName(string value, int limit)
        {
            return new string((value ?? string.Empty).Where(c => !char.IsControl(c)).Take(limit).ToArray()).Trim();
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> row, string key)
        {
            return ReadValue(row, key) as Dictionary<string, object>;
        }

        private static object ReadValue(Dictionary<string, object> row, string key)
        {
            object value;
            return row != null && row.TryGetValue(key, out value) ? value : null;
        }

        private static string ReadString(Dictionary<string, object> row, string key)
        {
            return ReadValue(row, key) as string;
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

        private void ClearLocked()
        {
            CancelActiveLocked();
            _pending.Clear();
            _attempted.Clear();
            _catalogPuuids.Clear();
            _catalogLobbyKey = null;
            _lobbyKey = null;
            _lastTick = 0;
        }

        private void CancelActiveLocked()
        {
            var active = _active;
            _active = null;
            if (active == null) return;
            try { active.Cancel(); }
            catch { }
            active.Dispose();
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                ClearLocked();
            }
        }

        private sealed class LeagueFriendLobbySnapshot
        {
            internal string Key;
            internal bool CanInvite;
            internal readonly HashSet<string> MemberPuuids = new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
