using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FACM.Services
{
    internal sealed class UiTextCatalog
    {
        private static readonly string LegacyConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FACM",
            "ui-text.ini");

        private static readonly KeyValuePair<string, string>[] DefaultText =
        {
            Pair(UiTextKeys.AppName, "GGman"),
            Pair(UiTextKeys.ControlCenter, "控制中心"),
            Pair(UiTextKeys.Cleanup, "清理环境"),
            Pair(UiTextKeys.ToolGroup, "快捷工具"),
            Pair(UiTextKeys.ToolA, "工具 A"),
            Pair(UiTextKeys.Mode1, "模式 1"),
            Pair(UiTextKeys.Mode2, "模式 2"),
            Pair(UiTextKeys.Mode3, "模式 3"),
            Pair(UiTextKeys.Mode4, "模式 4"),
            Pair(UiTextKeys.CheckUpdate, "检查更新"),
            Pair(UiTextKeys.OpenLog, "操作日志"),
            Pair(UiTextKeys.About, "程序信息"),
            Pair(UiTextKeys.EditText, "界面文字"),
            Pair(UiTextKeys.Exit, "退出程序"),
            Pair(UiTextKeys.PanelTheme, "面板主题"),
            Pair(UiTextKeys.ThemeSettings, "主题设置"),
            Pair(UiTextKeys.DesktopPet, "桌面宠物"),
            Pair(UiTextKeys.PetReset, "宠物复位"),
            Pair(UiTextKeys.RestoreFloatingBall, "恢复默认悬浮球"),
            Pair(UiTextKeys.MayhemRanking, "海斗排行榜"),
            Pair(UiTextKeys.WorkDirectory, "工作目录"),
            Pair(UiTextKeys.AutoDetect, "自动识别"),
            Pair(UiTextKeys.SelectDirectory, "选择目录"),
            Pair(UiTextKeys.RulesConfigured, "规则已配置"),
            Pair(UiTextKeys.WaitingConfiguration, "等待配置"),
            Pair(UiTextKeys.CleanupHint, "先预览路径，再确认执行"),
            Pair(UiTextKeys.StartCleanup, "开始清理"),
            Pair(UiTextKeys.UpdateAndAnnouncements, "更新与公告"),
            Pair(UiTextKeys.OnlineDownloadProgressFormat, "正在下载更新：{0}%"),
            Pair(UiTextKeys.OnlineInstallerStarting, "下载完成，正在启动安装器..."),
            Pair(UiTextKeys.OnlineUpdateReady, "发现新版本，可以下载并安装。"),
            Pair(UiTextKeys.AutoCheckAtStartup, "启动时自动检查"),
            Pair(UiTextKeys.Ready, "准备就绪"),
            Pair(UiTextKeys.Administrator, "管理员"),
            Pair(UiTextKeys.StandardMode, "标准模式"),
            Pair(UiTextKeys.Close, "关闭"),
            Pair(UiTextKeys.ApplyPet, "应用桌宠"),
            Pair(UiTextKeys.PetSource, "来源"),
            Pair(UiTextKeys.Open, "打开"),

            Pair(UiTextKeys.EscSettingsTitle, "ESC 设置备份"),
            Pair(UiTextKeys.EscSettingsHint, "备份 LOL 游戏内 ESC 设置。恢复前须完全退出英雄联盟，GGman 会先保存当前配置。"),
            Pair(UiTextKeys.EscSettingsFolder, "游戏目录（包含 Config 文件夹）"),
            Pair(UiTextKeys.EscSettingsBrowse, "选择目录"),
            Pair(UiTextKeys.EscSettingsLocalSave, "本地备份"),
            Pair(UiTextKeys.EscSettingsLocalRestore, "本地恢复"),
            Pair(UiTextKeys.EscSettingsCloudUpload, "上传云端"),
            Pair(UiTextKeys.EscSettingsCloudRestore, "云端恢复"),
            Pair(UiTextKeys.EscSettingsCloudScope, "云端备份属于已登录的 GGman 邮箱账号，不与匿名设备统计同步。上传或恢复前请关闭英雄联盟。"),
            Pair(UiTextKeys.EscSettingsReady, "请选择英雄联盟目录，然后备份或恢复。"),
            Pair(UiTextKeys.EscSettingsBusy, "正在处理，请稍候..."),
            Pair(UiTextKeys.EscSettingsLocalSaved, "本地备份已保存：{0}"),
            Pair(UiTextKeys.EscSettingsRestoreConfirm, "确认覆盖当前 ESC 游戏设置？\r\n\r\n备份内容：{0}\r\n\r\n系统会先保存你当前的配置。"),
            Pair(UiTextKeys.EscSettingsRestored, "恢复成功。{0}"),
            Pair(UiTextKeys.EscSettingsUploadConfirm, "确定上传当前 ESC 设置？\r\n\r\n现有云端版本：{0}\r\n包含文件：{1}\r\n原版本会被替换；已另存本地备份。"),
            Pair(UiTextKeys.EscSettingsUploaded, "云端上传成功（版本 {0}），本地也已保存当前备份。"),
            Pair(UiTextKeys.EscSettingsCloudEmpty, "当前 GGman 账号还没有 ESC 云备份。"),
            Pair(UiTextKeys.EscSettingsOperationFailed, "操作未完成：{0}"),
            Pair(UiTextKeys.EscSettingsCancelled, "操作已取消，设置没有被恢复。"),
            Pair(UiTextKeys.EscSettingsPickBackup, "选择 ESC 备份文件"),
            Pair(UiTextKeys.EscSettingsCloudRestoreConfirm, "确定从云端恢复并覆盖当前 ESC 设置？\\r\\n\\r\\n云端版本：{0}\\r\\n上传时间：{1}\\r\\n包含文件：{2}\\r\\n将先备份当前设置；游戏和客户端必须关闭。"),
            Pair(UiTextKeys.EscSettingsSessionChanged, "GGman 登录状态已更改，ESC 云端操作已取消。"),

            Pair(UiTextKeys.AccountCaptchaTitle, "图片验证码"),
            Pair(UiTextKeys.AccountCaptchaHint, "腾讯云要求验证是否为真人，完成图片校验后才会继续发送邮件验证码。"),
            Pair(UiTextKeys.AccountCaptchaRefresh, "换一张"),
            Pair(UiTextKeys.AccountCaptchaCode, "图片中的字符"),
            Pair(UiTextKeys.AccountCaptchaVerify, "提交验证"),
            Pair(UiTextKeys.AccountCaptchaLoading, "正在获取图片验证码..."),
            Pair(UiTextKeys.AccountCaptchaReady, "请输入图片中的 4–6 位字母或数字。"),
            Pair(UiTextKeys.AccountCaptchaFailed, "图片验证失败：{0}"),
            Pair(UiTextKeys.AccountCaptchaRetryLimit, "失败次数过多，请刷新图片后重试。"),
            Pair(UiTextKeys.AccountCaptchaCancelled, "未完成人机验证，邮件验证码没有继续发送。"),
            Pair(UiTextKeys.AccountCaptchaInvalid, "云端拒绝了图片验证码，请重新获取并稍后再试。"),
            Pair(UiTextKeys.AccountEmailInvalid, "请输入正确的邮箱地址。"),
            Pair(UiTextKeys.AccountTitle, "GGman 账号"),
            Pair(UiTextKeys.AccountHint, "使用邮箱验证码登录。登录后，账号 UID 可以跨电脑识别；目前不会自动上传原有数据。"),
            Pair(UiTextKeys.AccountEmail, "邮箱地址"),
            Pair(UiTextKeys.AccountCode, "邮件验证码"),
            Pair(UiTextKeys.AccountSendCode, "获取验证码"),
            Pair(UiTextKeys.AccountVerify, "登录 / 注册"),
            Pair(UiTextKeys.AccountLogout, "退出登录"),
            Pair(UiTextKeys.AccountSignedOut, "尚未登录 GGman 账号。其他功能仍可匿名使用。"),
            Pair(UiTextKeys.AccountSignedIn, "已登录：{0}"),
            Pair(UiTextKeys.AccountCodeSent, "验证码已发送，10 分钟内输入邮件中的六位数字。"),
            Pair(UiTextKeys.AccountBusy, "正在向 CloudBase 验证，请稍候..."),
            Pair(UiTextKeys.AccountSendWait, "发送过于频繁，请 {0} 秒后重试。"),
            Pair(UiTextKeys.AccountError, "账号操作失败：{0}"),
            Pair(UiTextKeys.AccountOpenFailed, "无法打开账号登录窗口，请查看 GGman 运行日志。"),
            Pair(UiTextKeys.AccountVerified, "登录成功。当前账号 UID：{0}"),
            Pair(UiTextKeys.AccountLoggedOut, "已退出此设备的 GGman 账号。"),
            Pair(UiTextKeys.AccountNoRemember, "当前版本不保存登录凭据，关闭 GGman 后需要重新验证。"),
            Pair(UiTextKeys.AccountCaptchaRequired, "CloudBase 要求额外验证码，请先检查后台验证方式。"),
            Pair(UiTextKeys.AccountTooManyAttempts, "验证码尝试次数过多，请稍后重新获取。"),
            Pair(UiTextKeys.AccountEmailChanged, "邮箱已更改，请重新获取验证码。"),
            Pair(UiTextKeys.AccountMenu, "GGman 账号登录"),
            Pair(UiTextKeys.AccountManage, "账号管理 · 已登录"),
            Pair(UiTextKeys.AccountUid, "账号 UID：{0}"),

            Pair(UiTextKeys.ShellLeague, "英雄联盟"),
            Pair(UiTextKeys.ShellMore, "更多"),
            Pair(UiTextKeys.ShellFeatureCenter, "功能中心"),
            Pair(UiTextKeys.ShellRepairTools, "修复工具"),
            Pair(UiTextKeys.ShellPersonalization, "个性化"),
            Pair(UiTextKeys.ShellMoreSettings, "设置"),
            Pair(UiTextKeys.ShellGettingStartedTitle, "第一次使用 GGman？"),
            Pair(UiTextKeys.ShellGettingStartedHint, "点击悬浮球打开快捷入口；战绩、攻略和工具都可以从工作台进入。"),
            Pair(UiTextKeys.ShellGettingStartedOpen, "打开工作台"),
            Pair(UiTextKeys.ShellGettingStartedDismiss, "知道了"),
            Pair(UiTextKeys.ShellGettingStartedMenu, "使用指南"),
            Pair(UiTextKeys.ShellManageDirectory, "管理"),
            Pair(UiTextKeys.ShellRepairHint, "驱动 / 窗口 / 客户端"),
            Pair(UiTextKeys.ShellLeagueHint, "战绩 / 实时 / OP.GG"),
            Pair(UiTextKeys.ShellPersonalizationHint, "主题 / 桌面形态"),
            Pair(UiTextKeys.ShellDirectoryReady, "游戏目录已识别"),
            Pair(UiTextKeys.ShellDirectoryMissing, "尚未识别游戏目录"),
            Pair(UiTextKeys.ShellSimpleHint, "常用功能只放第一层，其它功能按类别打开"),
            Pair(UiTextKeys.ShellArrow, "›"),
            Pair(UiTextKeys.ShellUnavailable, "暂无可用功能"),
            Pair(UiTextKeys.ShellEnabled, "已开启"),
            Pair(UiTextKeys.ShellDisabled, "已关闭"),
            Pair(UiTextKeys.ShellStatusFormat, "{0}：{1}"),
            Pair(UiTextKeys.ShellDirectoryReadyFormat, "● {0} · {1}"),
            Pair(UiTextKeys.ShellDirectoryMissingFormat, "● {0}"),
            Pair(UiTextKeys.ShellTrayTooltipFormat, "{0} {1}"),

            Pair(UiTextKeys.ThemePanelAppearance, "面板外观..."),
            Pair(UiTextKeys.ThemeDesktopMode, "桌面形态"),
            Pair(UiTextKeys.ThemeFacmShell, "GGman 悬浮入口"),
            Pair(UiTextKeys.ThemeSelectDesktopPet, "选择桌面宠物..."),
            Pair(UiTextKeys.ThemeResetDesktopPosition, "复位桌面位置"),

            Pair(UiTextKeys.LeagueDashboardMenu, "英雄联盟面板"),
            Pair(UiTextKeys.LeagueDashboardWindowTitle, "GGman · 英雄联盟面板"),
            Pair(UiTextKeys.LeagueDashboardTitle, "League Dashboard"),
            Pair(UiTextKeys.LeagueDashboardHint, "读取本机英雄联盟客户端状态与召唤师信息。"),
            Pair(UiTextKeys.LeagueDashboardConnection, "客户端连接"),
            Pair(UiTextKeys.LeagueDashboardConnected, "已连接"),
            Pair(UiTextKeys.LeagueDashboardDisconnected, "未检测到客户端"),
            Pair(UiTextKeys.LeagueDashboardAccount, "当前召唤师"),
            Pair(UiTextKeys.LeagueDashboardLevel, "等级"),
            Pair(UiTextKeys.LeagueDashboardPlatformRegion, "平台 / 区服"),
            Pair(UiTextKeys.LeagueDashboardGameflow, "当前阶段"),
            Pair(UiTextKeys.LeagueDashboardPerformance, "性能档位"),
            Pair(UiTextKeys.LeagueDashboardRefresh, "立即刷新"),
            Pair(UiTextKeys.LeagueDashboardWaitingClient, "正在等待英雄联盟客户端..."),
            Pair(UiTextKeys.LeagueDashboardUnknown, "暂未读取"),
            Pair(UiTextKeys.LeagueDashboardLastUpdated, "最后更新"),

            Pair(UiTextKeys.LeaguePersonalStatsWindowTitle, "GGman · 我的档案"),
            Pair(UiTextKeys.LeaguePersonalStatsTitle, "我的 GGman"),
            Pair(UiTextKeys.LeaguePersonalStatsHint, "把长期使用记录留在本机，需要时再选择加入匿名排行。"),
            Pair(UiTextKeys.LeaguePersonalStatsAccounts, "玩过的账号"),
            Pair(UiTextKeys.LeaguePersonalStatsActiveDays, "活跃天数"),
            Pair(UiTextKeys.LeaguePersonalStatsMemberSince, "加入 GGman"),
            Pair(UiTextKeys.LeaguePersonalStatsActivityFormat, "连续使用 {0} 天 · 近 7 天活跃 {1} 天 · 近 30 天活跃 {2} 天 · 本月新增 {3} 个账号"),
            Pair(UiTextKeys.LeaguePersonalStatsRanking, "匿名账号数排行"),
            Pair(UiTextKeys.LeaguePersonalStatsRankingDisabled, "未参与"),
            Pair(UiTextKeys.LeaguePersonalStatsRankingWaiting, "等待云端统计"),
            Pair(UiTextKeys.LeaguePersonalStatsRankingFormat, "第 {0} / {1} 名"),
            Pair(UiTextKeys.LeaguePersonalStatsPercentileFormat, "超过 {0:0.0}% 的参与玩家"),
            Pair(UiTextKeys.LeaguePersonalStatsLocalToggle, "记录我的 GGman 使用足迹"),
            Pair(UiTextKeys.LeaguePersonalStatsRankingToggle, "参与匿名账号数排行"),
            Pair(UiTextKeys.LeaguePersonalStatsPrivacyHint, "账号历史只保存设备内派生的哈希；不上传 PUUID、账号名、密码或 LCU 凭据。排行可随时关闭。"),
            Pair(UiTextKeys.LeaguePersonalStatsTelemetryToggle, "发送匿名功能使用统计（不包含账号、PUUID、密码或 IP）"),
            Pair(UiTextKeys.LeaguePersonalStatsRefresh, "刷新排行"),
            Pair(UiTextKeys.LeaguePersonalStatsPaused, "已暂停新增记录，已有本地历史不会删除。"),

            Pair(UiTextKeys.LeaguePlayerMenu, "玩家主页"),
            Pair(UiTextKeys.LeaguePlayerWindowTitle, "GGman · 玩家主页"),
            Pair(UiTextKeys.LeaguePlayerTitle, "Player"),
            Pair(UiTextKeys.LeaguePlayerHint, "当前账号与最近对局；先显示账号，再渐进读取战绩。"),
            Pair(UiTextKeys.LeaguePlayerLoadingProfile, "正在读取当前账号..."),
            Pair(UiTextKeys.LeaguePlayerLoadingMatches, "正在读取最近对局..."),
            Pair(UiTextKeys.LeaguePlayerClientRequired, "请先登录英雄联盟客户端"),
            Pair(UiTextKeys.LeaguePlayerNoMatches, "暂未读取到最近对局"),
            Pair(UiTextKeys.LeaguePlayerRecentMatches, "最近对局"),
            Pair(UiTextKeys.LeaguePlayerRefresh, "刷新"),
            Pair(UiTextKeys.LeaguePlayerLoadMore, "再加载 10 场"),
            Pair(UiTextKeys.LeaguePlayerTime, "时间"),
            Pair(UiTextKeys.LeaguePlayerMode, "模式"),
            Pair(UiTextKeys.LeaguePlayerChampion, "英雄"),
            Pair(UiTextKeys.LeaguePlayerChampionStatsFormat, "英雄表现（当前已加载 {0} 场）"),
            Pair(UiTextKeys.LeaguePlayerKda, "K / D / A"),
            Pair(UiTextKeys.LeaguePlayerCs, "补刀"),
            Pair(UiTextKeys.LeaguePlayerResult, "结果"),
            Pair(UiTextKeys.LeaguePlayerDuration, "时长"),
            Pair(UiTextKeys.LeaguePlayerWin, "胜利"),
            Pair(UiTextKeys.LeaguePlayerLoss, "失败"),
            Pair(UiTextKeys.LeaguePlayerUnknown, "--"),

            Pair(UiTextKeys.LeagueLiveMenu, "实时对局"),
            Pair(UiTextKeys.LeagueLiveWindowTitle, "GGman · 实时对局"),
            Pair(UiTextKeys.LeagueLiveTitle, "Champ Select / Current Game"),
            Pair(UiTextKeys.LeagueLiveHint, "只读显示选人和当前对局必要信息；不执行自动操作。"),
            Pair(UiTextKeys.LeagueLivePhase, "当前阶段"),
            Pair(UiTextKeys.LeagueLivePerformance, "性能档位"),
            Pair(UiTextKeys.LeagueLiveWaiting, "等待进入选人或游戏..."),
            Pair(UiTextKeys.LeagueLiveChampSelect, "英雄选择"),
            Pair(UiTextKeys.LeagueLiveCurrentGame, "当前对局"),
            Pair(UiTextKeys.LeagueLiveGame, "对局"),
            Pair(UiTextKeys.LeagueLiveMap, "地图"),
            Pair(UiTextKeys.LeagueLiveMode, "模式"),
            Pair(UiTextKeys.LeagueLiveQueue, "队列"),
            Pair(UiTextKeys.LeagueLiveTimer, "计时"),
            Pair(UiTextKeys.LeagueLiveLocalAction, "当前操作"),
            Pair(UiTextKeys.LeagueLiveBans, "禁用"),
            Pair(UiTextKeys.LeagueLiveTeam, "队伍"),
            Pair(UiTextKeys.LeagueLivePlayer, "玩家"),
            Pair(UiTextKeys.LeagueLivePosition, "位置"),
            Pair(UiTextKeys.LeagueLiveChampion, "英雄 ID"),
            Pair(UiTextKeys.LeagueLiveIntent, "预选 ID"),
            Pair(UiTextKeys.LeagueLiveSpells, "召唤师技能"),
            Pair(UiTextKeys.LeagueLiveRefresh, "立即刷新"),
            Pair(UiTextKeys.LeagueLiveReadOnly, "只读模式 · 不执行客户端操作"),
            Pair(UiTextKeys.LeagueLiveLocalPlayer, "我"),
            Pair(UiTextKeys.LeagueLiveAlly, "我方"),
            Pair(UiTextKeys.LeagueLiveEnemy, "对方"),
            Pair(UiTextKeys.LeagueLiveTeamOne, "队伍 1"),
            Pair(UiTextKeys.LeagueLiveTeamTwo, "队伍 2"),
            Pair(UiTextKeys.LeagueLiveUnknown, "--"),

            Pair(UiTextKeys.LeagueAdvisorMenu, "OP.GG 对局助手"),
            Pair(UiTextKeys.LeagueAdvisorWindowTitle, "GGman · OP.GG 对局助手"),
            Pair(UiTextKeys.LeagueAdvisorTitle, "OP.GG Build Advisor"),
            Pair(UiTextKeys.LeagueAdvisorHint, "自动识别当前英雄并显示 OP.GG Global 构筑建议；Gate 1 严格只读。"),
            Pair(UiTextKeys.LeagueAdvisorContext, "当前上下文"),
            Pair(UiTextKeys.LeagueAdvisorStats, "英雄数据"),
            Pair(UiTextKeys.LeagueAdvisorSource, "数据来源"),
            Pair(UiTextKeys.LeagueAdvisorVersion, "数据版本"),
            Pair(UiTextKeys.LeagueAdvisorCategory, "项目"),
            Pair(UiTextKeys.LeagueAdvisorRecommendation, "推荐"),
            Pair(UiTextKeys.LeagueAdvisorEvidence, "样本"),
            Pair(UiTextKeys.LeagueAdvisorRunes, "符文"),
            Pair(UiTextKeys.LeagueAdvisorStarterItems, "出门装"),
            Pair(UiTextKeys.LeagueAdvisorBoots, "鞋子"),
            Pair(UiTextKeys.LeagueAdvisorCoreItems, "核心装备"),
            Pair(UiTextKeys.LeagueAdvisorSkills, "技能加点"),
            Pair(UiTextKeys.LeagueAdvisorCounters, "克制关系"),
            Pair(UiTextKeys.LeagueAdvisorWaitingChampion, "等待你在选人阶段选择或预选英雄..."),
            Pair(UiTextKeys.LeagueAdvisorWaitingChampSelect, "进入英雄选择后会自动切换当前英雄的推荐。"),
            Pair(UiTextKeys.LeagueAdvisorUnsupportedMode, "当前模式暂未映射到 OP.GG 构筑数据。"),
            Pair(UiTextKeys.LeagueAdvisorOpggUnavailable, "OP.GG 暂时不可用；客户端主链不受影响。"),
            Pair(UiTextKeys.LeagueAdvisorInGameCache, "游戏中只读显示已缓存推荐，不发送新的 OP.GG 请求。"),
            Pair(UiTextKeys.LeagueAdvisorInGameNoCache, "游戏中禁止新增 OP.GG 请求；本局暂无已缓存推荐。"),
            Pair(UiTextKeys.LeagueAdvisorTimeout, "读取超时，已安全停止本轮请求。"),
            Pair(UiTextKeys.LeagueAdvisorReady, "推荐已就绪"),
            Pair(UiTextKeys.LeagueAdvisorReadOnly, "只读模式 · 不修改符文、技能、装备集或客户端设置"),

            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Menu, "OP.GG 一键应用"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.WindowTitle, "GGman · OP.GG 一键应用"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Title, "OP.GG Loadout Apply"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Hint, "仅在英雄选择阶段，由你确认后写入符文和召唤师技能；不会自动操作。"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Context, "当前上下文"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Spells, "召唤师技能"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Runes, "符文"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Apply, "预览并应用"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Refresh, "刷新推荐"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Waiting, "先进入英雄选择，并等待 OP.GG 推荐就绪。"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Ready, "推荐已就绪；点击后还会再次确认，不会自动写入。"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.ChampSelectOnly, "当前已离开英雄选择；没有执行任何写入。"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Preparing, "正在准备可写入的符文和召唤师技能..."),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.ConfirmTitle, "确认应用 OP.GG 推荐"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.ConfirmFormat, "将应用到：{0}\r\n\r\n召唤师技能：{1}\r\n符文：{2}\r\n\r\n只有点击“是”后才会写入。符文页已满时会跳过符文，不覆盖已有页面。"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Succeeded, "已读回验证：可应用内容全部成功。"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Partial, "只完成部分应用。{0}"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Failed, "没有完成应用。{0}"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.RuneSlotFull, "符文页已满，已跳过符文；没有覆盖现有符文页。"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.NoLoadout, "当前 OP.GG 数据缺少可安全应用的符文或召唤师技能。"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.ContextChanged, "确认期间英雄、队列或阶段已经变化；已安全取消，没有继续写入。"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.Applied, "已验证"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.WriteFailed, "写入或读回验证失败"),
            Pair(FACM.League.LeagueBuildApplyUiTextKeys.DetailsFormat, "符文：{0}；召唤师技能：{1}"),

            Pair(UiTextKeys.PetPickerWindowTitle, "GGman · 桌面宠物"),
            Pair(UiTextKeys.PetPickerTitle, "选择桌面宠物"),
            Pair(UiTextKeys.PetPickerHint, "六种轻量飞虫会在桌面自主移动；VPet 是动作更丰富、资源占用更高的独立选项。"),
            Pair(UiTextKeys.PetCurrentPrefix, "当前："),
            Pair(UiTextKeys.PetCurrentBadge, "当前"),
            Pair(UiTextKeys.PetCurrentUse, "当前使用"),
            Pair(UiTextKeys.PetInteractionVPet, "应用后在桌面直接体验；可拖动放置，也可从「复位桌面位置」找回。"),
            Pair(UiTextKeys.PetInteractionFlying, "可拖动放置 · 会自主移动并允许飞出屏幕 · 可用「复位桌面位置」找回"),
            Pair(UiTextKeys.PetRuntimeVPet, "高精度 · 独立桌宠"),
            Pair(UiTextKeys.PetRuntimeFlying, "轻量 · 自主飞行"),
            Pair(UiTextKeys.VPetPreviewTitle, "VPet Core"),
            Pair(UiTextKeys.VPetPreviewDescription, "动作更丰富的独立桌宠\r\n待机 · 移动 · 提起 · 触摸\r\n\r\n首次启用需要准备更多资源"),

            Pair(UiTextKeys.PetNameGreenFly, "绿苍蝇"),
            Pair(UiTextKeys.PetNameBee, "蜜蜂"),
            Pair(UiTextKeys.PetNameRealBee, "真实蜜蜂"),
            Pair(UiTextKeys.PetNameDragonfly, "蜻蜓"),
            Pair(UiTextKeys.PetNameButterfly, "蝴蝶"),
            Pair(UiTextKeys.PetNameMoth, "飞蛾"),
            Pair(UiTextKeys.PetNameVPet, "VPet Core"),

            Pair(UiTextKeys.PetSummaryGreenFly, "高速急转 · 灵活随机"),
            Pair(UiTextKeys.PetSummaryBee, "巡航悬停 · 转向平稳"),
            Pair(UiTextKeys.PetSummaryRealBee, "写真质感 · 灵活巡航"),
            Pair(UiTextKeys.PetSummaryDragonfly, "高速冲刺 · 长直线"),
            Pair(UiTextKeys.PetSummaryButterfly, "慢速漂浮 · 大曲线"),
            Pair(UiTextKeys.PetSummaryMoth, "短距游走 · 小范围绕行"),
            Pair(UiTextKeys.PetSummaryVPet, "动作丰富 · 资源占用较高"),
            Pair(UiTextKeys.PetSummaryDefaultVPet, "高精度桌宠"),
            Pair(UiTextKeys.PetSummaryDefaultFlying, "轻量桌宠"),

            Pair(UiTextKeys.PetBehaviorGreenFly, "飞行性格：快、急转、几乎不停"),
            Pair(UiTextKeys.PetBehaviorBee, "飞行性格：中速巡航，偶尔原地悬停"),
            Pair(UiTextKeys.PetBehaviorRealBee, "飞行性格：中速巡航，写真外观更接近实物"),
            Pair(UiTextKeys.PetBehaviorDragonfly, "飞行性格：快速长冲刺，短暂停顿后改向"),
            Pair(UiTextKeys.PetBehaviorButterfly, "飞行性格：慢速大曲线，上下轻柔漂浮"),
            Pair(UiTextKeys.PetBehaviorMoth, "飞行性格：短距离频繁改向，偶尔绕小圈"),
            Pair(UiTextKeys.PetBehaviorVPet, "桌面性格：动作真实，偏重交互，不主动漫游"),

            Pair(UiTextKeys.PetDescriptionGreenFly, "反应最快的小型飞虫，适合喜欢随机、灵活桌面运动的人。"),
            Pair(UiTextKeys.PetDescriptionBee, "速度适中，转向柔和，会穿插短暂停悬，整体更安静。"),
            Pair(UiTextKeys.PetDescriptionRealBee, "写真级真实蜜蜂，保留自然巡航节奏，更强调透明翅膀、真实材质和小尺寸桌面观感。"),
            Pair(UiTextKeys.PetDescriptionDragonfly, "速度最快、方向感最强，常做较长距离的直线飞行。"),
            Pair(UiTextKeys.PetDescriptionButterfly, "移动最慢，曲线和上下漂浮更明显，视觉节奏最舒缓。"),
            Pair(UiTextKeys.PetDescriptionMoth, "活动范围更紧凑，改向频繁，飞行轨迹带一点小范围绕行。"),
            Pair(UiTextKeys.PetDescriptionVPet, "动作和互动更丰富，但首次启用需要准备较多资源，运行也更重。")
        };

        private static readonly HashSet<string> LegacyNamedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            UiTextKeys.AppName, UiTextKeys.ControlCenter, UiTextKeys.Cleanup, UiTextKeys.ToolGroup, UiTextKeys.ToolA,
            UiTextKeys.Mode1, UiTextKeys.Mode2, UiTextKeys.Mode3, UiTextKeys.Mode4, UiTextKeys.CheckUpdate,
            UiTextKeys.OpenLog, UiTextKeys.About, UiTextKeys.EditText, UiTextKeys.Exit, UiTextKeys.PanelTheme,
            UiTextKeys.ThemeSettings, UiTextKeys.DesktopPet, UiTextKeys.PetReset, UiTextKeys.RestoreFloatingBall,
            UiTextKeys.MayhemRanking, UiTextKeys.WorkDirectory, UiTextKeys.AutoDetect, UiTextKeys.SelectDirectory,
            UiTextKeys.RulesConfigured, UiTextKeys.WaitingConfiguration, UiTextKeys.CleanupHint, UiTextKeys.StartCleanup,
            UiTextKeys.UpdateAndAnnouncements, UiTextKeys.AutoCheckAtStartup, UiTextKeys.Ready, UiTextKeys.Administrator,
            UiTextKeys.StandardMode, UiTextKeys.Close, UiTextKeys.ApplyPet, UiTextKeys.PetSource, UiTextKeys.Open
        };

        private static readonly Dictionary<string, string> DefaultValues =
            DefaultText.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _replacements =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private UiTextCatalog()
        {
            foreach (var entry in DefaultText) _values[entry.Key] = entry.Value;
        }

        public string AppName { get { return Get(UiTextKeys.AppName); } }
        public string ControlCenter { get { return Get(UiTextKeys.ControlCenter); } }
        public string Cleanup { get { return Get(UiTextKeys.Cleanup); } }
        public string ToolGroup { get { return Get(UiTextKeys.ToolGroup); } }
        public string ToolA { get { return Get(UiTextKeys.ToolA); } }
        public string Mode1 { get { return Get(UiTextKeys.Mode1); } }
        public string Mode2 { get { return Get(UiTextKeys.Mode2); } }
        public string Mode3 { get { return Get(UiTextKeys.Mode3); } }
        public string Mode4 { get { return Get(UiTextKeys.Mode4); } }
        public string CheckUpdate { get { return Get(UiTextKeys.CheckUpdate); } }
        public string OpenLog { get { return Get(UiTextKeys.OpenLog); } }
        internal string About { get { return Get(UiTextKeys.About); } }
        public string EditText { get { return Get(UiTextKeys.EditText); } }
        public string Exit { get { return Get(UiTextKeys.Exit); } }

        public static string ConfigPath
        {
            get { return RuntimePaths.UiTextPath; }
        }

        public static UiTextCatalog Load()
        {
            EnsureTemplate();
            var result = new UiTextCatalog();
            try
            {
                var section = string.Empty;
                foreach (var sourceLine in File.ReadAllLines(RuntimePaths.UiTextPath, Encoding.UTF8))
                {
                    var line = sourceLine ?? string.Empty;
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal)) continue;
                    if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal) && trimmed.Length > 2)
                    {
                        section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        continue;
                    }

                    var separator = FindUnescapedEquals(line);
                    if (separator <= 0) continue;
                    var key = Unescape(line.Substring(0, separator).Trim());
                    var value = Unescape(line.Substring(separator + 1).Trim());
                    if (key.Length == 0) continue;

                    if (section.Equals("Replace", StringComparison.OrdinalIgnoreCase))
                        result._replacements[key] = value;
                    else
                        result._values[key] = value;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to load UI text configuration", exception);
            }
            return result;
        }

        public static void OpenConfig()
        {
            EnsureTemplate();
            Process.Start(new ProcessStartInfo
            {
                FileName = RuntimePaths.UiTextPath,
                UseShellExecute = true
            });
        }

        public string Get(string key)
        {
            string fallback;
            return DefaultValues.TryGetValue(key ?? string.Empty, out fallback)
                ? Get(key, fallback)
                : string.Empty;
        }

        public string Get(string key, string fallback)
        {
            string value;
            return !string.IsNullOrEmpty(key) && _values.TryGetValue(key, out value) ? value : fallback;
        }

        public string Translate(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            var translated = text;

            foreach (var rule in OrderedReplacementRules())
            {
                if (rule.Key.Length == 0 || string.Equals(rule.Key, rule.Value, StringComparison.Ordinal)) continue;
                translated = translated.Replace(rule.Key, rule.Value);
            }

            foreach (var entry in OrderedNamedRules())
            {
                if (entry.Key.Length == 0 || string.Equals(entry.Key, entry.Value, StringComparison.Ordinal)) continue;
                translated = translated.Replace(entry.Key, entry.Value);
            }

            return translated;
        }

        public string Canonicalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            var canonical = text;

            foreach (var entry in OrderedNamedReverseRules())
            {
                if (entry.Key.Length == 0 || string.Equals(entry.Key, entry.Value, StringComparison.Ordinal)) continue;
                canonical = canonical.Replace(entry.Key, entry.Value);
            }

            foreach (var rule in OrderedReplacementReverseRules())
            {
                if (rule.Key.Length == 0 || string.Equals(rule.Key, rule.Value, StringComparison.Ordinal)) continue;
                canonical = canonical.Replace(rule.Key, rule.Value);
            }

            return canonical;
        }

        public string ModeName(int mode)
        {
            switch (mode)
            {
                case 1: return Mode1;
                case 2: return Mode2;
                case 3: return Mode3;
                case 4: return Mode4;
                default: return Translate("模式 " + mode);
            }
        }

        private IEnumerable<KeyValuePair<string, string>> OrderedNamedRules()
        {
            return DefaultText
                .Where(entry => LegacyNamedKeys.Contains(entry.Key))
                .Select(entry => Pair(entry.Value, Get(entry.Key, entry.Value)))
                .OrderByDescending(entry => entry.Key.Length);
        }

        private IEnumerable<KeyValuePair<string, string>> OrderedNamedReverseRules()
        {
            return DefaultText
                .Where(entry => LegacyNamedKeys.Contains(entry.Key))
                .Select(entry => Pair(Get(entry.Key, entry.Value), entry.Value))
                .Where(entry => entry.Key.Length > 0)
                .OrderByDescending(entry => entry.Key.Length);
        }

        private IEnumerable<KeyValuePair<string, string>> OrderedReplacementRules()
        {
            return _replacements.OrderByDescending(entry => entry.Key.Length);
        }

        private IEnumerable<KeyValuePair<string, string>> OrderedReplacementReverseRules()
        {
            return _replacements
                .Where(entry => !string.IsNullOrEmpty(entry.Value))
                .Select(entry => Pair(entry.Value, entry.Key))
                .OrderByDescending(entry => entry.Key.Length);
        }

        private static void EnsureTemplate()
        {
            try
            {
                RuntimePaths.Initialize();
                if (!File.Exists(RuntimePaths.UiTextPath))
                {
                    if (File.Exists(LegacyConfigPath))
                        File.Copy(LegacyConfigPath, RuntimePaths.UiTextPath, false);
                    else
                        File.WriteAllLines(RuntimePaths.UiTextPath, BuildTemplate(), new UTF8Encoding(false));
                }

                EnsureMissingKeysAndSections(RuntimePaths.UiTextPath);
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to create UI text configuration", exception);
            }
        }

        private static string[] BuildTemplate()
        {
            var lines = new List<string>
            {
                "# GGman 界面文字配置",
                "# 修改后保存即可，程序运行时会自动重新读取，不需要重新编译。",
                "# [Text] 是正式文字契约：Key 保持稳定，只修改等号右侧即可。",
                "# 新版本会自动补充缺失 Key，不覆盖你已经设置的值。",
                "# [Replace] 是历史/全局兼容层：可替换整句或关键词，但新功能优先使用 [Text] Key。",
                "# 需要换行时写 \\n；需要显示反斜杠写 \\\\。",
                string.Empty,
                "[Text]"
            };
            foreach (var entry in DefaultText) lines.Add(entry.Key + "=" + Escape(entry.Value));
            lines.Add(string.Empty);
            lines.Add("[Replace]");
            lines.Add("# 兼容示例（去掉前面的 # 即生效）：");
            lines.Add("# GGman=我的程序");
            lines.Add("# VPet Core=高精度桌宠");
            lines.Add("# 面向开发者=自定义文字");
            return lines.ToArray();
        }

        private static void EnsureMissingKeysAndSections(string path)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasReplace = false;
            var section = string.Empty;

            foreach (var sourceLine in lines)
            {
                var trimmed = (sourceLine ?? string.Empty).Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal) && trimmed.Length > 2)
                {
                    section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    if (section.Equals("Replace", StringComparison.OrdinalIgnoreCase)) hasReplace = true;
                    continue;
                }
                if (section.Equals("Replace", StringComparison.OrdinalIgnoreCase)) continue;
                var separator = FindUnescapedEquals(sourceLine ?? string.Empty);
                if (separator <= 0) continue;
                var key = Unescape(sourceLine.Substring(0, separator).Trim());
                if (key.Length > 0) known.Add(key);
            }

            var missing = DefaultText.Where(entry => !known.Contains(entry.Key)).ToList();
            if (missing.Count == 0 && hasReplace) return;

            lines.Add(string.Empty);
            if (missing.Count > 0)
            {
                lines.Add("# 自动补充的新版本可配置文字；已有值不会被覆盖。 ");
                lines.Add("[Text]");
                foreach (var entry in missing) lines.Add(entry.Key + "=" + Escape(entry.Value));
            }
            if (!hasReplace)
            {
                lines.Add(string.Empty);
                lines.Add("[Replace]");
                lines.Add("# 历史/全局兜底：原文=新文");
                lines.Add("# GGman=我的程序");
            }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private static int FindUnescapedEquals(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] != '=') continue;
                var slashCount = 0;
                for (var scan = index - 1; scan >= 0 && value[scan] == '\\'; scan--) slashCount++;
                if (slashCount % 2 == 0) return index;
            }
            return -1;
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (current != '\\' || index + 1 >= value.Length)
                {
                    builder.Append(current);
                    continue;
                }

                var next = value[++index];
                switch (next)
                {
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case '=': builder.Append('='); break;
                    case '\\': builder.Append('\\'); break;
                    default:
                        builder.Append('\\');
                        builder.Append(next);
                        break;
                }
            }
            return builder.ToString();
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("=", "\\=");
        }

        private static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }
    }
}
