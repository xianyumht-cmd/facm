using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FACM.Configuration;
using Microsoft.Win32;

namespace FACM.Services
{
    internal static class GameLocator
    {
        public static string FindGameRoot()
        {
            CleanupProfile.Validate();

            var running = FindFromRunningProcesses();
            if (!string.IsNullOrEmpty(running)) return running;

            foreach (var candidate in EnumerateRegistryCandidates())
            {
                var root = ResolveGameRoot(candidate);
                if (!string.IsNullOrEmpty(root)) return root;
            }

            return null;
        }

        public static string ResolveGameRoot(string selectedOrCandidatePath)
        {
            if (string.IsNullOrWhiteSpace(selectedOrCandidatePath)) return null;
            CleanupProfile.Validate();

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
                    var direct = ResolveDirectMarker(directory.FullName);
                    if (!string.IsNullOrEmpty(direct)) return direct;
                }

                return SearchBelow(Path.GetFullPath(candidate), CleanupProfile.MaxMarkerSearchDepth);
            }
            catch (Exception exception)
            {
                AppLog.Error("Resolve configured game root failed", exception);
                return null;
            }
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

        private static string FindFromRunningProcesses()
        {
            foreach (var processName in CleanupProfile.NormalizedProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        var executable = process.MainModule == null ? null : process.MainModule.FileName;
                        var root = ResolveGameRoot(executable);
                        if (!string.IsNullOrEmpty(root)) return root;
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

        private static string SearchBelow(string startPath, int maxDepth)
        {
            var queue = new Queue<Tuple<string, int>>();
            queue.Enqueue(Tuple.Create(NormalizeDirectory(startPath), 0));

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (string.IsNullOrEmpty(current.Item1)) continue;

                var direct = ResolveDirectMarker(current.Item1);
                if (!string.IsNullOrEmpty(direct)) return direct;
                if (current.Item2 >= maxDepth) continue;

                IEnumerable<string> children;
                try { children = Directory.EnumerateDirectories(current.Item1).ToArray(); }
                catch { continue; }

                foreach (var child in children)
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                        queue.Enqueue(Tuple.Create(child, current.Item2 + 1));
                    }
                    catch
                    {
                        // Ignore inaccessible directories while looking for the configured marker.
                    }
                }
            }

            return null;
        }

        private static string NormalizeDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsProtectedRoot(string path)
        {
            var full = NormalizeDirectory(path);
            if (string.IsNullOrEmpty(full)) return true;

            var root = Path.GetPathRoot(full);
            if (string.Equals(full, root == null ? null : root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return true;

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
    }
}
