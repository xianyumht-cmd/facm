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
        public const string Capture = "LeagueEfficiencyCapture";
        public const string Clear = "LeagueEfficiencyClear";
        public const string Save = "LeagueEfficiencySave";
        public const string Disabled = "LeagueEfficiencyDisabled";
        public const string CaptureTitle = "LeagueEfficiencyCaptureTitle";
        public const string CapturePrompt = "LeagueEfficiencyCapturePrompt";
        public const string CaptureUnsafe = "LeagueEfficiencyCaptureUnsafe";
        public const string Saved = "LeagueEfficiencySaved";
        public const string SaveFailed = "LeagueEfficiencySaveFailed";
        public const string PostGameSection = "LeagueEfficiencyPostGameSection";
        public const string AutoHonor = "LeagueEfficiencyAutoHonor";
        public const string AutoHonorHint = "LeagueEfficiencyAutoHonorHint";
        public const string AutoReturn = "LeagueEfficiencyAutoReturn";
        public const string AutoReturnHint = "LeagueEfficiencyAutoReturnHint";
        public const string PostGameSaved = "LeagueEfficiencyPostGameSaved";
        public const string NextGameSection = "LeagueEfficiencyNextGameSection";
        public const string AutoMatchmaking = "LeagueEfficiencyAutoMatchmaking";
        public const string AutoMatchmakingHint = "LeagueEfficiencyAutoMatchmakingHint";
        public const string AutoAccept = "LeagueEfficiencyAutoAccept";
        public const string AutoAcceptHint = "LeagueEfficiencyAutoAcceptHint";
        public const string NextGameSaved = "LeagueEfficiencyNextGameSaved";
    }

    internal static class LeagueEfficiencyText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueEfficiencyUiTextKeys.Menu, "游戏效率" },
            { LeagueEfficiencyUiTextKeys.WindowTitle, "FACM · 游戏效率" },
            { LeagueEfficiencyUiTextKeys.Title, "游戏效率" },
            { LeagueEfficiencyUiTextKeys.Hint, "快捷键全局生效；自动化只在对应英雄联盟阶段轻量工作，默认关闭。" },
            { LeagueEfficiencyUiTextKeys.HotkeySection, "快捷键" },
            { LeagueEfficiencyUiTextKeys.ExitGame, "一键结束游戏" },
            { LeagueEfficiencyUiTextKeys.ExitGameHint, "水晶爆炸后按一次：直接结束国服 League of Legends(TM) 游戏进程，快速返回大厅。" },
            { LeagueEfficiencyUiTextKeys.CloseLobby, "一键关闭大厅" },
            { LeagueEfficiencyUiTextKeys.CloseLobbyHint, "按一次直接关闭英雄联盟大厅进程；不会因为游戏正在运行而阻止。" },
            { LeagueEfficiencyUiTextKeys.Capture, "录入" },
            { LeagueEfficiencyUiTextKeys.Clear, "清除" },
            { LeagueEfficiencyUiTextKeys.Save, "保存快捷键" },
            { LeagueEfficiencyUiTextKeys.Disabled, "未设置" },
            { LeagueEfficiencyUiTextKeys.CaptureTitle, "设置快捷键" },
            { LeagueEfficiencyUiTextKeys.CapturePrompt, "请按下快捷键。Esc 清除；裸字母/数字不会保存。" },
            { LeagueEfficiencyUiTextKeys.CaptureUnsafe, "这个按键容易误触，请加 Ctrl / Alt / Shift / Win，或使用 F1-F12。" },
            { LeagueEfficiencyUiTextKeys.Saved, "快捷键已保存并立即生效。" },
            { LeagueEfficiencyUiTextKeys.SaveFailed, "没有修改快捷键：{0}" },
            { LeagueEfficiencyUiTextKeys.PostGameSection, "赛后" },
            { LeagueEfficiencyUiTextKeys.AutoHonor, "自动随机点赞一名队友" },
            { LeagueEfficiencyUiTextKeys.AutoHonorHint, "仅从可点赞队友中随机选 1 人；不点赞对手，也不会把多张票全部用掉。" },
            { LeagueEfficiencyUiTextKeys.AutoReturn, "自动返回大厅" },
            { LeagueEfficiencyUiTextKeys.AutoReturnHint, "结算阶段短暂等待点赞机会后自动回大厅；点赞失败也不会一直卡住。" },
            { LeagueEfficiencyUiTextKeys.PostGameSaved, "赛后自动化设置已保存。" },
            { LeagueEfficiencyUiTextKeys.NextGameSection, "自动下一局" },
            { LeagueEfficiencyUiTextKeys.AutoMatchmaking, "自动寻找对局" },
            { LeagueEfficiencyUiTextKeys.AutoMatchmakingHint, "只在你是房主且当前队伍满足开始条件时自动排队；条件不满足就不操作。" },
            { LeagueEfficiencyUiTextKeys.AutoAccept, "自动接受对局" },
            { LeagueEfficiencyUiTextKeys.AutoAcceptHint, "ReadyCheck 出现后自动接受一次；已接受或你主动拒绝时不会反复修改。" },
            { LeagueEfficiencyUiTextKeys.NextGameSaved, "自动下一局设置已保存。" }
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
