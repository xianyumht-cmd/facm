using System;
using FACM.League;
using FACM.Performance;
using FACM.Services;

namespace FACM
{
    internal static class DesktopLauncherContextUiText
    {
        public static string LeagueStatus(LeagueDashboardPhaseState state)
        {
            if (state == null || (!state.Connected && !state.ClientProcessDetected && !state.GameProcessDetected))
                return T("LOL · 未连接");

            if (!state.Connected)
                return state.GameProcessDetected ? T("LOL · 游戏中") : T("LOL · 客户端已检测");

            var phase = (state.Phase ?? string.Empty).Trim();
            if (EqualsPhase(phase, "Lobby")) return T("LOL · 大厅");
            if (EqualsPhase(phase, "Matchmaking")) return T("LOL · 寻找对局");
            if (EqualsPhase(phase, "ReadyCheck")) return T("LOL · 等待接受");
            if (EqualsPhase(phase, "ChampSelect")) return T("LOL · 选择英雄");
            if (EqualsPhase(phase, "EndOfGame") || EqualsPhase(phase, "PreEndOfGame") || EqualsPhase(phase, "WaitingForStats"))
                return T("LOL · 对局结束");
            if (state.Activity == LeagueActivityLevel.InGame) return T("LOL · 游戏中");
            return T("LOL · 已连接");
        }

        public static string AutomationStatus(AppSettings settings)
        {
            if (settings == null) return T("自动下一局 · 未读取设置");
            return string.Format(
                T("自动下一局 · 寻找 {0} · 接受 {1}"),
                settings.LeagueAutoMatchmakingEnabled ? T("开") : T("关"),
                settings.LeagueAutoAcceptEnabled ? T("开") : T("关"));
        }

        public static string ContextHint(LeagueDashboardPhaseState state)
        {
            var action = LeagueShellContextRouter.Resolve(state);
            if (action == LeagueShellContextAction.Live) return T("点击 LOL 助手直达实时对局");
            if (action == LeagueShellContextAction.Efficiency) return T("点击 LOL 助手直达下一局设置");
            if (action == LeagueShellContextAction.Dashboard) return T("点击 LOL 助手查看当前状态");
            return T("打开 LOL 客户端后会自动显示当前场景");
        }

        public static string DirectoryMissing { get { return T("工作目录未识别 · 修复/清理时再设置"); } }

        private static bool EqualsPhase(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string T(string value)
        {
            return UiTextRuntime.Translate(value);
        }
    }
}
