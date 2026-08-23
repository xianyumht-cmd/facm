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
            { LeagueEfficiencyUiTextKeys.Menu, "快捷工具" },
            { LeagueEfficiencyUiTextKeys.WindowTitle, "FACM · 快捷工具" },
            { LeagueEfficiencyUiTextKeys.Title, "快捷工具" },
            { LeagueEfficiencyUiTextKeys.Hint, "常用操作都放这里；把鼠标移到选项上方可查看说明。" },
            { LeagueEfficiencyUiTextKeys.HotkeySection, "快捷操作" },
            { LeagueEfficiencyUiTextKeys.ExitGame, "跳过卡结算" },
            { LeagueEfficiencyUiTextKeys.ExitGameHint, "水晶爆炸后如果结算一直转圈，可用快捷键结束本局游戏进程并快速回到大厅。" },
            { LeagueEfficiencyUiTextKeys.CloseLobby, "快速关闭大厅" },
            { LeagueEfficiencyUiTextKeys.CloseLobbyHint, "直接关闭英雄联盟大厅进程；游戏正在运行时也可以执行。" },
            { LeagueEfficiencyUiTextKeys.Capture, "设置" },
            { LeagueEfficiencyUiTextKeys.Clear, "清除" },
            { LeagueEfficiencyUiTextKeys.Save, "保存快捷键" },
            { LeagueEfficiencyUiTextKeys.Disabled, "未设置" },
            { LeagueEfficiencyUiTextKeys.CaptureTitle, "设置快捷键" },
            { LeagueEfficiencyUiTextKeys.CapturePrompt, "按下要使用的快捷键。Esc 清除；单独字母或数字不会保存。" },
            { LeagueEfficiencyUiTextKeys.CaptureUnsafe, "这个按键容易误触，请加 Ctrl / Alt / Shift / Win，或改用 F1-F12。" },
            { LeagueEfficiencyUiTextKeys.Saved, "快捷键已保存并立即生效。" },
            { LeagueEfficiencyUiTextKeys.SaveFailed, "快捷键没有修改：{0}" },
            { LeagueEfficiencyUiTextKeys.PostGameSection, "赛后处理" },
            { LeagueEfficiencyUiTextKeys.AutoHonor, "随机点赞队友" },
            { LeagueEfficiencyUiTextKeys.AutoHonorHint, "结算时从可点赞队友里随机选 1 人，不点赞对手，也不会把多张票一次用完。" },
            { LeagueEfficiencyUiTextKeys.AutoReturn, "自动回大厅" },
            { LeagueEfficiencyUiTextKeys.AutoReturnHint, "结算时短暂等待点赞机会后自动返回大厅；点赞失败也不会一直卡住。" },
            { LeagueEfficiencyUiTextKeys.PostGameSaved, "赛后处理设置已保存。" },
            { LeagueEfficiencyUiTextKeys.NextGameSection, "下一局" },
            { LeagueEfficiencyUiTextKeys.AutoMatchmaking, "自动开始排队" },
            { LeagueEfficiencyUiTextKeys.AutoMatchmakingHint, "你是房主且队伍满足开局条件时自动开始排队；条件不满足就不操作。" },
            { LeagueEfficiencyUiTextKeys.AutoAccept, "自动接受对局" },
            { LeagueEfficiencyUiTextKeys.AutoAcceptHint, "匹配成功后自动接受一次；已经接受或你主动拒绝时不会反复操作。" },
            { LeagueEfficiencyUiTextKeys.NextGameSaved, "下一局设置已保存。" }
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
