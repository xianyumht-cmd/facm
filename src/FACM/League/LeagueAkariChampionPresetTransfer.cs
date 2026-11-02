using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal sealed class LeagueAkariChampionPresetBundle
    {
        internal bool HasRunes;
        internal bool HasSpells;
        internal string Runes;
        internal string Spells;
        internal int RuneCount;
        internal int SpellCount;
    }

    // Akari v1.5.1 auto-champ-config/state.ts: champion ID -> mode/role -> config.
    // Clipboard-only transfer. Never imports account, session or LCU write state.
    internal static class LeagueAkariChampionPresetTransfer
    {
        private const int MaxClipboardLength = 128 * 1024;
        private const int MaxRows = 256;

        internal static LeagueAkariChampionPresetBundle Import(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxClipboardLength)
                throw new ArgumentException("Invalid champion preset transfer size.");
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = MaxClipboardLength, RecursionLimit = 16 }
                    .DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) throw new ArgumentException("Invalid champion preset transfer root.");
                object rawRunes, rawSpells;
                var hasRunes = root.TryGetValue("runesV2", out rawRunes);
                var hasSpells = root.TryGetValue("summonerSpells", out rawSpells);
                if (!hasRunes && !hasSpells)
                    throw new ArgumentException("No Akari champion presets found.");
                var runes = hasRunes ? ReadRunes(rawRunes) : new List<LeagueChampionRunePreset>();
                var spells = hasSpells ? ReadSpells(rawSpells) : new List<LeagueChampionSpellPreset>();
                return new LeagueAkariChampionPresetBundle
                {
                    HasRunes = hasRunes, HasSpells = hasSpells,
                    Runes = hasRunes ? LeagueChampionRunePresets.Serialize(runes) : null,
                    Spells = hasSpells ? LeagueChampionSpellPresets.Serialize(spells) : null,
                    RuneCount = runes.Count, SpellCount = spells.Count
                };
            }
            catch (InvalidOperationException error)
            {
                throw new ArgumentException("Invalid Akari champion preset structure.", error);
            }
        }

        internal static string Export(IEnumerable<LeagueChampionRunePreset> runes,
            IEnumerable<LeagueChampionSpellPreset> spells)
        {
            var runeRows = runes == null ? new List<LeagueChampionRunePreset>() : runes.ToList();
            var spellRows = spells == null ? new List<LeagueChampionSpellPreset>() : spells.ToList();
            // Reuse the native catalog's strict validation before emitting portable data.
            LeagueChampionRunePresets.Serialize(runeRows);
            LeagueChampionSpellPresets.Serialize(spellRows);
            var runeMap = new SortedDictionary<string, object>(StringComparer.Ordinal);
            var spellMap = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var row in runeRows)
            {
                var id = row.ChampionId.ToString(CultureInfo.InvariantCulture);
                if (!runeMap.ContainsKey(id)) runeMap[id] = new SortedDictionary<string, object>(StringComparer.Ordinal);
                ((SortedDictionary<string, object>)runeMap[id])[row.Group] = new
                {
                    primaryStyleId = row.PrimaryStyleId,
                    subStyleId = row.SecondaryStyleId,
                    selectedPerkIds = row.SelectedPerkIds.ToArray()
                };
            }
            foreach (var row in spellRows)
            {
                var id = row.ChampionId.ToString(CultureInfo.InvariantCulture);
                if (!spellMap.ContainsKey(id)) spellMap[id] = new SortedDictionary<string, object>(StringComparer.Ordinal);
                ((SortedDictionary<string, object>)spellMap[id])[row.Group] = new
                {
                    spell1Id = row.Spell1Id, spell2Id = row.Spell2Id
                };
            }
            var result = new JavaScriptSerializer { MaxJsonLength = MaxClipboardLength }.Serialize(
                new Dictionary<string, object> { { "runesV2", runeMap }, { "summonerSpells", spellMap } });
            if (result.Length > MaxClipboardLength)
                throw new ArgumentException("Champion preset export is too large.");
            return result;
        }

        private static List<LeagueChampionRunePreset> ReadRunes(object source)
        {
            var result = new List<LeagueChampionRunePreset>();
            foreach (var entry in Entries(source))
            {
                var championId = ChampionId(entry.Key);
                foreach (var config in Entries(entry.Value))
                {
                    if (config.Value == null) continue;
                    var fields = RequireObject(config.Value);
                    var perks = RequireArray(fields, "selectedPerkIds");
                    result.Add(new LeagueChampionRunePreset
                    {
                        ChampionId = championId, Group = config.Key,
                        PrimaryStyleId = ReadInt(fields, "primaryStyleId"),
                        SecondaryStyleId = ReadInt(fields, "subStyleId"),
                        SelectedPerkIds = perks.Select(ReadInteger).ToList()
                    });
                    if (result.Count > MaxRows) throw new ArgumentException("Too many rune presets.");
                }
            }
            return result;
        }

        private static List<LeagueChampionSpellPreset> ReadSpells(object source)
        {
            var result = new List<LeagueChampionSpellPreset>();
            foreach (var entry in Entries(source))
            {
                var championId = ChampionId(entry.Key);
                foreach (var config in Entries(entry.Value))
                {
                    if (config.Value == null) continue;
                    var fields = RequireObject(config.Value);
                    result.Add(new LeagueChampionSpellPreset
                    {
                        ChampionId = championId, Group = config.Key,
                        Spell1Id = ReadInt(fields, "spell1Id"),
                        Spell2Id = ReadInt(fields, "spell2Id")
                    });
                    if (result.Count > MaxRows) throw new ArgumentException("Too many spell presets.");
                }
            }
            return result;
        }

        private static Dictionary<string, object> RequireObject(object value)
        {
            var map = value as Dictionary<string, object>;
            if (map == null) throw new ArgumentException("Expected a champion preset object.");
            return map;
        }

        private static IEnumerable<KeyValuePair<string, object>> Entries(object value)
        {
            return RequireObject(value);
        }

        private static object[] RequireArray(Dictionary<string, object> value, string key)
        {
            object raw;
            var items = value.TryGetValue(key, out raw) ? raw as object[] : null;
            if (items == null || items.Length != 9)
                throw new ArgumentException("Invalid rune perk selection.");
            return items;
        }

        private static int ChampionId(string key)
        {
            int id;
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out id) ||
                id <= 0 || id > 10000) throw new ArgumentException("Invalid champion ID.");
            return id;
        }

        private static int ReadInt(Dictionary<string, object> value, string key)
        {
            object raw;
            if (!value.TryGetValue(key, out raw))
                throw new ArgumentException("Missing champion preset value.");
            return ReadInteger(raw);
        }

        private static int ReadInteger(object value)
        {
            if (!(value is int) && !(value is long))
                throw new ArgumentException("Champion preset IDs must be integers.");
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch (OverflowException error)
            {
                throw new ArgumentException("Champion preset ID is out of range.", error);
            }
        }
    }
}
