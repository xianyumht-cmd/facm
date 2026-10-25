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
        public const string HonorLastNone = "LeagueEfficiencyHonorLastNone";
        public const string HonorLastSuccess = "LeagueEfficiencyHonorLastSuccess";
        public const string HonorLastSkipped = "LeagueEfficiencyHonorLastSkipped";
        public const string HonorLastUnknown = "LeagueEfficiencyHonorLastUnknown";
        public const string HonorLastFailed = "LeagueEfficiencyHonorLastFailed";
        public const string AutoReturn = "LeagueEfficiencyAutoReturn";
        public const string AutoReturnHint = "LeagueEfficiencyAutoReturnHint";
        public const string PostGameSaved = "LeagueEfficiencyPostGameSaved";
        public const string NextGameSection = "LeagueEfficiencyNextGameSection";
        public const string AutoMatchmaking = "LeagueEfficiencyAutoMatchmaking";
        public const string AutoMatchmakingHint = "LeagueEfficiencyAutoMatchmakingHint";
        public const string AutoAccept = "LeagueEfficiencyAutoAccept";
        public const string AutoAcceptHint = "LeagueEfficiencyAutoAcceptHint";
        public const string AutoReconnect = "LeagueEfficiencyAutoReconnect";
        public const string AutoReconnectHint = "LeagueEfficiencyAutoReconnectHint";
        public const string AutoSelectSection = "LeagueEfficiencyAutoSelectSection";
        public const string AutoPick = "LeagueEfficiencyAutoPick";
        public const string AutoPickHint = "LeagueEfficiencyAutoPickHint";
        public const string AutoPickChampion = "LeagueEfficiencyAutoPickChampion";
        public const string AutoPickChampionHint = "LeagueEfficiencyAutoPickChampionHint";
        public const string AutoBan = "LeagueEfficiencyAutoBan";
        public const string AutoBanHint = "LeagueEfficiencyAutoBanHint";
        public const string AutoBanChampion = "LeagueEfficiencyAutoBanChampion";
        public const string AutoBanChampionHint = "LeagueEfficiencyAutoBanChampionHint";
        public const string ChampionNotSet = "LeagueEfficiencyChampionNotSet";
        public const string ChampionCatalogUnavailable = "LeagueEfficiencyChampionCatalogUnavailable";
        public const string AutoSelectSaved = "LeagueEfficiencyAutoSelectSaved";
        public const string AutoSelectPause = "LeagueEfficiencyAutoSelectPause";
        public const string AutoSelectPauseHint = "LeagueEfficiencyAutoSelectPauseHint";
        public const string AdvancedOpen = "LeagueEfficiencyAdvancedOpen";
        public const string AdvancedTitle = "LeagueEfficiencyAdvancedTitle";
        public const string AdvancedHint = "LeagueEfficiencyAdvancedHint";
        public const string AdvancedMode = "LeagueEfficiencyAdvancedMode";
        public const string AdvancedRole = "LeagueEfficiencyAdvancedRole";
        public const string AdvancedAction = "LeagueEfficiencyAdvancedAction";
        public const string AdvancedPriority = "LeagueEfficiencyAdvancedPriority";
        public const string AdvancedAdd = "LeagueEfficiencyAdvancedAdd";
        public const string AdvancedRemove = "LeagueEfficiencyAdvancedRemove";
        public const string AdvancedUp = "LeagueEfficiencyAdvancedUp";
        public const string AdvancedDown = "LeagueEfficiencyAdvancedDown";
        public const string AdvancedStrategy = "LeagueEfficiencyAdvancedStrategy";
        public const string AdvancedDelay = "LeagueEfficiencyAdvancedDelay";
        public const string AdvancedBenchControls = "LeagueEfficiencyAdvancedBenchControls";
        public const string AdvancedBenchSwap = "LeagueEfficiencyAdvancedBenchSwap";
        public const string AdvancedBenchUpgrade = "LeagueEfficiencyAdvancedBenchUpgrade";
        public const string AdvancedTrade = "LeagueEfficiencyAdvancedTrade";
        public const string AdvancedBenchDelay = "LeagueEfficiencyAdvancedBenchDelay";
        public const string AdvancedSave = "LeagueEfficiencyAdvancedSave";
        public const string AdvancedCancel = "LeagueEfficiencyAdvancedCancel";
        public const string ProfileModeDefault = "LeagueEfficiencyProfileModeDefault";
        public const string ProfileModeRift = "LeagueEfficiencyProfileModeRift";
        public const string ProfileModeAram = "LeagueEfficiencyProfileModeAram";
        public const string ProfileModeMayhem = "LeagueEfficiencyProfileModeMayhem";
        public const string ProfileRoleDefault = "LeagueEfficiencyProfileRoleDefault";
        public const string ProfileRoleTop = "LeagueEfficiencyProfileRoleTop";
        public const string ProfileRoleJungle = "LeagueEfficiencyProfileRoleJungle";
        public const string ProfileRoleMiddle = "LeagueEfficiencyProfileRoleMiddle";
        public const string ProfileRoleBottom = "LeagueEfficiencyProfileRoleBottom";
        public const string ProfileRoleSupport = "LeagueEfficiencyProfileRoleSupport";
        public const string StrategyImmediate = "LeagueEfficiencyStrategyImmediate";
        public const string StrategyShowLock = "LeagueEfficiencyStrategyShowLock";
        public const string StrategyShowOnly = "LeagueEfficiencyStrategyShowOnly";
        public const string MinPartySize = "LeagueEfficiencyMinPartySize";
        public const string MinPartySizeHint = "LeagueEfficiencyMinPartySizeHint";
        public const string MatchmakingDelay = "LeagueEfficiencyMatchmakingDelay";
        public const string MatchmakingDelayHint = "LeagueEfficiencyMatchmakingDelayHint";
        public const string AcceptDelay = "LeagueEfficiencyAcceptDelay";
        public const string AcceptDelayHint = "LeagueEfficiencyAcceptDelayHint";
        public const string MatchmakingStopPolicy = "LeagueEfficiencyMatchmakingStopPolicy";
        public const string MatchmakingStopPolicyHint = "LeagueEfficiencyMatchmakingStopPolicyHint";
        public const string MatchmakingStopNever = "LeagueEfficiencyMatchmakingStopNever";
        public const string MatchmakingStopFixed = "LeagueEfficiencyMatchmakingStopFixed";
        public const string MatchmakingStopEstimated = "LeagueEfficiencyMatchmakingStopEstimated";
        public const string MatchmakingStopAfter = "LeagueEfficiencyMatchmakingStopAfter";
        public const string MatchmakingStopAfterHint = "LeagueEfficiencyMatchmakingStopAfterHint";
        public const string NextGameSaved = "LeagueEfficiencyNextGameSaved";
    }

    internal static class LeagueEfficiencyText
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { LeagueEfficiencyUiTextKeys.Menu, "快捷工具" },
            { LeagueEfficiencyUiTextKeys.WindowTitle, "GGman · 快捷工具" },
            { LeagueEfficiencyUiTextKeys.Title, "快捷工具" },
            { LeagueEfficiencyUiTextKeys.Hint, "常用操作都放这里；把鼠标移到选项上方可查看说明。" },
            { LeagueEfficiencyUiTextKeys.HotkeySection, "快捷操作" },
            { LeagueEfficiencyUiTextKeys.ExitGame, "一键结束游戏" },
            { LeagueEfficiencyUiTextKeys.ExitGameHint, "立即结束当前英雄联盟游戏进程；与“跳过卡结算”是两个不同功能。" },
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
            { LeagueEfficiencyUiTextKeys.AutoHonorHint, "结算后会等待可点赞名单，优先走新版点赞接口；提交后再读回确认，只有明确没生效时才安全重试一次。" },
            { LeagueEfficiencyUiTextKeys.HonorLastNone, "上局点赞：暂无记录" },
            { LeagueEfficiencyUiTextKeys.HonorLastSuccess, "上局点赞：成功 · {0}" },
            { LeagueEfficiencyUiTextKeys.HonorLastSkipped, "上局点赞：未执行 · {0}" },
            { LeagueEfficiencyUiTextKeys.HonorLastUnknown, "上局点赞：已提交，未确认 · {0}" },
            { LeagueEfficiencyUiTextKeys.HonorLastFailed, "上局点赞：失败 · {0}" },
            { LeagueEfficiencyUiTextKeys.AutoReturn, "自动回大厅" },
            { LeagueEfficiencyUiTextKeys.AutoReturnHint, "结算时先给点赞流程留出机会，再自动返回大厅；点赞失败也不会一直卡住。" },
            { LeagueEfficiencyUiTextKeys.PostGameSaved, "赛后处理设置已保存。" },
            { LeagueEfficiencyUiTextKeys.NextGameSection, "下一局" },
            { LeagueEfficiencyUiTextKeys.AutoMatchmaking, "自动开始排队" },
            { LeagueEfficiencyUiTextKeys.AutoMatchmakingHint, "你是房主且队伍满足开局条件时自动开始排队；条件不满足就不操作。" },
            { LeagueEfficiencyUiTextKeys.AutoAccept, "自动接受对局" },
            { LeagueEfficiencyUiTextKeys.AutoAcceptHint, "匹配成功后自动接受一次；已经接受或你主动拒绝时不会反复操作。" },
            { LeagueEfficiencyUiTextKeys.AutoReconnect, "断线后自动重连" },
            { LeagueEfficiencyUiTextKeys.AutoReconnectHint, "游戏客户端进入重连状态后等待 10 秒，再确认状态并尝试重连一次；离开该状态或关闭开关就取消。默认关闭。" },
            { LeagueEfficiencyUiTextKeys.AutoSelectSection, "自动选禁英雄" },
            { LeagueEfficiencyUiTextKeys.AutoPick, "自动锁定英雄" },
            { LeagueEfficiencyUiTextKeys.AutoPickHint, "轮到自己选人时，先确认目标英雄可选，且你没有手动选其他英雄，再尝试锁定一次。默认关闭。" },
            { LeagueEfficiencyUiTextKeys.AutoPickChampion, "优先选择" },
            { LeagueEfficiencyUiTextKeys.AutoPickChampionHint, "从当前客户端英雄列表选一个默认目标。后续版本继续补齐分路、多英雄优先级和亮出后锁定策略。" },
            { LeagueEfficiencyUiTextKeys.AutoBan, "自动禁用英雄" },
            { LeagueEfficiencyUiTextKeys.AutoBanHint, "轮到自己禁用时，确认英雄在可禁列表中，才提交一次禁用。默认关闭。" },
            { LeagueEfficiencyUiTextKeys.AutoBanChampion, "优先禁用" },
            { LeagueEfficiencyUiTextKeys.AutoBanChampionHint, "从当前客户端英雄列表选一个默认禁用目标；已禁、不可禁时不执行。" },
            { LeagueEfficiencyUiTextKeys.ChampionNotSet, "未设置英雄" },
            { LeagueEfficiencyUiTextKeys.ChampionCatalogUnavailable, "无法读取英雄列表；请先进入 LOL 大厅，再重新打开此页面。" },
            { LeagueEfficiencyUiTextKeys.AutoSelectSaved, "自动选禁设置已保存。" },
            { LeagueEfficiencyUiTextKeys.AutoSelectPause, "临时暂停自动选禁" },
            { LeagueEfficiencyUiTextKeys.AutoSelectPauseHint, "立即取消本次运行的自动选、禁、候选席与英雄交换任务；取消勾选后恢复。不会改动已保存的设置。" },
            { LeagueEfficiencyUiTextKeys.AdvancedOpen, "模式、分路与候选优先级设置…" },
            { LeagueEfficiencyUiTextKeys.AdvancedTitle, "GGman · 自动选禁详细设置" },
            { LeagueEfficiencyUiTextKeys.AdvancedHint, "按模式和分路保存候选顺序；轮到自己时，从前往后选择第一个可用英雄。更改在保存后生效。" },
            { LeagueEfficiencyUiTextKeys.AdvancedMode, "游戏模式" },
            { LeagueEfficiencyUiTextKeys.AdvancedRole, "分路" },
            { LeagueEfficiencyUiTextKeys.AdvancedAction, "操作类型" },
            { LeagueEfficiencyUiTextKeys.AdvancedPriority, "英雄优先级" },
            { LeagueEfficiencyUiTextKeys.AdvancedAdd, "添加英雄" },
            { LeagueEfficiencyUiTextKeys.AdvancedRemove, "移除" },
            { LeagueEfficiencyUiTextKeys.AdvancedUp, "上移" },
            { LeagueEfficiencyUiTextKeys.AdvancedDown, "下移" },
            { LeagueEfficiencyUiTextKeys.AdvancedStrategy, "执行策略" },
            { LeagueEfficiencyUiTextKeys.AdvancedDelay, "延迟（秒）" },
            { LeagueEfficiencyUiTextKeys.AdvancedBenchControls, "候选席规则" },
            { LeagueEfficiencyUiTextKeys.AdvancedBenchSwap, "自动换候选席英雄（需开启自动选人）" },
            { LeagueEfficiencyUiTextKeys.AdvancedBenchUpgrade, "发现更高优先级英雄时继续换" },
            { LeagueEfficiencyUiTextKeys.AdvancedTrade, "按优先级自动接受或拒绝交换" },
            { LeagueEfficiencyUiTextKeys.AdvancedBenchDelay, "候选席等待（秒）" },
            { LeagueEfficiencyUiTextKeys.AdvancedSave, "保存" },
            { LeagueEfficiencyUiTextKeys.AdvancedCancel, "取消" },
            { LeagueEfficiencyUiTextKeys.ProfileModeDefault, "默认" },
            { LeagueEfficiencyUiTextKeys.ProfileModeRift, "召唤师峡谷" },
            { LeagueEfficiencyUiTextKeys.ProfileModeAram, "普通大乱斗" },
            { LeagueEfficiencyUiTextKeys.ProfileModeMayhem, "海克斯大乱斗" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleDefault, "通用" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleTop, "上路" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleJungle, "打野" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleMiddle, "中路" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleBottom, "下路" },
            { LeagueEfficiencyUiTextKeys.ProfileRoleSupport, "辅助" },
            { LeagueEfficiencyUiTextKeys.StrategyImmediate, "立即锁定" },
            { LeagueEfficiencyUiTextKeys.StrategyShowLock, "亮出后锁定" },
            { LeagueEfficiencyUiTextKeys.StrategyShowOnly, "只亮出" },
            { LeagueEfficiencyUiTextKeys.MinPartySize, "自动排队最低人数（人）" },
            { LeagueEfficiencyUiTextKeys.MinPartySizeHint, "只有当前大厅真人数量达到这个值时才自动开始排队；1-5 人。" },
            { LeagueEfficiencyUiTextKeys.MatchmakingDelay, "排队前等待（秒）" },
            { LeagueEfficiencyUiTextKeys.MatchmakingDelayHint, "进入大厅并满足条件后等待一段时间再开始排队；期间离开大厅会立即取消。" },
            { LeagueEfficiencyUiTextKeys.AcceptDelay, "接受对局延迟（秒）" },
            { LeagueEfficiencyUiTextKeys.AcceptDelayHint, "ReadyCheck 出现后等待指定时间再接受；离开 ReadyCheck 或关闭自动接受会立即取消。" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopPolicy, "停止匹配策略" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopPolicyHint, "自动匹配开启时，可选择永不停止、到固定时间停止，或超过客户端给出的预计排队时间后停止。只使用当前匹配状态，不新建 Gameflow 监听。" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopNever, "永不" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopFixed, "固定时间" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopEstimated, "超过队列预估时间" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopAfter, "退出匹配时间（秒）" },
            { LeagueEfficiencyUiTextKeys.MatchmakingStopAfterHint, "仅“固定时间”策略生效；排队达到该时长后停止当前匹配。离开匹配或进入 ReadyCheck 会取消待执行操作。" },
            { LeagueEfficiencyUiTextKeys.NextGameSaved, "下一局设置已保存。" }
        };

        public static string Get(UiTextCatalog ui, string key)
        {
            string fallback;
            if (!Defaults.TryGetValue(key ?? string.Empty, out fallback)) fallback = string.Empty;
            var resolved = ui == null ? fallback : ui.Get(key, fallback);

            // Earlier builds accidentally shipped two unrelated labels for the exit-game action.
            // Migrate only those known defaults so user-created custom wording remains untouched.
            if (string.Equals(key, LeagueEfficiencyUiTextKeys.ExitGame, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(resolved, "跳过卡结算", StringComparison.Ordinal) ||
                 string.Equals(resolved, "自动返回大厅", StringComparison.Ordinal)))
                return fallback;

            if (string.Equals(key, LeagueEfficiencyUiTextKeys.ExitGameHint, StringComparison.OrdinalIgnoreCase) &&
                (resolved.IndexOf("结算一直转圈", StringComparison.Ordinal) >= 0 ||
                 resolved.IndexOf("快速回到大厅", StringComparison.Ordinal) >= 0))
                return fallback;

            return resolved;
        }

        internal static IReadOnlyDictionary<string, string> DefaultsForSmokeTest()
        {
            return Defaults;
        }
    }
}
