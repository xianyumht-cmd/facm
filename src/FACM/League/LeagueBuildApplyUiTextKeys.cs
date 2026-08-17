using System;
using System.Collections.Generic;

namespace FACM.League
{
    /// <summary>
    /// Stable UI text keys owned by Tools / Automation Gate 2.
    /// Defaults are registered in the canonical UiTextCatalog where available; new post-3.4 copy also keeps
    /// a local fallback so a user's older ui-text.ini can never turn a navigation/card label blank.
    /// </summary>
    internal static class LeagueBuildApplyUiTextKeys
    {
        public const string Menu = "LeagueBuildApplyMenu";
        public const string WindowTitle = "LeagueBuildApplyWindowTitle";
        public const string Title = "LeagueBuildApplyTitle";
        public const string Hint = "LeagueBuildApplyHint";
        public const string Context = "LeagueBuildApplyContext";
        public const string Spells = "LeagueBuildApplySpells";
        public const string Runes = "LeagueBuildApplyRunes";
        public const string Apply = "LeagueBuildApplyApply";
        public const string Refresh = "LeagueBuildApplyRefresh";
        public const string Waiting = "LeagueBuildApplyWaiting";
        public const string Ready = "LeagueBuildApplyReady";
        public const string ChampSelectOnly = "LeagueBuildApplyChampSelectOnly";
        public const string Preparing = "LeagueBuildApplyPreparing";
        public const string ConfirmTitle = "LeagueBuildApplyConfirmTitle";
        public const string ConfirmFormat = "LeagueBuildApplyConfirmFormat";
        public const string Succeeded = "LeagueBuildApplySucceeded";
        public const string Partial = "LeagueBuildApplyPartial";
        public const string Failed = "LeagueBuildApplyFailed";
        public const string RuneSlotFull = "LeagueBuildApplyRuneSlotFull";
        public const string NoLoadout = "LeagueBuildApplyNoLoadout";
        public const string ContextChanged = "LeagueBuildApplyContextChanged";
        public const string Applied = "LeagueBuildApplyApplied";
        public const string WriteFailed = "LeagueBuildApplyWriteFailed";
        public const string DetailsFormat = "LeagueBuildApplyDetailsFormat";

        public const string Options = "LeagueBuildApplyOptions";
        public const string OptionMain = "LeagueBuildApplyOptionMain";
        public const string OptionAlternative = "LeagueBuildApplyOptionAlternative";
        public const string OptionThird = "LeagueBuildApplyOptionThird";
        public const string OptionRankFormat = "LeagueBuildApplyOptionRankFormat";
        public const string OptionStatsFormat = "LeagueBuildApplyOptionStatsFormat";
        public const string OptionUnavailable = "LeagueBuildApplyOptionUnavailable";
        public const string AutoUsesMain = "LeagueBuildApplyAutoUsesMain";
        public const string Items = "LeagueBuildApplyItems";
        public const string ItemsHint = "LeagueBuildApplyItemsHint";
        public const string ItemStarter = "LeagueBuildApplyItemStarter";
        public const string ItemBoots = "LeagueBuildApplyItemBoots";
        public const string ItemCore = "LeagueBuildApplyItemCore";
        public const string SelectedDetail = "LeagueBuildApplySelectedDetail";
        public const string ConfirmRankFormat = "LeagueBuildApplyConfirmRankFormat";

        private static readonly Dictionary<string, string> Defaults =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Options, "选择推荐组合" },
                { OptionMain, "主流组合" },
                { OptionAlternative, "热门备选" },
                { OptionThird, "第三组合" },
                { OptionRankFormat, "FACM 组合 #{0}" },
                { OptionStatsFormat, "符文热度 {0} · {1} 局   |   技能热度 {2} · {3} 局" },
                { OptionUnavailable, "当前 OP.GG 数据不足，无法组成这一备选组合" },
                { AutoUsesMain, "组合由 OP.GG 符文与召唤师技能的独立热度数据生成；自动应用固定使用组合 #1，手动可选其它组合。" },
                { Items, "装备建议" },
                { ItemsHint, "这里只预览当前装备建议；需要写入客户端 Recommended 时，请使用左侧“OP.GG 推荐装备集”。" },
                { ItemStarter, "出门" },
                { ItemBoots, "鞋子" },
                { ItemCore, "核心" },
                { SelectedDetail, "当前选择" },
                { ConfirmRankFormat, "方案：{0}（FACM 组合 #{1}）\r\n{2}" }
            };

        public static bool TryGetDefault(string key, out string value)
        {
            return Defaults.TryGetValue(key ?? string.Empty, out value);
        }

        internal static IReadOnlyDictionary<string, string> DefaultsForSmokeTest()
        {
            return Defaults;
        }
    }
}
