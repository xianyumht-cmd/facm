using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FACM.Models;

namespace FACM.Services
{
    internal sealed class CleanupService
    {
        private static readonly string ProgramFilesTarget = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "AntiCheatExpert");

        private static readonly string ProgramDataTarget = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AntiCheatExpert");

        public IReadOnlyList<CleanupItem> Scan(string selectedGameDirectory, CancellationToken cancellationToken)
        {
            var candidates = new List<CleanupItem>
            {
                CreateCandidate("系统程序残留", ProgramFilesTarget, false),
                CreateCandidate("公共数据残留", ProgramDataTarget, false)
            };

            string validatedGameDirectory;
            if (TryValidateGameDirectory(selectedGameDirectory, out validatedGameDirectory))
            {
                candidates.Add(CreateCandidate(
                    "游戏目录残留",
                    SafeCombineUnderRoot(validatedGameDirectory, "AntiCheatExpert"),
                    true));
                candidates.Add(CreateCandidate(
                    "游戏 Game 子目录残留",
                    SafeCombineUnderRoot(validatedGameDirectory, Path.Combine("Game", "AntiCheatExpert")),
                    true));
            }

            var uniqueCandidates = candidates
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .GroupBy(item => NormalizePath(item.Path), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            foreach (var item in uniqueCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                InspectItem(item, cancellationToken);
            }

            AppLog.Info("Scan completed: " + string.Join(" | ", uniqueCandidates.Select(item => item.Path + "=" + item.State)));
            return uniqueCandidates;
        }

        public void Delete(CleanupItem item, CancellationToken cancellationToken)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(item.Path))
            {
                item.State = CleanupItemState.Missing;
                item.Detail = "目录不存在";
                return;
            }

            EnsureAllowedTarget(item.Path, item.IsGameDirectoryItem);
            if (IsReparsePoint(item.Path))
            {
                item.State = CleanupItemState.Blocked;
                item.Detail = "检测到链接或重解析点，已拒绝删除";
                AppLog.Warning("Blocked reparse-point deletion: " + item.Path);
                return;
            }

            try
            {
                DeleteDirectoryTree(item.Path, cancellationToken);
                item.State = Directory.Exists(item.Path) ? CleanupItemState.Failed : CleanupItemState.Deleted;
                item.Detail = item.State == CleanupItemState.Deleted ? "已删除" : "目录仍然存在";
                AppLog.Info("Delete result: " + item.Path + " => " + item.State);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                item.State = CleanupItemState.Failed;
                item.Detail = exception.Message;
                AppLog.Error("Delete failed: " + item.Path, exception);
            }
        }

        private static CleanupItem CreateCandidate(string displayName, string path, bool gameItem)
        {
            return new CleanupItem
            {
                DisplayName = displayName,
                Path = NormalizePath(path),
                State = CleanupItemState.Missing,
                Detail = "等待扫描",
                IsGameDirectoryItem = gameItem
            };
        }

        private static void InspectItem(CleanupItem item, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(item.Path))
            {
                item.State = CleanupItemState.Missing;
                item.Detail = "未发现";
                item.EstimatedBytes = 0;
                return;
            }

            try
            {
                EnsureAllowedTarget(item.Path, item.IsGameDirectoryItem);
                if (IsReparsePoint(item.Path))
                {
                    item.State = CleanupItemState.Blocked;
                    item.Detail = "链接或重解析点，不会删除";
                    return;
                }

                long bytes = 0;
                var files = 0;
                var pending = new Stack<string>();
                pending.Push(item.Path);
                while (pending.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var current = pending.Pop();
                    foreach (var file in Directory.EnumerateFiles(current))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        files++;
                        try { bytes += new FileInfo(file).Length; } catch { }
                    }
                    foreach (var directory in Directory.EnumerateDirectories(current))
                    {
                        if (!IsReparsePoint(directory)) pending.Push(directory);
                    }
                }

                item.EstimatedBytes = bytes;
                item.State = CleanupItemState.Found;
                item.Detail = files + " 个文件";
            }
            catch (Exception exception)
            {
                item.State = CleanupItemState.Blocked;
                item.Detail = "无法完整读取：" + exception.Message;
                AppLog.Error("Scan failed: " + item.Path, exception);
            }
        }

        private static void DeleteDirectoryTree(string root, CancellationToken cancellationToken)
        {
            var directories = new Stack<string>();
            var ordered = new Stack<string>();
            directories.Push(root);

            while (directories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = directories.Pop();
                ordered.Push(current);
                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    if (IsReparsePoint(directory))
                    {
                        throw new IOException("子目录包含链接或重解析点：" + directory);
                    }
                    directories.Push(directory);
                }
            }

            foreach (var directory in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                File.SetAttributes(directory, FileAttributes.Normal);
                Directory.Delete(directory, false);
            }
        }

        private static bool TryValidateGameDirectory(string input, out string validated)
        {
            validated = null;
            if (string.IsNullOrWhiteSpace(input)) return false;

            try
            {
                var path = NormalizePath(input);
                var root = NormalizePath(Path.GetPathRoot(path));
                if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return false;

                var protectedRoots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                };
                if (protectedRoots.Any(value => string.Equals(path, NormalizePath(value), StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                if (!Directory.Exists(path)) return false;
                validated = path;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeCombineUnderRoot(string root, string relative)
        {
            var normalizedRoot = NormalizePath(root);
            var combined = NormalizePath(Path.Combine(normalizedRoot, relative));
            var rootWithSeparator = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("目标路径超出所选游戏目录。");
            }
            return combined;
        }

        private static void EnsureAllowedTarget(string path, bool gameItem)
        {
            var normalized = NormalizePath(path);
            if (string.Equals(normalized, NormalizePath(ProgramFilesTarget), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, NormalizePath(ProgramDataTarget), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (gameItem)
            {
                var leaf = new DirectoryInfo(normalized).Name;
                if (string.Equals(leaf, "AntiCheatExpert", StringComparison.OrdinalIgnoreCase)) return;
            }

            throw new InvalidOperationException("目标不在 FACM 允许清理的白名单中：" + normalized);
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
