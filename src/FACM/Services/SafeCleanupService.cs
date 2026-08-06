using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FACM.Services
{
    internal sealed class CleanupPreview
    {
        public string GameRoot { get; set; }
        public IReadOnlyList<string> Files { get; set; }
        public long Bytes { get; set; }
    }

    internal static class SafeCleanupService
    {
        public static CleanupPreview Preview(string gameRoot)
        {
            if (!GameLocator.IsValidGameRoot(gameRoot)) throw new InvalidOperationException("请选择正确的游戏根目录。目录中应包含 Game，并包含 LeagueClient 或 Launcher。");
            var root = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var files = new List<string>();
            var leagueClient = Path.Combine(root, "LeagueClient");
            if (Directory.Exists(leagueClient))
            {
                files.AddRange(Directory.EnumerateFiles(leagueClient, "*.log", SearchOption.TopDirectoryOnly));
            }

            var ownTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FACM", "Temp");
            if (Directory.Exists(ownTemp))
            {
                files.AddRange(Directory.EnumerateFiles(ownTemp, "*", SearchOption.TopDirectoryOnly));
            }

            long bytes = 0;
            foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { bytes += new FileInfo(file).Length; } catch { }
            }

            return new CleanupPreview
            {
                GameRoot = root,
                Files = files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                Bytes = bytes
            };
        }

        public static int Execute(CleanupPreview preview)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            var deleted = 0;
            foreach (var file in preview.Files)
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    deleted++;
                    AppLog.Info("Deleted safe cleanup file: " + file);
                }
                catch (Exception exception)
                {
                    AppLog.Error("Failed to delete safe cleanup file: " + file, exception);
                }
            }
            return deleted;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024L) return (bytes / 1024d).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024L * 1024L) return (bytes / 1024d / 1024d).ToString("0.0") + " MB";
            return (bytes / 1024d / 1024d / 1024d).ToString("0.00") + " GB";
        }
    }
}
