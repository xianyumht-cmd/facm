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
        public const string SectionRecommend = "LeagueHubSectionRecommend";
        public const string SectionEfficiency = "LeagueHubSectionEfficiency";
        public const string Dashboard = "LeagueHubDashboard";
    }

    internal static class LeagueHubText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueHubUiTextKeys.WindowTitle, "FACM · 英雄联盟中心" },
            { LeagueHubUiTextKeys.Title, "英雄联盟中心" },
            { LeagueHubUiTextKeys.Hint, "一个入口管理对局、推荐与效率功能；切换页面不会额外常驻多个功能窗口。" },
            { LeagueHubUiTextKeys.SectionMatch, "对局" },
            { LeagueHubUiTextKeys.SectionRecommend, "推荐" },
            { LeagueHubUiTextKeys.SectionEfficiency, "效率" },
            { LeagueHubUiTextKeys.Dashboard, "概览" }
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
