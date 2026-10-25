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
        public List<int> Picks { get; set; } = new List<int>();
        public List<int> Bans { get; set; } = new List<int>();
        public string PickStrategy { get; set; } = "lock-in-immediately";
        public string BanStrategy { get; set; } = "lock-in-immediately";
        public int PickDelayMs { get; set; }
        public int BanDelayMs { get; set; }
    }

    internal static class LeagueAutoSelectProfiles
    {
        internal static readonly string[] Modes = { "default", "rift", "aram", "mayhem" };
        internal static readonly string[] Roles = { "default", "top", "jungle", "middle", "bottom", "support" };
        internal static readonly string[] Strategies = { "lock-in-immediately", "show-and-lock-in", "just-show" };
        private const int MaxSerializedLength = 8192;

        internal static IReadOnlyList<LeagueAutoSelectProfile> Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<LeagueAutoSelectProfile>();
            if (value.Length > MaxSerializedLength) return Array.Empty<LeagueAutoSelectProfile>();
            try
            {
                var parsed = new JavaScriptSerializer { MaxJsonLength = MaxSerializedLength }
                    .Deserialize<List<LeagueAutoSelectProfile>>(value);
                if (parsed == null || parsed.Count > 24) return Array.Empty<LeagueAutoSelectProfile>();
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
            if (copy.Count > 24 || copy.Any(p => !Valid(p)) ||
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

        internal static List<int> Candidates(IReadOnlyList<LeagueAutoSelectProfile> profiles,
            string mode, string role, bool pick, int legacyDefault)
        {
            var ordered = new List<int>();
            var keys = new[] { (mode ?? "default") + ":" + (role ?? "default"),
                (mode ?? "default") + ":default", "default:default" };
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
            var keys = new[] { (mode ?? "default") + ":" + (role ?? "default"),
                (mode ?? "default") + ":default", "default:default" };
            foreach (var key in keys.Distinct(StringComparer.Ordinal))
            {
                var match = profiles == null ? null : profiles.FirstOrDefault(p => p.Mode + ":" + p.Role == key);
                if (match != null) return match;
            }
            return new LeagueAutoSelectProfile();
        }

        private static bool Valid(LeagueAutoSelectProfile profile)
        {
            return profile != null && Modes.Contains(profile.Mode, StringComparer.Ordinal) &&
                Roles.Contains(profile.Role, StringComparer.Ordinal) &&
                Strategies.Contains(profile.PickStrategy, StringComparer.Ordinal) &&
                Strategies.Contains(profile.BanStrategy, StringComparer.Ordinal) &&
                profile.PickDelayMs >= 0 && profile.PickDelayMs <= 15000 &&
                profile.BanDelayMs >= 0 && profile.BanDelayMs <= 15000 &&
                ValidIds(profile.Picks) && ValidIds(profile.Bans);
        }

        private static bool ValidIds(List<int> ids)
        {
            return ids != null && ids.Count <= 20 && ids.All(id => id > 0 && id <= 10000) &&
                ids.Distinct().Count() == ids.Count;
        }
    }
}
