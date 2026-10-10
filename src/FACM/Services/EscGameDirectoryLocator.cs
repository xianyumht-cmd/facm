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
            var gameConfig = Path.Combine(installPath, "Game", "Config");
            if (HasEscSettings(gameConfig)) return Path.GetFullPath(gameConfig);

            var config = string.Equals(Path.GetFileName(installPath), "Config",
                StringComparison.OrdinalIgnoreCase)
                ? installPath : Path.Combine(installPath, "Config");
            var parent = Directory.GetParent(config);
            if (parent != null &&
                string.Equals(parent.Name, "LeagueClient", StringComparison.OrdinalIgnoreCase))
                return null;

            return HasEscSettings(config) ? Path.GetFullPath(config) : null;
        }

        private static bool HasEscSettings(string directory)
        {
            if (!Directory.Exists(directory) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                return false;
            return ConfigNames.Any(name => File.Exists(Path.Combine(directory, name)));
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
                                if (string.IsNullOrWhiteSpace(title) ||
                                    (title.IndexOf("League of Legends", StringComparison.OrdinalIgnoreCase) < 0 &&
                                    title.IndexOf("英雄联盟", StringComparison.OrdinalIgnoreCase) < 0))
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
                // WeGame installs keep client YAML preferences separate from actual ESC settings.
                var wegame = Path.Combine(root, "WeGameApps", "英雄联盟");
                var clientConfig = Path.Combine(wegame, "LeagueClient", "Config");
                var gameConfig = Path.Combine(wegame, "Game", "Config");
                Directory.CreateDirectory(clientConfig);
                Directory.CreateDirectory(gameConfig);
                File.WriteAllText(Path.Combine(clientConfig, "LCUAccountPreferences.yaml"), "settings: true");
                File.WriteAllText(Path.Combine(clientConfig, "LeagueClientSettings.yaml"), "settings: true");
                File.WriteAllText(Path.Combine(wegame, "LeagueClient", "LeagueClient.exe"), string.Empty);
                File.WriteAllText(Path.Combine(wegame, "Game", "League of Legends.exe"), string.Empty);
                File.WriteAllText(Path.Combine(gameConfig, "game.cfg"), "[General]");
                File.WriteAllText(Path.Combine(gameConfig, "input.ini"), "[Game]");
                File.WriteAllText(Path.Combine(gameConfig, "PersistedSettings.json"), "{}");
                foreach (var hint in new[]
                {
                    wegame,
                    Path.Combine(wegame, "LeagueClient", "LeagueClient.exe"),
                    clientConfig,
                    Path.Combine(wegame, "Game", "League of Legends.exe"),
                    gameConfig
                })
                {
                    var actual = ResolveCandidate(hint);
                    if (!string.Equals(actual, gameConfig, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("WeGame ESC locator selected the client preferences folder.");
                }

                var captured = EscSettingsBackup.Capture(wegame);
                if (captured.Files.Count != 3 ||
                    captured.Files.Any(item => !ConfigNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("WeGame ESC capture missed the actual game configuration.");

                var capturedFromStaleClientPath = EscSettingsBackup.Capture(clientConfig);
                if (capturedFromStaleClientPath.Files.Count != 3)
                    throw new InvalidOperationException("Stale launcher Config hint did not recover the game settings.");

                var clientOnly = Path.Combine(root, "ClientOnly");
                Directory.CreateDirectory(Path.Combine(clientOnly, "LeagueClient", "Config"));
                File.WriteAllText(Path.Combine(clientOnly, "LeagueClient", "LeagueClient.exe"), string.Empty);
                File.WriteAllText(Path.Combine(clientOnly, "LeagueClient", "Config", "PerksPreferences.yaml"), "perks: true");
                if (ResolveCandidate(Path.Combine(clientOnly, "LeagueClient", "LeagueClient.exe")) != null)
                    throw new InvalidOperationException("LeagueClient YAML alone is not an ESC backup.");

                var unrelated = Path.Combine(root, "Unrelated");
                Directory.CreateDirectory(Path.Combine(unrelated, "Config"));
                if (ResolveCandidate(unrelated) != null)
                    throw new InvalidOperationException("ESC auto-locator accepted an unrelated Config directory.");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }
    }
}
