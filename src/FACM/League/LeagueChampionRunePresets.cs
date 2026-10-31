using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal sealed class LeagueChampionRunePreset
    {
        public int ChampionId { get; set; }
        public string Group { get; set; }
        public int PrimaryStyleId { get; set; }
        public int SecondaryStyleId { get; set; }
        public List<int> SelectedPerkIds { get; set; } = new List<int>();
    }

    internal static class LeagueChampionRunePresets
    {
        // AppSettingsRecovery bounds each complete key/value line to 8192 characters.
        private const int MaxLength = 7500;
        private const int MaxPresets = 32;

        internal static IReadOnlyList<LeagueChampionRunePreset> Parse(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return Array.Empty<LeagueChampionRunePreset>();
            if (source.Length > MaxLength) return Array.Empty<LeagueChampionRunePreset>();
            try
            {
                var rows = new JavaScriptSerializer { MaxJsonLength = MaxLength }
                    .Deserialize<List<LeagueChampionRunePreset>>(source);
                if (rows == null || rows.Count > MaxPresets || rows.Any(row => !Valid(row)) ||
                    rows.Select(Key).Distinct(StringComparer.Ordinal).Count() != rows.Count)
                    return Array.Empty<LeagueChampionRunePreset>();
                return rows;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (InvalidCastException) { }
            catch (FormatException) { }
            catch (OverflowException) { }
            return Array.Empty<LeagueChampionRunePreset>();
        }

        internal static string Serialize(IEnumerable<LeagueChampionRunePreset> presets)
        {
            var rows = presets == null ? new List<LeagueChampionRunePreset>() : presets.ToList();
            if (rows.Count > MaxPresets || rows.Any(row => !Valid(row)) ||
                rows.Select(Key).Distinct(StringComparer.Ordinal).Count() != rows.Count)
                throw new ArgumentException("Invalid champion rune presets.");
            var json = new JavaScriptSerializer().Serialize(rows
                .OrderBy(row => row.ChampionId).ThenBy(row => row.Group, StringComparer.Ordinal).ToList());
            if (json.Length > MaxLength) throw new ArgumentException("Champion rune presets are too large.");
            return json;
        }

        internal static string Upsert(string source, LeagueChampionRunePreset preset)
        {
            if (!Valid(preset)) throw new ArgumentException("Invalid champion rune preset.");
            var rows = Parse(source).ToList();
            rows.RemoveAll(row => Key(row) == Key(preset));
            rows.Add(preset);
            return Serialize(rows);
        }

        internal static string Remove(string source, int championId, string group)
        {
            return Serialize(Parse(source).Where(row => row.ChampionId != championId || row.Group != group));
        }

        internal static LeagueChampionRunePreset Resolve(string source, int championId, string group)
        {
            if (championId <= 0 || string.IsNullOrWhiteSpace(group)) return null;
            var rows = Parse(source);
            var exact = rows.FirstOrDefault(row => row.ChampionId == championId && row.Group == group);
            if (exact != null) return exact;
            return group.StartsWith("ranked-", StringComparison.Ordinal) ?
                rows.FirstOrDefault(row => row.ChampionId == championId && row.Group == "ranked-default") : null;
        }

        private static bool Valid(LeagueChampionRunePreset row)
        {
            if (row == null || row.ChampionId <= 0 || row.ChampionId > 10000 ||
                row.PrimaryStyleId <= 0 || row.SecondaryStyleId <= 0 ||
                row.PrimaryStyleId > 100000 || row.SecondaryStyleId > 100000 ||
                row.SelectedPerkIds == null || row.SelectedPerkIds.Count != 9 ||
                row.SelectedPerkIds.Any(id => id <= 0 || id > 100000)) return false;
            return LeagueChampionSpellPresets.ValidGroup(row.Group);
        }

        private static string Key(LeagueChampionRunePreset row)
        {
            return row.ChampionId + ":" + row.Group;
        }
    }
}
