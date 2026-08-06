using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FACM.Configuration;
using FACM.Models;

namespace FACM.Services
{
    internal sealed class CleanupService
    {
        public IReadOnlyList<CleanupItem> Scan(string selectedGameDirectory, CancellationToken cancellationToken)
        {
            CleanupProfile.Validate();

            var targetFolderName = CleanupProfile.TargetFolderName.Trim();
            var programFilesTarget = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                targetFolderName);
            var programDataTarget = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                targetFolderName);

            var candidates = new List<CleanupItem>
            {
                CreateDirectoryCandidate(
                    "系统程序目录目标",
                    programFilesTarget,
                    false,
                    CleanupRule.FixedProgramFilesDirectory,
                    null),
                CreateDirectoryCandidate(
                    "公共数据目录目标",
                    programDataTarget,
                    false,
                    CleanupRule.FixedProgramDataDirectory,
                    null)
            };

            string gameRoot;
            if (TryValidateGameDirectory(selectedGameDirectory, out gameRoot))
            {
                AddSelectedGameCandidates(candidates, gameRoot, targetFolderName, cancellationToken);
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
            CleanupProfile.Validate();
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                EnsureAllowedTarget(item);

                if (item.Kind == CleanupItemKind.File)
                {
                    DeleteFile(item, cancellationToken);
                }
                else
                {
                    DeleteDirectory(item, cancellationToken);
                }
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

        private static void AddSelectedGameCandidates(
            ICollection<CleanupItem> candidates,
            string gameRoot,
            string targetFolderName,
            CancellationToken cancellationToken)
        {
            candidates.Add(CreateDirectoryCandidate(
                "启动目录中的目标文件夹",
                SafeCombineUnderRoot(gameRoot, Path.Combine("Launcher", targetFolderName)),
                true,
                CleanupRule.LauncherNamedDirectory,
                gameRoot));

            candidates.Add(CreateDirectoryCandidate(
                "客户端目录中的目标文件夹",
                SafeCombineUnderRoot(gameRoot, Path.Combine("LeagueClient", targetFolderName)),
                true,
                CleanupRule.LeagueClientNamedDirectory,
                gameRoot));

            AddGameImmediateChildren(candidates, gameRoot, cancellationToken);
            AddLeagueClientLogs(candidates, gameRoot, cancellationToken);
        }

        private static void AddGameImmediateChildren(
            ICollection<CleanupItem> candidates,
            string gameRoot,
            CancellationToken cancellationToken)
        {
            var gameDirectory = SafeCombineUnderRoot(gameRoot, "Game");
            if (!Directory.Exists(gameDirectory)) return;

            if (IsReparsePoint(gameDirectory))
            {
                AppLog.Warning("Skipped Game directory because it is a reparse point: " + gameDirectory);
                return;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(gameDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (string.Equals(name, CleanupProfile.PreservedGameDirectoryName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var isDirectory = Directory.Exists(entry);
                candidates.Add(new CleanupItem
                {
                    DisplayName = isDirectory ? "Game 子目录" : "Game 文件",
                    Path = NormalizePath(entry),
                    Kind = isDirectory ? CleanupItemKind.Directory : CleanupItemKind.File,
                    Rule = CleanupRule.GameImmediateChild,
                    GameRoot = gameRoot,
                    IsGameDirectoryItem = true,
                    State = CleanupItemState.Missing,
                    Detail = "等待扫描"
                });
            }
        }

        private static void AddLeagueClientLogs(
            ICollection<CleanupItem> candidates,
            string gameRoot,
            CancellationToken cancellationToken)
        {
            var leagueClientDirectory = SafeCombineUnderRoot(gameRoot, "LeagueClient");
            if (!Directory.Exists(leagueClientDirectory)) return;

            if (IsReparsePoint(leagueClientDirectory))
            {
                AppLog.Warning("Skipped LeagueClient directory because it is a reparse point: " + leagueClientDirectory);
                return;
            }

            foreach (var file in Directory.EnumerateFiles(leagueClientDirectory, "*.log", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates.Add(new CleanupItem
                {
                    DisplayName = "客户端日志",
                    Path = NormalizePath(file),
                    Kind = CleanupItemKind.File,
                    Rule = CleanupRule.LeagueClientLogFile,
                    GameRoot = gameRoot,
                    IsGameDirectoryItem = true,
                    State = CleanupItemState.Missing,
                    Detail = "等待扫描"
                });
            }
        }

        private static CleanupItem CreateDirectoryCandidate(
            string displayName,
            string path,
            bool gameItem,
            CleanupRule rule,
            string gameRoot)
        {
            return new CleanupItem
            {
                DisplayName = displayName,
                Path = NormalizePath(path),
                Kind = CleanupItemKind.Directory,
                Rule = rule,
                GameRoot = gameRoot,
                State = CleanupItemState.Missing,
                Detail = "等待扫描",
                IsGameDirectoryItem = gameItem
            };
        }

        private static void InspectItem(CleanupItem item, CancellationToken cancellationToken)
        {
            if (!Exists(item))
            {
                item.State = CleanupItemState.Missing;
                item.Detail = "未发现";
                item.EstimatedBytes = 0;
                return;
            }

            try
            {
                EnsureAllowedTarget(item);
                if (IsReparsePoint(item.Path))
                {
                    item.State = CleanupItemState.Blocked;
                    item.Detail = "链接或重解析点，不会删除";
                    return;
                }

                if (item.Kind == CleanupItemKind.File)
                {
                    var file = new FileInfo(item.Path);
                    item.EstimatedBytes = file.Exists ? file.Length : 0;
                    item.State = file.Exists ? CleanupItemState.Found : CleanupItemState.Missing;
                    item.Detail = file.Exists ? "1 个文件" : "未发现";
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
                    foreach (var filePath in Directory.EnumerateFiles(current))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        files++;
                        try { bytes += new FileInfo(filePath).Length; } catch { }
                    }

                    foreach (var directory in Directory.EnumerateDirectories(current))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsReparsePoint(directory))
                        {
                            throw new IOException("子目录包含链接或重解析点：" + directory);
                        }
                        pending.Push(directory);
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

        private static void DeleteFile(CleanupItem item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(item.Path))
            {
                item.State = CleanupItemState.Missing;
                item.Detail = "文件不存在";
                return;
            }

            if (IsReparsePoint(item.Path))
            {
                item.State = CleanupItemState.Blocked;
                item.Detail = "检测到链接或重解析点，已拒绝删除";
                AppLog.Warning("Blocked reparse-point file deletion: " + item.Path);
                return;
            }

            File.SetAttributes(item.Path, FileAttributes.Normal);
            File.Delete(item.Path);
            item.State = File.Exists(item.Path) ? CleanupItemState.Failed : CleanupItemState.Deleted;
            item.Detail = item.State == CleanupItemState.Deleted ? "已删除" : "文件仍然存在";
            AppLog.Info("Delete result: " + item.Path + " => " + item.State);
        }

        private static void DeleteDirectory(CleanupItem item, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(item.Path))
            {
                item.State = CleanupItemState.Missing;
                item.Detail = "目录不存在";
                return;
            }

            if (IsReparsePoint(item.Path))
            {
                item.State = CleanupItemState.Blocked;
                item.Detail = "检测到链接或重解析点，已拒绝删除";
                AppLog.Warning("Blocked reparse-point directory deletion: " + item.Path);
                return;
            }

            DeleteDirectoryTree(item.Path, cancellationToken);
            item.State = Directory.Exists(item.Path) ? CleanupItemState.Failed : CleanupItemState.Deleted;
            item.Detail = item.State == CleanupItemState.Deleted ? "已删除" : "目录仍然存在";
            AppLog.Info("Delete result: " + item.Path + " => " + item.State);
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
                    if (IsReparsePoint(file))
                    {
                        throw new IOException("目录中包含链接或重解析文件：" + file);
                    }
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                File.SetAttributes(directory, FileAttributes.Normal);
                Directory.Delete(directory, false);
            }
        }

        private static void EnsureAllowedTarget(CleanupItem item)
        {
            var normalized = NormalizePath(item.Path);
            var targetFolderName = CleanupProfile.TargetFolderName.Trim();
            var programFilesTarget = NormalizePath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                targetFolderName));
            var programDataTarget = NormalizePath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                targetFolderName));

            switch (item.Rule)
            {
                case CleanupRule.FixedProgramFilesDirectory:
                    RequireExactPath(normalized, programFilesTarget);
                    RequireDirectory(item);
                    return;

                case CleanupRule.FixedProgramDataDirectory:
                    RequireExactPath(normalized, programDataTarget);
                    RequireDirectory(item);
                    return;

                case CleanupRule.LauncherNamedDirectory:
                    RequireValidGameRoot(item.GameRoot);
                    RequireExactPath(normalized, SafeCombineUnderRoot(item.GameRoot, Path.Combine("Launcher", targetFolderName)));
                    RequireDirectory(item);
                    return;

                case CleanupRule.LeagueClientNamedDirectory:
                    RequireValidGameRoot(item.GameRoot);
                    RequireExactPath(normalized, SafeCombineUnderRoot(item.GameRoot, Path.Combine("LeagueClient", targetFolderName)));
                    RequireDirectory(item);
                    return;

                case CleanupRule.GameImmediateChild:
                    RequireValidGameRoot(item.GameRoot);
                    EnsureImmediateChild(
                        normalized,
                        SafeCombineUnderRoot(item.GameRoot, "Game"),
                        CleanupProfile.PreservedGameDirectoryName);
                    return;

                case CleanupRule.LeagueClientLogFile:
                    RequireValidGameRoot(item.GameRoot);
                    EnsureImmediateLogFile(
                        normalized,
                        SafeCombineUnderRoot(item.GameRoot, "LeagueClient"));
                    RequireFile(item);
                    return;

                default:
                    throw new InvalidOperationException("未知清理规则，已拒绝删除：" + normalized);
            }
        }

        private static void EnsureImmediateChild(string path, string expectedParent, string preservedName)
        {
            var parent = NormalizePath(Path.GetDirectoryName(path));
            RequireExactPath(parent, NormalizePath(expectedParent));

            var leaf = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(leaf) ||
                string.Equals(leaf, preservedName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("目标属于保留项或不是有效的 Game 直接子项：" + path);
            }
        }

        private static void EnsureImmediateLogFile(string path, string expectedParent)
        {
            var parent = NormalizePath(Path.GetDirectoryName(path));
            RequireExactPath(parent, NormalizePath(expectedParent));
            if (!string.Equals(Path.GetExtension(path), ".log", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("目标不是允许清理的顶层日志文件：" + path);
            }
        }

        private static void RequireExactPath(string actual, string expected)
        {
            if (!string.Equals(NormalizePath(actual), NormalizePath(expected), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("目标不在 FACM 允许清理的精确路径中：" + actual);
            }
        }

        private static void RequireDirectory(CleanupItem item)
        {
            if (item.Kind != CleanupItemKind.Directory)
            {
                throw new InvalidOperationException("清理项目类型与目录规则不一致：" + item.Path);
            }
        }

        private static void RequireFile(CleanupItem item)
        {
            if (item.Kind != CleanupItemKind.File)
            {
                throw new InvalidOperationException("清理项目类型与文件规则不一致：" + item.Path);
            }
        }

        private static void RequireValidGameRoot(string gameRoot)
        {
            string validated;
            if (!TryValidateGameDirectory(gameRoot, out validated))
            {
                throw new InvalidOperationException("所选游戏目录已失效或不允许用于清理。");
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
                if (IsReparsePoint(path)) return false;
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

        private static bool Exists(CleanupItem item)
        {
            return item.Kind == CleanupItemKind.File
                ? File.Exists(item.Path)
                : Directory.Exists(item.Path);
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
