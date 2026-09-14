using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueProfileCustomizationUiTextKeys
    {
        public const string WindowTitle = "LeagueProfileCustomizationWindowTitle";
        public const string Title = "LeagueProfileCustomizationTitle";
        public const string Hint = "LeagueProfileCustomizationHint";
        public const string Champion = "LeagueProfileCustomizationChampion";
        public const string Skin = "LeagueProfileCustomizationSkin";
        public const string Apply = "LeagueProfileCustomizationApply";
        public const string Refresh = "LeagueProfileCustomizationRefresh";
        public const string Loading = "LeagueProfileCustomizationLoading";
        public const string ChooseChampion = "LeagueProfileCustomizationChooseChampion";
        public const string NoSkins = "LeagueProfileCustomizationNoSkins";
        public const string Applied = "LeagueProfileCustomizationApplied";
        public const string Overridden = "LeagueProfileCustomizationOverridden";
        public const string Unverified = "LeagueProfileCustomizationUnverified";
        public const string Unavailable = "LeagueProfileCustomizationUnavailable";
        public const string WriteFailed = "LeagueProfileCustomizationWriteFailed";
        public const string Invalid = "LeagueProfileCustomizationInvalid";
        public const string Footer = "LeagueProfileCustomizationFooter";
        public const string Entry = "LeagueProfileCustomizationEntry";
        public const string EntryHint = "LeagueProfileCustomizationEntryHint";
    }

    internal static class LeagueProfileCustomizationText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueProfileCustomizationUiTextKeys.WindowTitle, "FACM · 召唤师外观" },
            { LeagueProfileCustomizationUiTextKeys.Title, "生涯背景" },
            { LeagueProfileCustomizationUiTextKeys.Hint, "从当前英雄联盟客户端的本地游戏数据选择英雄与皮肤，并设置为召唤师生涯背景。不会解锁、购买或修改皮肤所有权。" },
            { LeagueProfileCustomizationUiTextKeys.Champion, "英雄" },
            { LeagueProfileCustomizationUiTextKeys.Skin, "皮肤" },
            { LeagueProfileCustomizationUiTextKeys.Apply, "设为背景" },
            { LeagueProfileCustomizationUiTextKeys.Refresh, "刷新英雄" },
            { LeagueProfileCustomizationUiTextKeys.Loading, "正在读取客户端游戏数据..." },
            { LeagueProfileCustomizationUiTextKeys.ChooseChampion, "请选择英雄" },
            { LeagueProfileCustomizationUiTextKeys.NoSkins, "当前英雄未读取到可用皮肤" },
            { LeagueProfileCustomizationUiTextKeys.Applied, "生涯背景已读回确认" },
            { LeagueProfileCustomizationUiTextKeys.Overridden, "客户端已恢复其它背景；FACM 没有继续强制覆盖" },
            { LeagueProfileCustomizationUiTextKeys.Unverified, "客户端接受了修改，但暂时无法读回确认" },
            { LeagueProfileCustomizationUiTextKeys.Unavailable, "未读取到英雄联盟客户端召唤师资料" },
            { LeagueProfileCustomizationUiTextKeys.WriteFailed, "客户端拒绝或未完成这次背景修改" },
            { LeagueProfileCustomizationUiTextKeys.Invalid, "请选择有效的英雄和皮肤" },
            { LeagueProfileCustomizationUiTextKeys.Footer, "该操作只在你点击“设为背景”时写入一次，并进行有限读回验证；不会在后台轮询或反复覆盖客户端。" },
            { LeagueProfileCustomizationUiTextKeys.Entry, "召唤师外观" },
            { LeagueProfileCustomizationUiTextKeys.EntryHint, "生涯背景等召唤师展示项" }
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
