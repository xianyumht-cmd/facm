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
    internal enum LeagueLobbyRoomAction { Queue, Leave, Kick, Promote, Positions, Spectator, GrantInvite, RevokeInvite }

    internal sealed class LeagueLobbyRoomMember
    {
        internal long Id { get; set; }
        internal string Name { get; set; }
        internal bool IsLeader { get; set; }
        internal bool? CanInvite { get; set; }
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
        internal Dictionary<int, string> QueueChoices { get; set; }
        internal bool ShowPositions { get; set; }
        internal int FirstRole { get; set; }
        internal int SecondRole { get; set; }
        internal bool? IsSpectator { get; set; }
        internal bool IsCustom { get; set; }
        internal string SpectatorPolicy { get; set; }
        internal bool CanToggleInvite { get; set; }
        internal bool CanSetPositions { get { return Phase == "Lobby" && ShowPositions && IsSpectator == false; } }
        internal bool CanSpectate { get { return Phase == "Lobby" && IsCustom && IsSpectator == false &&
            (SpectatorPolicy == "LobbyAllowed" || SpectatorPolicy == "FriendsAllowed" || SpectatorPolicy == "AllAllowed"); } }
        internal bool CanQueue { get { return (Phase == "None" || Phase == "Lobby" && IsLeader) &&
            QueueChoices != null && QueueChoices.Count > 0; } }
        internal string QueueName
        {
            get
            {
                string name;
                return QueueChoices != null && QueueChoices.TryGetValue(QueueId, out name)
                    ? name : LeagueLobbyRoomText.QueueLabel(QueueId);
            }
        }
        internal bool CanLeave { get { return Phase == "Lobby"; } }
        internal bool CanManageMembers { get { return Phase == "Lobby" && IsLeader; } }

        internal bool Allows(LeagueLobbyRoomAction action, long value)
        {
            switch (action)
            {
                case LeagueLobbyRoomAction.Queue: return CanQueue && LeagueLobbyRoomService.IsQueueId(value) && QueueChoices.ContainsKey((int)value);
                case LeagueLobbyRoomAction.Leave: return CanLeave && value == 0;
                case LeagueLobbyRoomAction.Positions: return CanSetPositions && LeagueLobbyRoomService.ValidRolePair(value);
                case LeagueLobbyRoomAction.Spectator: return CanSpectate && value == 0;
                case LeagueLobbyRoomAction.GrantInvite:
                case LeagueLobbyRoomAction.RevokeInvite:
                    return CanManageMembers && CanToggleInvite && value > 0 && value != SelfId &&
                        Members.Any(x => x.Id == value && !x.IsLeader &&
                            x.CanInvite == (action == LeagueLobbyRoomAction.RevokeInvite));
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
                ShowPositions == other.ShowPositions && FirstRole == other.FirstRole && SecondRole == other.SecondRole &&
                IsSpectator == other.IsSpectator && IsCustom == other.IsCustom &&
                SpectatorPolicy == other.SpectatorPolicy && CanToggleInvite == other.CanToggleInvite &&
                Members.OrderBy(x => x.Id).Select(x => new { x.Id, x.IsLeader, x.CanInvite }).SequenceEqual(
                    other.Members.OrderBy(x => x.Id).Select(x => new { x.Id, x.IsLeader, x.CanInvite }));
        }
    }

    internal sealed class LeagueLobbyRoomResult
    {
        internal string State { get; set; }
        internal LeagueLobbyRoomSnapshot Snapshot { get; set; }
    }

    internal sealed class LeagueLobbyRoomService
    {
        internal const string QueueCatalogPath = "/lol-game-queues/v1/queues";
        private readonly ILeagueClientApi _read;
        private readonly ILeagueLobbyRoomWriteApi _write;
        private readonly Func<LeagueClientSession> _session;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private LeagueLobbyRoomSnapshot _uncertainRoom;

        internal LeagueLobbyRoomService(ILeagueClientApi read, ILeagueLobbyRoomWriteApi write, Func<LeagueClientSession> session)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal static bool IsQueueId(long id) { return id > 0 && id <= 100000; }
        private static readonly string[] RoleNames = { "", "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY", "FILL" };
        internal static string RoleName(long id) { return id >= 1 && id <= 6 ? RoleNames[(int)id] : null; }
        internal static bool ValidRolePair(long value)
        {
            return value >= 11 && value <= 66 && RoleName(value / 10) != null &&
                RoleName(value % 10) != null && value / 10 != value % 10;
        }
        private static int RoleId(object value) { return Math.Max(0, Array.IndexOf(RoleNames, value as string)); }

        internal static Dictionary<int, string> ParseQueueCatalog(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024) return null;
            object[] rows;
            try { rows = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }
                .DeserializeObject(Encoding.UTF8.GetString(bytes)) as object[]; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            if (rows == null || rows.Length > 2048) return null;
            var choices = new Dictionary<int, string>();
            var seen = new HashSet<int>();
            foreach (var entry in rows)
            {
                var row = entry as Dictionary<string, object>;
                if (row == null) return null;
                var id = Long(row, "id");
                if (!IsQueueId(id)) continue;
                if (!seen.Add((int)id)) return null;
                var visible = Value(row, "isVisible");
                var name = Value(row, "name") as string;
                if (!string.Equals(Value(row, "queueAvailability") as string, "Available", StringComparison.Ordinal) ||
                    !(visible is bool) || !(bool)visible ||
                    string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl)) continue;
                choices.Add((int)id, name.Trim());
            }
            return choices;
        }

        private async Task<Dictionary<int, string>> ReadQueueCatalogAsync(CancellationToken token)
        {
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                budget.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    return ParseQueueCatalog(await _read.TryGetBytesAsync(QueueCatalogPath, budget.Token).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
            }
        }

        internal async Task<LeagueLobbyRoomSnapshot> ReadAsync(CancellationToken token, bool includeQueues = true)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    try { return await ReadCoreAsync(_session(), deadline.Token, includeQueues).ConfigureAwait(false); }
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
                        var fresh = await ReadCoreAsync(expected.Owner, ct, action == LeagueLobbyRoomAction.Queue).ConfigureAwait(false);
                        if (fresh != null && action != LeagueLobbyRoomAction.Queue && fresh.SelfId == expected.SelfId)
                            fresh.QueueChoices = expected.QueueChoices;
                        if (!expected.SameRoom(fresh) || !fresh.Allows(action, value)) return Result("stale", fresh);
                        if (action == LeagueLobbyRoomAction.Queue && fresh.Phase == "Lobby" && fresh.QueueId == value ||
                            action == LeagueLobbyRoomAction.Positions && fresh.FirstRole * 10 + fresh.SecondRole == value)
                            return Result("already", fresh);
                        if (_uncertainRoom != null && _uncertainRoom.SameRoom(fresh))
                            return Result("unverified", fresh);

                        // An unresolved mutation owns the unchanged room, including different actions.
                        ct.ThrowIfCancellationRequested();
                        _uncertainRoom = fresh;
                        var response = await _write.TryApplyRoomAsync(action, value, fresh.Owner, ct).ConfigureAwait(false);
                        if (response != null && response.StatusCode >= 400 && response.StatusCode < 500 && response.StatusCode != 408)
                        {
                            _uncertainRoom = null;
                            return Result("rejected", fresh);
                        }
                        LeagueLobbyRoomSnapshot observed = null;
                        for (var attempt = 0; attempt < 3; attempt++)
                        {
                            if (attempt > 0) await Task.Delay(350, ct).ConfigureAwait(false);
                            observed = await ReadCoreAsync(fresh.Owner, ct).ConfigureAwait(false);
                            if (observed == null || observed.SelfId != fresh.SelfId) { observed = null; break; }
                            observed.QueueChoices = fresh.QueueChoices;
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

        private async Task<LeagueLobbyRoomSnapshot> ReadCoreAsync(LeagueClientSession owner, CancellationToken token, bool includeQueues = false)
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
            if (includeQueues)
                result.QueueChoices = await ReadQueueCatalogAsync(token).ConfigureAwait(false);
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
                PartyId = party, QueueId = (int)queue, IsLeader = (bool)leader,
                ShowPositions = Bool(game, "showPositionSelector") == true,
                FirstRole = RoleId(Value(local, "firstPositionPreference")),
                SecondRole = RoleId(Value(local, "secondPositionPreference")),
                IsSpectator = Bool(local, "isSpectator"),
                IsCustom = Bool(game, "isCustom") == true,
                SpectatorPolicy = Value(game, "customSpectatorPolicy") as string,
                CanToggleInvite = Bool(local, "allowedToggleInvite") == true };
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
                    IsLeader = Bool(row, "isLeader") == true, CanInvite = Bool(row, "allowedInviteOthers") });
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
            if (before.QueueId != after.QueueId) return false;
            if (action == LeagueLobbyRoomAction.Positions)
                return after.FirstRole == value / 10 && after.SecondRole == value % 10;
            if (action == LeagueLobbyRoomAction.Spectator) return after.IsSpectator == true;
            if (action == LeagueLobbyRoomAction.GrantInvite || action == LeagueLobbyRoomAction.RevokeInvite)
                return after.Members.Any(x => x.Id == value && x.CanInvite == (action == LeagueLobbyRoomAction.GrantInvite));
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

        private static bool? Bool(Dictionary<string, object> root, string name)
        {
            var value = Value(root, name);
            return value is bool ? (bool?)value : null;
        }

        private static long Long(Dictionary<string, object> root, string name)
        {
            var value = Value(root, name);
            return value is int ? (int)value : value is long ? (long)value : 0;
        }
    }

    internal static class LeagueLobbyRoomText
    {
        internal static string RoleLabel(int role)
        {
            switch (role)
            {
                case 1: return "上路";
                case 2: return "打野";
                case 3: return "中路";
                case 4: return "下路";
                case 5: return "辅助";
                case 6: return "补位";
                default: return "未选择";
            }
        }

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

        internal static string CatalogStatus(LeagueLobbyRoomSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Phase == "Lobby" && !snapshot.IsLeader) return string.Empty;
            return snapshot.QueueChoices == null ? "队列目录暂不可用，请刷新" :
                snapshot.QueueChoices.Count == 0 ? "暂无可选队列" : string.Empty;
        }

        internal static string Status(string state)
        {
            switch (state)
            {
                case "confirmed": return "房间操作已确认";
                case "already": return "已是所选设置";
                case "rejected": return "客户端拒绝操作";
                case "stale": return "房间状态已变化，请刷新";
                default: return "房间操作未确认，请查看客户端";
            }
        }
    }
}
