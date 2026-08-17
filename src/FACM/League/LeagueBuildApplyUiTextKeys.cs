using System;
using System.Collections.Generic;

namespace FACM.League
{
    /// <summary>
    /// Stable UI text keys owned by Tools / Automation Gate 2. New post-3.4 shell text keeps
    /// local fallbacks here so ui-text.ini remains optional and older installs never render blanks.
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

        public const string ModeHint = "LeagueBuildApplyModeHint";
        public const string ModeSection = "LeagueBuildApplyModeSection";
        public const string ModeFull = "LeagueBuildApplyModeFull";
        public const string ModeFullHint = "LeagueBuildApplyModeFullHint";
        public const string ModeBuild = "LeagueBuildApplyModeBuild";
        public const string ModeBuildHint = "LeagueBuildApplyModeBuildHint";
        public const string ModeItems = "LeagueBuildApplyModeItems";
        public const string ModeItemsHint = "LeagueBuildApplyModeItemsHint";
        public const string Items = "LeagueBuildApplyItems";
        public const string FullConfirmFormat = "LeagueBuildApplyFullConfirmFormat";
        public const string ItemsConfirmFormat = "LeagueBuildApplyItemsConfirmFormat";
        public const string FullSucceeded = "LeagueBuildApplyFullSucceeded";
        public const string FullPartialFormat = "LeagueBuildApplyFullPartialFormat";
        public const string ItemsSucceeded = "LeagueBuildApplyItemsSucceeded";

        private static readonly Dictionary<string, string> Post34Defaults =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { ModeHint, "选人阶段自动读取当前英雄的 OP.GG 推荐。先选应用方式，再确认写入；三种模式都复用已验收的上下文二次校验。" },
                { ModeSection, "应用方式" },
                { ModeFull, "完整套装" },
                { ModeFullHint, "符文 + 召唤师技能 + 装备集，一次确认完成" },
                { ModeBuild, "符文与技能" },
                { ModeBuildHint, "只修改符文页与召唤师技能，不碰装备文件" },
                { ModeItems, "装备集" },
                { ModeItemsHint, "只更新 FACM 自己管理的 Recommended 装备集" },
                { Items, "装备推荐" },
                { FullConfirmFormat, "将应用完整套装：{0}\r\n\r\n召唤师技能：{1}\r\n符文：{2}\r\n装备：{3}\r\n\r\n确认后会依次执行已验收的符文/技能与装备集写入链；离开选人、英雄或队列变化都会阻止后续写入。" },
                { ItemsConfirmFormat, "将只写入装备集：{0}\r\n\r\n装备：{1}\r\n\r\nFACM 只管理 facm1- 前缀文件，不会删除你的其它推荐文件。" },
                { FullSucceeded, "完整套装已应用：符文/技能与装备集均完成。" },
                { FullPartialFormat, "完整套装只完成部分内容。符文/技能：{0}；装备集：{1}。" },
                { ItemsSucceeded, "装备集已写入并验证完成。" }
            };

        public static bool TryGetDefault(string key, out string value)
        {
            return Post34Defaults.TryGetValue(key ?? string.Empty, out value);
        }
    }
}
