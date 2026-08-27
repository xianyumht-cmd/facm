using System;
using System.Collections.Generic;
using FACM.Performance;
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
        public const string ContextStatus = "LeagueHubContextStatus";
        public const string ContextQuick = "LeagueHubContextQuick";
        public const string ContextClientConnected = "LeagueHubContextClientConnected";
        public const string ContextClientDetected = "LeagueHubContextClientDetected";
        public const string ContextClientDisconnected = "LeagueHubContextClientDisconnected";
        public const string ContextPhasePrefix = "LeagueHubContextPhasePrefix";
        public const string ContextChampSelectHint = "LeagueHubContextChampSelectHint";
        public const string ActivityNone = "LeagueHubActivityNone";
        public const string ActivityClient = "LeagueHubActivityClient";
        public const string ActivityQueueing = "LeagueHubActivityQueueing";
        public const string ActivityChampSelect = "LeagueHubActivityChampSelect";
        public const string ActivityInGame = "LeagueHubActivityInGame";
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
            { LeagueHubUiTextKeys.ContextHint, "把空闲空间留给当前状态和真正相关的下一步，而不是装饰。" },
            { LeagueHubUiTextKeys.ContextCurrent, "当前" },
            { LeagueHubUiTextKeys.ContextStatus, "对局状态" },
            { LeagueHubUiTextKeys.ContextQuick, "快捷操作" },
            { LeagueHubUiTextKeys.ContextClientConnected, "客户端 · 已连接" },
            { LeagueHubUiTextKeys.ContextClientDetected, "客户端 · 已发现进程" },
            { LeagueHubUiTextKeys.ContextClientDisconnected, "客户端 · 等待连接" },
            { LeagueHubUiTextKeys.ContextPhasePrefix, "阶段" },
            { LeagueHubUiTextKeys.ContextChampSelectHint, "进入选人阶段会自动打开实时对局快捷面板。" },
            { LeagueHubUiTextKeys.ActivityNone, "空闲" },
            { LeagueHubUiTextKeys.ActivityClient, "客户端" },
            { LeagueHubUiTextKeys.ActivityQueueing, "排队 / 接受" },
            { LeagueHubUiTextKeys.ActivityChampSelect, "选人中" },
            { LeagueHubUiTextKeys.ActivityInGame, "游戏中" }
        };

        public static string Get(UiTextCatalog ui, string key)
        {
            string fallback;
            if (!Defaults.TryGetValue(key ?? string.Empty, out fallback)) fallback = string.Empty;
            return ui == null ? fallback : ui.Get(key, fallback);
        }

        public static string Activity(UiTextCatalog ui, LeagueActivityLevel activity)
        {
            switch (activity)
            {
                case LeagueActivityLevel.Client: return Get(ui, LeagueHubUiTextKeys.ActivityClient);
                case LeagueActivityLevel.Queueing: return Get(ui, LeagueHubUiTextKeys.ActivityQueueing);
                case LeagueActivityLevel.ChampSelect: return Get(ui, LeagueHubUiTextKeys.ActivityChampSelect);
                case LeagueActivityLevel.InGame: return Get(ui, LeagueHubUiTextKeys.ActivityInGame);
                default: return Get(ui, LeagueHubUiTextKeys.ActivityNone);
            }
        }

        internal static IReadOnlyDictionary<string, string> DefaultsForSmokeTest()
        {
            return Defaults;
        }
    }
}
