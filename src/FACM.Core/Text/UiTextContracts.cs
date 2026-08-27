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
        [UiTextKeys.ShellLeague] = "英雄联盟",
        [UiTextKeys.ShellRepairTools] = "修复工具",
        [UiTextKeys.ShellPersonalization] = "个性化",
        [UiTextKeys.ShellMoreSettings] = "更多设置"
    };

    public static string Get(string key) => Values.TryGetValue(key, out var value) ? value : key;
}
