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
        public const string HonorStrategyTitle = "LeagueEfficiencyHonorStrategyTitle";
        public const string HonorPreferParty = "LeagueEfficiencyHonorPreferParty";
        public const string HonorOnlyParty = "LeagueEfficiencyHonorOnlyParty";
        public const string HonorAnyAlly = "LeagueEfficiencyHonorAnyAlly";
        public const string HonorOptOut = "LeagueEfficiencyHonorOptOut";
        public const string HonorIncludingOpponent = "LeagueEfficiencyHonorIncludingOpponent";
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
        public const string LobbyInvitationSection = "LeagueEfficiencyLobbyInvitationSection";
        public const string HandleInvitations = "LeagueEfficiencyHandleInvitations";
        public const string HandleInvitationsHint = "LeagueEfficiencyHandleInvitationsHint";
        public const string InvitationGameType = "LeagueEfficiencyInvitationGameType";
        public const string InvitationGameTypeHint = "LeagueEfficiencyInvitationGameTypeHint";
        public const string InvitationAction = "LeagueEfficiencyInvitationAction";
        public const string InvitationActionHint = "LeagueEfficiencyInvitationActionHint";
        public const string InvitationIgnore = "LeagueEfficiencyInvitationIgnore";
        public const string InvitationAccept = "LeagueEfficiencyInvitationAccept";
        public const string InvitationDecline = "LeagueEfficiencyInvitationDecline";
        public const string SkipInvitationsWhenAway = "LeagueEfficiencySkipInvitationsWhenAway";
        public const string SkipInvitationsWhenAwayHint = "LeagueEfficiencySkipInvitationsWhenAwayHint";
        public const string AutoSkipLeader = "LeagueEfficiencyAutoSkipLeader";
        public const string AutoSkipLeaderHint = "LeagueEfficiencyAutoSkipLeaderHint";
        public const string AramTeamSideSection = "LeagueEfficiencyAramSideSection";
        public const string AramTeamSide = "LeagueEfficiencyAramSide";
        public const string AramTeamSideHint = "LeagueEfficiencyAramSideHint";
        public const string AramTeamSideVisible = "LeagueEfficiencyAramSideVisible";
        public const string AramTeamSideVisibleHint = "LeagueEfficiencyAramSideVisibleHint";
        public const string LoginSignatureSection = "LeagueEfficiencyLoginSignatureSection";
        public const string LoginSignatureText = "LeagueEfficiencyLoginSignatureText";
        public const string LoginSignatureTextHint = "LeagueEfficiencyLoginSignatureTextHint";
        public const string LoginSignatureSave = "LeagueEfficiencyLoginSignatureSave";
        public const string LoginSignatureAuto = "LeagueEfficiencyLoginSignatureAuto";
        public const string LoginSignatureAutoHint = "LeagueEfficiencyLoginSignatureAutoHint";
        public const string LoginSignatureSaved = "LeagueEfficiencyLoginSignatureSaved";
        public const string LoginRankAuto = "LeagueEfficiencyLoginRankAuto";
        public const string LoginRankAutoHint = "LeagueEfficiencyLoginRankAutoHint";
        public const string LoginRankQueue = "LeagueEfficiencyLoginRankQueue";
        public const string LoginRankQueueHint = "LeagueEfficiencyLoginRankQueueHint";
        public const string LoginRankTier = "LeagueEfficiencyLoginRankTier";
        public const string LoginRankTierHint = "LeagueEfficiencyLoginRankTierHint";
        public const string LoginRankDivision = "LeagueEfficiencyLoginRankDivision";
        public const string LoginRankDivisionHint = "LeagueEfficiencyLoginRankDivisionHint";
        public const string LoginRankSaved = "LeagueEfficiencyLoginRankSaved";
        public const string OfflineLock = "LeagueEfficiencyOfflineLock";
        public const string OfflineLockHint = "LeagueEfficiencyOfflineLockHint";
        public const string OfflineLockSaved = "LeagueEfficiencyOfflineLockSaved";
        public const string LobbyInvitationSaved = "LeagueEfficiencyLobbyInvitationSaved";
        public const string FriendInviteOpen = "LeagueEfficiencyFriendInviteOpen";
        public const string FriendInviteTitle = "LeagueEfficiencyFriendInviteTitle";
        public const string FriendInviteHint = "LeagueEfficiencyFriendInviteHint";
        public const string FriendInviteAnnounce = "LeagueEfficiencyFriendInviteAnnounce";
        public const string FriendInviteSearch = "LeagueEfficiencyFriendInviteSearch";
        public const string FriendInviteRefresh = "LeagueEfficiencyFriendInviteRefresh";
        public const string FriendInviteName = "LeagueEfficiencyFriendInviteName";
        public const string FriendInviteAvailability = "LeagueEfficiencyFriendInviteAvailability";
        public const string FriendInviteLoading = "LeagueEfficiencyFriendInviteLoading";
        public const string FriendInviteUnavailable = "LeagueEfficiencyFriendInviteUnavailable";
        public const string FriendInviteCount = "LeagueEfficiencyFriendInviteCount";
        public const string FriendInviteRejected = "LeagueEfficiencyFriendInviteRejected";
        public const string FriendInviteClose = "LeagueEfficiencyFriendInviteClose";
        public const string FriendInviteOnline = "LeagueEfficiencyFriendInviteOnline";
        public const string FriendInviteInGame = "LeagueEfficiencyFriendInviteInGame";
        public const string FriendInviteAway = "LeagueEfficiencyFriendInviteAway";
        public const string FriendInviteOffline = "LeagueEfficiencyFriendInviteOffline";
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
        public const string AdvancedIntentControls = "LeagueEfficiencyAdvancedIntentControls";
        public const string AdvancedShowIntent = "LeagueEfficiencyAdvancedShowIntent";
        public const string AdvancedIgnoreIntent = "LeagueEfficiencyAdvancedIgnoreIntent";
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
            { LeagueEfficiencyUiTextKeys.AutoHonor, "赛后自动点赞" },
            { LeagueEfficiencyUiTextKeys.AutoHonorHint, "按右侧策略选择点赞对象，读取新版结算名单并验证写入；无法确认时不会盲目重复点赞。" },
            { LeagueEfficiencyUiTextKeys.HonorStrategyTitle, "点赞对象策略" },
            { LeagueEfficiencyUiTextKeys.HonorPreferParty, "优先组队成员" },
            { LeagueEfficiencyUiTextKeys.HonorOnlyParty, "仅组队成员" },
            { LeagueEfficiencyUiTextKeys.HonorAnyAlly, "随机队友" },
            { LeagueEfficiencyUiTextKeys.HonorOptOut, "跳过点赞" },
            { LeagueEfficiencyUiTextKeys.HonorIncludingOpponent, "队友与对手" },
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
            { LeagueEfficiencyUiTextKeys.LobbyInvitationSection, "组队与邀请" },
            { LeagueEfficiencyUiTextKeys.HandleInvitations, "自动处理收到的组队邀请" },
            { LeagueEfficiencyUiTextKeys.HandleInvitationsHint, "按下方模式策略接受或拒绝邀请。所有模式默认忽略；关闭时不处理邀请。" },
            { LeagueEfficiencyUiTextKeys.InvitationGameType, "组队邀请模式" },
            { LeagueEfficiencyUiTextKeys.InvitationGameTypeHint, "选择一种邀请模式后，设置右侧的动作。<DEFAULT> 是未单独设置模式时的默认策略。" },
            { LeagueEfficiencyUiTextKeys.InvitationAction, "当前模式邀请策略" },
            { LeagueEfficiencyUiTextKeys.InvitationActionHint, "忽略：不操作；接受：自动入队；拒绝：自动拒绝。保存后立即对新邀请生效。" },
            { LeagueEfficiencyUiTextKeys.InvitationIgnore, "忽略" },
            { LeagueEfficiencyUiTextKeys.InvitationAccept, "接受" },
            { LeagueEfficiencyUiTextKeys.InvitationDecline, "拒绝" },
            { LeagueEfficiencyUiTextKeys.SkipInvitationsWhenAway, "离开状态不处理邀请" },
            { LeagueEfficiencyUiTextKeys.SkipInvitationsWhenAwayHint, "聊天状态为离开时不自动接受或拒绝。状态无法核实时，也不会擅自处理。" },
            { LeagueEfficiencyUiTextKeys.AutoSkipLeader, "自动让出房主" },
            { LeagueEfficiencyUiTextKeys.AutoSkipLeaderHint, "组队大厅中自己为房主且有其他真人成员时，优先把房主交给已准备成员；默认关闭。" },
            { LeagueEfficiencyUiTextKeys.AramTeamSideSection, "大乱斗选人提醒" },
            { LeagueEfficiencyUiTextKeys.AramTeamSide, "自动提示蓝方 / 红方" },
            { LeagueEfficiencyUiTextKeys.AramTeamSideHint, "仅限大乱斗、海克斯大乱斗选人阶段，在聊天窗口提示自己的阵营；默认关闭。" },
            { LeagueEfficiencyUiTextKeys.AramTeamSideVisible, "让队友也看见阵营提示" },
            { LeagueEfficiencyUiTextKeys.AramTeamSideVisibleHint, "开启后会向选人聊天室发普通消息，队友可见；关闭时使用仅本地提示。" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureSection, "登录显示设置" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureText, "保存登录签名" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureTextHint, "填入希望登录后显示的游戏签名并保存；这里的文字仅用于您主动开启的自动恢复。" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureSave, "保存" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureAuto, "登录后自动恢复签名" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureAutoHint, "聊天服务就绪后延迟约 2 秒，仅尝试一次；若期间签名已被改动，则不覆盖。默认关闭。" },
            { LeagueEfficiencyUiTextKeys.LoginSignatureSaved, "登录签名设置已保存。" },
            { LeagueEfficiencyUiTextKeys.LoginRankAuto, "登录后自动恢复展示段位" },
            { LeagueEfficiencyUiTextKeys.LoginRankAutoHint, "聊天就绪后恢复你保存的展示段位，至多写一次；检测到期间手动更改就放弃。与签名共用登录检测，默认关闭。" },
            { LeagueEfficiencyUiTextKeys.LoginRankQueue, "展示段位队列" },
            { LeagueEfficiencyUiTextKeys.LoginRankQueueHint, "选择要展示段位的游戏队列，支持单排、灵活组排及其他客户端队列。" },
            { LeagueEfficiencyUiTextKeys.LoginRankTier, "展示段位等级" },
            { LeagueEfficiencyUiTextKeys.LoginRankTierHint, "选择要恢复的段位；大师及以上不需要填写小段。" },
            { LeagueEfficiencyUiTextKeys.LoginRankDivision, "展示段位小段" },
            { LeagueEfficiencyUiTextKeys.LoginRankDivisionHint, "选择 I / II / III / IV；大师、宗师与王者不会发送小段。" },
            { LeagueEfficiencyUiTextKeys.LoginRankSaved, "登录展示段位设置已保存。" },
            { LeagueEfficiencyUiTextKeys.OfflineLock, "维持离线显示" },
            { LeagueEfficiencyUiTextKeys.OfflineLockHint, "已观察到离线后，若大厅将状态改回在线或离开，会再次核实并恢复离线。复用大厅检测频率，关闭时不读取状态；默认关闭。" },
            { LeagueEfficiencyUiTextKeys.OfflineLockSaved, "离线显示设置已保存。" },
            { LeagueEfficiencyUiTextKeys.LobbyInvitationSaved, "组队和邀请设置已保存。" },
            { LeagueEfficiencyUiTextKeys.FriendInviteOpen, "好友上线后自动邀请…" },
            { LeagueEfficiencyUiTextKeys.FriendInviteTitle, "GGman · 好友邀请排队" },
            { LeagueEfficiencyUiTextKeys.FriendInviteHint, "在组队大厅勾选好友，等其状态变为在线时尝试邀请一次；离开大厅即清空。" },
            { LeagueEfficiencyUiTextKeys.FriendInviteAnnounce, "邀请成功后在组队聊天中提示（仅当前房间）" },
            { LeagueEfficiencyUiTextKeys.FriendInviteSearch, "搜索游戏昵称" },
            { LeagueEfficiencyUiTextKeys.FriendInviteRefresh, "刷新好友" },
            { LeagueEfficiencyUiTextKeys.FriendInviteName, "游戏好友" },
            { LeagueEfficiencyUiTextKeys.FriendInviteAvailability, "当前状态" },
            { LeagueEfficiencyUiTextKeys.FriendInviteLoading, "正在读取当前大厅与好友…" },
            { LeagueEfficiencyUiTextKeys.FriendInviteUnavailable, "无法读取好友或当前不在组队大厅。" },
            { LeagueEfficiencyUiTextKeys.FriendInviteCount, "等待邀请：{0} 人（最多 5 人）" },
            { LeagueEfficiencyUiTextKeys.FriendInviteRejected, "操作未生效：大厅已变更、好友已尝试邀请或排队已满。请刷新。" },
            { LeagueEfficiencyUiTextKeys.FriendInviteClose, "关闭" },
            { LeagueEfficiencyUiTextKeys.FriendInviteOnline, "在线" },
            { LeagueEfficiencyUiTextKeys.FriendInviteInGame, "游戏中" },
            { LeagueEfficiencyUiTextKeys.FriendInviteAway, "离开" },
            { LeagueEfficiencyUiTextKeys.FriendInviteOffline, "离线" },
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
            { LeagueEfficiencyUiTextKeys.AdvancedIntentControls, "预选规则" },
            { LeagueEfficiencyUiTextKeys.AdvancedShowIntent, "轮到选人前自动预选" },
            { LeagueEfficiencyUiTextKeys.AdvancedIgnoreIntent, "忽略队友的英雄预选" },
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
