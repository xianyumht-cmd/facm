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
        public override string ToString() { return Name + " (" + Id.ToString(CultureInfo.InvariantCulture) + ")"; }
    }

    internal sealed class LeagueRuneStyle
    {
        internal int Id { get; set; }
        internal string Name { get; set; }
        internal List<List<int>> Slots { get; set; }
        internal HashSet<int> AllowedSubStyles { get; set; }
        public override string ToString() { return Name; }
    }

    // One bounded, read-only snapshot of the current client's perkstyles and perk names.
    // The editor never accepts an arbitrary rune ID or invents an unavailable slot.
    internal sealed class LeagueRuneCatalog
    {
        private static readonly int[][] StatModRows =
        {
            new[] { 5008, 5005, 5007 },
            new[] { 5008, 5002, 5003 },
            new[] { 5001, 5011, 5013 }
        };
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
                    result.Add(new LeagueRuneOption { Id = id, Name = name });
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
                    var slots = Values(Get(style, "slots")).Select(slot =>
                    {
                        var data = slot as IDictionary<string, object>;
                        return Values(Get(data, "perks")).Select(Number).ToList();
                    }).ToList();
                    var allowed = Values(Get(style, "allowedSubStyles")).Select(Number).ToArray();
                    if (id <= 0 || id > 100000 || string.IsNullOrWhiteSpace(name) ||
                        name.Length > 120 || slots.Count < 4 || slots.Count > 8 ||
                        slots.Any(slot => slot.Count == 0 || slot.Count > 32 ||
                            slot.Any(perk => perk <= 0 || !names.ContainsKey(perk))) ||
                        allowed.Length == 0 || allowed.Length > 16 ||
                        allowed.Any(other => other <= 0 || other > 100000) ||
                        styles.Any(previous => previous.Id == id))
                        return null;
                    styles.Add(new LeagueRuneStyle
                    {
                        Id = id, Name = name, Slots = slots,
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

        internal List<LeagueRuneOption> StatChoices(int row)
        {
            if (row < 0 || row >= StatModRows.Length) return new List<LeagueRuneOption>();
            return Choices(StatModRows[row]);
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
                if (!StatChoices(row).Any(item => item.Id == preset.SelectedPerkIds[row + 6]))
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
