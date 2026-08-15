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
        public const string PostGameSection = "LeagueEfficiencyPostGameSection";
        public const string AutoHonor = "LeagueEfficiencyAutoHonor";
        public const string AutoHonorHint = "LeagueEfficiencyAutoHonorHint";
        public const string AutoReturn = "LeagueEfficiencyAutoReturn";
        public const string AutoReturnHint = "LeagueEfficiencyAutoReturnHint";
        public const string PostGameSaved = "LeagueEfficiencyPostGameSaved";
    }

    internal static class LeagueEfficiencyText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueEfficiencyUiTextKeys.Menu, "游戏效率" },
            { LeagueEfficiencyUiTextKeys.WindowTitle, "FACM · 游戏效率" },
            { LeagueEfficiencyUiTextKeys.Title, "游戏效率" },
            { LeagueEfficiencyUiTextKeys.Hint, "只在需要时做事：快捷键待机不轮询，赛后自动化只在结算阶段短时工作。" },
            { LeagueEfficiencyUiTextKeys.HotkeySection, "快捷键" },
            { LeagueEfficiencyUiTextKeys.ExitGame, "一键退出游戏" },
            { LeagueEfficiencyUiTextKeys.ExitGameHint, "水晶爆炸后按一次：先正常关闭游戏，未退出才精确结束游戏进程。" },
            { LeagueEfficiencyUiTextKeys.CloseLobby, "一键关闭大厅" },
            { LeagueEfficiencyUiTextKeys.CloseLobbyHint, "只关闭英雄联盟大厅；检测到游戏仍在运行时会拒绝执行。" },
            { LeagueEfficiencyUiTextKeys.Credentials, "账号密码快捷输入" },
            { LeagueEfficiencyUiTextKeys.CredentialsHint, "复制“账号-----密码”后按一次；仅登录窗口输入账号、Tab、密码，不自动回车。" },
            { LeagueEfficiencyUiTextKeys.Capture, "录入" },
            { LeagueEfficiencyUiTextKeys.Clear, "清除" },
            { LeagueEfficiencyUiTextKeys.Save, "保存快捷键" },
            { LeagueEfficiencyUiTextKeys.Disabled, "未设置" },
            { LeagueEfficiencyUiTextKeys.CaptureTitle, "设置快捷键" },
            { LeagueEfficiencyUiTextKeys.CapturePrompt, "请按下快捷键。Esc 清除；裸字母/数字不会保存。" },
            { LeagueEfficiencyUiTextKeys.CaptureUnsafe, "这个按键容易误触，请加 Ctrl / Alt / Shift / Win，或使用 F1-F12。" },
            { LeagueEfficiencyUiTextKeys.Saved, "快捷键已保存并立即生效。" },
            { LeagueEfficiencyUiTextKeys.SaveFailed, "没有修改快捷键：{0}" },
            { LeagueEfficiencyUiTextKeys.Privacy, "FACM 不保存账号或密码；凭据只在你按键时从剪贴板读取。" },
            { LeagueEfficiencyUiTextKeys.PostGameSection, "赛后" },
            { LeagueEfficiencyUiTextKeys.AutoHonor, "自动随机点赞一名队友" },
            { LeagueEfficiencyUiTextKeys.AutoHonorHint, "仅从可点赞队友中随机选 1 人；不点赞对手，也不会把多张票全部用掉。" },
            { LeagueEfficiencyUiTextKeys.AutoReturn, "自动返回大厅" },
            { LeagueEfficiencyUiTextKeys.AutoReturnHint, "结算阶段短暂等待点赞机会后自动回大厅；点赞失败也不会一直卡住。" },
            { LeagueEfficiencyUiTextKeys.PostGameSaved, "赛后自动化设置已保存。" }
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
