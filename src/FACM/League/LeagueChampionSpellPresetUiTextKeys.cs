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

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { Toggle, "选人时自动恢复英雄技能预设（与 OP.GG 自动应用互斥）" },
            { Save, "保存当前技能" },
            { Remove, "删除此预设" },
            { Saved, "已保存本英雄及模式的技能预设。" },
            { Removed, "已删除本英雄及模式的预设。" },
            { Unavailable, "请进入选英雄界面，选择英雄后再保存。" },
            { SaveFailed, "当前技能读取失败，未修改预设。" }
        };

        public static bool TryGetDefault(string key, out string value)
        {
            return Defaults.TryGetValue(key ?? string.Empty, out value);
        }
    }
}
