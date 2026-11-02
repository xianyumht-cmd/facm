using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace FACM.League
{
    // Akari 1.5.1 auto-select-main pickConfig / banConfig, not the GGman native profile JSON.
    internal static class LeagueAkariAutoSelectTransfer
    {
        private const int MaxLength = 128 * 1024;
        private static readonly string[] SourceRoles =
            { "default", "top", "jungle", "middle", "bottom", "utility" };

        internal static List<LeagueAutoSelectProfile> Import(
            string json, IEnumerable<LeagueAutoSelectProfile> existing)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxLength)
                throw new ArgumentException("Invalid Akari auto-select transfer size.");
            var root = new JavaScriptSerializer { MaxJsonLength = MaxLength, RecursionLimit = 16 }
                .DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new ArgumentException("Invalid Akari auto-select root.");
            object picks, bans;
            var hasPicks = root.TryGetValue("pickConfig", out picks);
            var hasBans = root.TryGetValue("banConfig", out bans);
            if (!hasPicks && !hasBans)
                throw new ArgumentException("No Akari auto-select configuration found.");

            var profiles = LeagueAutoSelectProfiles.Parse(
                LeagueAutoSelectProfiles.Serialize(existing)).ToList();
            if (hasPicks)
                foreach (var row in profiles)
                {
                    row.Picks.Clear();
                    row.PickEnabled = false;
                    row.ShowPickIntent = false;
                    row.IgnoreTeammateIntent = false;
                    row.BenchAutoSwapEnabled = false;
                    row.BenchPreferHigherPriority = false;
                    row.BenchHandleTradeEnabled = false;
                }
            if (hasBans)
                foreach (var row in profiles)
                {
                    row.Bans.Clear();
                    row.BanEnabled = false;
                }
            if (hasPicks) ReadCategory(picks, true, profiles);
            if (hasBans) ReadCategory(bans, false, profiles);
            // Validate all rows and the actual native 32-KiB persisted representation
            // before returning any mutation to the editor.
            return LeagueAutoSelectProfiles.Parse(
                LeagueAutoSelectProfiles.Serialize(profiles)).ToList();
        }

        internal static string Export(IEnumerable<LeagueAutoSelectProfile> source)
        {
            var profiles = LeagueAutoSelectProfiles.Parse(
                LeagueAutoSelectProfiles.Serialize(source)).ToList();
            var picks = new SortedDictionary<string, object>(StringComparer.Ordinal);
            var bans = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var group in profiles.GroupBy(p => p.Mode, StringComparer.Ordinal))
            {
                var rows = group.ToList();
                var first = rows.FirstOrDefault(p => p.Role == "default") ?? rows[0];
                if (rows.Any(p => p.PickEnabled != first.PickEnabled ||
                    p.PickStrategy != first.PickStrategy || p.PickDelayMs != first.PickDelayMs ||
                    p.ShowPickIntent != first.ShowPickIntent ||
                    p.IgnoreTeammateIntent != first.IgnoreTeammateIntent ||
                    p.BenchAutoSwapEnabled != first.BenchAutoSwapEnabled ||
                    p.BenchPreferHigherPriority != first.BenchPreferHigherPriority ||
                    p.BenchHandleTradeEnabled != first.BenchHandleTradeEnabled ||
                    p.BenchSwapDelayMs != first.BenchSwapDelayMs ||
                    p.BanEnabled != first.BanEnabled ||
                    p.BanStrategy != first.BanStrategy || p.BanDelayMs != first.BanDelayMs))
                    throw new ArgumentException("Role-specific strategies cannot be represented by Akari group settings.");
                var pickRoles = new SortedDictionary<string, object>(StringComparer.Ordinal);
                var banRoles = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (var role in SourceRoles)
                {
                    var nativeRole = role == "utility" ? "support" : role;
                    var row = rows.FirstOrDefault(p => p.Role == nativeRole);
                    pickRoles[role] = row == null ? new int[0] : row.Picks.ToArray();
                    banRoles[role] = row == null ? new int[0] : row.Bans.ToArray();
                }
                picks[group.Key] = new
                {
                    enabled = first.PickEnabled, champions = pickRoles,
                    delaySeconds = first.PickDelayMs / 1000.0,
                    ignoreIntent = first.IgnoreTeammateIntent,
                    strategy = first.PickStrategy, showIntent = first.ShowPickIntent,
                    benchSelectFirstAvailableChampion = first.BenchPreferHigherPriority,
                    benchSwapAccumulatedDelaySeconds = first.BenchSwapDelayMs / 1000.0,
                    benchHandleTradeEnabled = first.BenchHandleTradeEnabled,
                    ggmanBenchSwapEnabled = first.BenchAutoSwapEnabled
                };
                bans[group.Key] = new
                {
                    enabled = first.BanEnabled, champions = banRoles,
                    strategy = first.BanStrategy, delaySeconds = first.BanDelayMs / 1000.0
                };
            }
            var json = new JavaScriptSerializer { MaxJsonLength = MaxLength }.Serialize(
                new Dictionary<string, object> { { "pickConfig", picks }, { "banConfig", bans } });
            if (json.Length > MaxLength)
                throw new ArgumentException("Akari auto-select export is too large.");
            return json;
        }

        private static void ReadCategory(object source, bool pick,
            List<LeagueAutoSelectProfile> profiles)
        {
            var groups = Object(source);
            if (groups.Count > 72) throw new ArgumentException("Too many Akari auto-select groups.");
            foreach (var group in groups)
            {
                if (!LeagueAutoSelectProfiles.Modes.Contains(group.Key, StringComparer.Ordinal) &&
                    !LeagueAutoSelectGroupCatalog.RemoteGroups.Any(g => g.Item1 == group.Key))
                    throw new ArgumentException("Unknown or unverified Akari auto-select group.");
                var fields = Object(group.Value);
                var positions = Object(Field(fields, "champions"));
                if (positions.Keys.Any(k => !SourceRoles.Contains(k, StringComparer.Ordinal)))
                    throw new ArgumentException("Unsupported Akari auto-select position.");
                var lists = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                foreach (var role in SourceRoles)
                    lists[role == "utility" ? "support" : role] =
                        Ids(Field(positions, role));
                if (lists.Any(x => x.Key != "default" && x.Value.Count > 0) &&
                    group.Key != "rift" && group.Key != "ranked" && group.Key != "custom" &&
                    !LeagueAutoSelectGroupCatalog.SupportsRoles(group.Key))
                    throw new ArgumentException("Akari group positions cannot be represented natively.");

                var enabled = Boolean(fields, "enabled");
                var strategy = Text(fields, "strategy");
                if (!LeagueAutoSelectProfiles.Strategies.Contains(strategy, StringComparer.Ordinal))
                    throw new ArgumentException("Unsupported Akari auto-select strategy.");
                var delay = Milliseconds(fields, "delaySeconds");
                var showIntent = pick && Boolean(fields, "showIntent");
                var ignoreIntent = pick && Boolean(fields, "ignoreIntent");
                var benchUpgrade = pick && Boolean(fields, "benchSelectFirstAvailableChampion");
                var benchTrade = pick && Boolean(fields, "benchHandleTradeEnabled");
                var benchDelay = pick ? Milliseconds(fields, "benchSwapAccumulatedDelaySeconds") : 0;
                object rawBench = null;
                var benchEnabled = pick && (fields.TryGetValue("ggmanBenchSwapEnabled", out rawBench)
                    ? rawBench is bool && (bool)rawBench
                    : enabled && (group.Key == "aram" || group.Key == "mayhem" ||
                        LeagueAutoSelectGroupCatalog.SupportsBench(group.Key)));
                if (pick && fields.ContainsKey("ggmanBenchSwapEnabled") && !(rawBench is bool))
                    throw new ArgumentException("Invalid bench enable flag.");

                var roles = new HashSet<string>(
                    profiles.Where(p => p.Mode == group.Key).Select(p => p.Role), StringComparer.Ordinal)
                    { "default" };
                foreach (var entry in lists)
                    if (entry.Value.Count > 0) roles.Add(entry.Key);
                foreach (var role in roles)
                {
                    var row = profiles.FirstOrDefault(p => p.Mode == group.Key && p.Role == role);
                    if (row == null)
                    {
                        var template = profiles.FirstOrDefault(p => p.Mode == group.Key);
                        row = template == null
                            ? new LeagueAutoSelectProfile { Mode = group.Key, Role = role,
                                PickEnabled = false, BanEnabled = false }
                            : CloneForRole(template, role);
                        profiles.Add(row);
                    }
                    if (pick)
                    {
                        row.Picks = lists[role].ToList();
                        row.PickEnabled = enabled;
                        row.PickStrategy = strategy;
                        row.PickDelayMs = delay;
                        row.ShowPickIntent = showIntent;
                        row.IgnoreTeammateIntent = ignoreIntent;
                        row.BenchAutoSwapEnabled = benchEnabled;
                        row.BenchPreferHigherPriority = benchUpgrade;
                        row.BenchHandleTradeEnabled = benchTrade;
                        row.BenchSwapDelayMs = benchDelay;
                    }
                    else
                    {
                        row.Bans = lists[role].ToList();
                        row.BanEnabled = enabled;
                        row.BanStrategy = strategy;
                        row.BanDelayMs = delay;
                    }
                }
            }
        }

        private static LeagueAutoSelectProfile CloneForRole(LeagueAutoSelectProfile row, string role)
        {
            return new LeagueAutoSelectProfile
            {
                Mode = row.Mode, Role = role,
                PickEnabled = row.PickEnabled, BanEnabled = row.BanEnabled,
                PickStrategy = row.PickStrategy, BanStrategy = row.BanStrategy,
                PickDelayMs = row.PickDelayMs, BanDelayMs = row.BanDelayMs,
                ShowPickIntent = row.ShowPickIntent,
                IgnoreTeammateIntent = row.IgnoreTeammateIntent,
                BenchAutoSwapEnabled = row.BenchAutoSwapEnabled,
                BenchPreferHigherPriority = row.BenchPreferHigherPriority,
                BenchHandleTradeEnabled = row.BenchHandleTradeEnabled,
                BenchSwapDelayMs = row.BenchSwapDelayMs
            };
        }

        private static Dictionary<string, object> Object(object value)
        {
            var result = value as Dictionary<string, object>;
            if (result == null) throw new ArgumentException("Expected Akari auto-select object.");
            return result;
        }

        private static object Field(Dictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value))
                throw new ArgumentException("Missing Akari auto-select field.");
            return value;
        }

        private static bool Boolean(Dictionary<string, object> source, string key)
        {
            var value = Field(source, key);
            if (!(value is bool)) throw new ArgumentException("Invalid Akari boolean.");
            return (bool)value;
        }

        private static string Text(Dictionary<string, object> source, string key)
        {
            var value = Field(source, key) as string;
            if (value == null) throw new ArgumentException("Invalid Akari strategy.");
            return value;
        }

        private static int Milliseconds(Dictionary<string, object> source, string key)
        {
            var value = Field(source, key);
            if (!(value is int) && !(value is long) && !(value is double) && !(value is decimal))
                throw new ArgumentException("Invalid Akari delay.");
            var seconds = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 15)
                throw new ArgumentException("Akari delay is out of range.");
            return (int)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
        }

        private static List<int> Ids(object value)
        {
            var array = value as object[];
            if (array == null || array.Length > 20)
                throw new ArgumentException("Invalid Akari champion priority list.");
            var result = new List<int>();
            foreach (var item in array)
            {
                if (!(item is int) && !(item is long))
                    throw new ArgumentException("Invalid Akari champion ID.");
                var id = Convert.ToInt64(item, CultureInfo.InvariantCulture);
                if (id < -3 || id > 10000 || id == 0 || id == -2 ||
                    result.Contains((int)id))
                    throw new ArgumentException("Invalid or duplicate Akari champion ID.");
                result.Add((int)id);
            }
            return result;
        }
    }
}
