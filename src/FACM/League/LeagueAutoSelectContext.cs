using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal static class LeagueAutoSelectContext
    {
        internal static string GroupFor(string mode, string queueType, bool custom)
        {
            return GroupFor(mode, queueType, custom, null);
        }

        internal static string GroupFor(string mode, string queueType, bool custom, string sgpServerId)
        {
            if (string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(queueType)) return null;
            string remote;
            if (LeagueAutoSelectGroupCatalog.TryGroupFor(mode, queueType, custom, sgpServerId, out remote))
                return remote;
            if (custom) return (mode == "CLASSIC" || mode == "PRACTICETOOL") && queueType == "NORMAL" ? "custom" : null;
            switch (mode)
            {
                case "CLASSIC":
                    if (queueType == "RANKED_SOLO_5x5" || queueType == "RANKED_FLEX_SR") return "ranked";
                    return queueType == "NORMAL" ? "normal" : null;
                case "ARAM": case "KIWI": return "aram";
                case "CHERRY": return "cherry";
                case "URF": return "urf";
                case "ONEFORALL": return "oneforall";
                case "ULTBOOK": return "ultbook";
                case "SWIFTPLAY": return queueType == "RIOTSCRIPT_BOT" ? "bot" : null;
                default: return null;
            }
        }

        internal static byte[] Bind(byte[] selection, byte[] gameflow)
        {
            return Bind(selection, gameflow, null);
        }

        internal static byte[] Bind(byte[] selection, byte[] gameflow, string sgpServerId)
        {
            if (selection == null || gameflow == null || selection.Length > 2097152 || gameflow.Length > 2097152) return null;
            try
            {
                var json = new JavaScriptSerializer { MaxJsonLength = 2097152, RecursionLimit = 60 };
                var session = json.DeserializeObject(Encoding.UTF8.GetString(selection)) as Dictionary<string, object>;
                var flow = json.DeserializeObject(Encoding.UTF8.GetString(gameflow)) as Dictionary<string, object>;
                var data = Dictionary(flow, "gameData");
                var queue = Dictionary(data, "queue");
                var mode = Text(queue, "gameMode");
                var type = Text(queue, "type");
                var custom = Text(data, "isCustomGame").Equals("True", StringComparison.OrdinalIgnoreCase) ||
                    Text(session, "isCustomGame").Equals("True", StringComparison.OrdinalIgnoreCase);
                int id;
                var group = GroupFor(mode, type, custom, sgpServerId);
                if (session == null || Text(flow, "phase") != "ChampSelect" || group == null ||
                    !int.TryParse(Text(queue, "id"), out id) || id < 0) return null;
                var role = LeagueAutoSelectGroupCatalog.RoleFor(group, sgpServerId,
                    LeagueAutoSelectProfiles.LocalAssignedRole(session));
                if (role == null) return null;
                session["autoSelectRole"] = role;
                session["queueId"] = id;
                session["gameMode"] = mode;
                session["autoSelectGroup"] = group == "aram" && LeagueQueueModePolicy.IsAramMayhem(id, mode) ? "mayhem" : group;
                session["autoSelectContext"] = id.ToString(CultureInfo.InvariantCulture) + ":" + mode + ":" +
                    type + ":" + custom + ":" + (sgpServerId ?? "") + ":" +
                    role + ":" + session["autoSelectGroup"];
                return Encoding.UTF8.GetBytes(json.Serialize(session));
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        internal static int PhaseDelay(int configured, bool secondStage, IDictionary<string, object> timer, long nowMs)
        {
            configured = Math.Max(0, Math.Min(15000, configured));
            long adjusted, internalNow, total;
            if (timer == null || Text(timer, "isInfinite").Equals("True", StringComparison.OrdinalIgnoreCase) ||
                !long.TryParse(Text(timer, "adjustedTimeLeftInPhase"), out adjusted) ||
                !long.TryParse(Text(timer, "internalNowInEpochMs"), out internalNow) || internalNow <= 0 ||
                !long.TryParse(Text(timer, "totalTimeInPhase"), out total) || total <= 0 || total > 86400000 ||
                adjusted < -86400000 || adjusted > 86400000 || internalNow < nowMs - 86400000 ||
                internalNow > nowMs + 60000) return configured;
            var remaining = adjusted - Math.Max(0, nowMs - internalNow);
            var elapsed = total - remaining;
            return (int)Math.Max(0, Math.Min(configured, Math.Min(configured * (secondStage ? 2L : 1L) - elapsed, remaining)));
        }

        internal static Dictionary<string, object> Dictionary(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        internal static string Text(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";
        }
    }
}
