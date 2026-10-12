using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FACM.Configuration;
using Microsoft.Win32;

namespace FACM.Services
{
    internal sealed class GameLocationSearchLimitException : InvalidOperationException
    {
        public GameLocationSearchLimitException(int visitedDirectories, TimeSpan elapsed)
            : base(
                "目录识别范围过大，已检查 " + visitedDirectories +
                " 个目录、耗时 " + Math.Max(1, (int)Math.Ceiling(elapsed.TotalSeconds)) +
                " 秒。请改选更具体的安装目录后重试。")
        {
        }
    }

    internal sealed class GameLocationSearchCancelledException : InvalidOperationException
    {
        public GameLocationSearchCancelledException()
            : base("已取消目录识别。")
        {
        }
    }

    internal static class GameLocator
    {
        private const int DefaultMaxVisitedDirectories = 2500;
        private static readonly TimeSpan DefaultSearchTimeBudget = TimeSpan.FromSeconds(5);

        public static string FindGameRoot()
        {
            CleanupProfile.Validate();
            return ExecuteSearch(
                "正在从进程与注册表识别目录…",
                delegate(CancellationToken cancellationToken, IProgress<int> progress)
                {
                    var budget = new SearchBudget(DefaultMaxVisitedDirectories, DefaultSearchTimeBudget);
                    return FindGameRootCore(cancellationToken, progress, budget);
                });
        }

        public static string ResolveGameRoot(string selectedOrCandidatePath)
        {
            if (string.IsNullOrWhiteSpace(selectedOrCandidatePath)) return null;
            CleanupProfile.Validate();

            return ExecuteSearch(
                "正在搜索所选范围…",
                delegate(CancellationToken cancellationToken, IProgress<int> progress)
                {
                    var budget = new SearchBudget(DefaultMaxVisitedDirectories, DefaultSearchTimeBudget);
                    return ResolveGameRootCore(selectedOrCandidatePath, cancellationToken, progress, budget);
                });
        }

        internal static string ResolveGameRootForTest(
            string selectedOrCandidatePath,
            int maxVisitedDirectories,
            TimeSpan timeBudget,
            CancellationToken cancellationToken)
        {
            CleanupProfile.Validate();
            var budget = new SearchBudget(maxVisitedDirectories, timeBudget);
            return ResolveGameRootCore(selectedOrCandidatePath, cancellationToken, null, budget);
        }

        internal static string NormalizeDirectoryForTest(string path)
        {
            return NormalizeDirectory(path);
        }

        public static bool IsValidGameRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !CleanupProfile.IsConfigured) return false;
            try
            {
                var full = NormalizeDirectory(path);
                if (string.IsNullOrEmpty(full) || IsProtectedRoot(full)) return false;
                return Directory.Exists(Path.Combine(full, CleanupProfile.GameRootMarkerFolderName));
            }
            catch
            {
                return false;
            }
        }

        private static string ExecuteSearch(
            string statusText,
            Func<CancellationToken, IProgress<int>, string> worker)
        {
            if (worker == null) throw new ArgumentNullException(nameof(worker));

            // CompactMenuForm calls GameLocator from the WinForms UI thread. A modal progress dialog
            // keeps that message loop responsive while the synchronous file-system APIs run on a worker.
            if (Application.MessageLoop)
                return GameLocatorSearchDialog.Run(statusText, worker);

            return worker(CancellationToken.None, null);
        }

        private static string FindGameRootCore(
            CancellationToken cancellationToken,
            IProgress<int> progress,
            SearchBudget budget)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var running = FindFromRunningProcesses(cancellationToken, progress, budget);
            if (!string.IsNullOrEmpty(running)) return running;

            foreach (var candidate in EnumerateRegistryCandidates())
            {
                budget.Check(cancellationToken);
                var root = ResolveGameRootCore(candidate, cancellationToken, progress, budget);
                if (!string.IsNullOrEmpty(root)) return root;
            }

            return null;
        }

        private static string ResolveGameRootCore(
            string selectedOrCandidatePath,
            CancellationToken cancellationToken,
            IProgress<int> progress,
            SearchBudget budget)
        {
            if (string.IsNullOrWhiteSpace(selectedOrCandidatePath)) return null;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var candidate = Environment.ExpandEnvironmentVariables(selectedOrCandidatePath.Trim().Trim('"'));
                var comma = candidate.IndexOf(',');
                if (comma > 0) candidate = candidate.Substring(0, comma).Trim().Trim('"');
                if (File.Exists(candidate)) candidate = Path.GetDirectoryName(candidate);
                if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate)) return null;

                var directory = new DirectoryInfo(Path.GetFullPath(candidate));
                for (var i = 0; i < 8 && directory != null; i++, directory = directory.Parent)
                {
                    budget.Check(cancellationToken);
                    var direct = ResolveDirectMarker(directory.FullName);
                    if (!string.IsNullOrEmpty(direct)) return direct;
                }

                return SearchBelow(
                    Path.GetFullPath(candidate),
                    CleanupProfile.MaxMarkerSearchDepth,
                    cancellationToken,
                    progress,
                    budget);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (GameLocationSearchLimitException)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLog.Error("Resolve configured game root failed", exception);
                return null;
            }
        }

        private static string FindFromRunningProcesses(
            CancellationToken cancellationToken,
            IProgress<int> progress,
            SearchBudget budget)
        {
            foreach (var processName in CleanupProfile.NormalizedProcessNames)
            {
                budget.Check(cancellationToken);
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        budget.Check(cancellationToken);
                        var executable = process.MainModule == null ? null : process.MainModule.FileName;
                        var root = ResolveGameRootCore(executable, cancellationToken, progress, budget);
                        if (!string.IsNullOrEmpty(root)) return root;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (GameLocationSearchLimitException)
                    {
                        throw;
                    }
                    catch
                    {
                        // A process can exit or deny access while it is inspected.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateRegistryCandidates()
        {
            foreach (var value in ReadUninstallLocations(RegistryHive.CurrentUser, RegistryView.Default)) yield return value;
            foreach (var value in ReadUninstallLocations(RegistryHive.LocalMachine, RegistryView.Registry64)) yield return value;
            foreach (var value in ReadUninstallLocations(RegistryHive.LocalMachine, RegistryView.Registry32)) yield return value;
        }

        private static IEnumerable<string> ReadUninstallLocations(RegistryHive hive, RegistryView view)
        {
            RegistryKey baseKey = null;
            RegistryKey uninstall = null;
            try
            {
                baseKey = RegistryKey.OpenBaseKey(hive, view);
                uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) yield break;

                foreach (var subName in uninstall.GetSubKeyNames())
                {
                    using (var sub = uninstall.OpenSubKey(subName))
                    {
                        if (sub == null) continue;
                        var displayName = Convert.ToString(sub.GetValue("DisplayName"));
                        if (string.IsNullOrWhiteSpace(displayName) ||
                            displayName.IndexOf(CleanupProfile.RegistryDisplayNameKeyword, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        var installLocation = Convert.ToString(sub.GetValue("InstallLocation"));
                        if (!string.IsNullOrWhiteSpace(installLocation)) yield return installLocation;

                        var displayIcon = Convert.ToString(sub.GetValue("DisplayIcon"));
                        if (!string.IsNullOrWhiteSpace(displayIcon)) yield return displayIcon;

                        var uninstallString = Convert.ToString(sub.GetValue("UninstallString"));
                        if (!string.IsNullOrWhiteSpace(uninstallString)) yield return uninstallString;
                    }
                }
            }
            finally
            {
                if (uninstall != null) uninstall.Dispose();
                if (baseKey != null) baseKey.Dispose();
            }
        }

        private static string ResolveDirectMarker(string directory)
        {
            var full = NormalizeDirectory(directory);
            if (string.IsNullOrEmpty(full) || IsProtectedRoot(full)) return null;

            if (string.Equals(Path.GetFileName(full), CleanupProfile.GameRootMarkerFolderName, StringComparison.OrdinalIgnoreCase))
            {
                var parent = Directory.GetParent(full);
                return parent != null && !IsProtectedRoot(parent.FullName) ? parent.FullName : null;
            }

            var marker = Path.Combine(full, CleanupProfile.GameRootMarkerFolderName);
            return Directory.Exists(marker) ? full : null;
        }

        private static string SearchBelow(
            string startPath,
            int maxDepth,
            CancellationToken cancellationToken,
            IProgress<int> progress,
            SearchBudget budget)
        {
            var start = NormalizeDirectory(startPath);
            if (string.IsNullOrEmpty(start)) return null;

            var queue = new Queue<Tuple<string, int>>();
            budget.VisitDirectory(cancellationToken, progress);
            queue.Enqueue(Tuple.Create(start, 0));

            while (queue.Count > 0)
            {
                budget.Check(cancellationToken);
                var current = queue.Dequeue();
                if (string.IsNullOrEmpty(current.Item1)) continue;

                var direct = ResolveDirectMarker(current.Item1);
                if (!string.IsNullOrEmpty(direct)) return direct;
                if (current.Item2 >= maxDepth) continue;

                IEnumerable<string> children;
                try { children = Directory.EnumerateDirectories(current.Item1); }
                catch { continue; }

                try
                {
                    foreach (var child in children)
                    {
                        budget.Check(cancellationToken);
                        try
                        {
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                            if (IsProtectedRoot(child)) continue;
                            budget.VisitDirectory(cancellationToken, progress);
                            queue.Enqueue(Tuple.Create(child, current.Item2 + 1));
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (GameLocationSearchLimitException)
                        {
                            throw;
                        }
                        catch
                        {
                            // Ignore inaccessible directories while looking for the configured marker.
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (GameLocationSearchLimitException)
                {
                    throw;
                }
                catch
                {
                    // Directory enumeration can fail part-way through when a folder disappears or access changes.
                }
            }

            return null;
        }

        private static string NormalizeDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            var full = Path.GetFullPath(path.Trim().Trim('"'));
            var root = Path.GetPathRoot(full);
            var trimmedFull = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var trimmedRoot = string.IsNullOrEmpty(root)
                ? null
                : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Keep the trailing separator on a drive/UNC root. On Windows, "C:" means the current
            // directory on drive C, while "C:\\" is the actual root directory.
            if (!string.IsNullOrEmpty(root) &&
                string.Equals(trimmedFull, trimmedRoot, StringComparison.OrdinalIgnoreCase))
                return root;

            return trimmedFull;
        }

        private static bool IsProtectedRoot(string path)
        {
            var full = NormalizeDirectory(path);
            if (string.IsNullOrEmpty(full)) return true;

            var root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root) &&
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

            return protectedPaths.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeDirectory)
                .Any(value => string.Equals(value, full, StringComparison.OrdinalIgnoreCase));
        }

        private sealed class SearchBudget
        {
            private readonly int _maxVisitedDirectories;
            private readonly TimeSpan _timeBudget;
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private int _visitedDirectories;

            public SearchBudget(int maxVisitedDirectories, TimeSpan timeBudget)
            {
                if (maxVisitedDirectories < 1) throw new ArgumentOutOfRangeException(nameof(maxVisitedDirectories));
                if (timeBudget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeBudget));
                _maxVisitedDirectories = maxVisitedDirectories;
                _timeBudget = timeBudget;
            }

            public void VisitDirectory(CancellationToken cancellationToken, IProgress<int> progress)
            {
                Check(cancellationToken);
                if (_visitedDirectories >= _maxVisitedDirectories)
                    throw new GameLocationSearchLimitException(_visitedDirectories, _stopwatch.Elapsed);

                _visitedDirectories++;
                if (progress != null && (_visitedDirectories == 1 || _visitedDirectories % 25 == 0))
                    progress.Report(_visitedDirectories);
            }

            public void Check(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_stopwatch.Elapsed > _timeBudget)
                    throw new GameLocationSearchLimitException(_visitedDirectories, _stopwatch.Elapsed);
            }
        }
    }
}
