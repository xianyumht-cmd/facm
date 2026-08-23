using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueRecommendationUiTextKeys
    {
        public const string Menu = "LeagueRecommendationMenu";
        public const string WindowTitle = "LeagueRecommendationWindowTitle";
        public const string Title = "LeagueRecommendationTitle";
        public const string Hint = "LeagueRecommendationHint";
        public const string Choose = "LeagueRecommendationChoose";
        public const string Runes = "LeagueRecommendationRunes";
        public const string RunesHint = "LeagueRecommendationRunesHint";
        public const string Spells = "LeagueRecommendationSpells";
        public const string SpellsHint = "LeagueRecommendationSpellsHint";
        public const string Items = "LeagueRecommendationItems";
        public const string ItemsHint = "LeagueRecommendationItemsHint";
        public const string Context = "LeagueRecommendationContext";
        public const string Extra = "LeagueRecommendationExtra";
        public const string Skills = "LeagueRecommendationSkills";
        public const string Counters = "LeagueRecommendationCounters";
        public const string AutoHint = "LeagueRecommendationAutoHint";
        public const string Refresh = "LeagueRecommendationRefresh";
        public const string ApplySelected = "LeagueRecommendationApplySelected";
        public const string Waiting = "LeagueRecommendationWaiting";
        public const string Ready = "LeagueRecommendationReady";
        public const string Preparing = "LeagueRecommendationPreparing";
        public const string NoneSelected = "LeagueRecommendationNoneSelected";
        public const string NoAvailable = "LeagueRecommendationNoAvailable";
        public const string ConfirmTitle = "LeagueRecommendationConfirmTitle";
        public const string ConfirmIntro = "LeagueRecommendationConfirmIntro";
        public const string Selected = "LeagueRecommendationSelected";
        public const string NotSelected = "LeagueRecommendationNotSelected";
        public const string Unavailable = "LeagueRecommendationUnavailable";
        public const string Success = "LeagueRecommendationSuccess";
        public const string Partial = "LeagueRecommendationPartial";
        public const string Failed = "LeagueRecommendationFailed";
        public const string ContextChanged = "LeagueRecommendationContextChanged";
        public const string RuneSlotFull = "LeagueRecommendationRuneSlotFull";
        public const string ItemSummaryFormat = "LeagueRecommendationItemSummaryFormat";
    }

    internal static class LeagueRecommendationText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueRecommendationUiTextKeys.Menu, "对局推荐" },
            { LeagueRecommendationUiTextKeys.WindowTitle, "FACM · 对局推荐" },
            { LeagueRecommendationUiTextKeys.Title, "对局推荐" },
            { LeagueRecommendationUiTextKeys.Hint, "选好英雄后勾选要用的内容：符文会切到 FACM 符文页，召唤师技能保留闪现 D/F 习惯，装备写入游戏商店。" },
            { LeagueRecommendationUiTextKeys.Choose, "要应用的内容" },
            { LeagueRecommendationUiTextKeys.Runes, "符文" },
            { LeagueRecommendationUiTextKeys.RunesHint, "" },
            { LeagueRecommendationUiTextKeys.Spells, "召唤师技能" },
            { LeagueRecommendationUiTextKeys.SpellsHint, "" },
            { LeagueRecommendationUiTextKeys.Items, "推荐装备" },
            { LeagueRecommendationUiTextKeys.ItemsHint, "" },
            { LeagueRecommendationUiTextKeys.Context, "当前英雄和位置" },
            { LeagueRecommendationUiTextKeys.Extra, "技能与克制" },
            { LeagueRecommendationUiTextKeys.Skills, "加点" },
            { LeagueRecommendationUiTextKeys.Counters, "克制" },
            { LeagueRecommendationUiTextKeys.AutoHint, "" },
            { LeagueRecommendationUiTextKeys.Refresh, "刷新" },
            { LeagueRecommendationUiTextKeys.ApplySelected, "应用所选" },
            { LeagueRecommendationUiTextKeys.Waiting, "选好英雄后会自动显示推荐。" },
            { LeagueRecommendationUiTextKeys.Ready, "推荐已准备好。" },
            { LeagueRecommendationUiTextKeys.Preparing, "正在读取当前英雄的推荐..." },
            { LeagueRecommendationUiTextKeys.NoneSelected, "至少选一项再应用。" },
            { LeagueRecommendationUiTextKeys.NoAvailable, "当前选择里没有可应用的推荐。" },
            { LeagueRecommendationUiTextKeys.ConfirmTitle, "确认应用推荐" },
            { LeagueRecommendationUiTextKeys.ConfirmIntro, "本次会应用下面这些内容：" },
            { LeagueRecommendationUiTextKeys.Selected, "应用" },
            { LeagueRecommendationUiTextKeys.NotSelected, "跳过" },
            { LeagueRecommendationUiTextKeys.Unavailable, "暂无推荐" },
            { LeagueRecommendationUiTextKeys.Success, "所选推荐已应用。" },
            { LeagueRecommendationUiTextKeys.Partial, "部分内容已应用，没成功的保持原样。" },
            { LeagueRecommendationUiTextKeys.Failed, "这次没有成功应用，原配置保持不变。" },
            { LeagueRecommendationUiTextKeys.ContextChanged, "你已经换了英雄或离开选人，本次应用已停止。" },
            { LeagueRecommendationUiTextKeys.RuneSlotFull, "符文页已满，没有覆盖你现有的符文页。" },
            { LeagueRecommendationUiTextKeys.ItemSummaryFormat, "出门装：{0}   核心装：{1}" }
        };

        public static string Get(UiTextCatalog ui, string key)
        {
            string fallback;
            if (!Defaults.TryGetValue(key ?? string.Empty, out fallback)) fallback = string.Empty;
            return ui == null ? fallback : ui.Get(key, fallback);
        }

        internal static IReadOnlyDictionary<string, string> DefaultsForSmokeTest()
        {
            return Defaults;
        }
    }
}
