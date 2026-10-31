using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal sealed class LeagueRuneOption
    {
        internal int Id { get; set; }
        internal string Name { get; set; }
        internal HashSet<string> GameModes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal bool SupportsGroup(string group)
        {
            if (GameModes.Count == 0) return true;
            var mode = group != null && group.StartsWith("ranked-", StringComparison.Ordinal)
                || string.Equals(group, "normal", StringComparison.Ordinal) ? "CLASSIC" :
                string.Equals(group, "aram", StringComparison.Ordinal) ? "ARAM" :
                string.Equals(group, "urf", StringComparison.Ordinal) ? "URF" :
                string.Equals(group, "nexusblitz", StringComparison.Ordinal) ? "NEXUSBLITZ" :
                string.Equals(group, "ultbook", StringComparison.Ordinal) ? "ULTBOOK" : null;
            return mode != null && GameModes.Contains(mode);
        }
        public override string ToString() { return Name + " (" + Id.ToString(CultureInfo.InvariantCulture) + ")"; }
    }

    internal sealed class LeagueRuneStyle
    {
        internal int Id { get; set; }
        internal string Name { get; set; }
        internal List<List<int>> Slots { get; set; }
        internal List<List<int>> StatSlots { get; set; }
        internal HashSet<int> AllowedSubStyles { get; set; }
        public override string ToString() { return Name; }
    }

    // One bounded, read-only snapshot of the current client's perkstyles and perk names.
    // The editor never accepts an arbitrary rune ID or invents an unavailable slot.
    internal sealed class LeagueRuneCatalog
    {
        private readonly Dictionary<int, string> _names;
        internal List<LeagueRuneStyle> Styles { get; private set; }

        private LeagueRuneCatalog(List<LeagueRuneStyle> styles, Dictionary<int, string> names)
        {
            Styles = styles;
            _names = names;
        }

        internal static IReadOnlyList<LeagueRuneOption> ParseSummonerSpells(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 256 * 1024)
                return Array.Empty<LeagueRuneOption>();
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 };
                var rows = Values(serializer.DeserializeObject(Encoding.UTF8.GetString(bytes))).ToArray();
                if (rows.Length == 0 || rows.Length > 100) return Array.Empty<LeagueRuneOption>();
                var result = new List<LeagueRuneOption>();
                foreach (var entry in rows)
                {
                    var data = entry as IDictionary<string, object>;
                    var id = Number(Get(data, "id"));
                    var name = Get(data, "name") as string;
                    if (id <= 0) continue;
                    if (id > 1000 || string.IsNullOrWhiteSpace(name) ||
                        name.Length > 120 || result.Any(option => option.Id == id))
                        return Array.Empty<LeagueRuneOption>();
                    var gameModes = Values(Get(data, "gameModes")).Select(value =>
                        value as string).ToArray();
                    if (gameModes.Length > 32 || gameModes.Any(mode =>
                        string.IsNullOrWhiteSpace(mode) || mode.Length > 32 ||
                        mode.Any(ch => !(ch >= 'A' && ch <= 'Z') && !(ch >= '0' && ch <= '9') && ch != '_')))
                        return Array.Empty<LeagueRuneOption>();
                    result.Add(new LeagueRuneOption
                    {
                        Id = id, Name = name,
                        GameModes = new HashSet<string>(gameModes, StringComparer.OrdinalIgnoreCase)
                    });
                }
                return result.Count < 2 ? Array.Empty<LeagueRuneOption>() :
                    result.OrderBy(option => option.Name, StringComparer.CurrentCulture).ToArray();
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (InvalidCastException) { }
            catch (FormatException) { }
            catch (OverflowException) { }
            return Array.Empty<LeagueRuneOption>();
        }

        internal static IReadOnlyList<LeagueRuneOption> ParseChampions(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 1024 * 1024)
                return Array.Empty<LeagueRuneOption>();
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
                var rows = Values(serializer.DeserializeObject(Encoding.UTF8.GetString(bytes))).ToArray();
                if (rows.Length == 0 || rows.Length > 1000) return Array.Empty<LeagueRuneOption>();
                var result = new List<LeagueRuneOption>();
                foreach (var entry in rows)
                {
                    var data = entry as IDictionary<string, object>;
                    var id = Number(Get(data, "id"));
                    var name = Get(data, "name") as string;
                    // LCU includes a synthetic "None" champion (ID -1) on some patches.
                    if (id <= 0) continue;
                    if (id > 10000 || string.IsNullOrWhiteSpace(name) ||
                        name.Length > 120 || result.Any(option => option.Id == id))
                        return Array.Empty<LeagueRuneOption>();
                    result.Add(new LeagueRuneOption { Id = id, Name = name });
                }
                return result.Count == 0 ? Array.Empty<LeagueRuneOption>() :
                    result.OrderBy(option => option.Name, StringComparer.CurrentCulture).ToArray();
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (InvalidCastException) { }
            catch (FormatException) { }
            catch (OverflowException) { }
            return Array.Empty<LeagueRuneOption>();
        }

        internal static LeagueRuneCatalog Parse(byte[] stylesBytes, byte[] perksBytes)
        {
            if (stylesBytes == null || perksBytes == null || stylesBytes.Length == 0 ||
                perksBytes.Length == 0 || stylesBytes.Length > 2 * 1024 * 1024 ||
                perksBytes.Length > 2 * 1024 * 1024) return null;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
                var root = serializer.DeserializeObject(Encoding.UTF8.GetString(stylesBytes)) as IDictionary<string, object>;
                if (Number(Get(root, "schemaVersion")) != 2) return null;
                var rows = Values(Get(root, "styles")).ToArray();
                var perks = Values(serializer.DeserializeObject(Encoding.UTF8.GetString(perksBytes))).ToArray();
                if (rows.Length == 0 || rows.Length > 16 || perks.Length == 0 || perks.Length > 2000)
                    return null;

                var names = new Dictionary<int, string>();
                foreach (var entry in perks)
                {
                    var perk = entry as IDictionary<string, object>;
                    var id = Number(Get(perk, "id"));
                    var name = Get(perk, "name") as string;
                    if (id > 0 && id <= 100000 && !string.IsNullOrWhiteSpace(name) &&
                        name.Length <= 120 && !names.ContainsKey(id))
                        names.Add(id, name);
                }

                var styles = new List<LeagueRuneStyle>();
                foreach (var entry in rows)
                {
                    var style = entry as IDictionary<string, object>;
                    var id = Number(Get(style, "id"));
                    var name = Get(style, "name") as string;
                    var typedSlots = Values(Get(style, "slots")).Select(slot =>
                    {
                        var data = slot as IDictionary<string, object>;
                        return Tuple.Create(Get(data, "type") as string,
                            Values(Get(data, "perks")).Select(Number).ToList());
                    }).ToList();
                    var keyStones = typedSlots.Where(slot => slot.Item1 == "kKeyStone")
                        .Select(slot => slot.Item2).ToList();
                    var regular = typedSlots.Where(slot => slot.Item1 == "kMixedRegularSplashable")
                        .Select(slot => slot.Item2).ToList();
                    var statMods = typedSlots.Where(slot => slot.Item1 == "kStatMod")
                        .Select(slot => slot.Item2).ToList();
                    var slots = keyStones.Concat(regular).ToList();
                    var allowed = Values(Get(style, "allowedSubStyles")).Select(Number).ToArray();
                    if (id <= 0 || id > 100000 || string.IsNullOrWhiteSpace(name) ||
                        name.Length > 120 || typedSlots.Count != 7 ||
                        keyStones.Count != 1 || regular.Count != 3 || statMods.Count != 3 ||
                        typedSlots.Any(slot => slot.Item2.Count == 0 || slot.Item2.Count > 32 ||
                            slot.Item2.Any(perk => perk <= 0 || !names.ContainsKey(perk))) ||
                        allowed.Length == 0 || allowed.Length > 16 ||
                        allowed.Any(other => other <= 0 || other > 100000) ||
                        styles.Any(previous => previous.Id == id))
                        return null;
                    styles.Add(new LeagueRuneStyle
                    {
                        Id = id, Name = name, Slots = slots, StatSlots = statMods,
                        AllowedSubStyles = new HashSet<int>(allowed)
                    });
                }
                if (styles.Any(style => !style.AllowedSubStyles.Any(other =>
                    other != style.Id && styles.Any(row => row.Id == other))))
                    return null;
                return new LeagueRuneCatalog(styles, names);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (InvalidCastException) { }
            catch (FormatException) { }
            catch (OverflowException) { }
            return null;
        }

        internal List<LeagueRuneOption> Choices(IEnumerable<int> ids)
        {
            return ids.Where(id => _names.ContainsKey(id)).Distinct().Select(id =>
                new LeagueRuneOption { Id = id, Name = _names[id] }).ToList();
        }

        internal List<LeagueRuneOption> StatChoices(int primaryStyleId, int row)
        {
            var style = Style(primaryStyleId);
            if (style == null || row < 0 || row >= style.StatSlots.Count)
                return new List<LeagueRuneOption>();
            return Choices(style.StatSlots[row]);
        }

        internal LeagueRuneStyle Style(int id) { return Styles.FirstOrDefault(style => style.Id == id); }

        internal bool Valid(LeagueChampionRunePreset preset)
        {
            if (preset == null || preset.SelectedPerkIds == null || preset.SelectedPerkIds.Count != 9)
                return false;
            var primary = Style(preset.PrimaryStyleId);
            var secondary = Style(preset.SecondaryStyleId);
            if (primary == null || secondary == null || primary.Id == secondary.Id ||
                !primary.AllowedSubStyles.Contains(secondary.Id)) return false;
            for (var i = 0; i < 4; i++)
                if (!primary.Slots[i].Contains(preset.SelectedPerkIds[i])) return false;

            // Secondary runes come from two different non-keystone rows.
            var firstRow = -1;
            var secondRow = -1;
            for (var row = 1; row < Math.Min(4, secondary.Slots.Count); row++)
            {
                if (secondary.Slots[row].Contains(preset.SelectedPerkIds[4])) firstRow = row;
                if (secondary.Slots[row].Contains(preset.SelectedPerkIds[5])) secondRow = row;
            }
            if (firstRow < 0 || secondRow < 0 || firstRow == secondRow) return false;
            for (var row = 0; row < 3; row++)
                if (!StatChoices(primary.Id, row).Any(item => item.Id == preset.SelectedPerkIds[row + 6]))
                    return false;
            return true;
        }

        private static object Get(IDictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value : null;
        }

        private static IEnumerable<object> Values(object value)
        {
            return value is object[] ? (object[])value : Enumerable.Empty<object>();
        }

        private static int Number(object value)
        {
            if (value == null) return 0;
            int number;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : 0;
        }
    }
}
