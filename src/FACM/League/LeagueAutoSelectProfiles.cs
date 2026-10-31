using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal sealed class LeagueAutoSelectProfile
    {
        public string Mode { get; set; } = "default";
        public string Role { get; set; } = "default";
        public bool PickEnabled { get; set; } = true;
        public bool BanEnabled { get; set; } = true;
        public List<int> Picks { get; set; } = new List<int>();
        public List<int> Bans { get; set; } = new List<int>();
        public string PickStrategy { get; set; } = "lock-in-immediately";
        public bool ShowPickIntent { get; set; }
        public bool IgnoreTeammateIntent { get; set; }
        public string BanStrategy { get; set; } = "lock-in-immediately";
        public int PickDelayMs { get; set; }
        public int BanDelayMs { get; set; }
        public bool BenchAutoSwapEnabled { get; set; }
        public bool BenchPreferHigherPriority { get; set; }
        public bool BenchHandleTradeEnabled { get; set; }
        public int BenchSwapDelayMs { get; set; } = 2900;
    }

    internal static class LeagueAutoSelectProfiles
    {
        internal static readonly string[] Modes = { "default", "rift", "aram", "mayhem", "ranked", "normal", "cherry", "urf", "oneforall", "ultbook", "bot", "custom" };
        internal static readonly string[] Roles = { "default", "top", "jungle", "middle", "bottom", "support" };
        internal static readonly string[] Strategies = { "lock-in-immediately", "show-and-lock-in", "just-show" };
        internal const int MaxSerializedLength = 32768;

        internal static IReadOnlyList<LeagueAutoSelectProfile> Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<LeagueAutoSelectProfile>();
            if (value.Length > MaxSerializedLength) return Array.Empty<LeagueAutoSelectProfile>();
            try
            {
                var parsed = new JavaScriptSerializer { MaxJsonLength = MaxSerializedLength }
                    .Deserialize<List<LeagueAutoSelectProfile>>(value);
                if (parsed == null || parsed.Count > 72) return Array.Empty<LeagueAutoSelectProfile>();
                var unique = new HashSet<string>(StringComparer.Ordinal);
                foreach (var profile in parsed)
                {
                    if (!Valid(profile) || !unique.Add(profile.Mode + ":" + profile.Role))
                        return Array.Empty<LeagueAutoSelectProfile>();
                }
                return parsed;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (InvalidCastException) { }
            catch (FormatException) { }
            catch (OverflowException) { }
            return Array.Empty<LeagueAutoSelectProfile>();
        }

        internal static string Serialize(IEnumerable<LeagueAutoSelectProfile> profiles)
        {
            var copy = profiles == null ? new List<LeagueAutoSelectProfile>() :
                profiles.ToList();
            if (copy.Count > 72 || copy.Any(p => !Valid(p)) ||
                copy.Select(p => p.Mode + ":" + p.Role).Distinct(StringComparer.Ordinal).Count() != copy.Count)
                throw new ArgumentException("Invalid automatic-selection profile configuration.");
            copy = copy.OrderBy(p => Array.IndexOf(Modes, p.Mode))
                .ThenBy(p => Array.IndexOf(Roles, p.Role)).ToList();
            var encoded = new JavaScriptSerializer().Serialize(copy);
            if (encoded.Length > MaxSerializedLength)
                throw new ArgumentException("Automatic-selection profile configuration is too large.");
            return encoded;
        }

        internal static string ModeFor(int queueId, string gameMode)
        {
            if (LeagueQueueModePolicy.IsAramMayhem(queueId, gameMode)) return "mayhem";
            if (LeagueQueueModePolicy.IsBaseAram(queueId, gameMode)) return "aram";
            if (queueId == 400 || queueId == 420 || queueId == 430 || queueId == 440 ||
                string.Equals(gameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase)) return "rift";
            return "default";
        }

        internal static string LocalAssignedRole(IDictionary<string, object> session)
        {
            if (session == null) return null;
            object local, members;
            if (!session.TryGetValue("localPlayerCellId", out local) ||
                !session.TryGetValue("myTeam", out members)) return null;
            int cellId;
            try { cellId = Convert.ToInt32(local); }
            catch (FormatException) { return null; }
            catch (InvalidCastException) { return null; }
            catch (OverflowException) { return null; }
            var team = members as object[];
            if (team == null) return null;
            foreach (var value in team)
            {
                var player = value as Dictionary<string, object>;
                object cell, assigned;
                int current;
                if (player == null || !player.TryGetValue("cellId", out cell) ||
                    !int.TryParse(Convert.ToString(cell), out current) || current != cellId ||
                    !player.TryGetValue("assignedPosition", out assigned)) continue;
                switch ((Convert.ToString(assigned) ?? string.Empty).Trim().ToUpperInvariant())
                {
                    case "TOP": return "top";
                    case "JUNGLE": case "JG": return "jungle";
                    case "MIDDLE": case "MID": return "middle";
                    case "BOTTOM": case "BOT": case "ADC": return "bottom";
                    case "UTILITY": case "SUPPORT": case "SUP": return "support";
                    default: return null;
                }
            }
            return null;
        }

        internal static List<int> Candidates(IReadOnlyList<LeagueAutoSelectProfile> profiles,
            string mode, string role, bool pick, int legacyDefault)
        {
            var ordered = new List<int>();
            var keys = ProfileKeys(mode, role);
            foreach (var key in keys.Distinct(StringComparer.Ordinal))
            {
                var profile = profiles == null ? null : profiles.FirstOrDefault(p => p.Mode + ":" + p.Role == key);
                if (profile == null) continue;
                foreach (var id in pick ? profile.Picks : profile.Bans)
                    if (!ordered.Contains(id)) ordered.Add(id);
            }
            if (legacyDefault > 0 && !ordered.Contains(legacyDefault)) ordered.Add(legacyDefault);
            return ordered;
        }

        internal static LeagueAutoSelectProfile ResolveStrategy(IReadOnlyList<LeagueAutoSelectProfile> profiles,
            string mode, string role)
        {
            var keys = ProfileKeys(mode, role);
            foreach (var key in keys.Distinct(StringComparer.Ordinal))
            {
                var match = profiles == null ? null : profiles.FirstOrDefault(p => p.Mode + ":" + p.Role == key);
                if (match != null) return match;
            }
            return new LeagueAutoSelectProfile();
        }

        private static IEnumerable<string> ProfileKeys(string mode, string role)
        {
            mode = mode ?? "default";
            role = role ?? "default";
            if (mode != "rift" && mode != "ranked" && mode != "custom") role = "default";
            yield return mode + ":" + role;
            yield return mode + ":default";
            if (mode == "ranked" || mode == "normal")
            {
                yield return "rift:" + role;
                yield return "rift:default";
            }
            if (mode == "mayhem") yield return "aram:default";
            yield return "default:default";
        }

        private static bool Valid(LeagueAutoSelectProfile profile)
        {
            return profile != null && Modes.Contains(profile.Mode, StringComparer.Ordinal) &&
                Roles.Contains(profile.Role, StringComparer.Ordinal) &&
                Strategies.Contains(profile.PickStrategy, StringComparer.Ordinal) &&
                Strategies.Contains(profile.BanStrategy, StringComparer.Ordinal) &&
                profile.PickDelayMs >= 0 && profile.PickDelayMs <= 15000 &&
                profile.BanDelayMs >= 0 && profile.BanDelayMs <= 15000 &&
                profile.BenchSwapDelayMs >= 0 && profile.BenchSwapDelayMs <= 15000 &&
                ValidIds(profile.Picks, profile.Mode == "cherry" ? -3 : 0) &&
                ValidIds(profile.Bans, -1);
        }

        private static bool ValidIds(List<int> ids, int specialId)
        {
            return ids != null && ids.Count <= 20 &&
                ids.All(id => id > 0 && id <= 10000 || specialId != 0 && id == specialId) &&
                ids.Distinct().Count() == ids.Count;
        }
    }
}
