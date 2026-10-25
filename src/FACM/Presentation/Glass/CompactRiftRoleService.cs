using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.Presentation.Glass
{
    // Pure projection from a local ChampSelect snapshot. No player identifiers or raw
    // response bodies leave the client, and no role is inferred from champion choice.
    internal static class CompactRiftRoleService
    {
        internal static string Normalize(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "top": return "top";
                case "jungle": case "jg": return "jungle";
                case "middle": case "mid": return "middle";
                case "bottom": case "bot": case "adc": return "bottom";
                case "utility": case "support": case "sup": return "support";
                default: return null;
            }
        }

        internal static string DisplayName(string role)
        {
            switch (Normalize(role))
            {
                case "top": return "上路";
                case "jungle": return "打野";
                case "middle": return "中路";
                case "bottom": return "下路";
                case "support": return "辅助";
                default: return null;
            }
        }

        internal static string FromChampSelect(IDictionary<string, object> session)
        {
            if (session == null) return null;
            object cellValue, membersValue;
            if (!session.TryGetValue("localPlayerCellId", out cellValue) ||
                !session.TryGetValue("myTeam", out membersValue))
                return null;
            int cell;
            if (!int.TryParse(Convert.ToString(cellValue), out cell)) return null;
            var members = membersValue as object[];
            if (members == null) return null;
            foreach (var entry in members)
            {
                var member = entry as IDictionary<string, object>;
                if (member == null) continue;
                object index;
                int parsed;
                if (!member.TryGetValue("cellId", out index) ||
                    !int.TryParse(Convert.ToString(index), out parsed) || parsed != cell)
                    continue;
                object assigned;
                return member.TryGetValue("assignedPosition", out assigned) ?
                    Normalize(Convert.ToString(assigned)) : null;
            }
            return null;
        }

        internal static void ValidateForSmokeTest()
        {
            var roles = new[] { "top", "jungle", "middle", "bottom", "utility" };
            var canonical = new[] { "top", "jungle", "middle", "bottom", "support" };
            for (int i = 0; i < roles.Length; i++)
                if (Normalize(roles[i]) != canonical[i] ||
                    string.IsNullOrWhiteSpace(DisplayName(canonical[i])))
                    throw new InvalidOperationException("Rift role normalization rejected a known position.");
            if (Normalize("fill") != null || Normalize("any") != null ||
                Normalize("ARAM") != null)
                throw new InvalidOperationException("Rift role projection guessed an unspecified role.");
            var session = new Dictionary<string, object> {
                { "localPlayerCellId", 3 },
                { "myTeam", new object[] {
                    new Dictionary<string, object> {
                        { "cellId", 2 }, { "assignedPosition", "TOP" } },
                    new Dictionary<string, object> {
                        { "cellId", 3 }, { "assignedPosition", "MIDDLE" } }
                } }
            };
            if (FromChampSelect(session) != "middle")
                throw new InvalidOperationException("Rift role was read from another player's cell.");
            session["localPlayerCellId"] = 7;
            if (FromChampSelect(session) != null)
                throw new InvalidOperationException("Missing local cell silently adopted a teammate role.");
            session.Remove("localPlayerCellId");
            if (FromChampSelect(session) != null)
                throw new InvalidOperationException("Missing local identity silently assigned a role.");
        }
    }
}
