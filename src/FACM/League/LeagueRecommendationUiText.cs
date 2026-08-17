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
            { LeagueRecommendationUiTextKeys.Menu, "推荐中心" },
            { LeagueRecommendationUiTextKeys.WindowTitle, "FACM · 推荐中心" },
            { LeagueRecommendationUiTextKeys.Title, "推荐中心" },
            { LeagueRecommendationUiTextKeys.Hint, "看懂推荐、选择需要的内容，然后一次应用。底层仍使用 FACM 已验证的安全写入链。" },
            { LeagueRecommendationUiTextKeys.Choose, "这次要应用什么" },
            { LeagueRecommendationUiTextKeys.Runes, "符文" },
            { LeagueRecommendationUiTextKeys.RunesHint, "创建 FACM 符文页并切换" },
            { LeagueRecommendationUiTextKeys.Spells, "召唤师技能" },
            { LeagueRecommendationUiTextKeys.SpellsHint, "按推荐设置，并保留闪现 D/F 习惯" },
            { LeagueRecommendationUiTextKeys.Items, "推荐装备集" },
            { LeagueRecommendationUiTextKeys.ItemsHint, "写入游戏商店的 FACM 推荐装备" },
            { LeagueRecommendationUiTextKeys.Context, "当前英雄 / 位置" },
            { LeagueRecommendationUiTextKeys.Extra, "补充推荐" },
            { LeagueRecommendationUiTextKeys.Skills, "技能加点" },
            { LeagueRecommendationUiTextKeys.Counters, "克制关系" },
            { LeagueRecommendationUiTextKeys.AutoHint, "自动应用始终使用完整推荐；上面的勾选只影响本次手动应用。" },
            { LeagueRecommendationUiTextKeys.Refresh, "刷新推荐" },
            { LeagueRecommendationUiTextKeys.ApplySelected, "应用已选推荐" },
            { LeagueRecommendationUiTextKeys.Waiting, "进入英雄选择并选定英雄后，这里会自动显示 OP.GG 推荐。" },
            { LeagueRecommendationUiTextKeys.Ready, "推荐已就绪。按需要勾选后应用。" },
            { LeagueRecommendationUiTextKeys.Preparing, "正在核对当前英雄和推荐内容..." },
            { LeagueRecommendationUiTextKeys.NoneSelected, "至少选择一项要应用的内容。" },
            { LeagueRecommendationUiTextKeys.NoAvailable, "当前选择里没有可写入的推荐内容。" },
            { LeagueRecommendationUiTextKeys.ConfirmTitle, "确认应用 FACM 推荐" },
            { LeagueRecommendationUiTextKeys.ConfirmIntro, "将按下面的选择修改本局英雄选择配置：" },
            { LeagueRecommendationUiTextKeys.Selected, "应用" },
            { LeagueRecommendationUiTextKeys.NotSelected, "跳过" },
            { LeagueRecommendationUiTextKeys.Unavailable, "暂无推荐" },
            { LeagueRecommendationUiTextKeys.Success, "已完成全部选中的推荐。" },
            { LeagueRecommendationUiTextKeys.Partial, "已完成部分推荐；未完成项保持原状。" },
            { LeagueRecommendationUiTextKeys.Failed, "没有成功写入选中的推荐；客户端原配置保持安全边界。" },
            { LeagueRecommendationUiTextKeys.ContextChanged, "英雄选择上下文已变化，本次应用已安全停止。" },
            { LeagueRecommendationUiTextKeys.RuneSlotFull, "符文页已满，FACM 没有覆盖你的现有符文页。" },
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
