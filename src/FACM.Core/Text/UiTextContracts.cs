namespace FACM.Core.Text;

public interface IUiTextProvider
{
    string Get(string key);
}

public static class UiTextKeys
{
    public const string AppName = "AppName";
    public const string ControlCenter = "ControlCenter";
    public const string Cleanup = "Cleanup";
    public const string CheckUpdate = "CheckUpdate";
    public const string OpenLog = "OpenLog";
    public const string About = "About";
    public const string Exit = "Exit";
    public const string ThemeSettings = "ThemeSettings";
    public const string DesktopPet = "DesktopPet";
    public const string ShellLeague = "ShellLeague";
    public const string ShellRepairTools = "ShellRepairTools";
    public const string ShellPersonalization = "ShellPersonalization";
    public const string ShellMoreSettings = "ShellMoreSettings";
    public const string ShellRepairSubtitle = "ShellRepairSubtitle";
    public const string ShellLeagueSubtitle = "ShellLeagueSubtitle";
    public const string ShellPersonalizationSubtitle = "ShellPersonalizationSubtitle";
    public const string ShellMoreSettingsSubtitle = "ShellMoreSettingsSubtitle";
    public const string ShellStatusLabel = "ShellStatusLabel";
    public const string ShellStatusReady = "ShellStatusReady";
    public const string ShellStatusUpdateAvailable = "ShellStatusUpdateAvailable";
    public const string ShellStatusUnavailable = "ShellStatusUnavailable";
    public const string ShellOverviewTitle = "ShellOverviewTitle";
    public const string ShellOverviewBody = "ShellOverviewBody";
    public const string ShellStateTitle = "ShellStateTitle";
    public const string ShellStateBody = "ShellStateBody";
    public const string DesktopOpenShell = "DesktopOpenShell";
    public const string DesktopOpenShellHelp = "DesktopOpenShellHelp";

    public const string CleanupDirectoryTitle = "CleanupDirectoryTitle";
    public const string CleanupDirectoryDescription = "CleanupDirectoryDescription";
    public const string CleanupDirectoryMissing = "CleanupDirectoryMissing";
    public const string CleanupDirectoryReady = "CleanupDirectoryReady";
    public const string CleanupAutoDetect = "CleanupAutoDetect";
    public const string CleanupSelectDirectory = "CleanupSelectDirectory";
    public const string CleanupPreview = "CleanupPreview";
    public const string CleanupPreviewTitle = "CleanupPreviewTitle";
    public const string CleanupPreviewDescription = "CleanupPreviewDescription";
    public const string CleanupNoTargets = "CleanupNoTargets";
    public const string CleanupRunningProcesses = "CleanupRunningProcesses";
    public const string CleanupRequiresAdmin = "CleanupRequiresAdmin";
    public const string CleanupRestartElevated = "CleanupRestartElevated";
    public const string CleanupConfirmTitle = "CleanupConfirmTitle";
    public const string CleanupConfirmBody = "CleanupConfirmBody";
    public const string CleanupConfirmPrimary = "CleanupConfirmPrimary";
    public const string CleanupCancel = "CleanupCancel";
    public const string CleanupScanning = "CleanupScanning";
    public const string CleanupExecuting = "CleanupExecuting";
    public const string CleanupComplete = "CleanupComplete";
    public const string CleanupFailed = "CleanupFailed";
    public const string CleanupBlocked = "CleanupBlocked";
    public const string CleanupTargetSummary = "CleanupTargetSummary";
    public const string CleanupSafetyHint = "CleanupSafetyHint";
    public const string CleanupInvalidDirectory = "CleanupInvalidDirectory";
    public const string CleanupPathRecoveryReadOnly = "CleanupPathRecoveryReadOnly";

    public const string RepairToolsTitle = "RepairToolsTitle";
    public const string RepairToolsDescription = "RepairToolsDescription";
    public const string RepairToolsReady = "RepairToolsReady";
    public const string RepairPrivilegeLabel = "RepairPrivilegeLabel";
    public const string RepairPrivilegeAdministrator = "RepairPrivilegeAdministrator";
    public const string RepairPrivilegeStandard = "RepairPrivilegeStandard";
    public const string RepairDriverCleanup = "RepairDriverCleanup";
    public const string RepairDriverCleanupHint = "RepairDriverCleanupHint";
    public const string RepairDriverCleanupStarted = "RepairDriverCleanupStarted";
    public const string RepairDriverCleanupCancelled = "RepairDriverCleanupCancelled";
    public const string RepairDriverCleanupFailed = "RepairDriverCleanupFailed";
    public const string RepairGameRepair = "RepairGameRepair";
    public const string RepairGameRepairHint = "RepairGameRepairHint";
    public const string RepairFixWindow = "RepairFixWindow";
    public const string RepairFixWindowHint = "RepairFixWindowHint";
    public const string RepairAutoWindow = "RepairAutoWindow";
    public const string RepairAutoWindowDisable = "RepairAutoWindowDisable";
    public const string RepairAutoWindowHint = "RepairAutoWindowHint";
    public const string RepairSkipSettlement = "RepairSkipSettlement";
    public const string RepairSkipSettlementHint = "RepairSkipSettlementHint";
    public const string RepairRestartClientUx = "RepairRestartClientUx";
    public const string RepairRestartClientUxHint = "RepairRestartClientUxHint";
    public const string RepairExitGame = "RepairExitGame";
    public const string RepairExitGameHint = "RepairExitGameHint";
    public const string RepairGameRepairReady = "RepairGameRepairReady";

    public const string LeagueWorkbenchTitle = "LeagueWorkbenchTitle";
    public const string LeagueWorkbenchMatch = "LeagueWorkbenchMatch";
    public const string LeagueWorkbenchMatchDescription = "LeagueWorkbenchMatchDescription";
    public const string LeagueWorkbenchStrategy = "LeagueWorkbenchStrategy";
    public const string LeagueWorkbenchStrategyDescription = "LeagueWorkbenchStrategyDescription";
    public const string LeagueWorkbenchAutomation = "LeagueWorkbenchAutomation";
    public const string LeagueWorkbenchAutomationDescription = "LeagueWorkbenchAutomationDescription";
    public const string LeagueWorkbenchStateLabel = "LeagueWorkbenchStateLabel";
    public const string LeagueWorkbenchBudgetLabel = "LeagueWorkbenchBudgetLabel";
    public const string LeagueStateNotRunning = "LeagueStateNotRunning";
    public const string LeagueStateConnecting = "LeagueStateConnecting";
    public const string LeagueStateLobby = "LeagueStateLobby";
    public const string LeagueStateMatchmaking = "LeagueStateMatchmaking";
    public const string LeagueStateReadyCheck = "LeagueStateReadyCheck";
    public const string LeagueStateChampSelect = "LeagueStateChampSelect";
    public const string LeagueStateInGame = "LeagueStateInGame";
    public const string LeagueStatePostGame = "LeagueStatePostGame";
    public const string LeagueStateClientError = "LeagueStateClientError";

    public const string DiagnosticsTitle = "DiagnosticsTitle";
    public const string DiagnosticsSubtitle = "DiagnosticsSubtitle";
    public const string DiagnosticsSummaryLabel = "DiagnosticsSummaryLabel";
    public const string DiagnosticsRefresh = "DiagnosticsRefresh";
    public const string DiagnosticsCopySummary = "DiagnosticsCopySummary";
    public const string DiagnosticsExportBundle = "DiagnosticsExportBundle";
    public const string DiagnosticsRefreshHelp = "DiagnosticsRefreshHelp";
    public const string DiagnosticsCopySummaryHelp = "DiagnosticsCopySummaryHelp";
    public const string DiagnosticsExportBundleHelp = "DiagnosticsExportBundleHelp";
    public const string DiagnosticsStatusReady = "DiagnosticsStatusReady";
    public const string DiagnosticsStatusRefreshed = "DiagnosticsStatusRefreshed";
    public const string DiagnosticsStatusCopied = "DiagnosticsStatusCopied";
    public const string DiagnosticsStatusExported = "DiagnosticsStatusExported";
    public const string DiagnosticsStatusFailed = "DiagnosticsStatusFailed";
}

public static class FoundationUiTextDefaults
{
    private static readonly IReadOnlyDictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [UiTextKeys.AppName] = "FACM",
        [UiTextKeys.ControlCenter] = "控制中心",
        [UiTextKeys.Cleanup] = "清理环境",
        [UiTextKeys.CheckUpdate] = "检查更新",
        [UiTextKeys.OpenLog] = "操作日志",
        [UiTextKeys.About] = "程序信息",
        [UiTextKeys.Exit] = "退出程序",
        [UiTextKeys.ThemeSettings] = "主题设置",
        [UiTextKeys.DesktopPet] = "桌面宠物",
        [UiTextKeys.ShellLeague] = "LOL 工作台",
        [UiTextKeys.ShellRepairTools] = "清理与修复",
        [UiTextKeys.ShellPersonalization] = "个性化",
        [UiTextKeys.ShellMoreSettings] = "更多设置",
        [UiTextKeys.ShellRepairSubtitle] = "环境清理与修复入口，危险操作始终先预览再确认。",
        [UiTextKeys.ShellLeagueSubtitle] = "比赛 / 攻略 / 自动化，共用唯一 League runtime。",
        [UiTextKeys.ShellPersonalizationSubtitle] = "主题、桌面入口与个性化体验的统一入口。",
        [UiTextKeys.ShellMoreSettingsSubtitle] = "设置、更新与诊断能力的集中入口。",
        [UiTextKeys.ShellStatusLabel] = "当前状态",
        [UiTextKeys.ShellStatusReady] = "准备就绪",
        [UiTextKeys.ShellStatusUpdateAvailable] = "发现可用更新",
        [UiTextKeys.ShellStatusUnavailable] = "状态暂不可用",
        [UiTextKeys.ShellOverviewTitle] = "统一控制中心",
        [UiTextKeys.ShellOverviewBody] = "一个窗口承载四个产品入口，业务能力继续由 Core 与平台 owner 管理。",
        [UiTextKeys.ShellStateTitle] = "状态驱动",
        [UiTextKeys.ShellStateBody] = "界面只消费 Product State 与 Core intent，不创建第二套 League、网络或文件运行时。",
        [UiTextKeys.DesktopOpenShell] = "打开 FACM 控制中心",
        [UiTextKeys.DesktopOpenShellHelp] = "打开或激活 FACM 主窗口。",

        [UiTextKeys.CleanupDirectoryTitle] = "游戏目录",
        [UiTextKeys.CleanupDirectoryDescription] = "先确认英雄联盟安装目录。FACM 只会按内置白名单生成清理预览。",
        [UiTextKeys.CleanupDirectoryMissing] = "尚未识别有效游戏目录",
        [UiTextKeys.CleanupDirectoryReady] = "游戏目录已就绪",
        [UiTextKeys.CleanupAutoDetect] = "自动识别",
        [UiTextKeys.CleanupSelectDirectory] = "选择目录",
        [UiTextKeys.CleanupPreview] = "扫描并预览",
        [UiTextKeys.CleanupPreviewTitle] = "清理预览",
        [UiTextKeys.CleanupPreviewDescription] = "确认前不会删除任何内容。请核对完整路径、目标数量与被阻止项目。",
        [UiTextKeys.CleanupNoTargets] = "没有发现可清理目标",
        [UiTextKeys.CleanupRunningProcesses] = "请先退出英雄联盟与 Riot 客户端后再清理。",
        [UiTextKeys.CleanupRequiresAdmin] = "预览包含系统目录，需要管理员权限才能执行。",
        [UiTextKeys.CleanupRestartElevated] = "以管理员身份重新打开",
        [UiTextKeys.CleanupConfirmTitle] = "确认执行清理",
        [UiTextKeys.CleanupConfirmBody] = "只有预览中的白名单目标会被处理。执行时仍会逐项重新校验路径和重解析点。",
        [UiTextKeys.CleanupConfirmPrimary] = "确认清理",
        [UiTextKeys.CleanupCancel] = "取消",
        [UiTextKeys.CleanupScanning] = "正在扫描清理目标…",
        [UiTextKeys.CleanupExecuting] = "正在执行清理…",
        [UiTextKeys.CleanupComplete] = "清理完成",
        [UiTextKeys.CleanupFailed] = "清理未完成",
        [UiTextKeys.CleanupBlocked] = "已阻止",
        [UiTextKeys.CleanupTargetSummary] = "目标摘要",
        [UiTextKeys.CleanupSafetyHint] = "FACM 不会结束进程、停止服务或删除白名单之外的文件。",
        [UiTextKeys.CleanupInvalidDirectory] = "所选目录无法解析为有效英雄联盟安装目录。",
        [UiTextKeys.CleanupPathRecoveryReadOnly] = "设置当前处于恢复模式，本次目录只用于运行，不覆盖损坏的主设置文件。",

        [UiTextKeys.RepairToolsTitle] = "修复工具",
        [UiTextKeys.RepairToolsDescription] = "沿用 FACM 3.5.15 已验证的修复行为；旧 Fix-LCU 外部模式不会在 4.0 重新启用。",
        [UiTextKeys.RepairToolsReady] = "修复工具已就绪",
        [UiTextKeys.RepairPrivilegeLabel] = "当前权限",
        [UiTextKeys.RepairPrivilegeAdministrator] = "管理员模式",
        [UiTextKeys.RepairPrivilegeStandard] = "标准权限",
        [UiTextKeys.RepairDriverCleanup] = "驱动清理",
        [UiTextKeys.RepairDriverCleanupHint] = "启动内置驱动清理工具。工具释放前会校验固定 SHA-256，是否提权由 Windows / 工具自身决定。",
        [UiTextKeys.RepairDriverCleanupStarted] = "驱动清理工具已启动",
        [UiTextKeys.RepairDriverCleanupCancelled] = "已取消驱动清理工具",
        [UiTextKeys.RepairDriverCleanupFailed] = "驱动清理工具启动失败",
        [UiTextKeys.RepairGameRepair] = "游戏修复",
        [UiTextKeys.RepairGameRepairHint] = "游戏运行期间遇到客户端窗口、大厅或结算异常时使用。",
        [UiTextKeys.RepairFixWindow] = "立即修复窗口",
        [UiTextKeys.RepairFixWindowHint] = "按当前显示器、窗口状态与合理尺寸原生修复客户端窗口。",
        [UiTextKeys.RepairAutoWindow] = "自动修复窗口",
        [UiTextKeys.RepairAutoWindowDisable] = "关闭自动修复",
        [UiTextKeys.RepairAutoWindowHint] = "监听客户端窗口变化；仅在检测到异常后处理，不常驻轮询。",
        [UiTextKeys.RepairSkipSettlement] = "跳过卡结算",
        [UiTextKeys.RepairSkipSettlementHint] = "通过 FACM 当前客户端连接跳过卡住、持续转圈的结算页面。",
        [UiTextKeys.RepairRestartClientUx] = "重启客户端界面",
        [UiTextKeys.RepairRestartClientUxHint] = "重新加载 LeagueClient UX；不结束正在进行的游戏进程。",
        [UiTextKeys.RepairExitGame] = "一键结束游戏",
        [UiTextKeys.RepairExitGameHint] = "结束当前英雄联盟游戏进程；与“跳过卡结算”是两个不同功能。",
        [UiTextKeys.RepairGameRepairReady] = "准备就绪 · 游戏修复已由 FACM 原生接管",

        [UiTextKeys.LeagueWorkbenchTitle] = "LOL 工作台",
        [UiTextKeys.LeagueWorkbenchMatch] = "比赛",
        [UiTextKeys.LeagueWorkbenchMatchDescription] = "对局状态、实时信息与比赛相关能力集中在这里。",
        [UiTextKeys.LeagueWorkbenchStrategy] = "攻略",
        [UiTextKeys.LeagueWorkbenchStrategyDescription] = "英雄、符文、出装与推荐能力按当前对局阶段提供。",
        [UiTextKeys.LeagueWorkbenchAutomation] = "自动化",
        [UiTextKeys.LeagueWorkbenchAutomationDescription] = "只展示已授权的自动化能力；高风险写操作继续遵守窄 capability。",
        [UiTextKeys.LeagueWorkbenchStateLabel] = "客户端状态",
        [UiTextKeys.LeagueWorkbenchBudgetLabel] = "性能档位",
        [UiTextKeys.LeagueStateNotRunning] = "客户端未运行",
        [UiTextKeys.LeagueStateConnecting] = "正在连接客户端",
        [UiTextKeys.LeagueStateLobby] = "大厅",
        [UiTextKeys.LeagueStateMatchmaking] = "匹配中",
        [UiTextKeys.LeagueStateReadyCheck] = "等待接受",
        [UiTextKeys.LeagueStateChampSelect] = "英雄选择",
        [UiTextKeys.LeagueStateInGame] = "游戏中",
        [UiTextKeys.LeagueStatePostGame] = "赛后",
        [UiTextKeys.LeagueStateClientError] = "客户端连接异常",

        [UiTextKeys.DiagnosticsTitle] = "诊断中心",
        [UiTextKeys.DiagnosticsSubtitle] = "生成只读状态摘要并导出经过再次脱敏的诊断包，不包含设置文件、LCU 凭据或任意目录扫描。",
        [UiTextKeys.DiagnosticsSummaryLabel] = "脱敏摘要",
        [UiTextKeys.DiagnosticsRefresh] = "刷新摘要",
        [UiTextKeys.DiagnosticsCopySummary] = "复制摘要",
        [UiTextKeys.DiagnosticsExportBundle] = "导出诊断包",
        [UiTextKeys.DiagnosticsRefreshHelp] = "重新读取允许的诊断事件并生成脱敏摘要。",
        [UiTextKeys.DiagnosticsCopySummaryHelp] = "把当前脱敏摘要复制到剪贴板。",
        [UiTextKeys.DiagnosticsExportBundleHelp] = "导出只包含允许条目的脱敏诊断压缩包。",
        [UiTextKeys.DiagnosticsStatusReady] = "诊断中心已就绪",
        [UiTextKeys.DiagnosticsStatusRefreshed] = "摘要已刷新",
        [UiTextKeys.DiagnosticsStatusCopied] = "摘要已复制",
        [UiTextKeys.DiagnosticsStatusExported] = "脱敏诊断包已导出",
        [UiTextKeys.DiagnosticsStatusFailed] = "诊断操作失败"
    };

    public static string Get(string key) => Values.TryGetValue(key, out var value) ? value : key;
}
