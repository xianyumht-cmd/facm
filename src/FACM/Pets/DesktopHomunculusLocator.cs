using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;
using FACM.Services;

namespace FACM.Pets
{
    internal static class DesktopHomunculusLocator
    {
        private sealed class InstallRecord
        {
            public string DisplayName { get; set; }
            public string InstallLocation { get; set; }
            public string DisplayIcon { get; set; }
            public string UninstallString { get; set; }
        }

        public static string Find()
        {
            if (!DesktopPetLaunchGate.ExplicitUseAllowed) return null;

            var candidates = new List<string>();
            Add(candidates, FindFromRunningProcesses());

            foreach (var record in ReadInstallRecords())
            {
                Add(candidates, NormalizeExecutableReference(record.DisplayIcon));
                Add(candidates, NormalizeExecutableReference(record.UninstallString));
                AddExecutablesFromDirectory(candidates, record.InstallLocation, true);
            }

            AddKnownLocations(candidates);
            foreach (var target in ReadStartMenuShortcutTargets()) Add(candidates, target);

            var best = candidates
                .Where(IsUsableExecutable)
                .OrderByDescending(Score)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(best))
            {
                AppLog.Info("Desktop Homunculus executable located: " + best);
                return best;
            }

            foreach (var root in GetLikelySearchRoots())
            {
                AddExecutablesFromDirectory(candidates, root, false);
            }

            best = candidates
                .Where(IsUsableExecutable)
                .OrderByDescending(Score)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(best)) AppLog.Info("Desktop Homunculus executable located by fallback scan: " + best);
            return best;
        }

        public static string WaitForInstalledExecutable(DateTime installStartedUtc, TimeSpan timeout, CancellationToken token)
        {
            var until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until)
            {
                token.ThrowIfCancellationRequested();
                var processPath = FindFromRunningProcesses(installStartedUtc);
                if (IsUsableExecutable(processPath)) return processPath;

                var path = Find();
                if (IsUsableExecutable(path)) return path;
                Thread.Sleep(700);
            }
            return null;
        }

        private static IEnumerable<InstallRecord> ReadInstallRecords()
        {
            var output = new List<InstallRecord>();
            ReadInstallRecords(output, RegistryHive.CurrentUser, RegistryView.Default);
            ReadInstallRecords(output, RegistryHive.LocalMachine, RegistryView.Registry64);
            ReadInstallRecords(output, RegistryHive.LocalMachine, RegistryView.Registry32);
            return output;
        }

        private static void ReadInstallRecords(ICollection<InstallRecord> output, RegistryHive hive, RegistryView view)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var root = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (root == null) return;
                    foreach (var subName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (var sub = root.OpenSubKey(subName))
                            {
                                if (sub == null) continue;
                                var displayName = Convert.ToString(sub.GetValue("DisplayName")) ?? string.Empty;
                                var installLocation = Convert.ToString(sub.GetValue("InstallLocation")) ?? string.Empty;
                                var displayIcon = Convert.ToString(sub.GetValue("DisplayIcon")) ?? string.Empty;
                                var uninstallString = Convert.ToString(sub.GetValue("UninstallString")) ?? string.Empty;
                                var searchable = displayName + " " + installLocation + " " + displayIcon + " " + uninstallString;
                                if (searchable.IndexOf("homunculus", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                output.Add(new InstallRecord
                                {
                                    DisplayName = displayName,
                                    InstallLocation = TrimPath(installLocation),
                                    DisplayIcon = displayIcon,
                                    UninstallString = uninstallString
                                });
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddKnownLocations(ICollection<string> candidates)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var names = new[] { "desktop-homunculus.exe", "desktop_homunculus.exe", "homunculus.exe", "desktop homunculus.exe" };
            var folders = new[]
            {
                Path.Combine(local, "Programs", "desktop-homunculus"),
                Path.Combine(local, "Programs", "Desktop Homunculus"),
                Path.Combine(local, "desktop-homunculus"),
                Path.Combine(programFiles, "desktop-homunculus"),
                Path.Combine(programFiles, "Desktop Homunculus"),
                Path.Combine(programFilesX86, "desktop-homunculus"),
                Path.Combine(programFilesX86, "Desktop Homunculus")
            };
            foreach (var folder in folders)
                foreach (var name in names)
                    Add(candidates, Path.Combine(folder, name));
        }

        private static IEnumerable<string> GetLikelySearchRoots()
        {
            var output = new List<string>();
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            AddLikelyChildDirectories(output, Path.Combine(local, "Programs"));
            AddLikelyChildDirectories(output, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddLikelyChildDirectories(output, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            foreach (var record in ReadInstallRecords())
            {
                if (!string.IsNullOrWhiteSpace(record.InstallLocation)) output.Add(record.InstallLocation);
            }
            return output.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void AddLikelyChildDirectories(ICollection<string> output, string root)
        {
            try
            {
                if (!Directory.Exists(root)) return;
                foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(directory) ?? string.Empty;
                    if (name.IndexOf("homunculus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("not-elm", StringComparison.OrdinalIgnoreCase) >= 0)
                        output.Add(directory);
                }
            }
            catch
            {
            }
        }

        private static void AddExecutablesFromDirectory(ICollection<string> candidates, string directory, bool trustDirectory)
        {
            try
            {
                directory = TrimPath(directory);
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
                foreach (var path in Directory.GetFiles(directory, "*.exe", SearchOption.AllDirectories))
                {
                    if (trustDirectory || Score(path) > 0) Add(candidates, path);
                }
            }
            catch
            {
            }
        }

        private static string FindFromRunningProcesses(DateTime? startedAfterUtc = null)
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (startedAfterUtc.HasValue && process.StartTime.ToUniversalTime() < startedAfterUtc.Value.AddSeconds(-2)) continue;
                    var processName = process.ProcessName ?? string.Empty;
                    string path = null;
                    try { path = process.MainModule == null ? null : process.MainModule.FileName; } catch { }
                    var searchable = processName + " " + path;
                    if (searchable.IndexOf("homunculus", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (IsUsableExecutable(path)) return path;
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
            return null;
        }

        private static IEnumerable<string> ReadStartMenuShortcutTargets()
        {
            var output = new List<string>();
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            };
            foreach (var root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var shortcut in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileNameWithoutExtension(shortcut) ?? string.Empty;
                        if (name.IndexOf("homunculus", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var target = ResolveShortcut(shortcut);
                        if (!string.IsNullOrWhiteSpace(target)) output.Add(target);
                    }
                }
                catch
                {
                }
            }
            return output;
        }

        private static string ResolveShortcut(string shortcutPath)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                shell = Activator.CreateInstance(type);
                shortcut = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                var target = shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
                return TrimPath(target);
            }
            catch
            {
                return null;
            }
            finally
            {
                ReleaseCom(shortcut);
                ReleaseCom(shell);
            }
        }

        private static void ReleaseCom(object value)
        {
            if (value == null) return;
            try
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(value))
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(value);
            }
            catch
            {
            }
        }

        private static string NormalizeExecutableReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = Environment.ExpandEnvironmentVariables(value.Trim());
            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = text.IndexOf('"', 1);
                if (end > 1) text = text.Substring(1, end - 1);
            }
            else
            {
                var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exe >= 0) text = text.Substring(0, exe + 4);
            }
            var comma = text.LastIndexOf(',');
            if (comma > 0 && comma < text.Length - 1)
            {
                int index;
                if (int.TryParse(text.Substring(comma + 1), out index)) text = text.Substring(0, comma);
            }
            return TrimPath(text);
        }

        private static bool IsUsableExecutable(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                if (!File.Exists(path)) return false;
                var name = Path.GetFileName(path) ?? string.Empty;
                if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("unins", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("update.exe", StringComparison.OrdinalIgnoreCase)) return false;
                return Score(path) > 0;
            }
            catch
            {
                return false;
            }
        }

        private static int Score(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return -1000;
            var name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            var full = path.ToLowerInvariant();
            var score = 0;
            if (name.IndexOf("desktop-homunculus", StringComparison.OrdinalIgnoreCase) >= 0) score += 180;
            if (name.IndexOf("desktop_homunculus", StringComparison.OrdinalIgnoreCase) >= 0) score += 180;
            if (name.IndexOf("homunculus", StringComparison.OrdinalIgnoreCase) >= 0) score += 130;
            if (name.IndexOf("desktop", StringComparison.OrdinalIgnoreCase) >= 0) score += 30;
            if (full.IndexOf("homunculus", StringComparison.OrdinalIgnoreCase) >= 0) score += 45;
            if (full.IndexOf("not-elm", StringComparison.OrdinalIgnoreCase) >= 0) score += 20;
            if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0) score -= 300;
            return score;
        }

        private static void Add(ICollection<string> list, string path)
        {
            path = TrimPath(path);
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!list.Contains(path, StringComparer.OrdinalIgnoreCase)) list.Add(path);
        }

        private static string TrimPath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
        }
    }
}
