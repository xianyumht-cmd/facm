using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using FACM.Core.Cleanup;
using Microsoft.Win32;

namespace FACM.Platform.Windows.Cleanup;

public sealed class WindowsCleanupEnvironment : ICleanupEnvironment
{
    private const int MaxVisitedDirectories = 2500;
    private static readonly TimeSpan SearchTimeBudget = TimeSpan.FromSeconds(5);
    private readonly CleanupProfileSnapshot _profile;

    public WindowsCleanupEnvironment(CleanupProfileSnapshot? profile = null)
    {
        _profile = profile ?? CleanupProfileContract.Facm35;
        CleanupProfileContract.Validate(_profile);
    }

    public bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public Task<string?> FindGameRootAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => FindGameRootCore(cancellationToken), cancellationToken);

    public Task<string?> ResolveGameRootAsync(
        string selectedOrCandidatePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selectedOrCandidatePath))
            return Task.FromResult<string?>(null);
        return Task.Run(
            () => ResolveGameRootCore(selectedOrCandidatePath, new SearchBudget(), cancellationToken),
            cancellationToken);
    }

    public bool IsValidGameRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = NormalizeDirectory(path);
            return !IsProtectedRoot(full) && Directory.Exists(Path.Combine(full, _profile.GameRootMarkerFolderName));
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<string> GetRunningRelatedProcesses()
    {
        var configured = new HashSet<string>(
            CleanupProfileContract.NormalizeProcessNames(_profile.RelatedProcessNames),
            StringComparer.OrdinalIgnoreCase);
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (configured.Contains(process.ProcessName)) running.Add(process.ProcessName);
            }
            catch
            {
                // A process can disappear or deny access while being inspected.
            }
            finally
            {
                process.Dispose();
            }
        }
        return running.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool RestartElevatedForCleanup()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return false;
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--cleanup",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
            });
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private string? FindGameRootCore(CancellationToken cancellationToken)
    {
        var budget = new SearchBudget();
        foreach (var processName in CleanupProfileContract.NormalizeProcessNames(_profile.RelatedProcessNames))
        {
            budget.Check(cancellationToken);
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    budget.Check(cancellationToken);
                    var executable = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(executable)) continue;
                    var root = ResolveGameRootCore(executable, budget, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(root)) return root;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (GameRootSearchLimitException)
                {
                    throw;
                }
                catch
                {
                    // Access to another process's module path is best-effort.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        foreach (var candidate in EnumerateRegistryCandidates())
        {
            budget.Check(cancellationToken);
            var root = ResolveGameRootCore(candidate, budget, cancellationToken);
            if (!string.IsNullOrWhiteSpace(root)) return root;
        }
        return null;
    }

    private string? ResolveGameRootCore(
        string selectedOrCandidatePath,
        SearchBudget budget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var candidate = Environment.ExpandEnvironmentVariables(selectedOrCandidatePath.Trim().Trim('"'));
            var comma = candidate.IndexOf(',');
            if (comma > 0) candidate = candidate[..comma].Trim().Trim('"');
            if (File.Exists(candidate)) candidate = Path.GetDirectoryName(candidate) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate)) return null;

            var directory = new DirectoryInfo(Path.GetFullPath(candidate));
            for (var index = 0; index < 8 && directory is not null; index++, directory = directory.Parent)
            {
                budget.Check(cancellationToken);
                var direct = ResolveDirectMarker(directory.FullName);
                if (!string.IsNullOrWhiteSpace(direct)) return direct;
            }

            return SearchBelow(Path.GetFullPath(candidate), _profile.MaxMarkerSearchDepth, budget, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GameRootSearchLimitException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private string? SearchBelow(
        string startPath,
        int maxDepth,
        SearchBudget budget,
        CancellationToken cancellationToken)
    {
        var start = NormalizeDirectory(startPath);
        var queue = new Queue<(string Path, int Depth)>();
        budget.Visit(cancellationToken);
        queue.Enqueue((start, 0));

        while (queue.Count > 0)
        {
            budget.Check(cancellationToken);
            var current = queue.Dequeue();
            var direct = ResolveDirectMarker(current.Path);
            if (!string.IsNullOrWhiteSpace(direct)) return direct;
            if (current.Depth >= maxDepth) continue;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(current.Path, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            try
            {
                foreach (var child in children)
                {
                    budget.Check(cancellationToken);
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                        if (IsProtectedRoot(child)) continue;
                        budget.Visit(cancellationToken);
                        queue.Enqueue((child, current.Depth + 1));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (GameRootSearchLimitException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Ignore inaccessible children while resolving the configured marker.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (GameRootSearchLimitException)
            {
                throw;
            }
            catch
            {
                // Enumeration can fail midway if the directory changes.
            }
        }
        return null;
    }

    private string? ResolveDirectMarker(string directory)
    {
        var full = NormalizeDirectory(directory);
        if (IsProtectedRoot(full)) return null;
        if (string.Equals(Path.GetFileName(full), _profile.GameRootMarkerFolderName, StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(full);
            return parent is not null && !IsProtectedRoot(parent.FullName) ? parent.FullName : null;
        }
        return Directory.Exists(Path.Combine(full, _profile.GameRootMarkerFolderName)) ? full : null;
    }

    private IEnumerable<string> EnumerateRegistryCandidates()
    {
        foreach (var candidate in ReadUninstallLocations(RegistryHive.CurrentUser, RegistryView.Default)) yield return candidate;
        foreach (var candidate in ReadUninstallLocations(RegistryHive.LocalMachine, RegistryView.Registry64)) yield return candidate;
        foreach (var candidate in ReadUninstallLocations(RegistryHive.LocalMachine, RegistryView.Registry32)) yield return candidate;
    }

    private IEnumerable<string> ReadUninstallLocations(RegistryHive hive, RegistryView view)
    {
        RegistryKey? baseKey = null;
        RegistryKey? uninstall = null;
        try
        {
            baseKey = RegistryKey.OpenBaseKey(hive, view);
            uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) yield break;
            foreach (var subName in uninstall.GetSubKeyNames())
            {
                using var sub = uninstall.OpenSubKey(subName);
                if (sub is null) continue;
                var displayName = Convert.ToString(sub.GetValue("DisplayName"));
                if (string.IsNullOrWhiteSpace(displayName) ||
                    displayName.IndexOf(_profile.RegistryDisplayNameKeyword, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                foreach (var name in new[] { "InstallLocation", "DisplayIcon", "UninstallString" })
                {
                    var value = Convert.ToString(sub.GetValue(name));
                    if (!string.IsNullOrWhiteSpace(value)) yield return value;
                }
            }
        }
        finally
        {
            uninstall?.Dispose();
            baseKey?.Dispose();
        }
    }

    private static string NormalizeDirectory(string path)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        var root = Path.GetPathRoot(full);
        var trimmedFull = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var trimmedRoot = string.IsNullOrWhiteSpace(root)
            ? null
            : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(root) && string.Equals(trimmedFull, trimmedRoot, StringComparison.OrdinalIgnoreCase))
            return root;
        return trimmedFull;
    }

    private static bool IsProtectedRoot(string path)
    {
        var full = NormalizeDirectory(path);
        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrWhiteSpace(root) &&
            string.Equals(
                full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            return true;

        var protectedPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        };
        return protectedPaths
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeDirectory)
            .Any(value => string.Equals(value, full, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class SearchBudget
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _visited;

        public void Visit(CancellationToken cancellationToken)
        {
            Check(cancellationToken);
            if (_visited >= MaxVisitedDirectories) throw new GameRootSearchLimitException(_visited, _clock.Elapsed);
            _visited++;
        }

        public void Check(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_clock.Elapsed > SearchTimeBudget) throw new GameRootSearchLimitException(_visited, _clock.Elapsed);
        }
    }
}

public sealed class GameRootSearchLimitException(int visitedDirectories, TimeSpan elapsed)
    : InvalidOperationException(
        $"目录识别范围过大，已检查 {visitedDirectories} 个目录、耗时 {Math.Max(1, (int)Math.Ceiling(elapsed.TotalSeconds))} 秒。请改选更具体的安装目录后重试。")
{
    public int VisitedDirectories { get; } = visitedDirectories;
    public TimeSpan Elapsed { get; } = elapsed;
}
