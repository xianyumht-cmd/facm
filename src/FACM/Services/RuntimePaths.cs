using System;
using System.IO;

namespace FACM.Services
{
    internal static class RuntimePaths
    {
        private static readonly string BaseDirectoryValue =
            Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);

        public static string BaseDirectory
        {
            get { return BaseDirectoryValue; }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(BaseDirectoryValue, "settings.ini"); }
        }

        public static string UiTextPath
        {
            get { return Path.Combine(BaseDirectoryValue, "ui-text.ini"); }
        }

        public static string ToolBundlePath
        {
            get { return Path.Combine(BaseDirectoryValue, "FACM.ToolBundle.dll"); }
        }

        public static string LogsDirectory
        {
            get { return Path.Combine(BaseDirectoryValue, "logs"); }
        }

        public static string RuntimeDirectory
        {
            get { return Path.Combine(BaseDirectoryValue, "runtime"); }
        }

        public static string UpdatesDirectory
        {
            get { return Path.Combine(RuntimeDirectory, "updates"); }
        }

        public static void Initialize()
        {
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(RuntimeDirectory);
            Directory.CreateDirectory(UpdatesDirectory);
        }
    }
}
