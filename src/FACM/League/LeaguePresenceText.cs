using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.League
{
    internal static class LeaguePresenceText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeaguePresenceUiTextKeys.Menu, "在线状态" },
            { LeaguePresenceUiTextKeys.WindowTitle, "FACM · 在线状态" },
            { LeaguePresenceUiTextKeys.Title, "好友展示状态" },
            { LeaguePresenceUiTextKeys.Hint, "修改好友列表里看到的状态；每次点击只写一次，不在后台反复抢写。" },
            { LeaguePresenceUiTextKeys.Current, "当前" },
            { LeaguePresenceUiTextKeys.Refresh, "刷新" },
            { LeaguePresenceUiTextKeys.Online, "在线" },
            { LeaguePresenceUiTextKeys.Away, "离开" },
            { LeaguePresenceUiTextKeys.DoNotDisturb, "勿扰" },
            { LeaguePresenceUiTextKeys.Mobile, "手机在线" },
            { LeaguePresenceUiTextKeys.Offline, "隐身" },
            { LeaguePresenceUiTextKeys.InGame, "显示为游戏中" },
            { LeaguePresenceUiTextKeys.Waiting, "正在读取客户端状态..." },
            { LeaguePresenceUiTextKeys.Applied, "状态已读回确认" },
            { LeaguePresenceUiTextKeys.Overridden, "客户端已恢复实际状态；FACM 没有继续强制覆盖" },
            { LeaguePresenceUiTextKeys.Unavailable, "未读取到英雄联盟客户端在线状态" },
            { LeaguePresenceUiTextKeys.WriteFailed, "客户端拒绝或未完成这次状态修改" },
            { LeaguePresenceUiTextKeys.Footer, "隐身和“显示为游戏中”是否长期保持取决于当前客户端。FACM 会读回验证，但不会使用代理、拦截或后台循环去强制伪装。" },
            { LeaguePresenceUiTextKeys.CurrentFormat, "{0}" }
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
