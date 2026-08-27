using System;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueHubSubtitleText
    {
        public static string ForSection(UiTextCatalog ui, string sectionKey)
        {
            if (string.Equals(sectionKey, LeagueHubUiTextKeys.SectionMatch, StringComparison.Ordinal))
                return LeagueHubText.Get(ui, LeagueHubUiTextKeys.SectionMatchHint);
            if (string.Equals(sectionKey, LeagueHubUiTextKeys.SectionRecommend, StringComparison.Ordinal))
                return LeagueHubText.Get(ui, LeagueHubUiTextKeys.SectionRecommendHint);
            if (string.Equals(sectionKey, LeagueHubUiTextKeys.SectionEfficiency, StringComparison.Ordinal))
                return LeagueHubText.Get(ui, LeagueHubUiTextKeys.SectionEfficiencyHint);
            return LeagueHubText.Get(ui, LeagueHubUiTextKeys.Hint);
        }

        public static string ForView(UiTextCatalog ui, string viewId)
        {
            var title = ViewTitle(ui, viewId);
            var detail = ViewDetail(viewId);
            if (string.IsNullOrWhiteSpace(title)) return detail;
            if (string.IsNullOrWhiteSpace(detail)) return title;
            return title + " · " + detail;
        }

        private static string ViewTitle(UiTextCatalog ui, string viewId)
        {
            foreach (var definition in LeagueHubNavigation.Views)
            {
                if (!string.Equals(definition.Id, viewId, StringComparison.Ordinal)) continue;
                return LeagueHubText.DefaultsForSmokeTest().ContainsKey(definition.TextKey)
                    ? LeagueHubText.Get(ui, definition.TextKey)
                    : (ui == null ? string.Empty : ui.Get(definition.TextKey));
            }
            return string.Empty;
        }

        private static string ViewDetail(string viewId)
        {
            if (string.Equals(viewId, LeagueHubNavigation.Dashboard, StringComparison.Ordinal)) return "查看客户端与召唤师状态";
            if (string.Equals(viewId, LeagueHubNavigation.Player, StringComparison.Ordinal)) return "查询近期战绩与对局详情";
            if (string.Equals(viewId, LeagueHubNavigation.Live, StringComparison.Ordinal)) return "查看选人与当前对局信息";
            if (string.Equals(viewId, LeagueHubNavigation.Mayhem, StringComparison.Ordinal)) return "查看当前版本实战推荐";
            if (string.Equals(viewId, LeagueHubNavigation.Recommendation, StringComparison.Ordinal)) return "查看符文、技能与装备建议";
            if (string.Equals(viewId, LeagueHubNavigation.Efficiency, StringComparison.Ordinal)) return "管理游戏快捷键与自动化";
            if (string.Equals(viewId, LeagueHubNavigation.Repair, StringComparison.Ordinal)) return "处理游戏运行期间的客户端异常";
            if (string.Equals(viewId, LeagueHubNavigation.Presence, StringComparison.Ordinal)) return "查看并管理在线状态";
            return string.Empty;
        }
    }
}
