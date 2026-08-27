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
        [UiTextKeys.DiagnosticsStatusReady] = "诊断中心已就绪",
        [UiTextKeys.DiagnosticsStatusRefreshed] = "摘要已刷新",
        [UiTextKeys.DiagnosticsStatusCopied] = "摘要已复制",
        [UiTextKeys.DiagnosticsStatusExported] = "脱敏诊断包已导出",
        [UiTextKeys.DiagnosticsStatusFailed] = "诊断操作失败"
    };

    public static string Get(string key) => Values.TryGetValue(key, out var value) ? value : key;
}
