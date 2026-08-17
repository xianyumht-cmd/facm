using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueHubUiTextKeys
    {
        public const string WindowTitle = "LeagueHubWindowTitle";
        public const string Title = "LeagueHubTitle";
        public const string Hint = "LeagueHubHint";
        public const string SectionMatch = "LeagueHubSectionMatch";
        public const string SectionMatchHint = "LeagueHubSectionMatchHint";
        public const string SectionRecommend = "LeagueHubSectionRecommend";
        public const string SectionRecommendHint = "LeagueHubSectionRecommendHint";
        public const string SectionEfficiency = "LeagueHubSectionEfficiency";
        public const string SectionEfficiencyHint = "LeagueHubSectionEfficiencyHint";
        public const string Dashboard = "LeagueHubDashboard";
        public const string Recommendation = "LeagueHubRecommendation";
    }

    internal static class LeagueHubText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueHubUiTextKeys.WindowTitle, "FACM · 英雄联盟中心" },
            { LeagueHubUiTextKeys.Title, "英雄联盟中心" },
            { LeagueHubUiTextKeys.Hint, "三个入口解决大多数事情：看对局、用推荐、提效率。需要的细节再在顶部切换。" },
            { LeagueHubUiTextKeys.SectionMatch, "对局" },
            { LeagueHubUiTextKeys.SectionMatchHint, "账号 · 实时 · 海斗" },
            { LeagueHubUiTextKeys.SectionRecommend, "推荐" },
            { LeagueHubUiTextKeys.SectionRecommendHint, "符文 · 技能 · 装备" },
            { LeagueHubUiTextKeys.SectionEfficiency, "效率" },
            { LeagueHubUiTextKeys.SectionEfficiencyHint, "快捷键 · 赛后 · 下一局" },
            { LeagueHubUiTextKeys.Dashboard, "概览" },
            { LeagueHubUiTextKeys.Recommendation, "推荐中心" }
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
