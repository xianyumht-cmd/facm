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

            Pair(UiTextKeys.UnifiedSyncSettings, "GGman 软件偏好"),
            Pair(UiTextKeys.UnifiedSyncText, "界面文字"),
            Pair(UiTextKeys.UnifiedSyncEsc, "游戏 ESC"),
            Pair(UiTextKeys.GameSyncTitle, "英雄联盟游戏设置 · 主动上传，登录后自动恢复"),
            Pair(UiTextKeys.GameSyncAutoRestore, "登录后自动恢复云端游戏设置"),
            Pair(UiTextKeys.GameSyncPublish, "上传大厅与 ESC 设置"),
            Pair(UiTextKeys.GameSyncDetail, "仅同步指定大厅偏好与 ESC 设置，不上传账号或安装标识。"),
            Pair(UiTextKeys.GameSyncConfirm, "将当前已保存的 ESC 设置和大厅偏好上传到 GGman 云端？这会覆盖此前主动上传的游戏配置。请先结束训练或正式对局，回到大厅即可上传，无需关闭大厅。"),
            Pair(UiTextKeys.GameSyncUploading, "正在校验并上传游戏配置……"),
            Pair(UiTextKeys.GameSyncUploaded, "游戏配置已保存到云端，版本 {0}。"),
            Pair(UiTextKeys.GameSyncFailed, "上传未完成：{0}"),
            Pair(UiTextKeys.AutoSyncEnabled, "自动同步程序配置"),
            Pair(UiTextKeys.AutoSyncWaiting, "已开启；登录 GGman 邮箱后开始同步。"),
            Pair(UiTextKeys.AutoSyncReady, "自动同步已开启，仅在配置发生变化时同步。"),
            Pair(UiTextKeys.AutoSyncWorking, "正在后台同步配置……"),
            Pair(UiTextKeys.AutoSyncRetrying, "网络或配置暂时不可用，将自动重试。"),
            Pair(UiTextKeys.AutoSyncOff, "自动同步已关闭，本地设置不受影响。"),
            Pair(UiTextKeys.AutoSyncConflict, "{0} 发现不同配置，已暂停该项自动覆盖。"),
            Pair(UiTextKeys.AutoSyncKeepLocal, "保留本机配置"),
            Pair(UiTextKeys.AutoSyncUseCloud, "采用云端配置"),
            Pair(UiTextKeys.AutoSyncConflictHint, "只有发生版本冲突时才需要选择；其他配置仍正常自动同步。"),
            Pair(UiTextKeys.AutoSyncDescription, "云端已有备份时优先恢复；主动保存的程序偏好和文字自动上传；游戏配置只手动上传。"),
            Pair(UiTextKeys.AutoSyncNoRemote, "{0}：云端尚未创建备份。点击「创建云端备份」保存本机设置。"),
            Pair(UiTextKeys.AutoSyncNoRemoteAction, "创建云端备份"),
            Pair(UiTextKeys.AutoSyncConflictConfirm, "这项本机配置与云端不同，确定采用云端版本吗？当前本地文件会先备份。"),
            Pair(UiTextKeys.UnifiedSyncTitle, "云端配置中心"),
            Pair(UiTextKeys.UnifiedSyncUpload, "上传全部配置"),
            Pair(UiTextKeys.UnifiedSyncRestore, "恢复云端配置"),
            Pair(UiTextKeys.UnifiedSyncHint, "统一同步软件偏好、文字自定义和游戏 ESC；大厅 YAML 安全字段仍在规划中。"),
            Pair(UiTextKeys.UnifiedSyncSignedOut, "登录 GGman 邮箱后即可同步配置。"),
            Pair(UiTextKeys.UnifiedSyncReady, "上传和恢复均需要确认；各配置独立保存，出现部分失败会单独提示。"),
            Pair(UiTextKeys.UnifiedSyncWorking, "正在处理云端配置，请稍候……"),
            Pair(UiTextKeys.UnifiedSyncUnsaved, "界面文字还有未保存的修改，请先在文字编辑器点击“保存本地”再同步。"),
            Pair(UiTextKeys.UnifiedSyncConfirmUpload, "确定上传到当前 GGman 邮箱吗？\r\n软件偏好：1 份；文字自定义：1 份；ESC：{0}\r\n上传会更新云端版本，之前的云端配置可能被替换。"),
            Pair(UiTextKeys.UnifiedSyncConfirmRestore, "确定恢复云端配置到这台电脑吗？\r\n云端可用：{0}\r\n{1}\r\n恢复前会分别备份现有本地配置，部分设置可能需要重新打开界面或游戏才能生效。"),
            Pair(UiTextKeys.UnifiedSyncUploadResult, "本次上传：成功 {0} 项，跳过 {1} 项。{2}"),
            Pair(UiTextKeys.UnifiedSyncRestoreResult, "本次恢复：成功 {0} 项，跳过 {1} 项。{2}"),
            Pair(UiTextKeys.UnifiedSyncError, "配置同步中断：已处理 {0} 项；错误：{1}。其他配置未继续处理。"),
            Pair(UiTextKeys.UnifiedSyncNoCloud, "云端暂时没有可以恢复的配置。"),
            Pair(UiTextKeys.UnifiedSyncNoGame, "未找到 LOL 游戏内 ESC 配置，本次跳过 ESC，不会清空云端已有备份。"),
            Pair(UiTextKeys.UnifiedSyncCancelled, "已取消同步。"),
            Pair(UiTextKeys.UnifiedSyncSessionChanged, "GGman 登录身份已变化，本次同步已取消。"),
            Pair(UiTextKeys.UiTextEditorTitle, "界面文字自定义"),
            Pair(UiTextKeys.UiTextEditorExpand, "展开编辑"),
            Pair(UiTextKeys.UiTextEditorCollapse, "收起"),
            Pair(UiTextKeys.UiTextEditorSearch, "搜索文字、功能或配置项"),
            Pair(UiTextKeys.UiTextEditorTextMode, "界面文字"),
            Pair(UiTextKeys.UiTextEditorReplaceMode, "高级替换"),
            Pair(UiTextKeys.UiTextEditorDefault, "程序默认文字"),
            Pair(UiTextKeys.UiTextEditorValue, "修改后的文字"),
            Pair(UiTextKeys.UiTextEditorSource, "需要替换的原文"),
            Pair(UiTextKeys.UiTextEditorSave, "保存本地"),
            Pair(UiTextKeys.UiTextEditorRestore, "恢复默认 / 删除规则"),
            Pair(UiTextKeys.UiTextEditorAddRule, "新增替换"),
            Pair(UiTextKeys.UiTextEditorCloudUpload, "上传云端"),
            Pair(UiTextKeys.UiTextEditorCloudRestore, "云端恢复"),
            Pair(UiTextKeys.UiTextEditorReady, "从左侧选择一项文字即可编辑；保存后界面会自动刷新。"),
            Pair(UiTextKeys.UiTextEditorSaved, "文字已经保存到本地；修改前的 ui-text.ini 已备份。"),
            Pair(UiTextKeys.UiTextEditorRestored, "云端文字已恢复到本机；旧配置已备份。"),
            Pair(UiTextKeys.UiTextEditorError, "文字修改未完成：{0}"),
            Pair(UiTextKeys.UiTextEditorCloudConfirm, "确定把本机文字配置上传至当前 GGman 邮箱账号？\\r\\n云端版本：{0}\\r\\n自定义文字：{1} 项；替换规则：{2} 项。\\r\\n将替换该账号现有的云端版本。"),
            Pair(UiTextKeys.UiTextEditorCloudRestoreConfirm, "确定使用云端文字覆盖本机文字自定义？\\r\\n云端版本：{0}\\r\\n上传时间：{1}\\r\\n文字：{2} 项；替换规则：{3} 项。\\r\\n原本的 ui-text.ini 会先备份。"),
            Pair(UiTextKeys.UiTextEditorUploaded, "已上传云端，当前版本：{0}。"),
            Pair(UiTextKeys.UiTextEditorCloudMissing, "当前邮箱账号尚无文字自定义云备份。"),
            Pair(UiTextKeys.UiTextEditorCloudScope, "仅同步文字覆盖项及全局替换规则；不会上传账号令牌、游戏路径或软件其他设置。"),
            Pair(UiTextKeys.UiTextEditorNoLogin, "登录 GGman 邮箱账号后可上传、恢复云端文字。"),
            Pair(UiTextKeys.UiTextEditorSelectEntry, "请先选择一项文字，或在高级替换中添加规则。"),
            Pair(UiTextKeys.UiTextEditorSaveBeforeCloud, "有尚未保存的文字修改。请先点击“保存本地”，再执行云端操作。"),
            Pair(UiTextKeys.UiTextEditorLeavePrompt, "界面文字仍有未保存的修改。\r\n选择“是”保存本地并继续；选择“否”放弃本次修改；选择“取消”返回编辑。"),
            Pair(UiTextKeys.UiTextEditorExternalConflict, "该项文字已被其他操作更新，请放弃当前修改或重新打开后再编辑。"),
            Pair(UiTextKeys.EscSettingsTitle, "ESC 设置备份"),
            Pair(UiTextKeys.EscSettingsHint, "云端保存和恢复游戏 ESC 配置。游戏运行时也可操作，设置可能需要重新进入游戏才能生效。"),
            Pair(UiTextKeys.EscSettingsFolder, "英雄联盟配置目录（自动识别，可手动调整）"),
            Pair(UiTextKeys.EscSettingsBrowse, "选择目录"),
            Pair(UiTextKeys.EscSettingsLocalSave, "本地备份"),
            Pair(UiTextKeys.EscSettingsLocalRestore, "本地恢复"),
            Pair(UiTextKeys.EscSettingsCloudUpload, "上传云端"),
            Pair(UiTextKeys.EscSettingsCloudRestore, "云端恢复"),
            Pair(UiTextKeys.EscSettingsCloudScope, "云端设置属于当前登录的 GGman 账号。运行中的游戏可能在退出时重新写入设置；云端上传的是磁盘上已保存的文件。"),
            Pair(UiTextKeys.EscSettingsReady, "正在等待自动识别游戏目录；如未找到可手动选择。"),
            Pair(UiTextKeys.EscSettingsBusy, "正在处理，请稍候..."),
            Pair(UiTextKeys.EscSettingsLocalSaved, "本地备份已保存：{0}"),
            Pair(UiTextKeys.EscSettingsRestoreConfirm, "确认覆盖当前 ESC 游戏设置？\r\n\r\n备份内容：{0}\r\n恢复前将保存旧配置。游戏运行时允许恢复，但可能在退出时覆盖新文件。"),
            Pair(UiTextKeys.EscSettingsRestored, "恢复成功。{0}"),
            Pair(UiTextKeys.EscSettingsUploadConfirm, "确定上传当前 ESC 设置？\r\n\r\n现有云端版本：{0}\r\n包含文件：{1}\r\n原版本会被替换；已另存本地备份。"),
            Pair(UiTextKeys.EscSettingsUploaded, "云端上传成功（版本 {0}），本地也已保存当前备份。"),
            Pair(UiTextKeys.EscSettingsCloudEmpty, "当前 GGman 账号还没有 ESC 云备份。"),
            Pair(UiTextKeys.EscSettingsOperationFailed, "操作未完成：{0}"),
            Pair(UiTextKeys.EscSettingsCancelled, "操作已取消，设置没有被恢复。"),
            Pair(UiTextKeys.EscSettingsPickBackup, "选择 ESC 备份文件"),
            Pair(UiTextKeys.EscSettingsCloudRestoreConfirm, "确定从云端恢复并覆盖当前 ESC 设置？\r\n云端版本：{0}\r\n上传时间：{1}\r\n包含文件：{2}\r\n恢复前自动备份旧配置。游戏运行中可能被客户端重新写回。"),
            Pair(UiTextKeys.EscSettingsSessionChanged, "GGman 登录状态已更改，ESC 云端操作已取消。"),
            Pair(UiTextKeys.EscSettingsProbe, "检查云端权限"),
            Pair(UiTextKeys.EscSettingsProbePassed, "已登录账号读取成功（备份：{0}），未携带凭据的请求已被服务器拒绝。"),
            Pair(UiTextKeys.EscSettingsProbeEmpty, "尚无备份"),
            Pair(UiTextKeys.EscSettingsProbeUnexpectedStatus, "无凭据请求未获得预期的拒绝结果（HTTP {0}），请先停止使用云端功能。"),
            Pair(UiTextKeys.EscSettingsDetect, "重新识别"),
            Pair(UiTextKeys.EscSettingsDetecting, "正在通过运行中的游戏、已有路径和注册表识别目录..."),
            Pair(UiTextKeys.EscSettingsDetected, "已自动识别 LOL 配置目录，可直接使用备份或恢复。"),
            Pair(UiTextKeys.EscSettingsNotFound, "没有找到有效的游戏配置目录，请点击“选择目录”。"),

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
            Pair(UiTextKeys.LeagueDashboardRespawnCountdown, "复活倒计时  {0} 秒"),

            Pair(UiTextKeys.LeaguePersonalStatsWindowTitle, "GGman · 我的档案"),
            Pair(UiTextKeys.LeaguePersonalStatsTitle, "我的 GGman"),
            Pair(UiTextKeys.LeaguePersonalStatsHint, "本地自动记录使用情况；登录后参与注册账号排行榜。旧设备历史可在“数据与隐私”中导入。"),
            Pair(UiTextKeys.LeaguePersonalStatsAccounts, "玩过的账号"),
            Pair(UiTextKeys.LeaguePersonalStatsActiveDays, "活跃天数"),
            Pair(UiTextKeys.LeaguePersonalStatsMemberSince, "加入 GGman"),
            Pair(UiTextKeys.LeaguePersonalStatsActivityFormat, "连续使用 {0} 天 · 近 7 天活跃 {1} 天 · 近 30 天活跃 {2} 天 · 本月新增 {3} 个账号"),
            Pair(UiTextKeys.LeaguePersonalStatsRanking, "GGman 账号排行榜"),
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
            Pair(UiTextKeys.RegisteredStatsPrivacy, "数据与隐私"),
            Pair(UiTextKeys.RegisteredStatsPrivacyHint, "默认保留本地使用记录；登录后参与注册账号排行。功能使用统计仍需单独授权。"),
            Pair(UiTextKeys.RegisteredStatsRankOptIn, "参与 GGman 注册账号排行榜"),
            Pair(UiTextKeys.RegisteredStatsUsageConsent, "允许发送匿名功能使用统计（不包含 LOL 账号信息）"),
            Pair(UiTextKeys.RegisteredStatsImport, "导入本机历史记录"),
            Pair(UiTextKeys.RegisteredStatsImportPrompt, "确定将这台电脑记录的 {0} 个历史账号及 {1} 个活跃日关联到当前 GGman 邮箱账号？\\r\\n\\r\\n如果这台电脑曾供别人使用，请不要导入。旧记录无法跨设备完全去重，不会直接累加进入排行。"),
            Pair(UiTextKeys.RegisteredStatsImportSuccess, "本机历史已登记到当前账号，旧文件保留不变。"),
            Pair(UiTextKeys.RegisteredStatsSave, "保存偏好"),
            Pair(UiTextKeys.RegisteredStatsSaved, "隐私设置已保存。"),
            Pair(UiTextKeys.RegisteredStatsLoginRequired, "请先登录 GGman 邮箱账号，再进行历史数据导入。"),
            Pair(UiTextKeys.RegisteredStatsPrivacyError, "操作未完成：{0}"),
            Pair(UiTextKeys.RegisteredStatsRankSignedOut, "登录 GGman 邮箱后参与账号排行"),
            Pair(UiTextKeys.RegisteredStatsRankLoading, "尚未获得该账号的排行榜偏好，暂不修改排行设置；使用统计授权仍可单独管理。"),


            Pair(UiTextKeys.LeaguePlayerMenu, "玩家主页"),
            Pair(UiTextKeys.LeaguePlayerWindowTitle, "GGman · 玩家主页"),
            Pair(UiTextKeys.LeaguePlayerTitle, "Player"),
            Pair(UiTextKeys.LeaguePlayerHint, "当前账号与最近对局；先显示账号，再渐进读取战绩。"),
            Pair(UiTextKeys.LeaguePlayerLoadingProfile, "正在读取当前账号..."),
            Pair(UiTextKeys.LeaguePlayerLoadingMatches, "正在读取最近对局..."),
            Pair(UiTextKeys.LeaguePlayerClientRequired, "请先登录英雄联盟客户端"),
            Pair(UiTextKeys.LeaguePlayerNoMatches, "暂未读取到最近对局"),
            Pair(UiTextKeys.LeaguePlayerRecentMatches, "最近对局"),
            Pair(UiTextKeys.LeaguePlayerFilterAll, "全部"),
            Pair(UiTextKeys.LeaguePlayerFilterWinLoss, "胜负"),
            Pair(UiTextKeys.LeaguePlayerFilterTimeRange, "时间"),
            Pair(UiTextKeys.LeaguePlayerFilterChampion, "本场英雄"),
            Pair(UiTextKeys.LeaguePlayerFilterPlayer, "参赛玩家"),
            Pair(UiTextKeys.LeaguePlayerAdvancedFilter, "高级筛选"),
            Pair(UiTextKeys.LeaguePlayerAdvancedActive, "高级筛选 · 已启用"),
            Pair(UiTextKeys.LeaguePlayerAdvancedTitle, "高级战绩筛选"),
            Pair(UiTextKeys.LeaguePlayerAdvancedHint, "按组组合 AND / OR / NOT，并与基础筛选叠加；仅过滤已加载战绩，缺失数据不会通过 NOT。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedAnd, "满足全部（AND）"),
            Pair(UiTextKeys.LeaguePlayerAdvancedOr, "满足任意（OR）"),
            Pair(UiTextKeys.LeaguePlayerAdvancedNot, "排除条件（NOT）"),
            Pair(UiTextKeys.LeaguePlayerAdvancedAddCondition, "添加条件"),
            Pair(UiTextKeys.LeaguePlayerAdvancedAddAnd, "添加 AND 组"),
            Pair(UiTextKeys.LeaguePlayerAdvancedAddOr, "添加 OR 组"),
            Pair(UiTextKeys.LeaguePlayerAdvancedAddNot, "添加 NOT 组"),
            Pair(UiTextKeys.LeaguePlayerAdvancedRemove, "删除选中"),
            Pair(UiTextKeys.LeaguePlayerAdvancedClear, "清空规则"),
            Pair(UiTextKeys.LeaguePlayerAdvancedCancel, "取消"),
            Pair(UiTextKeys.LeaguePlayerAdvancedApply, "应用筛选"),
            Pair(UiTextKeys.LeaguePlayerAdvancedMode, "游戏模式"),
            Pair(UiTextKeys.LeaguePlayerAdvancedQueue, "队列 ID"),
            Pair(UiTextKeys.LeaguePlayerAdvancedMap, "地图 ID"),
            Pair(UiTextKeys.LeaguePlayerAdvancedDuration, "时长范围（秒）"),
            Pair(UiTextKeys.LeaguePlayerAdvancedWin, "本人胜利"),
            Pair(UiTextKeys.LeaguePlayerAdvancedLoss, "本人失败"),
            Pair(UiTextKeys.LeaguePlayerAdvancedNormalLoss, "普通失败（非投降）"),
            Pair(UiTextKeys.LeaguePlayerAdvancedSurrenderLoss, "投降失败"),
            Pair(UiTextKeys.LeaguePlayerAdvancedRemake, "对局重开"),
            Pair(UiTextKeys.LeaguePlayerAdvancedAbort, "对局异常终止"),
            Pair(UiTextKeys.LeaguePlayerAdvancedMatched, "匹配对局"),
            Pair(UiTextKeys.LeaguePlayerAdvancedPve, "人机 / PvE 对局"),
            Pair(UiTextKeys.LeaguePlayerAdvancedChampion, "本场有英雄"),
            Pair(UiTextKeys.LeaguePlayerAdvancedPlayer, "本场有玩家 PUUID"),
            Pair(UiTextKeys.LeaguePlayerAdvancedKills, "本人击杀范围"),
            Pair(UiTextKeys.LeaguePlayerAdvancedDeaths, "本人死亡范围"),
            Pair(UiTextKeys.LeaguePlayerAdvancedAssists, "本人助攻范围"),
            Pair(UiTextKeys.LeaguePlayerAdvancedCs, "本人补刀范围"),
            Pair(UiTextKeys.LeaguePlayerAdvancedUnknown, "未知条件"),
            Pair(UiTextKeys.LeaguePlayerAdvancedEnterValue, "（请填写）"),
            Pair(UiTextKeys.LeaguePlayerAdvancedNotLimit, "NOT 组只允许一个子条件，请先选择其已有子条件。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedSizeLimit, "筛选条件数量或嵌套层级已达到上限。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedSelectHint, "选中一个条件后可修改类型及参数。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedModeHint, "填写原始游戏模式，如 ARAM / CLASSIC / KIWI，区分大小写。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedPlayerHint, "填写准确的玩家 PUUID，区分大小写；不支持玩家昵称猜测。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedResultHint, "选择结果、重开、异常终止或游戏类别；均无需参数，来源字段缺失时不误判。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedIdHint, "填写正整数 ID，例如队列 450、地图 12 或英雄 22。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedRangeHint, "填写 最小值,最大值，例如 600,1800；时长用秒，范围包含两端。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedInvalid, "筛选条件不完整"),
            Pair(UiTextKeys.LeaguePlayerAdvancedErrorRoot, "筛选根节点必须是 AND 或 OR。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedErrorComplex, "筛选条件过多、嵌套过深或存在循环引用。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedErrorNot, "NOT 条件组必须包含且只包含一个条件。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedErrorEmptyGroup, "非根条件组不能留空。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedErrorLeaf, "单个条件不能包含子条件。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedErrorId, "队列、地图和英雄需要填写正整数 ID。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedErrorRange, "范围必须填写两个非负整数：最小值,最大值（包含边界）。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedErrorIdentity, "游戏模式、玩家 PUUID 或位置不能为空、过长或包含控制字符。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedErrorUnsupported, "不支持的高级筛选条件。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedItem, "本人装备"),
            Pair(UiTextKeys.LeaguePlayerAdvancedSpell, "本人召唤师技能"),
            Pair(UiTextKeys.LeaguePlayerAdvancedPerk, "本人符文"),
            Pair(UiTextKeys.LeaguePlayerAdvancedPerkStyle, "本人符文流派"),
            Pair(UiTextKeys.LeaguePlayerAdvancedAugment, "本人海克斯强化"),
            Pair(UiTextKeys.LeaguePlayerAdvancedSlotHint, "填写正整数 ID；可附加 @槽位（从 0 开始），如 3340@0。不写槽位则匹配任意位置。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedErrorSlot, "请输入正整数 ID，或 ID@槽位；槽位不能超出相应条件范围。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedErrorDecimalRange, "数值范围需要两个有限的非负数字：最小值,最大值。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedDecimalHint, "填写最小值,最大值，可使用小数，如 2.5,8.0；范围包含两端。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedMeasureHint, "数值：2,10；占比：teamShare:20,80、teamMaxShare:20,80、gameShare:20,80、gameMaxShare:20,80。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedPercentHint, "输入百分数，不带 %，如 25,75；来源不足时不会匹配。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedPositionHint, "填写客户端返回的位置，如 TOP、JUNGLE、MIDDLE、BOTTOM、UTILITY。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedPosition, "本人位置"),
            Pair(UiTextKeys.LeaguePlayerAdvancedKda, "本人 KDA"),
            Pair(UiTextKeys.LeaguePlayerAdvancedLevel, "本人等级"),
            Pair(UiTextKeys.LeaguePlayerAdvancedGold, "本人获得金币"),
            Pair(UiTextKeys.LeaguePlayerAdvancedGoldSpent, "本人花费金币"),
            Pair(UiTextKeys.LeaguePlayerAdvancedDamage, "对英雄伤害"),
            Pair(UiTextKeys.LeaguePlayerAdvancedPhysicalDamage, "物理英雄伤害"),
            Pair(UiTextKeys.LeaguePlayerAdvancedMagicDamage, "魔法英雄伤害"),
            Pair(UiTextKeys.LeaguePlayerAdvancedTrueDamage, "真实英雄伤害"),
            Pair(UiTextKeys.LeaguePlayerAdvancedTakenDamage, "承受伤害"),
            Pair(UiTextKeys.LeaguePlayerAdvancedTakenPhysical, "承受物理伤害"),
            Pair(UiTextKeys.LeaguePlayerAdvancedTakenMagic, "承受魔法伤害"),
            Pair(UiTextKeys.LeaguePlayerAdvancedTakenTrue, "承受真实伤害"),
            Pair(UiTextKeys.LeaguePlayerAdvancedTowerDamage, "对塔伤害"),
            Pair(UiTextKeys.LeaguePlayerAdvancedHeal, "治疗量"),
            Pair(UiTextKeys.LeaguePlayerAdvancedVision, "视野分数"),
            Pair(UiTextKeys.LeaguePlayerAdvancedCc, "控制时间"),
            Pair(UiTextKeys.LeaguePlayerAdvancedSoloKills, "单杀数"),
            Pair(UiTextKeys.LeaguePlayerAdvancedParticipation, "参团率 (%)"),
            Pair(UiTextKeys.LeaguePlayerAdvancedEfficiency, "伤金效率 (%)"),
            Pair(UiTextKeys.LeaguePlayerAdvancedDamageShare, "英雄伤害占比 (%)"),
            Pair(UiTextKeys.LeaguePlayerAdvancedTakenShare, "承伤占比 (%)"),
            Pair(UiTextKeys.LeaguePlayerAdvancedGoldShare, "金币占比 (%)"),
            Pair(UiTextKeys.LeaguePlayerAdvancedDoubleKills, "双杀次数"),
            Pair(UiTextKeys.LeaguePlayerAdvancedTripleKills, "三杀次数"),
            Pair(UiTextKeys.LeaguePlayerAdvancedQuadraKills, "四杀次数"),
            Pair(UiTextKeys.LeaguePlayerAdvancedPentaKills, "五杀次数"),
            Pair(UiTextKeys.LeaguePlayerAdvancedScope, "玩家范围 ▾"),
            Pair(UiTextKeys.LeaguePlayerAdvancedPlayerScope, "指定玩家"),
            Pair(UiTextKeys.LeaguePlayerAdvancedAnyone, "任意玩家满足"),
            Pair(UiTextKeys.LeaguePlayerAdvancedEveryone, "所有玩家满足"),
            Pair(UiTextKeys.LeaguePlayerAdvancedAll, "全场玩家范围"),
            Pair(UiTextKeys.LeaguePlayerAdvancedAllies, "指定玩家同队"),
            Pair(UiTextKeys.LeaguePlayerAdvancedEnemies, "指定玩家敌队"),
            Pair(UiTextKeys.LeaguePlayerAdvancedScopeHint, "填写玩家 PUUID；self 表示本人。范围节点只能包含一组子条件。"),
            Pair(UiTextKeys.LeaguePlayerAdvancedScopeLimit, "NOT 和玩家范围节点只能添加一个直接子条件。"),
            Pair(UiTextKeys.LeaguePlayerFilterSelected, "已选 {0} 项"),
            Pair(UiTextKeys.LeaguePlayerFilterClear, "清空选择"),
            Pair(UiTextKeys.LeaguePlayerFilterSearchChampion, "搜索英雄名称或 ID"),
            Pair(UiTextKeys.LeaguePlayerFilterSearchPlayer, "搜索玩家名称或 PUUID"),
            Pair(UiTextKeys.LeaguePlayerRemoteLookup, "按 Riot ID 查找 (名称#标签)"),
            Pair(UiTextKeys.LeaguePlayerRemoteLoading, "正在查询..."),
            Pair(UiTextKeys.LeaguePlayerRemoteFound, "已找到，请勾选玩家"),
            Pair(UiTextKeys.LeaguePlayerRemoteInvalid, "输入名称#标签"),
            Pair(UiTextKeys.LeaguePlayerRemoteNotFound, "未找到该玩家"),
            Pair(UiTextKeys.LeaguePlayerRemoteAccountChanged, "账号已变化，请重试"),
            Pair(UiTextKeys.LeaguePlayerRemoteUnavailable, "Riot 查询暂不可用"),
            Pair(UiTextKeys.LeaguePlayerRemoteHistoryAction, "查看该玩家近期战绩"),
            Pair(UiTextKeys.LeaguePlayerReplayAction, "下载 / 观看回放"),
            Pair(UiTextKeys.LeaguePlayerCollectAction, "收集筛选战绩"),
            Pair(UiTextKeys.LeaguePlayerCollectTitle, "收集模式"),
            Pair(UiTextKeys.LeaguePlayerCollectHint, "按当前筛选条件逐页扫描。关闭窗口或点击停止即可中断；不修改原始战绩。"),
            Pair(UiTextKeys.LeaguePlayerCollectBatch, "每次扫描场数（1–200）"),
            Pair(UiTextKeys.LeaguePlayerCollectExpected, "目标收集场数（1–1000）"),
            Pair(UiTextKeys.LeaguePlayerCollectIterations, "最多扫描轮次（1–100）"),
            Pair(UiTextKeys.LeaguePlayerCollectStart, "开始收集"),
            Pair(UiTextKeys.LeaguePlayerCollectStop, "停止"),
            Pair(UiTextKeys.LeaguePlayerCollectStopping, "正在停止收集..."),
            Pair(UiTextKeys.LeaguePlayerCollectProgress, "已收集 {0} 场 · 扫描 {1} 场 · 第 {2} 轮"),
            Pair(UiTextKeys.LeaguePlayerCollectSummary, "收集结果 {0} 场 · 扫描 {1} 场 / {2} 轮"),
            Pair(UiTextKeys.LeaguePlayerCollectStopped, " · 已停止"),
            Pair(UiTextKeys.LeaguePlayerCollectUnavailable, " · 读取中断，可重新收集"),
            Pair(UiTextKeys.LeaguePlayerCollectBackToPage, "返回分页"),
            Pair(UiTextKeys.LeagueTimelineEventDescription, "{0} · {1}\r\n参与者：{2}　目标：{3}\r\n协助者：{4}"),
            Pair(UiTextKeys.LeagueTimelineUndoDescription, "装备变更：{0} → {1}"),
            Pair(UiTextKeys.LeagueTimelineSkillDescription, "升级技能槽位：{0}"),
            Pair(UiTextKeys.LeagueTimelineMetric, "对比指标"),
            Pair(UiTextKeys.LeagueTimelineComparison, "对比曲线"),
            Pair(UiTextKeys.LeagueTimelineDamage, "英雄伤害"),
            Pair(UiTextKeys.LeagueTimelineTaken, "承受伤害"),
            Pair(UiTextKeys.LeagueTimelineSelectPlayers, "全选参与者"),
            Pair(UiTextKeys.LeagueTimelineTeamAverage, "队伍 {0} 平均"),
            Pair(UiTextKeys.LeagueTimelineNoSamples, "当前指标暂无可用采样。"),
            Pair(UiTextKeys.LeagueFrameTitle, "时点属性"),
            Pair(UiTextKeys.LeagueFrameValue, "数值"),
            Pair(UiTextKeys.LeagueFrameUnavailable, "当前采样未提供英雄属性。"),
            Pair(UiTextKeys.LeagueFrameHealth, "生命 / 上限"),
            Pair(UiTextKeys.LeagueFrameHealthRegen, "生命恢复"),
            Pair(UiTextKeys.LeagueFramePower, "资源 / 上限"),
            Pair(UiTextKeys.LeagueFramePowerRegen, "资源恢复"),
            Pair(UiTextKeys.LeagueFrameAttackDamage, "攻击力"),
            Pair(UiTextKeys.LeagueFrameAttackSpeed, "攻击速度"),
            Pair(UiTextKeys.LeagueFrameAbilityPower, "法术强度"),
            Pair(UiTextKeys.LeagueFrameAbilityHaste, "技能急速"),
            Pair(UiTextKeys.LeagueFrameCooldown, "冷却缩减"),
            Pair(UiTextKeys.LeagueFrameArmor, "护甲"),
            Pair(UiTextKeys.LeagueFrameMagicResist, "魔法抗性"),
            Pair(UiTextKeys.LeagueFrameArmorPen, "护甲穿透"),
            Pair(UiTextKeys.LeagueFrameArmorPenPercent, "护甲穿透比例"),
            Pair(UiTextKeys.LeagueFrameBonusArmorPen, "额外护甲穿透比例"),
            Pair(UiTextKeys.LeagueFrameMagicPen, "法术穿透"),
            Pair(UiTextKeys.LeagueFrameMagicPenPercent, "法术穿透比例"),
            Pair(UiTextKeys.LeagueFrameBonusMagicPen, "额外魔抗穿透比例"),
            Pair(UiTextKeys.LeagueFrameMoveSpeed, "移动速度"),
            Pair(UiTextKeys.LeagueFrameLifesteal, "生命偷取"),
            Pair(UiTextKeys.LeagueFramePhysicalVamp, "物理吸血"),
            Pair(UiTextKeys.LeagueFrameSpellVamp, "法术吸血"),
            Pair(UiTextKeys.LeagueFrameOmnivamp, "全能吸血"),
            Pair(UiTextKeys.LeagueFrameCcReduction, "控制减免"),
            Pair(UiTextKeys.LeagueTimelineAnvils, "强化砧 {0} 次"),
            Pair(UiTextKeys.LeagueTimelinePurchaseGroup, "购买组 {0} · {1}"),
            Pair(UiTextKeys.LeagueTimelineEvolve, "进化 {0}"),
            Pair(UiTextKeys.LeagueTimelineSkillLevel, "等级 {0} · {1}"),
            Pair(UiTextKeys.LeagueTimelineTitle, "时间线与出装过程"),
            Pair(UiTextKeys.LeagueTimelineParticipants, "对局统计"),
            Pair(UiTextKeys.LeagueTimelinePlayer, "参与者"),
            Pair(UiTextKeys.LeagueTimelineAllPlayers, "全部参与者"),
            Pair(UiTextKeys.LeagueTimelineEventType, "事件类型"),
            Pair(UiTextKeys.LeagueTimelineAllEvents, "全部事件"),
            Pair(UiTextKeys.LeagueTimelineTime, "时间"),
            Pair(UiTextKeys.LeagueTimelineLevel, "等级"),
            Pair(UiTextKeys.LeagueTimelineGold, "总金币"),
            Pair(UiTextKeys.LeagueTimelineCurrentGold, "现有金币"),
            Pair(UiTextKeys.LeagueTimelineXp, "经验"),
            Pair(UiTextKeys.LeagueDeathDamageTitle, "死亡伤害详情"),
            Pair(UiTextKeys.LeagueDamageReceived, "死者承受伤害"),
            Pair(UiTextKeys.LeagueDamageDealt, "死者造成伤害"),
            Pair(UiTextKeys.LeagueDamageTotals, "合计 {0} · 物理 {1} / 魔法 {2} / 真实 {3}"),
            Pair(UiTextKeys.LeagueDamagePeer, "伤害来源 / 目标"),
            Pair(UiTextKeys.LeagueDamageAbility, "攻击 / 技能"),
            Pair(UiTextKeys.LeagueDamageTotal, "合计"),
            Pair(UiTextKeys.LeagueDamagePhysical, "物理"),
            Pair(UiTextKeys.LeagueDamageMagic, "魔法"),
            Pair(UiTextKeys.LeagueDamageTrue, "真实"),
            Pair(UiTextKeys.LeagueDamageTower, "防御塔"),
            Pair(UiTextKeys.LeagueDamageMinion, "小兵"),
            Pair(UiTextKeys.LeagueDamageMonster, "野怪"),
            Pair(UiTextKeys.LeagueDamageOther, "其他 / 未知来源"),
            Pair(UiTextKeys.LeagueDamageUnavailable, "没有可用的伤害记录"),
            Pair(UiTextKeys.LeagueEventMap, "事件位置"),
            Pair(UiTextKeys.LeagueEventMapMissing, "此事件没有可用的位置"),
            Pair(UiTextKeys.LeagueEventMapUnsupported, "此地图暂不支持位置视图"),
            Pair(UiTextKeys.LeagueProfileDetailsTitle, "玩家资料详情"),
            Pair(UiTextKeys.LeagueProfileField, "项目"),
            Pair(UiTextKeys.LeagueProfileSolo, "单双排位"),
            Pair(UiTextKeys.LeagueProfileFlex, "灵活组排"),
            Pair(UiTextKeys.LeagueProfileRanked, "资料与排位"),
            Pair(UiTextKeys.LeagueProfileQueue, "排位队列"),
            Pair(UiTextKeys.LeagueProfileMastery, "英雄熟练度"),
            Pair(UiTextKeys.LeagueProfileMasterySearch, "筛选英雄名称或 ID"),
            Pair(UiTextKeys.LeagueProfileMasteryLevel, "熟练度等级"),
            Pair(UiTextKeys.LeagueProfileMasteryPoints, "熟练度点数"),
            Pair(UiTextKeys.LeagueProfileMasteryLast, "最后使用"),
            Pair(UiTextKeys.LeagueProfileBackground, "生涯背景皮肤 ID：{0}"),
            Pair(UiTextKeys.LeagueProfileTier, "当前段位"),
            Pair(UiTextKeys.LeagueProfileLp, "胜点"),
            Pair(UiTextKeys.LeagueProfileWins, "胜场"),
            Pair(UiTextKeys.LeagueProfileLosses, "负场"),
            Pair(UiTextKeys.LeagueProfileRating, "评级分数"),
            Pair(UiTextKeys.LeagueProfileProvisional, "定位赛剩余场次"),
            Pair(UiTextKeys.LeagueProfileRewardWins, "赛季奖励胜场"),
            Pair(UiTextKeys.LeagueProfileHighest, "赛季最高段位"),
            Pair(UiTextKeys.LeagueProfilePrevious, "上赛季结束段位"),
            Pair(UiTextKeys.LeagueProfilePreviousHighest, "上赛季最高段位"),
            Pair(UiTextKeys.LeagueProfileWinrate, "胜率"),
            Pair(UiTextKeys.LeagueProfileMasteryProgress, "本级已获点数 {0} · 升级还需 {1}"),
            Pair(UiTextKeys.LeagueProfileMasteryMarks, "已获标记 {0} · 下一级要求 {1}"),
            Pair(UiTextKeys.LeagueProfileMasterySeason, "赛季里程碑 {0} · 最高评分 {1}"),
            Pair(UiTextKeys.LeagueProfileUnranked, "未定级"),
            Pair(UiTextKeys.LeagueProfileTiers, "黑铁|青铜|白银|黄金|铂金|翡翠|钻石|大师|宗师|王者"),
            Pair(UiTextKeys.LeagueProfileUnsupported, "此区域不支持排位、生涯装饰或英雄熟练度查询"),
            Pair(UiTextKeys.LeagueProfileReady, "资料已更新"),
            Pair(UiTextKeys.LeagueProfilePartial, "部分资料暂不可用，缺失字段显示为 —"),
            Pair(UiTextKeys.LeagueProfileChallenges, "挑战与收藏"),
            Pair(UiTextKeys.LeagueProfileChallengeValue, "当前数值"),
            Pair(UiTextKeys.LeagueProfileChallengePoints, "挑战总点数"),
            Pair(UiTextKeys.LeagueProfileChallengeLevel, "挑战等级"),
            Pair(UiTextKeys.LeagueProfileOwnedChampions, "拥有英雄"),
            Pair(UiTextKeys.LeagueProfileOwnedSkins, "拥有皮肤"),
            Pair(UiTextKeys.LeagueProfileOwnedChromas, "拥有炫彩"),
            Pair(UiTextKeys.LeagueProfileOwnedWards, "拥有守卫皮肤"),
            Pair(UiTextKeys.LeagueProfileOwnedIcons, "拥有召唤师图标"),
            Pair(UiTextKeys.LeagueProfileOwnedEmotes, "拥有表情"),
            Pair(UiTextKeys.LeagueProfilePentas, "五杀次数"),
            Pair(UiTextKeys.LeagueProfileMasteryAll, "显示全部英雄"),
            Pair(UiTextKeys.LeagueProfileMasteryTop, "熟练度前八 · 总评分 {0}"),
            Pair(UiTextKeys.LeagueProfileMasteryCount, "{0}/{1} 英雄"),
            Pair(UiTextKeys.LeagueProfileMasteryScore, "熟练度总评分 {0}"),
            Pair(UiTextKeys.LeagueTimelineEvents, "对局事件"),
            Pair(UiTextKeys.LeagueTimelineProgression, "成长记录"),
            Pair(UiTextKeys.LeagueTimelineBuilds, "装备变化"),
            Pair(UiTextKeys.LeagueTimelineDetails, "事件详情"),
            Pair(UiTextKeys.LeagueTimelineUnavailable, "暂时无法读取时间线，可点击重试。"),
            Pair(UiTextKeys.LeagueTimelineLoading, "正在读取时间线…"),
            Pair(UiTextKeys.LeagueTimelineSummary, "{0} 个时间采样 · {1} 条所选事件"),
            Pair(UiTextKeys.LeagueTimelineTarget, "目标"),
            Pair(UiTextKeys.LeagueTimelineKill, "击杀英雄"),
            Pair(UiTextKeys.LeagueTimelineMonster, "击杀野怪"),
            Pair(UiTextKeys.LeagueTimelineBuilding, "摧毁建筑"),
            Pair(UiTextKeys.LeagueTimelineItemBuy, "购买装备"),
            Pair(UiTextKeys.LeagueTimelineItemSell, "出售装备"),
            Pair(UiTextKeys.LeagueTimelineItemDestroy, "移除装备"),
            Pair(UiTextKeys.LeagueTimelineItemUndo, "撤销购买"),
            Pair(UiTextKeys.LeagueTimelineSkill, "升级技能"),
            Pair(UiTextKeys.LeaguePlayerMatchDetail, "对局详情"),
            Pair(UiTextKeys.LeaguePlayerMatchDetailLoading, "正在读取对局详情…"),
            Pair(UiTextKeys.LeaguePlayerMatchDetailUnavailable, "暂时无法读取这场对局的详情，请稍后重试。"),
            Pair(UiTextKeys.LeaguePlayerMatchDetailReady, "已读取对局详情。"),
            Pair(UiTextKeys.LeaguePlayerHistoryBrowse, "分页与区域战绩"),
            Pair(UiTextKeys.LeaguePlayerHistorySource, "来源"),
            Pair(UiTextKeys.LeaguePlayerHistorySourceAuto, "自动（本地优先）"),
            Pair(UiTextKeys.LeaguePlayerHistorySourceLcu, "本地客户端"),
            Pair(UiTextKeys.LeaguePlayerHistorySourceSgp, "区域服务"),
            Pair(UiTextKeys.LeaguePlayerHistoryRegion, "大区"),
            Pair(UiTextKeys.LeaguePlayerHistoryCurrentRegion, "当前客户端大区"),
            Pair(UiTextKeys.LeaguePlayerRemoteHistoryTitle, "玩家资料与近期战绩"),
            Pair(UiTextKeys.LeaguePlayerRemoteNoMatches, "暂无可读取的近期战绩"),
            Pair(UiTextKeys.LeaguePlayerRemoteRecentCount, "最近读取 {0} 场 · 当前客户端数据"),
            Pair(UiTextKeys.LeaguePlayerRemoteHistoryUnavailable, "当前客户端暂无法读取该玩家战绩"),
            Pair(UiTextKeys.LeaguePlayerRemoteHistoryNotFound, "当前客户端未找到该玩家资料"),
            Pair(UiTextKeys.LeaguePlayerRemotePageSize, "每页"),
            Pair(UiTextKeys.LeaguePlayerRemotePrevPage, "上一页"),
            Pair(UiTextKeys.LeaguePlayerRemoteNextPage, "下一页"),
            Pair(UiTextKeys.LeaguePlayerRemoteGoPage, "跳转"),
            Pair(UiTextKeys.LeaguePlayerRemotePageLabel, "第 {0} 页"),
            Pair(UiTextKeys.LeaguePlayerRemoteRetry, "重试"),
            Pair(UiTextKeys.LeaguePlayerFilter3Hours, "3 小时内"),
            Pair(UiTextKeys.LeaguePlayerFilter12Hours, "12 小时内"),
            Pair(UiTextKeys.LeaguePlayerFilter24Hours, "24 小时内"),
            Pair(UiTextKeys.LeaguePlayerFilter3Days, "3 天内"),
            Pair(UiTextKeys.LeaguePlayerFilter7Days, "7 天内"),
            Pair(UiTextKeys.LeaguePlayerFilter30Days, "30 天内"),
            Pair(UiTextKeys.LeaguePlayerFilteredCount, "符合筛选 {0} 场 / 已加载 {1} 场"),
            Pair(UiTextKeys.LeaguePlayerNoFilteredMatches, "已加载战绩，但暂无符合筛选的对局"),
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
            Pair(UiTextKeys.LeaguePlayerRemake, "重开"),
            Pair(UiTextKeys.LeaguePlayerAbort, "异常终止"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHydrate, "补全分析数据（最多20场）"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHydrateScope, "仅补全当前样本；每批最多20场、24秒。同一窗口不重复请求已尝试的对局。"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHydrating, "正在读取对局详情，可随时停止…"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHydrated, "已更新 {0} 场 / 尝试 {1} 场。{2} 未提供的统计仍显示为未知。"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHydrateTimeout, "部分请求超时。"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHydrateStopped, "已停止，本次未应用的结果已丢弃。"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHydrateFailed, "补全失败，当前样本未更改。"),
            Pair(UiTextKeys.LeaguePlayerOverviewTitle, "英雄分析总览"),
            Pair(UiTextKeys.LeaguePlayerOverviewPerformance, "表现"),
            Pair(UiTextKeys.LeaguePlayerOverviewCombat, "伤害与战斗"),
            Pair(UiTextKeys.LeaguePlayerOverviewEconomy, "经济与发育"),
            Pair(UiTextKeys.LeaguePlayerOverviewSummary, "{0} · {1} 场 · {2} 胜 {3} 负 · 胜率 {4}"),
            Pair(UiTextKeys.LeaguePlayerCompareTitle, "英雄比较"),
            Pair(UiTextKeys.LeaguePlayerCompareScope, "仅比较当前样本；括号为可用/有效场次。缺失不算零；单杀、信号需完整覆盖，LCU 无信号数据。"),
            Pair(UiTextKeys.LeaguePlayerCompareMetric, "统计指标"),
            Pair(UiTextKeys.LeaguePlayerCompareLeft, "左侧英雄"),
            Pair(UiTextKeys.LeaguePlayerCompareRight, "右侧英雄"),
            Pair(UiTextKeys.LeaguePlayerCompareScore, "Akari 综合评分 / 17"),
            Pair(UiTextKeys.LeaguePlayerCompareKda, "累计 KDA 比"),
            Pair(UiTextKeys.LeaguePlayerCompareKdaVariation, "逐场 KDA 波动系数"),
            Pair(UiTextKeys.LeaguePlayerCompareWinRate, "胜率"),
            Pair(UiTextKeys.LeaguePlayerCompareParticipation, "参团率"),
            Pair(UiTextKeys.LeaguePlayerCompareVision, "场均视野得分"),
            Pair(UiTextKeys.LeaguePlayerCompareVisionShare, "视野占团队比例"),
            Pair(UiTextKeys.LeaguePlayerCompareDamageMinute, "场均每分钟英雄伤害"),
            Pair(UiTextKeys.LeaguePlayerCompareDamageShare, "英雄伤害占团队比例"),
            Pair(UiTextKeys.LeaguePlayerCompareDamageTeamMax, "英雄伤害 / 队内最高"),
            Pair(UiTextKeys.LeaguePlayerCompareDamageGameMax, "英雄伤害 / 全场最高"),
            Pair(UiTextKeys.LeaguePlayerCompareTakenShare, "承伤占团队比例"),
            Pair(UiTextKeys.LeaguePlayerCompareTakenTeamMax, "承伤 / 队内最高"),
            Pair(UiTextKeys.LeaguePlayerCompareTakenGameMax, "承伤 / 全场最高"),
            Pair(UiTextKeys.LeaguePlayerCompareDamageGold, "伤害 / 金币"),
            Pair(UiTextKeys.LeaguePlayerCompareKillDamageEfficiency, "击杀 / 伤害效率"),
            Pair(UiTextKeys.LeaguePlayerCompareSoloKills, "场均单杀"),
            Pair(UiTextKeys.LeaguePlayerComparePings, "场均信号次数（SGP）"),
            Pair(UiTextKeys.LeaguePlayerCompareMissingPings, "场均敌人消失信号（SGP）"),
            Pair(UiTextKeys.LeaguePlayerCompareGoldShare, "金币占团队比例"),
            Pair(UiTextKeys.LeaguePlayerCompareGoldTeamMax, "金币 / 队内最高"),
            Pair(UiTextKeys.LeaguePlayerCompareGoldGameMax, "金币 / 全场最高"),
            Pair(UiTextKeys.LeaguePlayerCompareCsMinute, "场均每分钟补刀"),
            Pair(UiTextKeys.LeaguePlayerCompareCsShare, "补刀占团队比例"),
            Pair(UiTextKeys.LeaguePlayerCompareCsTeamMax, "补刀 / 队内最高"),
            Pair(UiTextKeys.LeaguePlayerCompareCsGameMax, "补刀 / 全场最高"),
            Pair(UiTextKeys.LeaguePlayerCompareTowerShare, "防御塔伤害占团队比例"),
            Pair(UiTextKeys.LeaguePlayerCompareTowerTeamMax, "防御塔伤害 / 队内最高"),
            Pair(UiTextKeys.LeaguePlayerCompareTowerGameMax, "防御塔伤害 / 全场最高"),
            Pair(UiTextKeys.LeagueJungleTitle, "打野路线分析"),
            Pair(UiTextKeys.LeagueJungleScope, "仅分析已加载的召唤师峡谷打野时间线。采用前14个分钟采样及前14分钟击杀参与（权重5）；营地和早期抓人为来源规则推断，不代表实际清野或移动次数。"),
            Pair(UiTextKeys.LeagueJungleUnavailable, "请选择有完整身份、地图和打野依据的玩家，并加载时间线。"),
            Pair(UiTextKeys.LeagueJungleCoverage, "可用位置采样：{0}/{1}；缺失位置的击杀参与：{2}。比例只覆盖已知位置。"),
            Pair(UiTextKeys.LeagueJungleFirstCamp, "首分钟位置最近营地（推断）：{0}"),
            Pair(UiTextKeys.LeagueJungleEarly, "三级抓人迹象：{0}；3–4分钟抓人迹象：{1}"),
            Pair(UiTextKeys.LeagueJungleZone, "{0}：加权位置占比 {1}；击杀参与型抓人 {2} 次"),
            Pair(UiTextKeys.LeagueJungleDetected, "检测到"),
            Pair(UiTextKeys.LeagueJungleNotDetected, "未检测到"),
            Pair(UiTextKeys.LeagueJungleCampBlue, "蓝BUFF"),
            Pair(UiTextKeys.LeagueJungleCampWolves, "三狼"),
            Pair(UiTextKeys.LeagueJungleCampRed, "红BUFF"),
            Pair(UiTextKeys.LeagueJungleCampRaptors, "锋喙鸟"),
            Pair(UiTextKeys.LeagueJungleBlueSide, "蓝方野区"),
            Pair(UiTextKeys.LeagueJungleRedSide, "红方野区"),
            Pair(UiTextKeys.LeagueJungleObjectivesScope, "资源统计覆盖已加载的整场事件；未知归属不计入己方，时间为己方首次获得该资源。"),
            Pair(UiTextKeys.LeagueJungleFirstDragon, "首龙归己方记录：{0}；本人单杀小龙：{1}/{2}；归属未知事件：{3}"),
            Pair(UiTextKeys.LeagueJungleObjectiveRow, "{0}：{1}；首次时间 {2}"),
            Pair(UiTextKeys.LeagueJungleDragons, "小龙"),
            Pair(UiTextKeys.LeagueJungleGrubs, "巢虫"),
            Pair(UiTextKeys.LeagueJungleHeralds, "峡谷先锋"),
            Pair(UiTextKeys.LeagueJungleBarons, "纳什男爵"),
            Pair(UiTextKeys.LeagueJungleHistoryTitle, "多场打野统计"),
            Pair(UiTextKeys.LeagueJungleHistoryLoad, "补全8场打野时间线"),
            Pair(UiTextKeys.LeagueJungleHistoryLoading, "正在按顺序读取所选玩家的打野时间线…"),
            Pair(UiTextKeys.LeagueJungleHistoryDone, "已分析 {0} 场，尝试读取 {1} 场；可再次点击继续。"),
            Pair(UiTextKeys.LeagueJungleHistoryScope, "仅当前筛选战绩的召唤师峡谷打野场次；手动分批读取，不自动刷新。营地、三级/四级抓人均为来源算法推断。"),
            Pair(UiTextKeys.LeagueJungleHistoryCoverage, "成功 {0}/{1} 场候选；尝试 {2} 场；最多保留 {3} 场。缺失时间线不补零。"),
            Pair(UiTextKeys.LeagueJungleHistoryUnavailable, "尚无可分析的时间线。点击下方按钮分批补全（8场/轮，总预算48秒）。"),
            Pair(UiTextKeys.LeagueJungleHistoryLane, "{0}：位置加权 {1}；击杀参与抓人 {2} 次（位置覆盖 {3} 场）"),
            Pair(UiTextKeys.LeagueJungleHistoryPreference, "区域倾向：{0}"),
            Pair(UiTextKeys.LeagueJungleHistoryZoneWeights, "分路位置权重：{0} {1} | {2} {3} | {4} {5}"),
            Pair(UiTextKeys.LeagueJungleHistoryPreferenceTop, "偏上路"),
            Pair(UiTextKeys.LeagueJungleHistoryPreferenceMid, "偏中路"),
            Pair(UiTextKeys.LeagueJungleHistoryPreferenceBot, "偏下路"),
            Pair(UiTextKeys.LeagueJungleHistoryPreferenceTopMid, "偏上中"),
            Pair(UiTextKeys.LeagueJungleHistoryPreferenceMidBot, "偏中下"),
            Pair(UiTextKeys.LeagueJungleHistoryPreferenceBalanced, "分布均衡"),
            Pair(UiTextKeys.LeagueJungleHistoryPreferenceUnknown, "位置数据不足"),
            Pair(UiTextKeys.LeagueJungleHistoryEarly, "三级抓人 {0}/{1}；四级抓人 {2}/{3}；缺位置的击杀参与 {4} 次"),
            Pair(UiTextKeys.LeagueJungleHistoryBySide, "{0} {1} 场：三级迹象 {2}/{3}；四级迹象 {4}/{5}（未知不计失败）"),
            Pair(UiTextKeys.LeagueJungleHistoryReportTab, "统计摘要"),
            Pair(UiTextKeys.LeagueJungleMapTitle, "位置分布"),
            Pair(UiTextKeys.LeagueJungleMapEmpty, "暂无已验证的位置坐标；可按需读取打野时间线。"),
            Pair(UiTextKeys.LeagueJungleMapLegend, "红/黄/蓝圆点为上/中/下路前14分钟位置采样，同色X为已判定分路的抓人落点；绿圈为3分钟前击杀参与，紫圈为3—4分钟击杀参与。散点不代表完整轨迹或热度。"),
            Pair(UiTextKeys.LeagueJungleMapCoverage, "位置{0} 抓人X{1} 绿圈3级{2} 紫圈4级{3} 溢出{4}"),
            Pair(UiTextKeys.LeagueJungleMapHeatmapToggle, "热度分布"),
            Pair(UiTextKeys.LeagueJungleMapCampToggle, "首营地"),
            Pair(UiTextKeys.LeagueJungleMapCampEmpty, "暂无已知首营地记录；可按需读取打野时间线。"),
            Pair(UiTextKeys.LeagueJungleMapCampLegend, "营地颜色：红BUFF、蓝BUFF、三狼、锋喙鸟；金色边框表示入侵开野。橙色X为3分钟前击杀参与，紫色X为3—4分钟击杀参与。首营地根据首分钟位置推断。"),
            Pair(UiTextKeys.LeagueJungleMapCampCoverage, "首营地样本 {0}/{1} 场 · 已标记 {2} 处"),
            Pair(UiTextKeys.LeagueJungleMapCampDetailTitle, "{0} · {1}"),
            Pair(UiTextKeys.LeagueJungleMapCampOwnDetail, "{0}己方开野：该营地 {1} 场 / 同类 {2} 场 / 阵营有效样本 {3} 场"),
            Pair(UiTextKeys.LeagueJungleMapCampInvadeDetail, "{0}入侵开野：该营地 {1} 场 / 同类 {2} 场 / 阵营有效样本 {3} 场"),
            Pair(UiTextKeys.LeagueJungleMapCampBlueTeam, "蓝方"),
            Pair(UiTextKeys.LeagueJungleMapCampRedTeam, "红方"),
            Pair(UiTextKeys.LeagueJungleMapSideFilter, "阵营筛选"),
            Pair(UiTextKeys.LeagueJungleMapAllSides, "全部阵营"),
            Pair(UiTextKeys.LeagueJungleMapHeatmapLegend, "7×7 位置热度：前14分钟采样权重1，已判定抓人权重5；仅显示权重最高的14格。颜色表示上中下区域，不代表完整路线。"),
            Pair(UiTextKeys.LeagueJungleHistoryCamps, "首分钟最近营地（推断）覆盖 {0}/{1} 场；按阵营/入侵方向计数："),
            Pair(UiTextKeys.LeagueJungleHistoryCampRow, "{0} · {1} · {2}：{3} 场（已知首营地中占 {4}）"),
            Pair(UiTextKeys.LeagueJungleHistoryCampSideCoverage, "{0}首营地样本：{1}/{2} 场"),
            Pair(UiTextKeys.LeagueJungleHistoryCampKindSummary, "{0} · {1}开野：{2}/{3} 场（占该阵营已知首营地 {4}）"),
            Pair(UiTextKeys.LeagueJungleHistoryCampWithinKindRow, "{0} · {1} · {2}：{3} 场（该开野方向中占 {4}）"),
            Pair(UiTextKeys.LeagueJungleHistoryNormal, "己方野区"),
            Pair(UiTextKeys.LeagueJungleHistoryInvade, "入侵对面野区"),
            Pair(UiTextKeys.LeagueJungleHistoryObjectives, "资源归属覆盖 {0}/{1} 场；己方首龙 {2}/{3}（仅归属已知）；本人单杀小龙占己方小龙比例 {4}"),
            Pair(UiTextKeys.LeagueJungleHistoryObjectiveRow, "{0}：场均 {1}；有记录时的首次时间均值 {2}"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAction, "战绩分析"),
            Pair(UiTextKeys.LeaguePlayerAnalysisScope, "仅统计当前已加载、已筛选的对局，不代表全部历史。"),
            Pair(UiTextKeys.LeaguePlayerAnalysisTotals, "样本 {0} · 胜 {1} · 负 {2} · 重开 {3} · 异常 {4} · 未知 {5} · 非匹配 {6}"),
            Pair(UiTextKeys.LeaguePlayerAnalysisWinRate, "有效对局胜率：{0}"),
            Pair(UiTextKeys.LeaguePlayerAnalysisKda, "场均 K / D / A：{0} / {1} / {2}"),
            Pair(UiTextKeys.LeaguePlayerAnalysisCs, "补刀效率：{0} / 分钟（仅含有效时长）"),
            Pair(UiTextKeys.LeaguePlayerAnalysisShares, "场均伤害占比：{0} · 经济占比：{1}"),
            Pair(UiTextKeys.LeaguePlayerAnalysisStreak, "近期连续胜负：{0}"),
            Pair(UiTextKeys.LeaguePlayerAnalysisTopChampions, "常用英雄（当前样本）"),
            Pair(UiTextKeys.LeaguePlayerAnalysisChampionRow, "{0} · {1} 场 · 胜率 {2}%"),
            Pair(UiTextKeys.LeaguePlayerAnalysisExtraShares, "场均参团率：{0} · 承伤占比：{1}"),
            Pair(UiTextKeys.LeaguePlayerAnalysisRecentCompare, "最近 5 场 / 此前 5 场胜率：{0} / {1}（需至少 10 场有时间的有效对局）"),
            Pair(UiTextKeys.LeaguePlayerAnalysisPositions, "位置表现（仅记录明确位置的有效对局）"),
            Pair(UiTextKeys.LeaguePlayerAnalysisPositionRow, "{0} · {1} 场 · 胜率 {2}% · K/D/A {3}/{4}/{5}"),
            Pair(UiTextKeys.LeaguePlayerAnalysisPositionTop, "上路"),
            Pair(UiTextKeys.LeaguePlayerAnalysisPositionJungle, "打野"),
            Pair(UiTextKeys.LeaguePlayerAnalysisPositionMiddle, "中路"),
            Pair(UiTextKeys.LeaguePlayerAnalysisPositionBottom, "下路"),
            Pair(UiTextKeys.LeaguePlayerAnalysisPositionUtility, "辅助"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariTitle, "Akari 17 分制 · 九项表现评分（需完整队伍数据）"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariUnavailable, "当前已加载对局缺少完整队伍统计，暂无法计算评分。"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariTotal, "综合 {0}/17 · 有效评分 {1}/{2} 场 · {3}"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariNormal, "普通"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariOutstanding, "优秀"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariExtraordinary, "卓越"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariPart, "{0}：{1}/{2}"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariKda, "KDA"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariWinRate, "胜率"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariDamage, "英雄伤害"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariDamageTaken, "承伤"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariHealing, "治疗"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariCs, "补刀"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariGold, "经济"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariParticipation, "参团"),
            Pair(UiTextKeys.LeaguePlayerAnalysisAkariVision, "视野"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHeroSelect, "切换英雄评分"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHeroAll, "全部 · 综合分析"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHeroRating, "{0} · {1} 场 · 胜率 {2}% · 单英雄评分"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHeroBreakdown, "单英雄战绩分组（仅统计已加载、结果明确的对局）"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHeroModeRow, "{0} · {1} 场 · {2} 胜"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHeroNormal, "普通模式"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHeroCherry, "斗魂竞技场"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHeroUnknownMode, "模式未知 · {0} 场（不归入普通模式）"),
            Pair(UiTextKeys.LeaguePlayerAnalysisHeroCherryPlacement, "斗魂名次：第一名 {0} · 前半区 {1} · 平均第 {2} 名（仅已知数据）"),

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

        internal static IReadOnlyList<KeyValuePair<string, string>> DefaultEntries
        {
            get { return DefaultText; }
        }

        internal IReadOnlyDictionary<string, string> ReplacementEntries
        {
            get { return _replacements; }
        }

        internal string GetConfiguredValue(string key)
        {
            string value;
            return _values.TryGetValue(key, out value) ? value : string.Empty;
        }

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
                MigrateLegacyEscGuidance(RuntimePaths.UiTextPath);
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to create UI text configuration", exception);
            }
        }

        private static void MigrateLegacyEscGuidance(string path)
        {
            var previousDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { UiTextKeys.LeaguePersonalStatsHint, "把长期使用记录留在本机，需要时再选择加入匿名排行。" },
                { UiTextKeys.LeaguePersonalStatsRanking, "匿名账号数排行" },
                { UiTextKeys.EscSettingsHint, "备份 LOL 游戏内 ESC 设置。恢复前须完全退出英雄联盟，GGman 会先保存当前配置。" },
                { UiTextKeys.EscSettingsCloudScope, "云端备份属于已登录的 GGman 邮箱账号，不与匿名设备统计同步。上传或恢复前请关闭英雄联盟。" },
                { UiTextKeys.EscSettingsFolder, "游戏目录（包含 Config 文件夹）" },
                { UiTextKeys.EscSettingsReady, "请选择英雄联盟目录，然后备份或恢复。" }
            };

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            var section = string.Empty;
            var changed = false;
            for (var index = 0; index < lines.Length; index++)
            {
                var trimmed = (lines[index] ?? string.Empty).Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) &&
                    trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    continue;
                }
                if (!section.Equals("Text", StringComparison.OrdinalIgnoreCase)) continue;
                var equal = FindUnescapedEquals(lines[index] ?? string.Empty);
                if (equal <= 0) continue;
                var key = Unescape(lines[index].Substring(0, equal).Trim());
                string previous;
                string current;
                if (!previousDefaults.TryGetValue(key, out previous) ||
                    !DefaultValues.TryGetValue(key, out current) ||
                    !string.Equals(Unescape(lines[index].Substring(equal + 1).Trim()),
                        previous, StringComparison.Ordinal))
                    continue;

                lines[index] = lines[index].Substring(0, equal + 1) + Escape(current);
                changed = true;
            }
            if (changed) File.WriteAllLines(path, lines, new UTF8Encoding(false));
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

        internal static int FindUnescapedEquals(string value)
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

        internal static string Unescape(string value)
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

        internal static string Escape(string value)
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
