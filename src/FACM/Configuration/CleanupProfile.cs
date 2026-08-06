using System;
using System.IO;
using System.Linq;

namespace FACM.Configuration
{
    /// <summary>
    /// 开发者清理配置。
    /// 发布前只需要修改 TargetFolderName，然后重新编译。
    /// 此值不会在程序界面中提供给最终用户修改。
    /// </summary>
    internal static class CleanupProfile
    {
        public const string TargetFolderName = "REPLACE_WITH_TARGET_FOLDER_NAME";

        public const string PreservedGameDirectoryName = "DATA";

        public static void Validate()
        {
            var name = (TargetFolderName ?? string.Empty).Trim();
            if (name.Length == 0 ||
                string.Equals(name, "REPLACE_WITH_TARGET_FOLDER_NAME", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "开发者尚未配置目标文件夹名。请修改 Configuration/CleanupProfile.cs 中的 TargetFolderName 后重新编译。");
            }

            if (name == "." || name == ".." ||
                name.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                name.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException("开发者配置的目标文件夹名不是合法的单级文件夹名称。");
            }

            string[] blockedNames =
            {
                "Windows", "System32", "SysWOW64", "Program Files",
                "Program Files (x86)", "ProgramData", PreservedGameDirectoryName
            };

            if (blockedNames.Any(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("开发者配置的目标文件夹名属于受保护名称，FACM 已拒绝启动清理。 ");
            }
        }
    }
}
