using System;
using System.Diagnostics;
using System.IO;

namespace FACM.Services
{
    internal sealed class UiTextCatalog
    {
        private static readonly string LegacyConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FACM",
            "ui-text.ini");

        public string ControlCenter { get; private set; } = "控制中心";
        public string Cleanup { get; private set; } = "清理环境";
        public string ToolGroup { get; private set; } = "快捷工具";
        public string ToolA { get; private set; } = "工具 A";
        public string Mode1 { get; private set; } = "模式 1";
        public string Mode2 { get; private set; } = "模式 2";
        public string Mode3 { get; private set; } = "模式 3";
        public string Mode4 { get; private set; } = "模式 4";
        public string CheckUpdate { get; private set; } = "检查更新";
        public string OpenLog { get; private set; } = "操作日志";
        internal string About { get; private set; } = "程序信息";
        public string EditText { get; private set; } = "界面文字";
        public string Exit { get; private set; } = "退出程序";

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
                foreach (var line in File.ReadAllLines(RuntimePaths.UiTextPath))
                {
                    var trimmed = (line ?? string.Empty).Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal)) continue;
                    var separator = trimmed.IndexOf('=');
                    if (separator <= 0) continue;
                    var key = trimmed.Substring(0, separator).Trim();
                    var value = trimmed.Substring(separator + 1).Trim();
                    if (value.Length == 0) continue;
                    result.Apply(key, value);
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

        private static void EnsureTemplate()
        {
            try
            {
                RuntimePaths.Initialize();
                if (File.Exists(RuntimePaths.UiTextPath)) return;
                if (File.Exists(LegacyConfigPath))
                {
                    File.Copy(LegacyConfigPath, RuntimePaths.UiTextPath, false);
                    return;
                }

                File.WriteAllLines(RuntimePaths.UiTextPath, new[]
                {
                    "# 修改等号右侧文字并重启 FACM 即可生效。",
                    "ControlCenter=控制中心",
                    "Cleanup=清理环境",
                    "ToolGroup=快捷工具",
                    "ToolA=工具 A",
                    "Mode1=模式 1",
                    "Mode2=模式 2",
                    "Mode3=模式 3",
                    "Mode4=模式 4",
                    "CheckUpdate=检查更新",
                    "OpenLog=操作日志",
                    "EditText=界面文字",
                    "Exit=退出程序"
                });
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to create UI text configuration", exception);
            }
        }

        private void Apply(string key, string value)
        {
            if (key.Equals("ControlCenter", StringComparison.OrdinalIgnoreCase)) ControlCenter = value;
            else if (key.Equals("Cleanup", StringComparison.OrdinalIgnoreCase)) Cleanup = value;
            else if (key.Equals("ToolGroup", StringComparison.OrdinalIgnoreCase)) ToolGroup = value;
            else if (key.Equals("ToolA", StringComparison.OrdinalIgnoreCase)) ToolA = value;
            else if (key.Equals("Mode1", StringComparison.OrdinalIgnoreCase)) Mode1 = value;
            else if (key.Equals("Mode2", StringComparison.OrdinalIgnoreCase)) Mode2 = value;
            else if (key.Equals("Mode3", StringComparison.OrdinalIgnoreCase)) Mode3 = value;
            else if (key.Equals("Mode4", StringComparison.OrdinalIgnoreCase)) Mode4 = value;
            else if (key.Equals("CheckUpdate", StringComparison.OrdinalIgnoreCase)) CheckUpdate = value;
            else if (key.Equals("OpenLog", StringComparison.OrdinalIgnoreCase)) OpenLog = value;
            else if (key.Equals("EditText", StringComparison.OrdinalIgnoreCase)) EditText = value;
            else if (key.Equals("Exit", StringComparison.OrdinalIgnoreCase)) Exit = value;
        }

        public string ModeName(int mode)
        {
            switch (mode)
            {
                case 1: return Mode1;
                case 2: return Mode2;
                case 3: return Mode3;
                case 4: return Mode4;
                default: return "模式 " + mode;
            }
        }
    }
}
