using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueEfficiencyUiTextKeys
    {
        public const string Menu = "LeagueEfficiencyMenu";
        public const string WindowTitle = "LeagueEfficiencyWindowTitle";
        public const string Title = "LeagueEfficiencyTitle";
        public const string Hint = "LeagueEfficiencyHint";
        public const string HotkeySection = "LeagueEfficiencyHotkeySection";
        public const string ExitGame = "LeagueEfficiencyExitGame";
        public const string ExitGameHint = "LeagueEfficiencyExitGameHint";
        public const string CloseLobby = "LeagueEfficiencyCloseLobby";
        public const string CloseLobbyHint = "LeagueEfficiencyCloseLobbyHint";
        public const string Credentials = "LeagueEfficiencyCredentials";
        public const string CredentialsHint = "LeagueEfficiencyCredentialsHint";
        public const string Capture = "LeagueEfficiencyCapture";
        public const string Clear = "LeagueEfficiencyClear";
        public const string Save = "LeagueEfficiencySave";
        public const string Disabled = "LeagueEfficiencyDisabled";
        public const string CaptureTitle = "LeagueEfficiencyCaptureTitle";
        public const string CapturePrompt = "LeagueEfficiencyCapturePrompt";
        public const string CaptureUnsafe = "LeagueEfficiencyCaptureUnsafe";
        public const string Saved = "LeagueEfficiencySaved";
        public const string SaveFailed = "LeagueEfficiencySaveFailed";
        public const string Privacy = "LeagueEfficiencyPrivacy";
    }

    internal static class LeagueEfficiencyText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueEfficiencyUiTextKeys.Menu, "游戏效率" },
            { LeagueEfficiencyUiTextKeys.WindowTitle, "FACM · 游戏效率" },
            { LeagueEfficiencyUiTextKeys.Title, "游戏效率" },
            { LeagueEfficiencyUiTextKeys.Hint, "快捷键全局生效：FACM 在后台、最小化或你正在游戏时也能触发；待机不轮询、不截图。" },
            { LeagueEfficiencyUiTextKeys.HotkeySection, "快捷键" },
            { LeagueEfficiencyUiTextKeys.ExitGame, "一键结束游戏" },
            { LeagueEfficiencyUiTextKeys.ExitGameHint, "水晶爆炸后按一次：直接结束国服 League of Legends(TM) 游戏进程，快速返回大厅。" },
            { LeagueEfficiencyUiTextKeys.CloseLobby, "一键关闭大厅" },
            { LeagueEfficiencyUiTextKeys.CloseLobbyHint, "按一次直接关闭英雄联盟大厅进程；不会因为游戏正在运行而阻止。" },
            { LeagueEfficiencyUiTextKeys.Credentials, "账号密码快捷输入" },
            { LeagueEfficiencyUiTextKeys.CredentialsHint, "先点账号输入框，再复制“账号-----密码”并按快捷键：自动输入账号、Tab、密码，不自动回车。" },
            { LeagueEfficiencyUiTextKeys.Capture, "录入" },
            { LeagueEfficiencyUiTextKeys.Clear, "清除" },
            { LeagueEfficiencyUiTextKeys.Save, "保存快捷键" },
            { LeagueEfficiencyUiTextKeys.Disabled, "未设置" },
            { LeagueEfficiencyUiTextKeys.CaptureTitle, "设置快捷键" },
            { LeagueEfficiencyUiTextKeys.CapturePrompt, "请按下快捷键。Esc 清除；裸字母/数字不会保存。" },
            { LeagueEfficiencyUiTextKeys.CaptureUnsafe, "这个按键容易误触，请加 Ctrl / Alt / Shift / Win，或使用 F1-F12。" },
            { LeagueEfficiencyUiTextKeys.Saved, "快捷键已保存并立即全局生效。" },
            { LeagueEfficiencyUiTextKeys.SaveFailed, "没有修改快捷键：{0}" },
            { LeagueEfficiencyUiTextKeys.Privacy, "FACM 不保存账号或密码；凭据只在你按键时从剪贴板读取。" }
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
