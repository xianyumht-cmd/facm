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
    // Pinned Akari /config/v1/auto-select/groups, restricted to groups already represented
    // by the native profile editor. No remote group may add a new write type or credential route.
    internal static class LeagueAutoSelectGroupCatalog
    {
        private sealed class Group
        {
            internal string Id;
            internal bool Custom;
            internal string[] SupportedServers;
            internal string[] Positions;
            internal readonly List<Tuple<string, string[]>> Targets = new List<Tuple<string, string[]>>();
        }

        private const string CatalogUrl = "https://akari-api.yuru-yuri.com/config/v1/auto-select/groups";
        private static readonly DateTime BuiltinTimestamp = new DateTime(2026, 7, 18, 4, 0, 0, DateTimeKind.Utc);
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly object Sync = new object();
        private static readonly HashSet<string> KnownGroups = new HashSet<string>(
            new[] { "ranked", "normal", "aram", "cherry", "urf", "oneforall", "ultbook", "bot", "custom" },
            StringComparer.Ordinal);
        private static Group[] _groups;
        private static DateTime _updatedAt = BuiltinTimestamp;
        private static DateTime _lastAttempt = DateTime.MinValue;

        internal static bool TryGroupFor(string mode, string queueType, bool custom, out string group)
        {
            return TryGroupFor(mode, queueType, custom, null, out group);
        }

        internal static bool TryGroupFor(string mode, string queueType, bool custom,
            string sgpServerId, out string group)
        {
            group = null;
            Group[] snapshot;
            lock (Sync) snapshot = _groups;
            if (snapshot == null) return false;
            foreach (var entry in snapshot)
            {
                if (entry.Custom != custom ||
                    !entry.SupportedServers.Contains("*", StringComparer.Ordinal) &&
                    (string.IsNullOrEmpty(sgpServerId) ||
                     !entry.SupportedServers.Contains(sgpServerId, StringComparer.Ordinal))) continue;
                foreach (var target in entry.Targets)
                {
                    if (target.Item1 != mode) continue;
                    if (target.Item2.Contains("*", StringComparer.Ordinal) ||
                        target.Item2.Contains(queueType, StringComparer.Ordinal))
                    {
                        group = entry.Id;
                        return true;
                    }
                }
            }
            return true; // Remote configuration is authoritative, including no match.
        }

        internal static string RoleFor(string groupId, string sgpServerId, string assignedRole)
        {
            Group[] snapshot;
            lock (Sync) snapshot = _groups;
            if (snapshot == null) return assignedRole ?? "default";
            var group = snapshot.FirstOrDefault(entry => entry.Id == groupId);
            if (group == null || !group.SupportedServers.Contains("*", StringComparer.Ordinal) &&
                (string.IsNullOrEmpty(sgpServerId) ||
                 !group.SupportedServers.Contains(sgpServerId, StringComparer.Ordinal))) return null;
            var sourceRole = assignedRole == "support" ? "utility" : assignedRole ?? "default";
            return group.Positions.Contains(sourceRole, StringComparer.Ordinal)
                ? assignedRole ?? "default" : "default";
        }

        internal static async Task<bool> TryRefreshAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (DateTime.UtcNow - _lastAttempt < TimeSpan.FromHours(2)) return false;
                _lastAttempt = DateTime.UtcNow;
                var bytes = await LeaguePublicDataTransport.TryGetBytesAsync(
                    CatalogUrl, TimeSpan.FromSeconds(4), token).ConfigureAwait(false);
                return TryApply(bytes);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
            finally { Gate.Release(); }
        }

        internal static bool TryApply(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 128 * 1024) return false;
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 }
                    .DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                if (root == null) return false;
                object timestamp, groupsValue;
                if (!root.TryGetValue("updatedAt", out timestamp) ||
                    !root.TryGetValue("groups", out groupsValue)) return false;
                DateTimeOffset updated;
                if (!DateTimeOffset.TryParse(timestamp as string, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out updated) ||
                    updated.UtcDateTime > DateTime.UtcNow.AddDays(1)) return false;
                var raw = groupsValue as object[];
                if (raw == null || raw.Length == 0 || raw.Length > 16) return false;
                var parsed = new List<Group>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in raw)
                {
                    var entry = item as Dictionary<string, object>;
                    var id = Text(entry, "groupId");
                    if (!KnownGroups.Contains(id) || !seen.Add(id) ||
                        !(Value(entry, "isCustom") is bool) ||
                        ((bool)Value(entry, "isCustom")) != (id == "custom"))
                        return false;
                    var names = Value(entry, "name") as Dictionary<string, object>;
                    if (!SafeName(Text(names, "zh-CN")) || !SafeName(Text(names, "en")))
                        return false;
                    var icon = Text(entry, "iconPath");
                    if (string.IsNullOrEmpty(icon) || icon.Length > 256 ||
                        !icon.StartsWith("/lol-game-data/assets/", StringComparison.Ordinal) ||
                        icon.Contains("..") || icon.Any(char.IsControl)) return false;
                    var servers = Strings(Value(entry, "supportedSgpServers"), 64);
                    if (servers == null || servers.Length == 0 ||
                        servers.Contains("*", StringComparer.Ordinal) && servers.Length != 1 ||
                        servers.Any(server => server != "*" &&
                            !LeagueSgpMatchHistoryApiClient.Servers.Any(known => known.Id == server)))
                        return false;
                    var positions = Strings(Value(entry, "positions"), 8);
                    if (positions == null || positions.Length == 0 ||
                        positions.Any(p => !new[] { "default", "top", "jungle", "middle", "bottom", "utility" }.Contains(p)))
                        return false;
                    if (!ValidActions(Value(entry, "additionalPicks"), id == "cherry" ? -3 : 0) ||
                        !ValidActions(Value(entry, "additionalBans"), 0) ||
                        !ValidActions(Value(entry, "excludedPicks"), -1) ||
                        !ValidActions(Value(entry, "excludedBans"), 0)) return false;
                    var targets = Value(entry, "targetGameModes") as object[];
                    if (targets == null || targets.Length == 0 || targets.Length > 10) return false;
                    var group = new Group { Id = id, Custom = (bool)Value(entry, "isCustom"),
                        SupportedServers = servers, Positions = positions };
                    foreach (var targetValue in targets)
                    {
                        var target = targetValue as Dictionary<string, object>;
                        var mode = Text(target, "gameMode");
                        var queues = Strings(Value(target, "queueTypes"), 16);
                        if (!SafeCode(mode) || !AllowedMode(id, mode) ||
                            queues == null || queues.Length == 0 ||
                            queues.Any(q => q != "*" && !SafeCode(q))) return false;
                        group.Targets.Add(Tuple.Create(mode, queues));
                    }
                    parsed.Add(group);
                }
                lock (Sync)
                {
                    if (updated.UtcDateTime <= _updatedAt) return false;
                    _groups = parsed.ToArray();
                    _updatedAt = updated.UtcDateTime;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        // Dynamic queue types are supported within the pinned game-mode families.
        // A remote catalog cannot silently reinterpret ranked profiles as ARAM or Arena.
        private static bool AllowedMode(string group, string mode)
        {
            switch (group)
            {
                case "ranked": case "normal": return mode == "CLASSIC";
                case "aram": return mode == "ARAM" || mode == "KIWI";
                case "cherry": return mode == "CHERRY";
                case "urf": return mode == "URF";
                case "oneforall": return mode == "ONEFORALL";
                case "ultbook": return mode == "ULTBOOK";
                case "bot": return mode == "SWIFTPLAY";
                case "custom": return mode == "CLASSIC" || mode == "PRACTICETOOL";
                default: return false;
            }
        }

        private static bool ValidActions(object value, int special)
        {
            var raw = value as object[];
            if (raw == null || raw.Length > 16) return false;
            foreach (var item in raw)
            {
                int id;
                if (item == null || !int.TryParse(Convert.ToString(item, CultureInfo.InvariantCulture), out id) ||
                    id != special || special == 0) return false;
            }
            return true;
        }

        private static bool SafeName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && !value.Any(char.IsControl);
        }

        private static bool SafeCode(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length <= 40 &&
                value.All(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' ||
                    c >= '0' && c <= '9' || c == '_');
        }

        private static string[] Strings(object value, int maximum)
        {
            var raw = value as object[];
            if (raw == null || raw.Length > maximum) return null;
            var result = new List<string>();
            foreach (var item in raw)
            {
                var text = item as string;
                if (string.IsNullOrWhiteSpace(text) || text.Length > 64 ||
                    text.Any(char.IsControl) || result.Contains(text)) return null;
                result.Add(text);
            }
            return result.ToArray();
        }

        private static object Value(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static string Text(Dictionary<string, object> source, string key)
        {
            return Value(source, key) as string;
        }

        internal static void ResetForSmokeTest()
        {
            lock (Sync)
            {
                _groups = null;
                _updatedAt = BuiltinTimestamp;
                _lastAttempt = DateTime.MinValue;
            }
        }
    }
}
