using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FACM.League;
using Microsoft.Win32;

namespace FACM.Services
{
    internal static class EscGameDirectoryLocator
    {
        private static readonly string[] ProcessNames =
        {
            "League of Legends", "LeagueClient", "LeagueClientUx", "LeagueClientUxRender"
        };
        private static readonly string[] ConfigNames =
        {
            "PersistedSettings.json", "game.cfg", "input.ini"
        };

        internal static string Find(string configuredPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var processName in ProcessNames)
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(processName); }
                catch { continue; }

                foreach (var process in processes)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string imagePath = null;
                        try { imagePath = process.MainModule == null ? null : process.MainModule.FileName; }
                        catch { }
                        if (string.IsNullOrWhiteSpace(imagePath))
                            WmiProcessImagePathReader.TryRead(process.Id, out imagePath);
                        var config = ResolveCandidate(imagePath);
                        if (config != null) return config;
                    }
                    finally { process.Dispose(); }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var saved = ResolveCandidate(configuredPath);
            if (saved != null) return saved;

            foreach (var hint in EnumerateUninstallLocations())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var config = ResolveCandidate(hint);
                if (config != null) return config;
            }
            return null;
        }

        internal static string ResolveCandidate(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var candidate = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                var comma = candidate.LastIndexOf(',');
                if (comma > 0 && int.TryParse(candidate.Substring(comma + 1), out _))
                    candidate = candidate.Substring(0, comma).Trim('"');
                if (File.Exists(candidate)) candidate = Path.GetDirectoryName(candidate);
                if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
                    return null;

                var parent = new DirectoryInfo(Path.GetFullPath(candidate));
                for (var depth = 0; parent != null && depth < 6; depth++, parent = parent.Parent)
                {
                    if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                    var config = FindValidatedConfig(parent.FullName);
                    if (config != null) return config;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            return null;
        }

        private static string FindValidatedConfig(string installPath)
        {
            foreach (var directory in new[]
            {
                string.Equals(Path.GetFileName(installPath), "Config", StringComparison.OrdinalIgnoreCase)
                    ? installPath : Path.Combine(installPath, "Config"),
                Path.Combine(installPath, "Game", "Config")
            })
            {
                if (!Directory.Exists(directory)) continue;
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;

                var hasEscFiles = ConfigNames.Any(name => File.Exists(Path.Combine(directory, name)));
                var hasGameClient = File.Exists(Path.Combine(installPath, "LeagueClient.exe")) ||
                    File.Exists(Path.Combine(installPath, "Game", "League of Legends.exe"));
                if (hasEscFiles || hasGameClient)
                    return Path.GetFullPath(directory);
            }
            return null;
        }

        private static IEnumerable<string> EnumerateUninstallLocations()
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    string[] names;
                    try
                    {
                        using (var key = RegistryKey.OpenBaseKey(hive, view)
                            .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (key == null) continue;
                            names = key.GetSubKeyNames();
                        }
                    }
                    catch { continue; }

                    foreach (var name in names)
                    {
                        RegistryKey item = null;
                        try
                        {
                            using (var root = RegistryKey.OpenBaseKey(hive, view))
                            using (var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                            {
                                item = uninstall == null ? null : uninstall.OpenSubKey(name);
                                if (item == null) continue;
                                var title = Convert.ToString(item.GetValue("DisplayName"));
                                if (title.IndexOf("League of Legends", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    title.IndexOf("英雄联盟", StringComparison.OrdinalIgnoreCase) < 0)
                                    continue;
                                foreach (var field in new[] { "InstallLocation", "DisplayIcon" })
                                {
                                    var value = Convert.ToString(item.GetValue(field));
                                    if (!string.IsNullOrWhiteSpace(value)) yield return value;
                                }
                            }
                        }
                        finally { if (item != null) item.Dispose(); }
                    }
                }
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "GGman-EscLocate-" + Guid.NewGuid().ToString("N"));
            try
            {
                var install = Path.Combine(root, "League of Legends");
                var game = Path.Combine(install, "Game");
                var config = Path.Combine(install, "Config");
                Directory.CreateDirectory(game);
                Directory.CreateDirectory(config);
                File.WriteAllText(Path.Combine(config, "input.ini"), "[Game]");
                var exe = Path.Combine(game, "League of Legends.exe");
                File.WriteAllText(exe, string.Empty);
                var found = ResolveCandidate(exe);
                if (!string.Equals(found, config, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(ResolveCandidate(install), config, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("ESC auto-locator failed process and installation paths.");
                var unrelated = Path.Combine(root, "Unrelated");
                Directory.CreateDirectory(Path.Combine(unrelated, "Config"));
                if (ResolveCandidate(unrelated) != null)
                    throw new InvalidOperationException("ESC auto-locator accepted an unrelated Config directory.");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }
    }
}
