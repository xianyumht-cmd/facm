using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FACM.Services
{
    internal static class GameLocator
    {
        private static readonly string[] ProcessNames =
        {
            "LeagueClient",
            "LeagueClientUx",
            "League of Legends"
        };

        public static string FindGameRoot()
        {
            var running = FindFromRunningProcesses();
            if (!string.IsNullOrEmpty(running)) return running;

            foreach (var candidate in EnumerateRegistryCandidates())
            {
                var root = NormalizeCandidate(candidate);
                if (!string.IsNullOrEmpty(root)) return root;
            }
            return null;
        }

        public static bool IsValidGameRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                var full = Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Directory.Exists(full) &&
                       Directory.Exists(Path.Combine(full, "Game")) &&
                       (Directory.Exists(Path.Combine(full, "LeagueClient")) || Directory.Exists(Path.Combine(full, "Launcher")));
            }
            catch
            {
                return false;
            }
        }

        private static string FindFromRunningProcesses()
        {
            foreach (var name in ProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        var executable = process.MainModule == null ? null : process.MainModule.FileName;
                        var root = NormalizeCandidate(executable);
                        if (!string.IsNullOrEmpty(root)) return root;
                    }
                    catch
                    {
                        // Access can be denied for a process that is shutting down or running elevated.
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

            foreach (var value in ReadTencentTree(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Tencent\WeGame", 0)) yield return value;
            foreach (var value in ReadTencentTree(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Tencent\WeGame", 0)) yield return value;
            foreach (var value in ReadTencentTree(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Tencent\WeGame", 0)) yield return value;
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
                        if (string.IsNullOrEmpty(displayName) ||
                            (displayName.IndexOf("League of Legends", StringComparison.OrdinalIgnoreCase) < 0 &&
                             displayName.IndexOf("英雄联盟", StringComparison.OrdinalIgnoreCase) < 0)) continue;

                        var installLocation = Convert.ToString(sub.GetValue("InstallLocation"));
                        if (!string.IsNullOrWhiteSpace(installLocation)) yield return installLocation;
                        var displayIcon = Convert.ToString(sub.GetValue("DisplayIcon"));
                        if (!string.IsNullOrWhiteSpace(displayIcon)) yield return displayIcon;
                    }
                }
            }
            finally
            {
                if (uninstall != null) uninstall.Dispose();
                if (baseKey != null) baseKey.Dispose();
            }
        }

        private static IEnumerable<string> ReadTencentTree(RegistryHive hive, RegistryView view, string path, int depth)
        {
            if (depth > 4) yield break;
            RegistryKey baseKey = null;
            RegistryKey key = null;
            try
            {
                baseKey = RegistryKey.OpenBaseKey(hive, view);
                key = baseKey.OpenSubKey(path);
                if (key == null) yield break;

                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName) as string;
                    if (!string.IsNullOrWhiteSpace(value) &&
                        (value.IndexOf("WeGameApps", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         value.IndexOf("League", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         value.IndexOf("英雄联盟", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        yield return value;
                    }
                }

                foreach (var subName in key.GetSubKeyNames())
                {
                    foreach (var value in ReadTencentTree(hive, view, path + "\\" + subName, depth + 1)) yield return value;
                }
            }
            finally
            {
                if (key != null) key.Dispose();
                if (baseKey != null) baseKey.Dispose();
            }
        }

        private static string NormalizeCandidate(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            try
            {
                var candidate = Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'));
                var comma = candidate.IndexOf(',');
                if (comma > 0) candidate = candidate.Substring(0, comma).Trim().Trim('"');
                if (File.Exists(candidate)) candidate = Path.GetDirectoryName(candidate);
                if (string.IsNullOrWhiteSpace(candidate)) return null;

                var directory = new DirectoryInfo(Path.GetFullPath(candidate));
                for (var i = 0; i < 7 && directory != null; i++, directory = directory.Parent)
                {
                    if (IsValidGameRoot(directory.FullName)) return directory.FullName;
                }
            }
            catch
            {
                return null;
            }
            return null;
        }
    }
}
