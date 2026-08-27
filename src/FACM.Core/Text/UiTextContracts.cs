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
        [UiTextKeys.ShellStateBody] = "界面只消费 Product State 与 Core intent，不创建第二套 League、网络或文件运行时。"
    };

    public static string Get(string key) => Values.TryGetValue(key, out var value) ? value : key;
}
