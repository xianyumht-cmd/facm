using System;
using System.Collections.Generic;

namespace FACM.League
{
    /// <summary>
    /// Scoped UI text contract for Tools / Automation Gate 4.
    /// Defaults remain overridable through ui-text.ini without storing behavior in that file.
    /// </summary>
    internal static class LeagueAutoApplyUiTextKeys
    {
        public const string Toggle = "LeagueAutoApplyToggle";
        public const string Disabled = "LeagueAutoApplyDisabled";
        public const string Waiting = "LeagueAutoApplyWaiting";
        public const string Applying = "LeagueAutoApplyApplying";
        public const string Succeeded = "LeagueAutoApplySucceeded";
        public const string Partial = "LeagueAutoApplyPartial";
        public const string Failed = "LeagueAutoApplyFailed";

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { Toggle, "选人时自动应用 OP.GG 推荐" },
            { Disabled, "已关闭：保持现有手动一键应用。" },
            { Waiting, "已开启：英雄和分路稳定后，自动应用一次符文、召唤师技能和推荐装备。" },
            { Applying, "正在自动应用本次 OP.GG 推荐..." },
            { Succeeded, "本次自动应用已完成并通过写后校验。" },
            { Partial, "本次只完成部分自动应用；不会循环重试。" },
            { Failed, "本次自动应用未完成；不会循环重试，可继续手动应用。" }
        };

        public static bool TryGetDefault(string key, out string value)
        {
            return Defaults.TryGetValue(key ?? string.Empty, out value);
        }

        internal static IEnumerable<string> AllKeys
        {
            get { return Defaults.Keys; }
        }
    }
}
