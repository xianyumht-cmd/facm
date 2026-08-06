using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FACM.Configuration
{
    internal static class CleanupProfile
    {
        // ===== 开发者只需要修改这一段 =====
        // 这里只填写单级文件夹名。
        public const string ProgramFilesFolderName = "REPLACE_PROGRAM_FILES_FOLDER";
        public const string ProgramDataFolderName = "REPLACE_PROGRAM_DATA_FOLDER";

        // 用于确认/定位安装根目录的标记文件夹名。用户选择其上级目录或更高层目录均可。
        public const string GameRootMarkerFolderName = "REPLACE_GAME_ROOT_MARKER_FOLDER";

        // 以下路径均相对于解析出的安装根目录，可填写多级相对路径，例如 @"A\\B"。
        public const string CleanupContainerRelativePath = @"REPLACE_CLEANUP_CONTAINER_RELATIVE_PATH";
        public const string PreservedChildFolderName = "DATA";
        public const string ExtraFolderRelativePath1 = @"REPLACE_EXTRA_FOLDER_RELATIVE_PATH_1";
        public const string ExtraFolderRelativePath2 = @"REPLACE_EXTRA_FOLDER_RELATIVE_PATH_2";
        public const string LogFolderRelativePath = @"REPLACE_LOG_FOLDER_RELATIVE_PATH";
        public const string LogSearchPattern = "*.log";

        // 自动定位：卸载项显示名称关键词，以及需要阻止清理的进程名（不带 .exe）。
        public const string RegistryDisplayNameKeyword = "REPLACE_REGISTRY_DISPLAY_NAME_KEYWORD";
        public static readonly string[] RelatedProcessNames =
        {
            "REPLACE_PROCESS_NAME_1",
            "REPLACE_PROCESS_NAME_2"
        };

        public const int MaxMarkerSearchDepth = 5;
        // ===== 开发者配置结束 =====

        public static bool IsConfigured
        {
            get
            {
                try
                {
                    Validate();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static IReadOnlyList<string> ExtraFolderRelativePaths
        {
            get
            {
                return new[] { ExtraFolderRelativePath1, ExtraFolderRelativePath2 }
                    .Select((value, index) => NormalizeRelativePath(value, "ExtraFolderRelativePath" + (index + 1)))
                    .ToArray();
            }
        }

        public static IReadOnlyList<string> NormalizedProcessNames
        {
            get
            {
                return RelatedProcessNames
                    .Where(value => !string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value))
                    .Select(value => Path.GetFileNameWithoutExtension(value.Trim()))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        public static void Validate()
        {
            ValidateFolderName(ProgramFilesFolderName, nameof(ProgramFilesFolderName));
            ValidateFolderName(ProgramDataFolderName, nameof(ProgramDataFolderName));
            ValidateFolderName(GameRootMarkerFolderName, nameof(GameRootMarkerFolderName));
            ValidateFolderName(PreservedChildFolderName, nameof(PreservedChildFolderName));

            NormalizeRelativePath(CleanupContainerRelativePath, nameof(CleanupContainerRelativePath));
            NormalizeRelativePath(ExtraFolderRelativePath1, nameof(ExtraFolderRelativePath1));
            NormalizeRelativePath(ExtraFolderRelativePath2, nameof(ExtraFolderRelativePath2));
            NormalizeRelativePath(LogFolderRelativePath, nameof(LogFolderRelativePath));

            if (string.IsNullOrWhiteSpace(LogSearchPattern) || IsPlaceholder(LogSearchPattern) ||
                LogSearchPattern.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                LogSearchPattern.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                LogSearchPattern.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                throw new InvalidOperationException("LogSearchPattern 配置无效。");
            }

            if (string.IsNullOrWhiteSpace(RegistryDisplayNameKeyword) || IsPlaceholder(RegistryDisplayNameKeyword))
            {
                throw new InvalidOperationException("RegistryDisplayNameKeyword 尚未配置。");
            }

            if (NormalizedProcessNames.Count == 0)
            {
                throw new InvalidOperationException("RelatedProcessNames 尚未配置。");
            }
        }

        public static string NormalizeRelativePath(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value) || IsPlaceholder(value))
            {
                throw new InvalidOperationException(fieldName + " 尚未配置。");
            }

            var normalized = value.Trim().Trim('"').Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Trim(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized))
            {
                throw new InvalidOperationException(fieldName + " 必须是相对路径。");
            }

            var segments = normalized.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(segment => segment == "." || segment == ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                throw new InvalidOperationException(fieldName + " 包含非法路径段。");
            }

            return string.Join(Path.DirectorySeparatorChar.ToString(), segments);
        }

        private static void ValidateFolderName(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value) || IsPlaceholder(value))
            {
                throw new InvalidOperationException(fieldName + " 尚未配置。");
            }

            var trimmed = value.Trim();
            if (trimmed == "." || trimmed == ".." || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                trimmed.IndexOf(Path.DirectorySeparatorChar) >= 0 || trimmed.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                throw new InvalidOperationException(fieldName + " 必须是合法的单级文件夹名。");
            }
        }

        private static bool IsPlaceholder(string value)
        {
            return value != null && value.IndexOf("REPLACE_", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
