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
        public const string LauncherHint = "LeagueHubLauncherHint";
        public const string SectionMatch = "LeagueHubSectionMatch";
        public const string SectionMatchHint = "LeagueHubSectionMatchHint";
        public const string SectionRecommend = "LeagueHubSectionRecommend";
        public const string SectionRecommendHint = "LeagueHubSectionRecommendHint";
        public const string SectionEfficiency = "LeagueHubSectionEfficiency";
        public const string SectionEfficiencyHint = "LeagueHubSectionEfficiencyHint";
        public const string Dashboard = "LeagueHubDashboard";
        public const string Player = "LeagueHubPlayer";
        public const string Live = "LeagueHubLive";
        public const string Mayhem = "LeagueHubMayhem";
        public const string Recommendation = "LeagueHubRecommendation";
        public const string Efficiency = "LeagueHubEfficiency";
        public const string Presence = "LeagueHubPresence";
    }

    internal static class LeagueHubText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueHubUiTextKeys.WindowTitle, "FACM · LOL 助手" },
            { LeagueHubUiTextKeys.Title, "LOL 助手" },
            { LeagueHubUiTextKeys.Hint, "查战绩、看实时、用推荐、改在线状态。" },
            { LeagueHubUiTextKeys.LauncherHint, "战绩、实时、推荐、在线状态" },
            { LeagueHubUiTextKeys.SectionMatch, "对局" },
            { LeagueHubUiTextKeys.SectionMatchHint, "账号、战绩、实时对局、海斗榜" },
            { LeagueHubUiTextKeys.SectionRecommend, "推荐" },
            { LeagueHubUiTextKeys.SectionRecommendHint, "符文、技能、装备，一处查看和应用" },
            { LeagueHubUiTextKeys.SectionEfficiency, "工具" },
            { LeagueHubUiTextKeys.SectionEfficiencyHint, "快捷键、赛后、自动下一局、在线状态" },
            { LeagueHubUiTextKeys.Dashboard, "当前状态" },
            { LeagueHubUiTextKeys.Player, "我的战绩" },
            { LeagueHubUiTextKeys.Live, "实时对局" },
            { LeagueHubUiTextKeys.Mayhem, "海斗榜" },
            { LeagueHubUiTextKeys.Recommendation, "出装推荐" },
            { LeagueHubUiTextKeys.Efficiency, "快捷工具" },
            { LeagueHubUiTextKeys.Presence, "在线状态" }
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
