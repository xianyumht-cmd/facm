using System.Diagnostics;
using FACM.Core.Cleanup;

namespace FACM.Platform.Windows.Cleanup;

public sealed class WindowsCleanupEngine : ICleanupPlanner, ICleanupExecutor
{
    private const int MaxScannedEntriesPerTarget = 200_000;
    private static readonly TimeSpan MaxScanTimePerTarget = TimeSpan.FromSeconds(30);

    private readonly CleanupProfileSnapshot _profile;
    private readonly ICleanupEnvironment _environment;

    public WindowsCleanupEngine(
        ICleanupEnvironment environment,
        CleanupProfileSnapshot? profile = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _profile = profile ?? CleanupProfileContract.Facm35;
        CleanupProfileContract.Validate(_profile);
    }

    public async Task<CleanupPlan> CreatePlanAsync(
        string selectedPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        cancellationToken.ThrowIfCancellationRequested();
        var gameRoot = await _environment.ResolveGameRootAsync(selectedPath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(gameRoot) || !_environment.IsValidGameRoot(gameRoot))
            throw new InvalidOperationException("未能从所选目录解析出有效英雄联盟安装根目录。");

        return await Task.Run(() => CreatePlanCore(gameRoot, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public Task<CleanupResult> ExecuteAsync(
        CleanupPlan plan,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Task.Run(() => ExecuteCore(plan, progress, cancellationToken), cancellationToken);
    }

    private CleanupPlan CreatePlanCore(string gameRoot, CancellationToken cancellationToken)
    {
        CleanupProfileContract.Validate(_profile);
        var normalizedRoot = NormalizePath(gameRoot);
        if (!_environment.IsValidGameRoot(normalizedRoot))
            throw new InvalidOperationException("清理预览前安装根目录再次校验失败。");

        var candidates = new List<CleanupTarget>();
        AddDirectoryCandidate(
            candidates,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), _profile.ProgramFilesFolderName),
            "系统目录",
            CleanupRuleKind.ProgramFilesDirectory,
            cancellationToken);
        AddDirectoryCandidate(
            candidates,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), _profile.ProgramDataFolderName),
            "公共数据目录",
            CleanupRuleKind.ProgramDataDirectory,
            cancellationToken);

        var container = CombineInsideRoot(
            normalizedRoot,
            CleanupProfileContract.NormalizeRelativePath(
                _profile.CleanupContainerRelativePath,
                nameof(_profile.CleanupContainerRelativePath)));
        if (Directory.Exists(container))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(container, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(Path.GetFileName(entry), _profile.PreservedChildFolderName, StringComparison.OrdinalIgnoreCase))
                    continue;
                AddEntryCandidate(candidates, entry, "主体目录清理", CleanupRuleKind.ContainerChild, cancellationToken);
            }
        }

        foreach (var relativePath in _profile.ExtraFolderRelativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddDirectoryCandidate(
                candidates,
                CombineInsideRoot(normalizedRoot, CleanupProfileContract.NormalizeRelativePath(relativePath, "ExtraFolderRelativePath")),
                "指定文件夹",
                CleanupRuleKind.ExtraDirectory,
                cancellationToken);
        }

        var logDirectory = CombineInsideRoot(
            normalizedRoot,
            CleanupProfileContract.NormalizeRelativePath(_profile.LogFolderRelativePath, nameof(_profile.LogFolderRelativePath)));
        if (Directory.Exists(logDirectory))
        {
            foreach (var log in Directory.EnumerateFiles(logDirectory, _profile.LogSearchPattern, SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddFileCandidate(candidates, log, "日志文件", CleanupRuleKind.LogFile);
            }
        }

        var targets = CollapseNestedTargets(candidates)
            .OrderBy(target => target.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new CleanupPlan(normalizedRoot, targets);
    }

    private CleanupResult ExecuteCore(
        CleanupPlan plan,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        CleanupProfileContract.Validate(_profile);
        if (!_environment.IsValidGameRoot(plan.GameRoot))
            throw new InvalidOperationException("清理执行前安装根目录再次校验失败。");

        var running = _environment.GetRunningRelatedProcesses();
        if (running.Count > 0)
            throw new InvalidOperationException("检测到相关进程仍在运行：" + string.Join("、", running));

        if (plan.DeletableTargets.Any(target =>
                target.Rule is CleanupRuleKind.ProgramFilesDirectory or CleanupRuleKind.ProgramDataDirectory) &&
            !_environment.IsAdministrator)
            throw new InvalidOperationException("清理系统目录需要管理员权限。");

        var deletedFiles = 0;
        var deletedDirectories = 0;
        var failures = new List<string>();
        var ordered = plan.Targets.OrderByDescending(target => target.Path.Length).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = ordered[index];
            progress?.Report(new CleanupProgress("validate", index, ordered.Length, target.Path));

            if (target.Blocked)
            {
                failures.Add(target.Path + "：" + target.Detail);
                continue;
            }

            try
            {
                RevalidateTarget(plan.GameRoot, target);
                cancellationToken.ThrowIfCancellationRequested();
                if (target.Kind == CleanupTargetKind.File)
                {
                    if (!File.Exists(target.Path)) continue;
                    EnsureNotReparsePoint(target.Path, isDirectory: false);
                    File.SetAttributes(target.Path, FileAttributes.Normal);
                    File.Delete(target.Path);
                    deletedFiles++;
                }
                else
                {
                    if (!Directory.Exists(target.Path)) continue;
                    DeleteDirectoryTree(target.Path, cancellationToken);
                    deletedFiles += target.FileCount;
                    deletedDirectories += Math.Max(1, target.DirectoryCount);
                }
                progress?.Report(new CleanupProgress("deleted", index + 1, ordered.Length, target.Path));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(target.Path + "：" + exception.Message);
                progress?.Report(new CleanupProgress("failed", index + 1, ordered.Length, target.Path));
            }
        }

        return new CleanupResult(deletedFiles, deletedDirectories, failures);
    }

    private void AddEntryCandidate(
        List<CleanupTarget> targets,
        string path,
        string group,
        CleanupRuleKind rule,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path)) AddFileCandidate(targets, path, group, rule);
        else if (Directory.Exists(path)) AddDirectoryCandidate(targets, path, group, rule, cancellationToken);
    }

    private static void AddFileCandidate(
        List<CleanupTarget> targets,
        string path,
        string group,
        CleanupRuleKind rule)
    {
        if (!File.Exists(path)) return;
        var full = NormalizePath(path);
        var blocked = false;
        var detail = "可清理";
        long bytes = 0;
        try
        {
            var attributes = File.GetAttributes(full);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                blocked = true;
                detail = "检测到重解析点，已阻止";
            }
            else
            {
                bytes = new FileInfo(full).Length;
            }
        }
        catch (Exception exception)
        {
            blocked = true;
            detail = "无法读取：" + exception.Message;
        }

        targets.Add(new CleanupTarget(full, group, rule, CleanupTargetKind.File, bytes, 1, 0, blocked, detail));
    }

    private static void AddDirectoryCandidate(
        List<CleanupTarget> targets,
        string path,
        string group,
        CleanupRuleKind rule,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path)) return;
        var full = NormalizePath(path);
        var inspection = InspectDirectory(full, cancellationToken);
        targets.Add(new CleanupTarget(
            full,
            group,
            rule,
            CleanupTargetKind.Directory,
            inspection.EstimatedBytes,
            inspection.FileCount,
            inspection.DirectoryCount,
            inspection.Blocked,
            inspection.Detail));
    }

    private static DirectoryInspection InspectDirectory(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var clock = Stopwatch.StartNew();
        var visitedEntries = 0;
        var files = 0;
        var directories = 1;
        long bytes = 0;
        try
        {
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (clock.Elapsed > MaxScanTimePerTarget || visitedEntries > MaxScannedEntriesPerTarget)
                    return new DirectoryInspection(bytes, files, directories, true, "扫描范围超过安全预算，已阻止");

                var current = pending.Pop();
                EnsureNotReparsePoint(current, isDirectory: true);
                foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    visitedEntries++;
                    EnsureNotReparsePoint(file, isDirectory: false);
                    files++;
                    try { bytes += new FileInfo(file).Length; } catch { }
                    if (visitedEntries > MaxScannedEntriesPerTarget)
                        return new DirectoryInspection(bytes, files, directories, true, "扫描范围超过安全预算，已阻止");
                }

                foreach (var directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    visitedEntries++;
                    EnsureNotReparsePoint(directory, isDirectory: true);
                    directories++;
                    pending.Push(directory);
                    if (visitedEntries > MaxScannedEntriesPerTarget)
                        return new DirectoryInspection(bytes, files, directories, true, "扫描范围超过安全预算，已阻止");
                }
            }
            return new DirectoryInspection(bytes, files, directories, false, $"{files} 个文件，{directories} 个文件夹");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException exception) when (exception.Message.Contains("重解析点", StringComparison.Ordinal))
        {
            return new DirectoryInspection(bytes, files, directories, true, "目录中包含重解析点，已阻止");
        }
        catch (Exception exception)
        {
            return new DirectoryInspection(bytes, files, directories, true, "无法完整读取：" + exception.Message);
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
            var covered = result.Any(parent =>
                parent.Kind == CleanupTargetKind.Directory && IsInside(candidate.Path, parent.Path));
            if (!covered) result.Add(candidate);
        }
        return result;
    }

    private void RevalidateTarget(string gameRoot, CleanupTarget target)
    {
        var actual = NormalizePath(target.Path);
        switch (target.Rule)
        {
            case CleanupRuleKind.ProgramFilesDirectory:
                var expectedProgramFiles = NormalizePath(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    _profile.ProgramFilesFolderName));
                if (!string.Equals(actual, expectedProgramFiles, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("系统目录规则校验失败。");
                break;

            case CleanupRuleKind.ProgramDataDirectory:
                var expectedProgramData = NormalizePath(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    _profile.ProgramDataFolderName));
                if (!string.Equals(actual, expectedProgramData, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("公共数据目录规则校验失败。");
                break;

            case CleanupRuleKind.ContainerChild:
                var container = CombineInsideRoot(
                    gameRoot,
                    CleanupProfileContract.NormalizeRelativePath(
                        _profile.CleanupContainerRelativePath,
                        nameof(_profile.CleanupContainerRelativePath)));
                if (!string.Equals(Path.GetDirectoryName(actual), container, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(actual), _profile.PreservedChildFolderName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("主体目录规则校验失败。");
                break;

            case CleanupRuleKind.ExtraDirectory:
                var allowedExtras = _profile.ExtraFolderRelativePaths
                    .Select(relative => CombineInsideRoot(gameRoot, CleanupProfileContract.NormalizeRelativePath(relative, "ExtraFolderRelativePath")));
                if (!allowedExtras.Any(path => string.Equals(path, actual, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("指定文件夹规则校验失败。");
                break;

            case CleanupRuleKind.LogFile:
                var logDirectory = CombineInsideRoot(
                    gameRoot,
                    CleanupProfileContract.NormalizeRelativePath(_profile.LogFolderRelativePath, nameof(_profile.LogFolderRelativePath)));
                if (!string.Equals(Path.GetDirectoryName(actual), logDirectory, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetExtension(actual), ".log", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("日志文件规则校验失败。");
                break;

            default:
                throw new InvalidOperationException("未知清理规则。");
        }
    }

    private static void DeleteDirectoryTree(string root, CancellationToken cancellationToken)
    {
        var normalizedRoot = NormalizePath(root);
        var stack = new Stack<(string Path, bool ChildrenVisited)>();
        stack.Push((normalizedRoot, false));
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = stack.Pop();
            if (!Directory.Exists(item.Path)) continue;
            EnsureNotReparsePoint(item.Path, isDirectory: true);

            if (!item.ChildrenVisited)
            {
                stack.Push((item.Path, true));
                foreach (var file in Directory.EnumerateFiles(item.Path, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureNotReparsePoint(file, isDirectory: false);
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                foreach (var directory in Directory.EnumerateDirectories(item.Path, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureNotReparsePoint(directory, isDirectory: true);
                    stack.Push((directory, false));
                }
            }
            else
            {
                File.SetAttributes(item.Path, FileAttributes.Normal);
                Directory.Delete(item.Path, recursive: false);
            }
        }
    }

    private static void EnsureNotReparsePoint(string path, bool isDirectory)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"拒绝删除重解析点{(isDirectory ? "目录" : "文件")}：{path}");
    }

    private static string CombineInsideRoot(string root, string relativePath)
    {
        var normalizedRoot = NormalizePath(root);
        var combined = NormalizePath(Path.Combine(normalizedRoot, relativePath));
        if (!IsInside(combined, normalizedRoot))
            throw new InvalidOperationException("配置路径超出安装根目录。");
        return combined;
    }

    private static bool IsInside(string candidate, string parent)
    {
        var normalizedCandidate = NormalizePath(candidate);
        var normalizedParent = NormalizePath(parent);
        if (string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase)) return false;
        return normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed record DirectoryInspection(
        long EstimatedBytes,
        int FileCount,
        int DirectoryCount,
        bool Blocked,
        string Detail);
}
