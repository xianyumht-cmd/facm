using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FACM.Configuration;

namespace FACM.Services
{
    internal enum CleanupRuleKind
    {
        ProgramFilesDirectory,
        ProgramDataDirectory,
        ContainerChild,
        ExtraDirectory,
        LogFile
    }

    internal enum CleanupTargetKind
    {
        File,
        Directory
    }

    internal sealed class CleanupTarget
    {
        public string Path { get; set; }
        public string Group { get; set; }
        public CleanupRuleKind Rule { get; set; }
        public CleanupTargetKind Kind { get; set; }
        public long EstimatedBytes { get; set; }
        public int FileCount { get; set; }
        public int DirectoryCount { get; set; }
        public bool Blocked { get; set; }
        public string Detail { get; set; }
    }

    internal sealed class CleanupPlan
    {
        public string GameRoot { get; set; }
        public IReadOnlyList<CleanupTarget> Targets { get; set; }
        public long EstimatedBytes { get; set; }
        public int FileCount { get; set; }
        public int DirectoryCount { get; set; }
        public int BlockedCount { get; set; }

        public IReadOnlyList<CleanupTarget> DeletableTargets
        {
            get { return Targets.Where(target => !target.Blocked).ToArray(); }
        }
    }

    internal sealed class CleanupResult
    {
        public int DeletedFiles { get; set; }
        public int DeletedDirectories { get; set; }
        public List<string> Failures { get; } = new List<string>();
    }

    internal static class SafeCleanupService
    {
        public static CleanupPlan CreatePlan(string selectedPath)
        {
            if (Application.MessageLoop)
            {
                return BackgroundOperationDialog.Run(
                    "FACM 清理预览",
                    "正在扫描清理目标并统计文件，请稍候…",
                    delegate { return CreatePlanCore(selectedPath); });
            }
            return CreatePlanCore(selectedPath);
        }

        private static CleanupPlan CreatePlanCore(string selectedPath)
        {
            CleanupProfile.Validate();
            var gameRoot = GameLocator.ResolveGameRoot(selectedPath);
            if (string.IsNullOrEmpty(gameRoot) || !GameLocator.IsValidGameRoot(gameRoot))
            {
                throw new InvalidOperationException("未能从所选目录解析出有效安装根目录。请检查开发者配置中的标记文件夹名。");
            }

            var candidates = new List<CleanupTarget>();
            AddDirectoryCandidate(
                candidates,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), CleanupProfile.ProgramFilesFolderName),
                "系统目录",
                CleanupRuleKind.ProgramFilesDirectory);
            AddDirectoryCandidate(
                candidates,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), CleanupProfile.ProgramDataFolderName),
                "公共数据目录",
                CleanupRuleKind.ProgramDataDirectory);

            var container = CombineInsideRoot(gameRoot, CleanupProfile.NormalizeRelativePath(
                CleanupProfile.CleanupContainerRelativePath,
                nameof(CleanupProfile.CleanupContainerRelativePath)));
            if (Directory.Exists(container))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(container, "*", SearchOption.TopDirectoryOnly))
                {
                    if (string.Equals(Path.GetFileName(entry), CleanupProfile.PreservedChildFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        AppLog.Info("Preserved configured directory: " + entry);
                        continue;
                    }

                    AddEntryCandidate(candidates, entry, "主体目录清理", CleanupRuleKind.ContainerChild);
                }
            }

            foreach (var relativePath in CleanupProfile.ExtraFolderRelativePaths)
            {
                AddDirectoryCandidate(
                    candidates,
                    CombineInsideRoot(gameRoot, relativePath),
                    "指定文件夹",
                    CleanupRuleKind.ExtraDirectory);
            }

            var logDirectory = CombineInsideRoot(gameRoot, CleanupProfile.NormalizeRelativePath(
                CleanupProfile.LogFolderRelativePath,
                nameof(CleanupProfile.LogFolderRelativePath)));
            if (Directory.Exists(logDirectory))
            {
                foreach (var log in Directory.EnumerateFiles(logDirectory, CleanupProfile.LogSearchPattern, SearchOption.TopDirectoryOnly))
                {
                    AddFileCandidate(candidates, log, "日志文件", CleanupRuleKind.LogFile);
                }
            }

            var targets = CollapseNestedTargets(candidates)
                .OrderBy(target => target.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new CleanupPlan
            {
                GameRoot = gameRoot,
                Targets = targets,
                EstimatedBytes = targets.Where(target => !target.Blocked).Sum(target => target.EstimatedBytes),
                FileCount = targets.Where(target => !target.Blocked).Sum(target => target.FileCount),
                DirectoryCount = targets.Where(target => !target.Blocked).Sum(target => target.DirectoryCount),
                BlockedCount = targets.Count(target => target.Blocked)
            };
        }

        public static CleanupResult Execute(CleanupPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (Application.MessageLoop)
            {
                return BackgroundOperationDialog.Run(
                    "FACM 正在清理",
                    "正在安全删除已确认项目，请勿关闭程序…",
                    delegate { return ExecuteCore(plan); });
            }
            return ExecuteCore(plan);
        }

        private static CleanupResult ExecuteCore(CleanupPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            CleanupProfile.Validate();

            if (plan.DeletableTargets.Any(target => target.Rule == CleanupRuleKind.ProgramFilesDirectory ||
                                                    target.Rule == CleanupRuleKind.ProgramDataDirectory) &&
                !ElevationService.IsAdministrator)
            {
                throw new InvalidOperationException("清理系统目录需要管理员权限。");
            }

            var result = new CleanupResult();
            foreach (var target in plan.Targets.OrderByDescending(item => item.Path.Length))
            {
                if (target.Blocked)
                {
                    result.Failures.Add(target.Path + "：" + target.Detail);
                    continue;
                }

                try
                {
                    RevalidateTarget(plan.GameRoot, target);
                    if (target.Kind == CleanupTargetKind.File)
                    {
                        if (!File.Exists(target.Path)) continue;
                        File.SetAttributes(target.Path, FileAttributes.Normal);
                        File.Delete(target.Path);
                        result.DeletedFiles++;
                    }
                    else
                    {
                        if (!Directory.Exists(target.Path)) continue;
                        DeleteDirectoryTree(target.Path);
                        result.DeletedFiles += target.FileCount;
                        result.DeletedDirectories += Math.Max(1, target.DirectoryCount);
                    }

                    AppLog.Info("Deleted configured cleanup target: " + target.Path);
                }
                catch (Exception exception)
                {
                    var message = target.Path + "：" + exception.Message;
                    result.Failures.Add(message);
                    AppLog.Error("Failed to delete configured cleanup target: " + target.Path, exception);
                }
            }

            return result;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024L) return (bytes / 1024d).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024L * 1024L) return (bytes / 1024d / 1024d).ToString("0.0") + " MB";
            return (bytes / 1024d / 1024d / 1024d).ToString("0.00") + " GB";
        }

        private static void AddEntryCandidate(List<CleanupTarget> targets, string path, string group, CleanupRuleKind rule)
        {
            if (File.Exists(path)) AddFileCandidate(targets, path, group, rule);
            else if (Directory.Exists(path)) AddDirectoryCandidate(targets, path, group, rule);
        }

        private static void AddFileCandidate(List<CleanupTarget> targets, string path, string group, CleanupRuleKind rule)
        {
            if (!File.Exists(path)) return;
            var full = NormalizePath(path);
            var target = new CleanupTarget
            {
                Path = full,
                Group = group,
                Rule = rule,
                Kind = CleanupTargetKind.File,
                FileCount = 1,
                DirectoryCount = 0,
                Detail = "可清理"
            };

            try
            {
                var attributes = File.GetAttributes(full);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    target.Blocked = true;
                    target.Detail = "检测到重解析点，已阻止";
                }
                else
                {
                    target.EstimatedBytes = new FileInfo(full).Length;
                }
            }
            catch (Exception exception)
            {
                target.Blocked = true;
                target.Detail = "无法读取：" + exception.Message;
            }

            targets.Add(target);
        }

        private static void AddDirectoryCandidate(List<CleanupTarget> targets, string path, string group, CleanupRuleKind rule)
        {
            if (!Directory.Exists(path)) return;
            var full = NormalizePath(path);
            var target = new CleanupTarget
            {
                Path = full,
                Group = group,
                Rule = rule,
                Kind = CleanupTargetKind.Directory,
                DirectoryCount = 1,
                Detail = "可清理"
            };

            InspectDirectory(target);
            targets.Add(target);
        }

        private static void InspectDirectory(CleanupTarget target)
        {
            var pending = new Stack<string>();
            pending.Push(target.Path);
            try
            {
                while (pending.Count > 0)
                {
                    var current = pending.Pop();
                    var attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        target.Blocked = true;
                        target.Detail = "目录中包含重解析点，已阻止";
                        return;
                    }

                    foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
                    {
                        var fileAttributes = File.GetAttributes(file);
                        if ((fileAttributes & FileAttributes.ReparsePoint) != 0)
                        {
                            target.Blocked = true;
                            target.Detail = "目录中包含重解析点，已阻止";
                            return;
                        }

                        target.FileCount++;
                        try { target.EstimatedBytes += new FileInfo(file).Length; } catch { }
                    }

                    foreach (var directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
                    {
                        var directoryAttributes = File.GetAttributes(directory);
                        if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
                        {
                            target.Blocked = true;
                            target.Detail = "目录中包含重解析点，已阻止";
                            return;
                        }

                        target.DirectoryCount++;
                        pending.Push(directory);
                    }
                }

                target.Detail = target.FileCount + " 个文件，" + target.DirectoryCount + " 个文件夹";
            }
            catch (Exception exception)
            {
                target.Blocked = true;
                target.Detail = "无法完整读取：" + exception.Message;
            }
        }

        private static IReadOnlyList<CleanupTarget> CollapseNestedTargets(IEnumerable<CleanupTarget> candidates)
        {
            var distinct = candidates
                .GroupBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(target => target.Path.Length)
                .ToList();
            var result = new List<CleanupTarget>();

            foreach (var candidate in distinct)
            {
                var covered = result.Any(parent => parent.Kind == CleanupTargetKind.Directory &&
                    IsInside(candidate.Path, parent.Path));
                if (!covered) result.Add(candidate);
            }

            return result;
        }

        private static void RevalidateTarget(string gameRoot, CleanupTarget target)
        {
            var actual = NormalizePath(target.Path);
            string expected;

            switch (target.Rule)
            {
                case CleanupRuleKind.ProgramFilesDirectory:
                    expected = NormalizePath(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        CleanupProfile.ProgramFilesFolderName));
                    if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("系统目录规则校验失败。");
                    break;

                case CleanupRuleKind.ProgramDataDirectory:
                    expected = NormalizePath(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        CleanupProfile.ProgramDataFolderName));
                    if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("公共数据目录规则校验失败。");
                    break;

                case CleanupRuleKind.ContainerChild:
                    var container = CombineInsideRoot(gameRoot, CleanupProfile.NormalizeRelativePath(
                        CleanupProfile.CleanupContainerRelativePath,
                        nameof(CleanupProfile.CleanupContainerRelativePath)));
                    if (!string.Equals(Path.GetDirectoryName(actual), container, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileName(actual), CleanupProfile.PreservedChildFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("主体目录规则校验失败。");
                    }
                    break;

                case CleanupRuleKind.ExtraDirectory:
                    var allowedExtra = CleanupProfile.ExtraFolderRelativePaths
                        .Select(relative => CombineInsideRoot(gameRoot, relative));
                    if (!allowedExtra.Any(path => string.Equals(path, actual, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException("指定文件夹规则校验失败。");
                    }
                    break;

                case CleanupRuleKind.LogFile:
                    var logDirectory = CombineInsideRoot(gameRoot, CleanupProfile.NormalizeRelativePath(
                        CleanupProfile.LogFolderRelativePath,
                        nameof(CleanupProfile.LogFolderRelativePath)));
                    if (!string.Equals(Path.GetDirectoryName(actual), logDirectory, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(Path.GetExtension(actual), ".log", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("日志文件规则校验失败。");
                    }
                    break;

                default:
                    throw new InvalidOperationException("未知清理规则。");
            }
        }

        private static void DeleteDirectoryTree(string root)
        {
            var normalizedRoot = NormalizePath(root);
            var stack = new Stack<Tuple<string, bool>>();
            stack.Push(Tuple.Create(normalizedRoot, false));

            while (stack.Count > 0)
            {
                var item = stack.Pop();
                var current = item.Item1;
                if (!Directory.Exists(current)) continue;

                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("拒绝删除重解析点目录：" + current);
                }

                if (!item.Item2)
                {
                    stack.Push(Tuple.Create(current, true));
                    foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
                    {
                        var fileAttributes = File.GetAttributes(file);
                        if ((fileAttributes & FileAttributes.ReparsePoint) != 0)
                        {
                            throw new IOException("拒绝删除重解析点文件：" + file);
                        }

                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }

                    foreach (var directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
                    {
                        var directoryAttributes = File.GetAttributes(directory);
                        if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
                        {
                            throw new IOException("拒绝删除重解析点目录：" + directory);
                        }

                        stack.Push(Tuple.Create(directory, false));
                    }
                }
                else
                {
                    File.SetAttributes(current, FileAttributes.Normal);
                    Directory.Delete(current, false);
                }
            }
        }

        private static string CombineInsideRoot(string root, string relativePath)
        {
            var normalizedRoot = NormalizePath(root);
            var combined = NormalizePath(Path.Combine(normalizedRoot, relativePath));
            if (!IsInside(combined, normalizedRoot)) throw new InvalidOperationException("配置路径超出安装根目录。");
            return combined;
        }

        private static bool IsInside(string candidate, string parent)
        {
            var normalizedCandidate = NormalizePath(candidate);
            var normalizedParent = NormalizePath(parent);
            if (string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase)) return false;
            return normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
