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
        public const string ContextTitle = "LeagueHubContextTitle";
        public const string ContextHint = "LeagueHubContextHint";
        public const string ContextCurrent = "LeagueHubContextCurrent";
    }

    internal static class LeagueHubText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueHubUiTextKeys.WindowTitle, "FACM · LOL 工作台" },
            { LeagueHubUiTextKeys.Title, "LOL 工作台" },
            { LeagueHubUiTextKeys.Hint, "状态、战绩、实时、海斗、推荐和自动化都在这里接着做。" },
            { LeagueHubUiTextKeys.LauncherHint, "一个工作台处理战绩、海斗、推荐和对局工具" },
            { LeagueHubUiTextKeys.SectionMatch, "比赛" },
            { LeagueHubUiTextKeys.SectionMatchHint, "从当前状态继续看战绩、实时对局或海斗攻略" },
            { LeagueHubUiTextKeys.SectionRecommend, "攻略" },
            { LeagueHubUiTextKeys.SectionRecommendHint, "符文、技能、装备和自动应用放在同一条使用链里" },
            { LeagueHubUiTextKeys.SectionEfficiency, "自动化" },
            { LeagueHubUiTextKeys.SectionEfficiencyHint, "快捷键、赛后、自动下一局和在线状态集中管理" },
            { LeagueHubUiTextKeys.Dashboard, "当前状态" },
            { LeagueHubUiTextKeys.Player, "我的战绩" },
            { LeagueHubUiTextKeys.Live, "实时对局" },
            { LeagueHubUiTextKeys.Mayhem, "海斗攻略" },
            { LeagueHubUiTextKeys.Recommendation, "出装推荐" },
            { LeagueHubUiTextKeys.Efficiency, "快捷工具" },
            { LeagueHubUiTextKeys.Presence, "在线状态" },
            { LeagueHubUiTextKeys.ContextTitle, "接着做" },
            { LeagueHubUiTextKeys.ContextHint, "不用退回主页。这里会按当前功能给出最相关的下一步。" },
            { LeagueHubUiTextKeys.ContextCurrent, "当前" }
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
