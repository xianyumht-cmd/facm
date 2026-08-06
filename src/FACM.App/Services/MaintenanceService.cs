namespace FACM.App.Services;

public sealed class MaintenanceService
{
    private readonly string _appDataRoot;

    public MaintenanceService(string appDataRoot)
    {
        _appDataRoot = Path.GetFullPath(appDataRoot);
    }

    public IReadOnlyList<string> ManagedDirectories =>
    [
        Path.Combine(_appDataRoot, "Runtime"),
        Path.Combine(_appDataRoot, "Cache"),
        Path.Combine(_appDataRoot, "Logs")
    ];

    public Task<MaintenancePreview> InspectAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            int fileCount = 0;
            long totalBytes = 0;
            List<string> warnings = [];

            foreach (string directory in ManagedDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                DirectoryInfo rootInfo = new(directory);
                if (rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    warnings.Add($"已跳过重解析目录：{directory}");
                    continue;
                }

                foreach (string file in EnumerateFilesSafely(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        FileInfo info = new(file);
                        fileCount++;
                        totalBytes += info.Length;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        warnings.Add($"无法读取：{file}");
                    }
                }
            }

            return new MaintenancePreview(fileCount, totalBytes, warnings);
        }, cancellationToken);
    }

    public Task<MaintenanceResult> CleanAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            int deletedFiles = 0;
            int deletedDirectories = 0;
            List<string> failures = [];

            foreach (string directory in ManagedDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                DirectoryInfo rootInfo = new(directory);
                if (rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    failures.Add($"拒绝清理重解析目录：{directory}");
                    continue;
                }

                foreach (string file in EnumerateFilesSafely(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                        deletedFiles++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures.Add($"{file}：{ex.Message}");
                    }
                }

                foreach (string childDirectory in EnumerateDirectoriesSafely(directory)
                             .OrderByDescending(path => path.Length))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        DirectoryInfo info = new(childDirectory);
                        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            failures.Add($"已跳过重解析目录：{childDirectory}");
                            continue;
                        }

                        if (!Directory.EnumerateFileSystemEntries(childDirectory).Any())
                        {
                            Directory.Delete(childDirectory, false);
                            deletedDirectories++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures.Add($"{childDirectory}：{ex.Message}");
                    }
                }
            }

            return new MaintenanceResult(deletedFiles, deletedDirectories, failures);
        }, cancellationToken);
    }

    private IEnumerable<string> EnumerateFilesSafely(string root)
    {
        EnsureManagedPath(root);
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        return Directory.EnumerateFiles(root, "*", options);
    }

    private IEnumerable<string> EnumerateDirectoriesSafely(string root)
    {
        EnsureManagedPath(root);
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        return Directory.EnumerateDirectories(root, "*", options);
    }

    private void EnsureManagedPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string rootWithSeparator = _appDataRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("维护路径不属于 FACM 数据目录。");
        }
    }
}

public sealed record MaintenancePreview(
    int FileCount,
    long TotalBytes,
    IReadOnlyList<string> Warnings);

public sealed record MaintenanceResult(
    int DeletedFiles,
    int DeletedDirectories,
    IReadOnlyList<string> Failures);
