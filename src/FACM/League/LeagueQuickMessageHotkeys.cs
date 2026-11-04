using System;
using System.Linq;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal static class LeagueQuickMessageHotkeys
    {
        internal const int Count = LeagueQuickMessagePresets.SlotCount + 1;

        internal static string[] Parse(string json)
        {
            var empty = new string[Count];
            if (string.IsNullOrEmpty(json) || json.Length > 500) return empty;
            try
            {
                var raw = new JavaScriptSerializer().Deserialize<string[]>(json);
                if (raw == null || raw.Length != Count) return empty;
                var parsed = new string[Count];
                for (var i = 0; i < Count; i++)
                {
                    LeagueHotkeyBinding binding;
                    string error;
                    if (!LeagueHotkeyBinding.TryParse(raw[i], out binding, out error)) return empty;
                    parsed[i] = binding.ToString();
                }
                return parsed.Where(value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() == parsed.Count(value => value.Length > 0)
                    ? parsed : empty;
            }
            catch { return empty; }
        }

        internal static string NormalizeJson(string json)
        {
            return new JavaScriptSerializer().Serialize(Parse(json));
        }

        internal static string Save(string[] hotkeys)
        {
            if (hotkeys == null || hotkeys.Length != Count)
                throw new ArgumentException("Invalid quick-message hotkeys.", nameof(hotkeys));
            var json = new JavaScriptSerializer().Serialize(hotkeys);
            if (Parse(json).SequenceEqual(new string[Count]) && hotkeys.Any(s => !string.IsNullOrEmpty(s)))
                throw new ArgumentException("Conflicting or invalid quick-message hotkeys.", nameof(hotkeys));
            return NormalizeJson(json);
        }
    }
}
