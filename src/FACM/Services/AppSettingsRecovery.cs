using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FACM.Services
{
    internal enum AppSettingsLoadOrigin
    {
        Primary,
        LastKnownGood,
        RecoveryDefaults
    }

    /// <summary>
    /// Lightweight Settings 2.0 recovery idea adapted to the legacy 3.5 INI contract.
    /// The primary INI remains authoritative. A validated normalized snapshot is kept as LKG and is
    /// only used when the primary file is clearly unreadable/corrupt; no new settings format is introduced.
    /// </summary>
    internal static class AppSettingsRecovery
    {
        private const long MaxSettingsBytes = 256 * 1024;
        private static readonly HashSet<string> KnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BallX",
            "BallY",
            "GamePath",
            "AutoUpdateEnabled",
            "LastAnnouncementId",
            "ThemeId",
            "PetStyleId",
            "AnimalPetEnabled",
            "LeagueAutoApplyRecommended",
            "LeagueExitGameHotkey",
            "LeagueCloseLobbyHotkey",
            "LeagueAutoHonorTeammateEnabled",
            "LeagueAutoReturnLobbyEnabled",
            "LeagueAutoMatchmakingEnabled",
            "LeagueAutoAcceptEnabled"
        };

        public static AppSettings Load(string primaryPath, string recoveryPath, out AppSettingsLoadOrigin origin)
        {
            AppSettings settings;
            if (TryRead(primaryPath, out settings))
            {
                origin = AppSettingsLoadOrigin.Primary;
                TrySave(recoveryPath, settings.BuildLines());
                return settings;
            }

            if (TryRead(recoveryPath, out settings))
            {
                origin = AppSettingsLoadOrigin.LastKnownGood;
                return settings;
            }

            settings = new AppSettings { AutoUpdateEnabled = false };
            origin = AppSettingsLoadOrigin.RecoveryDefaults;
            return settings;
        }

        public static void SaveLastKnownGood(string recoveryPath, AppSettings settings)
        {
            if (settings == null) return;
            TrySave(recoveryPath, settings.BuildLines());
        }

        private static bool TryRead(string path, out AppSettings settings)
        {
            settings = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
                var info = new FileInfo(path);
                if (info.Length < 1 || info.Length > MaxSettingsBytes) return false;

                var lines = File.ReadAllLines(path);
                if (lines.Length == 0 || !ContainsKnownSetting(lines)) return false;
                settings = AppSettings.ParseLines(lines);
                return settings != null;
            }
            catch
            {
                settings = null;
                return false;
            }
        }

        private static bool ContainsKnownSetting(IEnumerable<string> lines)
        {
            if (lines == null) return false;
            foreach (var source in lines)
            {
                var line = source ?? string.Empty;
                if (line.IndexOf('\0') >= 0 || line.Length > 8192) return false;
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                var key = line.Substring(0, separator).Trim();
                if (KnownKeys.Contains(key)) return true;
            }
            return false;
        }

        private static void TrySave(string path, IEnumerable<string> lines)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                var fullPath = Path.GetFullPath(path);
                var directory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrWhiteSpace(directory)) return;
                Directory.CreateDirectory(directory);

                var values = lines == null ? new string[0] : new List<string>(lines).ToArray();
                var text = string.Join(Environment.NewLine, values) + Environment.NewLine;
                if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxSettingsBytes) return;

                var temp = Path.Combine(directory, Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var stream = new FileStream(
                        temp,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        4096,
                        FileOptions.WriteThrough))
                    using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                    {
                        writer.Write(text);
                        writer.Flush();
                        stream.Flush(true);
                    }

                    if (File.Exists(fullPath))
                        File.Replace(temp, fullPath, null, true);
                    else
                        File.Move(temp, fullPath);
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch { }
                }
            }
            catch
            {
                // LKG is best-effort. A recovery-copy failure must never turn a valid primary save into a failure.
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "FACM-LKG-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var primary = Path.Combine(root, "settings.ini");
                var recovery = Path.Combine(root, "runtime", "settings.last-known-good.ini");
                File.WriteAllLines(primary, new[]
                {
                    "BallX=123",
                    "BallY=456",
                    "AutoUpdateEnabled=True",
                    "ThemeId=glass-blue"
                });

                AppSettingsLoadOrigin origin;
                var first = Load(primary, recovery, out origin);
                if (origin != AppSettingsLoadOrigin.Primary || first.BallX != 123 || first.BallY != 456)
                    throw new InvalidOperationException("Primary settings recovery smoke load failed.");
                if (!File.Exists(recovery))
                    throw new InvalidOperationException("Primary settings did not create an LKG snapshot.");

                File.WriteAllText(primary, "not-a-settings-document");
                var recovered = Load(primary, recovery, out origin);
                if (origin != AppSettingsLoadOrigin.LastKnownGood || recovered.BallX != 123 || recovered.BallY != 456)
                    throw new InvalidOperationException("Last-known-good settings recovery failed.");

                File.WriteAllText(recovery, string.Empty);
                var defaults = Load(primary, recovery, out origin);
                if (origin != AppSettingsLoadOrigin.RecoveryDefaults || defaults.AutoUpdateEnabled)
                    throw new InvalidOperationException("Settings recovery defaults are not fail-safe.");

                var oversized = new string('x', 260 * 1024);
                File.WriteAllText(primary, "GamePath=" + oversized);
                defaults = Load(primary, recovery, out origin);
                if (origin != AppSettingsLoadOrigin.RecoveryDefaults)
                    throw new InvalidOperationException("Oversized settings input was not rejected.");
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }
    }
}
