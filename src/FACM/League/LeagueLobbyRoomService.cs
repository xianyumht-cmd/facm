using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal enum LeagueLobbyRoomAction { Queue, Leave, Kick, Promote }

    internal sealed class LeagueLobbyRoomMember
    {
        internal long Id { get; set; }
        internal string Name { get; set; }
        internal bool IsLeader { get; set; }
    }

    internal sealed class LeagueLobbyRoomSnapshot
    {
        internal LeagueClientSession Owner { get; set; }
        internal string Phase { get; set; }
        internal long SelfId { get; set; }
        internal string PartyId { get; set; }
        internal int QueueId { get; set; }
        internal bool IsLeader { get; set; }
        internal List<LeagueLobbyRoomMember> Members { get; } = new List<LeagueLobbyRoomMember>();
        internal bool CanQueue { get { return Phase == "None" || Phase == "Lobby" && IsLeader; } }
        internal bool CanLeave { get { return Phase == "Lobby"; } }
        internal bool CanManageMembers { get { return Phase == "Lobby" && IsLeader; } }

        internal bool Allows(LeagueLobbyRoomAction action, long value)
        {
            switch (action)
            {
                case LeagueLobbyRoomAction.Queue: return CanQueue && LeagueLobbyRoomService.IsKnownQueue(value);
                case LeagueLobbyRoomAction.Leave: return CanLeave && value == 0;
                case LeagueLobbyRoomAction.Kick:
                case LeagueLobbyRoomAction.Promote:
                    return CanManageMembers && value > 0 && value != SelfId && Members.Any(x => x.Id == value);
                default: return false;
            }
        }

        internal bool SameRoom(LeagueLobbyRoomSnapshot other)
        {
            return other != null && LeagueEncounterReplayService.SessionMatches(Owner, other.Owner) &&
                SelfId == other.SelfId && Phase == other.Phase && PartyId == other.PartyId &&
                QueueId == other.QueueId && IsLeader == other.IsLeader &&
                Members.Select(x => x.Id).OrderBy(x => x).SequenceEqual(other.Members.Select(x => x.Id).OrderBy(x => x));
        }
    }

    internal sealed class LeagueLobbyRoomResult
    {
        internal string State { get; set; }
        internal LeagueLobbyRoomSnapshot Snapshot { get; set; }
    }

    internal sealed class LeagueLobbyRoomService
    {
        private static readonly int[] KnownQueues = { 420, 440, 430, 400, 450, 3270 };
        internal static IReadOnlyList<int> Queues { get { return Array.AsReadOnly(KnownQueues); } }
        private readonly ILeagueClientApi _read;
        private readonly ILeagueLobbyRoomWriteApi _write;
        private readonly Func<LeagueClientSession> _session;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private LeagueLobbyRoomSnapshot _uncertainRoom;
        private LeagueLobbyRoomAction _uncertainAction;
        private long _uncertainValue;

        internal LeagueLobbyRoomService(ILeagueClientApi read, ILeagueLobbyRoomWriteApi write, Func<LeagueClientSession> session)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal static bool IsKnownQueue(long id) { return KnownQueues.Any(x => x == id); }

        internal async Task<LeagueLobbyRoomSnapshot> ReadAsync(CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    try { return await ReadCoreAsync(_session(), deadline.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
                }
            }
            finally { _gate.Release(); }
        }

        internal async Task<LeagueLobbyRoomResult> ApplyAsync(LeagueLobbyRoomSnapshot expected,
            LeagueLobbyRoomAction action, long value, CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (expected == null || !expected.Allows(action, value) || !Current(expected.Owner))
                    return Result("stale", null);
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(9));
                    var ct = deadline.Token;
                    try
                    {
                        var fresh = await ReadCoreAsync(expected.Owner, ct).ConfigureAwait(false);
                        if (!expected.SameRoom(fresh) || !fresh.Allows(action, value)) return Result("stale", fresh);
                        if (action == LeagueLobbyRoomAction.Queue && fresh.Phase == "Lobby" && fresh.QueueId == value)
                            return Result("already", fresh);
                        if (_uncertainRoom != null && _uncertainRoom.SameRoom(fresh) &&
                            _uncertainAction == action && _uncertainValue == value)
                            return Result("unverified", fresh);

                        // Fence the one operation before transport; a lost response is not a retry signal.
                        _uncertainRoom = fresh;
                        _uncertainAction = action;
                        _uncertainValue = value;
                        ct.ThrowIfCancellationRequested();
                        var response = await _write.TryApplyRoomAsync(action, value, fresh.Owner, ct).ConfigureAwait(false);
                        if (response != null && !response.IsSuccessStatusCode && response.StatusCode < 500)
                        {
                            _uncertainRoom = null;
                            return Result("rejected", fresh);
                        }
                        LeagueLobbyRoomSnapshot observed = null;
                        for (var attempt = 0; attempt < 3; attempt++)
                        {
                            if (attempt > 0) await Task.Delay(350, ct).ConfigureAwait(false);
                            observed = await ReadCoreAsync(fresh.Owner, ct).ConfigureAwait(false);
                            if (observed == null || observed.SelfId != fresh.SelfId) break;
                            if (Confirms(fresh, observed, action, value))
                            {
                                _uncertainRoom = null;
                                return Result("confirmed", observed);
                            }
                            if (!fresh.SameRoom(observed)) break;
                        }
                        return Result("unverified", observed);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Result("unverified", null); }
                }
            }
            finally { _gate.Release(); }
        }

        private bool Current(LeagueClientSession owner)
        {
            return LeagueEncounterReplayService.SessionMatches(owner, _session());
        }

        private async Task<LeagueLobbyRoomSnapshot> ReadCoreAsync(LeagueClientSession owner, CancellationToken token)
        {
            if (owner == null || !Current(owner)) return null;
            var phase = await ReadPhaseAsync(token).ConfigureAwait(false);
            if (phase != "None" && phase != "Lobby") return null;
            var account = Object(await _read.TryGetBytesAsync(LeaguePostGameAutomationController.CurrentSummonerPath, token).ConfigureAwait(false));
            var self = Long(account, "summonerId");
            if (self <= 0 || !Current(owner)) return null;
            var result = phase == "None"
                ? new LeagueLobbyRoomSnapshot { Owner = owner, SelfId = self, Phase = phase }
                : ParseLobby(await _read.TryGetBytesAsync(LeagueLiveDataService.LobbyPath, token).ConfigureAwait(false), owner, self);
            if (result == null || !Current(owner)) return null;
            var finalPhase = await ReadPhaseAsync(token).ConfigureAwait(false);
            return finalPhase == phase && Current(owner) ? result : null;
        }

        private async Task<string> ReadPhaseAsync(CancellationToken token)
        {
            var bytes = await _read.TryGetBytesAsync(LeaguePostGameAutomationController.GameflowPhasePath, token).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0 || bytes.Length > 256) return null;
            try { return new JavaScriptSerializer().Deserialize<string>(Encoding.UTF8.GetString(bytes)); }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        internal static LeagueLobbyRoomSnapshot ParseLobby(byte[] bytes, LeagueClientSession owner, long self)
        {
            var root = Object(bytes);
            var local = Value(root, "localMember") as Dictionary<string, object>;
            var game = Value(root, "gameConfig") as Dictionary<string, object>;
            var members = Value(root, "members") as object[];
            var party = Value(root, "partyId") as string;
            var leader = Value(local, "isLeader");
            var queue = Value(game, "queueId");
            if (self <= 0 || Long(local, "summonerId") != self || !(leader is bool) ||
                !(queue is int) || (int)queue < 0 || string.IsNullOrWhiteSpace(party) ||
                party.Length > 128 || members == null || members.Length == 0 || members.Length > 25)
                return null;
            var result = new LeagueLobbyRoomSnapshot { Owner = owner, SelfId = self, Phase = "Lobby",
                PartyId = party, QueueId = (int)queue, IsLeader = (bool)leader };
            var seen = new HashSet<long>();
            foreach (var value in members)
            {
                var row = value as Dictionary<string, object>;
                if (row == null) return null;
                if (Value(row, "isBot") is bool && (bool)Value(row, "isBot")) continue;
                var id = Long(row, "summonerId");
                if (id <= 0 || !seen.Add(id)) return null;
                var name = Value(row, "summonerName") as string ?? Value(row, "gameName") as string;
                if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
                    name = "#" + id.ToString(CultureInfo.InvariantCulture);
                result.Members.Add(new LeagueLobbyRoomMember { Id = id, Name = name,
                    IsLeader = Value(row, "isLeader") is bool && (bool)Value(row, "isLeader") });
            }
            return seen.Contains(self) ? result : null;
        }

        internal static bool Confirms(LeagueLobbyRoomSnapshot before, LeagueLobbyRoomSnapshot after,
            LeagueLobbyRoomAction action, long value)
        {
            if (after == null || before == null || before.SelfId != after.SelfId ||
                !LeagueEncounterReplayService.SessionMatches(before.Owner, after.Owner)) return false;
            if (action == LeagueLobbyRoomAction.Queue) return after.Phase == "Lobby" && after.QueueId == value;
            if (action == LeagueLobbyRoomAction.Leave) return after.Phase == "None";
            if (after.Phase != "Lobby" || before.PartyId != after.PartyId) return false;
            if (action == LeagueLobbyRoomAction.Kick) return !after.Members.Any(x => x.Id == value);
            return action == LeagueLobbyRoomAction.Promote && !after.IsLeader &&
                after.Members.Any(x => x.Id == value && x.IsLeader);
        }

        private static LeagueLobbyRoomResult Result(string state, LeagueLobbyRoomSnapshot snapshot)
        {
            return new LeagueLobbyRoomResult { State = state, Snapshot = snapshot };
        }

        private static Dictionary<string, object> Object(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024) return null;
            try { return new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }
                .DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static object Value(Dictionary<string, object> root, string name)
        {
            object value;
            return root != null && root.TryGetValue(name, out value) ? value : null;
        }

        private static long Long(Dictionary<string, object> root, string name)
        {
            var value = Value(root, name);
            return value is int ? (int)value : value is long ? (long)value : 0;
        }
    }

    internal static class LeagueLobbyRoomText
    {
        internal static string QueueLabel(int queue)
        {
            switch (queue)
            {
                case 420: return "单双排";
                case 440: return "灵活组排";
                case 430: return "匹配盲选";
                case 400: return "匹配征召";
                case 450: return "极地大乱斗";
                case 3270: return "海克斯大乱斗";
                default: return "#" + queue.ToString(CultureInfo.InvariantCulture);
            }
        }

        internal static string Status(string state)
        {
            switch (state)
            {
                case "confirmed": return "房间操作已确认";
                case "already": return "已在所选队列";
                case "rejected": return "客户端拒绝操作";
                case "stale": return "房间状态已变化，请刷新";
                default: return "房间操作未确认，请查看客户端";
            }
        }
    }
}
