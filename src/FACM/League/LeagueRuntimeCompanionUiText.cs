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
        public const string MayhemAugments = "LeagueRuntimeCompanionMayhemAugments";
        public const string Pin = "LeagueRuntimeCompanionPin";
        public const string Unpin = "LeagueRuntimeCompanionUnpin";
        public const string Collapse = "LeagueRuntimeCompanionCollapse";
        public const string Expand = "LeagueRuntimeCompanionExpand";
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
            { LeagueRuntimeCompanionUiTextKeys.MayhemAugments, "海克斯强化" },
            { LeagueRuntimeCompanionUiTextKeys.Pin, "保持置顶" },
            { LeagueRuntimeCompanionUiTextKeys.Unpin, "取消置顶" },
            { LeagueRuntimeCompanionUiTextKeys.Collapse, "收起" },
            { LeagueRuntimeCompanionUiTextKeys.Expand, "展开" }
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
