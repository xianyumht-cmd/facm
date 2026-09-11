using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueRuntimeCompanionUiTextKeys
    {
        public const string StarterItems = "LeagueRuntimeCompanionStarterItems";
        public const string Boots = "LeagueRuntimeCompanionBoots";
        public const string CoreItems = "LeagueRuntimeCompanionCoreItems";
        public const string BuildWaiting = "LeagueRuntimeCompanionBuildWaiting";
        public const string BuildLoading = "LeagueRuntimeCompanionBuildLoading";
        public const string BuildUnavailable = "LeagueRuntimeCompanionBuildUnavailable";
        public const string ChampionWaiting = "LeagueRuntimeCompanionChampionWaiting";
        public const string SourceCache = "LeagueRuntimeCompanionSourceCache";
        public const string AramBaseBalance = "LeagueRuntimeCompanionAramBaseBalance";
        public const string MayhemAugments = "LeagueRuntimeCompanionMayhemAugments";
        public const string Pin = "LeagueRuntimeCompanionPin";
        public const string Unpin = "LeagueRuntimeCompanionUnpin";
        public const string Collapse = "LeagueRuntimeCompanionCollapse";
        public const string Expand = "LeagueRuntimeCompanionExpand";
        public const string ShowMore = "LeagueRuntimeCompanionShowMore";
        public const string ShowLess = "LeagueRuntimeCompanionShowLess";
        public const string WinShort = "LeagueRuntimeCompanionWinShort";
        public const string PickShort = "LeagueRuntimeCompanionPickShort";
        public const string BanShort = "LeagueRuntimeCompanionBanShort";
        public const string RankShort = "LeagueRuntimeCompanionRankShort";
        public const string ImportItems = "LeagueRuntimeCompanionImportItems";
        public const string ApplyShort = "LeagueRuntimeCompanionApplyShort";
        public const string ImportShort = "LeagueRuntimeCompanionImportShort";
        public const string PreviousPage = "LeagueRuntimeCompanionPreviousPage";
        public const string NextPage = "LeagueRuntimeCompanionNextPage";
        public const string ChampionResolving = "LeagueRuntimeCompanionChampionResolving";
        public const string QuitChampSelectShort = "LeagueRuntimeCompanionQuitChampSelectShort";
        public const string QuitChampSelectTooltip = "LeagueRuntimeCompanionQuitChampSelectTooltip";
        public const string QuitChampSelectBusy = "LeagueRuntimeCompanionQuitChampSelectBusy";
        public const string QuitChampSelectSucceeded = "LeagueRuntimeCompanionQuitChampSelectSucceeded";
        public const string QuitChampSelectBlocked = "LeagueRuntimeCompanionQuitChampSelectBlocked";
        public const string QuitChampSelectFailed = "LeagueRuntimeCompanionQuitChampSelectFailed";
        public const string ItemSetPreparing = "LeagueRuntimeCompanionItemSetPreparing";
        public const string ItemSetUnavailable = "LeagueRuntimeCompanionItemSetUnavailable";
        public const string ItemSetSucceeded = "LeagueRuntimeCompanionItemSetSucceeded";
        public const string ItemSetBlocked = "LeagueRuntimeCompanionItemSetBlocked";
        public const string ItemSetFailed = "LeagueRuntimeCompanionItemSetFailed";
        public const string ItemSetConfirmTitle = "LeagueRuntimeCompanionItemSetConfirmTitle";
        public const string ItemSetConfirmFormat = "LeagueRuntimeCompanionItemSetConfirmFormat";
    }

    internal static class LeagueRuntimeCompanionText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueRuntimeCompanionUiTextKeys.StarterItems, "出门装" },
            { LeagueRuntimeCompanionUiTextKeys.Boots, "鞋子" },
            { LeagueRuntimeCompanionUiTextKeys.CoreItems, "核心装备" },
            { LeagueRuntimeCompanionUiTextKeys.BuildWaiting, "等待当前英雄" },
            { LeagueRuntimeCompanionUiTextKeys.BuildLoading, "正在读取推荐" },
            { LeagueRuntimeCompanionUiTextKeys.BuildUnavailable, "当前推荐暂不可用" },
            { LeagueRuntimeCompanionUiTextKeys.ChampionWaiting, "等待你选定英雄" },
            { LeagueRuntimeCompanionUiTextKeys.SourceCache, "缓存" },
            { LeagueRuntimeCompanionUiTextKeys.AramBaseBalance, "大乱斗基础平衡" },
            { LeagueRuntimeCompanionUiTextKeys.MayhemAugments, "海克斯强化" },
            { LeagueRuntimeCompanionUiTextKeys.Pin, "保持置顶" },
            { LeagueRuntimeCompanionUiTextKeys.Unpin, "取消置顶" },
            { LeagueRuntimeCompanionUiTextKeys.Collapse, "收起" },
            { LeagueRuntimeCompanionUiTextKeys.Expand, "展开" },
            { LeagueRuntimeCompanionUiTextKeys.ShowMore, "更多" },
            { LeagueRuntimeCompanionUiTextKeys.ShowLess, "收起" },
            { LeagueRuntimeCompanionUiTextKeys.WinShort, "胜" },
            { LeagueRuntimeCompanionUiTextKeys.PickShort, "登" },
            { LeagueRuntimeCompanionUiTextKeys.BanShort, "禁" },
            { LeagueRuntimeCompanionUiTextKeys.RankShort, "排名" },
            { LeagueRuntimeCompanionUiTextKeys.ImportItems, "导入装备" },
            { LeagueRuntimeCompanionUiTextKeys.ApplyShort, "应用" },
            { LeagueRuntimeCompanionUiTextKeys.ImportShort, "导入" },
            { LeagueRuntimeCompanionUiTextKeys.PreviousPage, "上一页" },
            { LeagueRuntimeCompanionUiTextKeys.NextPage, "下一页" },
            { LeagueRuntimeCompanionUiTextKeys.ChampionResolving, "正在读取英雄" },
            { LeagueRuntimeCompanionUiTextKeys.QuitChampSelectShort, "退" },
            { LeagueRuntimeCompanionUiTextKeys.QuitChampSelectTooltip, "退出当前选人，保留大厅/队伍；游戏自身的秒退处罚仍可能生效" },
            { LeagueRuntimeCompanionUiTextKeys.QuitChampSelectBusy, "正在退出当前选人" },
            { LeagueRuntimeCompanionUiTextKeys.QuitChampSelectSucceeded, "已退出选人并保留大厅" },
            { LeagueRuntimeCompanionUiTextKeys.QuitChampSelectBlocked, "当前不在可退出的选人阶段" },
            { LeagueRuntimeCompanionUiTextKeys.QuitChampSelectFailed, "退出选人未确认成功" },
            { LeagueRuntimeCompanionUiTextKeys.ItemSetPreparing, "正在准备装备方案" },
            { LeagueRuntimeCompanionUiTextKeys.ItemSetUnavailable, "当前没有可导入的装备方案" },
            { LeagueRuntimeCompanionUiTextKeys.ItemSetSucceeded, "装备方案已导入" },
            { LeagueRuntimeCompanionUiTextKeys.ItemSetBlocked, "选人状态已变化" },
            { LeagueRuntimeCompanionUiTextKeys.ItemSetFailed, "装备方案导入失败" },
            { LeagueRuntimeCompanionUiTextKeys.ItemSetConfirmTitle, "导入装备方案" },
            { LeagueRuntimeCompanionUiTextKeys.ItemSetConfirmFormat, "{0}\n\n将导入 {1} 件推荐装备到英雄联盟推荐装备目录。\n仅写入 FACM 自有方案，是否继续？" }
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
