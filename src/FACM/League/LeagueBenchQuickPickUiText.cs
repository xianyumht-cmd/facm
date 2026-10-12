using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueBenchQuickPickUiTextKeys
    {
        public const string LiveHint = "LeagueBenchQuickPickLiveHint";
        public const string Title = "LeagueBenchQuickPickTitle";
        public const string Hint = "LeagueBenchQuickPickHint";
        public const string Waiting = "LeagueBenchQuickPickWaiting";
        public const string ManualOnly = "LeagueBenchQuickPickManualOnly";
        public const string Swapping = "LeagueBenchQuickPickSwapping";
        public const string Success = "LeagueBenchQuickPickSuccess";
        public const string Unavailable = "LeagueBenchQuickPickUnavailable";
        public const string Disabled = "LeagueBenchQuickPickDisabled";
        public const string Rejected = "LeagueBenchQuickPickRejected";
        public const string VerifyFailed = "LeagueBenchQuickPickVerifyFailed";
        public const string Tooltip = "LeagueBenchQuickPickTooltip";
    }

    internal static class LeagueBenchQuickPickText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueBenchQuickPickUiTextKeys.LiveHint, "实时显示选人和当前对局；大乱斗可用英雄支持手动快速切换。" },
            { LeagueBenchQuickPickUiTextKeys.Title, "可用英雄快速选择" },
            { LeagueBenchQuickPickUiTextKeys.Hint, "点英雄立即换，不弹确认；只有你的点击才会执行一次切换。" },
            { LeagueBenchQuickPickUiTextKeys.Waiting, "进入支持可用英雄席的大乱斗后，这里会自动出现英雄。" },
            { LeagueBenchQuickPickUiTextKeys.ManualOnly, "实时读取 · 只有你点击可用英雄时才执行一次切换" },
            { LeagueBenchQuickPickUiTextKeys.Swapping, "正在切换英雄" },
            { LeagueBenchQuickPickUiTextKeys.Success, "已切换到英雄" },
            { LeagueBenchQuickPickUiTextKeys.Unavailable, "没抢到：这个英雄已经被别人拿走" },
            { LeagueBenchQuickPickUiTextKeys.Disabled, "当前对局没有可用英雄席" },
            { LeagueBenchQuickPickUiTextKeys.Rejected, "客户端拒绝了这次切换" },
            { LeagueBenchQuickPickUiTextKeys.VerifyFailed, "客户端已响应，但英雄没有切换成功" },
            { LeagueBenchQuickPickUiTextKeys.Tooltip, "点击立即切换到这个英雄" }
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
