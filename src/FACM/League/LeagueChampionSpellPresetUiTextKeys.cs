using System;
using System.Collections.Generic;

namespace FACM.League
{
    internal static class LeagueChampionSpellPresetUiTextKeys
    {
        public const string Toggle = "LeagueChampionSpellPresetToggle";
        public const string Save = "LeagueChampionSpellPresetSave";
        public const string Remove = "LeagueChampionSpellPresetRemove";
        public const string Saved = "LeagueChampionSpellPresetSaved";
        public const string Removed = "LeagueChampionSpellPresetRemoved";
        public const string Unavailable = "LeagueChampionSpellPresetUnavailable";
        public const string SaveFailed = "LeagueChampionSpellPresetSaveFailed";
        public const string SavedRunes = "LeagueChampionSpellPresetSavedRunes";
        public const string SavedSpells = "LeagueChampionSpellPresetSavedSpells";

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { Toggle, "选人时自动恢复英雄配置（符文/技能，与 OP.GG 自动应用互斥）" },
            { Save, "保存当前配置" },
            { Remove, "删除此预设" },
            { Saved, "已保存本英雄及模式的符文与技能预设。" },
            { SavedRunes, "已保存符文预设；技能读取不可用，旧技能预设未改动。" },
            { SavedSpells, "已保存技能预设；符文读取不可用，旧符文预设未改动。" },
            { Removed, "已删除本英雄及模式的预设。" },
            { Unavailable, "请进入选英雄界面，选择英雄后再保存。" },
            { SaveFailed, "当前配置读取失败，未修改预设。" }
        };

        public static bool TryGetDefault(string key, out string value)
        {
            return Defaults.TryGetValue(key ?? string.Empty, out value);
        }
    }
}
