using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FACM.Pets;
using FACM.Theming;

namespace FACM.Services
{
    internal sealed class AppSettings
    {
        private const int MoveFileReplaceExisting = 0x1;
        private const int MoveFileWriteThrough = 0x8;
        private static readonly object SettingsWriteSync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly string LegacySettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FACM",
            "settings.ini");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        public int BallX { get; set; } = int.MinValue;
        public int BallY { get; set; } = int.MinValue;
        public string GamePath { get; set; } = string.Empty;
        public bool AutoUpdateEnabled { get; set; } = true;
        public string LastAnnouncementId { get; set; } = string.Empty;
        public string ThemeId { get; set; } = ThemeCatalog.DefaultThemeId;
        public string PetStyleId { get; set; } = AnimalPetCatalog.DefaultPetId;
        public bool AnimalPetEnabled { get; set; } = false;
        public bool LeagueAutoApplyRecommended { get; set; } = false;
        public string LeagueExitGameHotkey { get; set; } = string.Empty;
        public string LeagueCloseLobbyHotkey { get; set; } = string.Empty;
        public bool LeagueAutoHonorTeammateEnabled { get; set; } = false;
        public bool LeagueAutoReturnLobbyEnabled { get; set; } = false;
        public bool LeagueAutoMatchmakingEnabled { get; set; } = false;
        public bool LeagueAutoAcceptEnabled { get; set; } = false;

        public static AppSettings Load()
        {
            var result = new AppSettings();
            try
            {
                MigrateLegacySettings();
                if (!File.Exists(RuntimePaths.SettingsPath))
                {
                    result.Save();
                    return result;
                }

                AppSettingsLoadOrigin origin;
                result = AppSettingsRecovery.Load(
                    RuntimePaths.SettingsPath,
                    RuntimePaths.SettingsRecoveryPath,
                    out origin);
                if (origin == AppSettingsLoadOrigin.LastKnownGood)
                    AppLog.Warning("Settings primary file was invalid; loaded last-known-good snapshot.");
                else if (origin == AppSettingsLoadOrigin.RecoveryDefaults)
                    AppLog.Warning("Settings primary and recovery files were invalid; loaded fail-safe defaults with auto-update disabled.");
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to load settings", exception);
            }
            Normalize(result);
            return result;
        }

        public void Save()
        {
            try
            {
                lock (SettingsWriteSync)
                {
                    RuntimePaths.Initialize();
                    WriteLinesAtomically(RuntimePaths.SettingsPath, BuildLines());
                    AppSettingsRecovery.SaveLastKnownGood(RuntimePaths.SettingsRecoveryPath, this);
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to save settings", exception);
            }
        }

        internal static AppSettings ParseLines(IEnumerable<string> lines)
        {
            var result = new AppSettings();
            if (lines != null)
            {
                foreach (var line in lines) ApplyLine(result, line);
            }
            Normalize(result);
            return result;
        }

        internal string[] BuildLines()
        {
            return new[]
            {
                "BallX=" + BallX.ToString(CultureInfo.InvariantCulture),
                "BallY=" + BallY.ToString(CultureInfo.InvariantCulture),
                "GamePath=" + Sanitize(GamePath),
                "AutoUpdateEnabled=" + AutoUpdateEnabled,
                "LastAnnouncementId=" + Sanitize(LastAnnouncementId),
                "ThemeId=" + ThemeCatalog.Get(ThemeId).Id,
                "PetStyleId=" + AnimalPetCatalog.Get(PetStyleId).Id,
                "AnimalPetEnabled=" + AnimalPetEnabled,
                "LeagueAutoApplyRecommended=" + LeagueAutoApplyRecommended,
                "LeagueExitGameHotkey=" + Sanitize(LeagueExitGameHotkey),
                "LeagueCloseLobbyHotkey=" + Sanitize(LeagueCloseLobbyHotkey),
                "LeagueAutoHonorTeammateEnabled=" + LeagueAutoHonorTeammateEnabled,
                "LeagueAutoReturnLobbyEnabled=" + LeagueAutoReturnLobbyEnabled,
                "LeagueAutoMatchmakingEnabled=" + LeagueAutoMatchmakingEnabled,
                "LeagueAutoAcceptEnabled=" + LeagueAutoAcceptEnabled
            };
        }

        internal static void ApplyLineForSmokeTest(AppSettings result, string line)
        {
            ApplyLine(result, line);
            Normalize(result);
        }

        internal IEnumerable<string> BuildLinesForSmokeTest()
        {
            return BuildLines();
        }

        internal static void ValidateAtomicSaveForSmokeTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "FACM-AppSettings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, "settings.ini");
                var first = new[] { "BallX=10", "BallY=20", "ThemeId=midnight" };
                var second = new[] { "BallX=30", "BallY=40", "ThemeId=glass-blue" };

                WriteLinesAtomically(path, first);
                RequireLines(path, first, "initial atomic settings write failed");

                WriteLinesAtomically(path, second);
                RequireLines(path, second, "replacement atomic settings write failed");

                if (Directory.GetFiles(root, "*.tmp").Length != 0)
                    throw new InvalidOperationException("Atomic settings write left a temporary file behind.");
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        private static void WriteLinesAtomically(string path, IEnumerable<string> lines)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Settings path is required.", nameof(path));

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidDataException("Settings directory is unavailable.");
            Directory.CreateDirectory(directory);

            var temporary = Path.Combine(
                directory,
                Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                var text = string.Join(Environment.NewLine, lines ?? new string[0]) + Environment.NewLine;
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, Utf8NoBom))
                {
                    writer.Write(text);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(fullPath))
                {
                    try
                    {
                        File.Replace(temporary, fullPath, null, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        AtomicMoveReplace(temporary, fullPath, true);
                    }
                    catch (IOException)
                    {
                        AtomicMoveReplace(temporary, fullPath, true);
                    }
                }
                else
                {
                    AtomicMoveReplace(temporary, fullPath, false);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch
                {
                    // Best-effort cleanup. Never mask the primary settings write failure.
                }
            }
        }

        private static void AtomicMoveReplace(string source, string destination, bool replaceExisting)
        {
            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(source));
            var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (!string.Equals(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Atomic settings replacement must stay in the same directory.");

            var flags = MoveFileWriteThrough | (replaceExisting ? MoveFileReplaceExisting : 0);
            if (!MoveFileEx(source, destination, flags))
                throw new IOException("Windows atomic settings replacement failed.", Marshal.GetHRForLastWin32Error());
        }

        private static void RequireLines(string path, string[] expected, string message)
        {
            var actual = File.ReadAllLines(path);
            if (actual.Length != expected.Length) throw new InvalidOperationException(message);
            for (var index = 0; index < expected.Length; index++)
            {
                if (!string.Equals(actual[index], expected[index], StringComparison.Ordinal))
                    throw new InvalidOperationException(message);
            }
        }

        private static void ApplyLine(AppSettings result, string line)
        {
            if (result == null || string.IsNullOrWhiteSpace(line)) return;
            var separator = line.IndexOf('=');
            if (separator <= 0) return;
            var key = line.Substring(0, separator).Trim();
            var value = line.Substring(separator + 1).Trim();
            int number;
            bool flag;
            if (key.Equals("BallX", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) result.BallX = number;
            else if (key.Equals("BallY", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) result.BallY = number;
            else if (key.Equals("GamePath", StringComparison.OrdinalIgnoreCase)) result.GamePath = value;
            else if (key.Equals("AutoUpdateEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.AutoUpdateEnabled = flag;
            else if (key.Equals("LastAnnouncementId", StringComparison.OrdinalIgnoreCase)) result.LastAnnouncementId = value;
            else if (key.Equals("ThemeId", StringComparison.OrdinalIgnoreCase)) result.ThemeId = ThemeCatalog.Get(value).Id;
            else if (key.Equals("PetStyleId", StringComparison.OrdinalIgnoreCase)) result.PetStyleId = AnimalPetCatalog.Get(value).Id;
            else if (key.Equals("AnimalPetEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.AnimalPetEnabled = flag;
            else if (key.Equals("LeagueAutoApplyRecommended", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoApplyRecommended = flag;
            else if (key.Equals("LeagueExitGameHotkey", StringComparison.OrdinalIgnoreCase)) result.LeagueExitGameHotkey = value;
            else if (key.Equals("LeagueCloseLobbyHotkey", StringComparison.OrdinalIgnoreCase)) result.LeagueCloseLobbyHotkey = value;
            else if (key.Equals("LeagueAutoHonorTeammateEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoHonorTeammateEnabled = flag;
            else if (key.Equals("LeagueAutoReturnLobbyEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoReturnLobbyEnabled = flag;
            else if (key.Equals("LeagueAutoMatchmakingEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoMatchmakingEnabled = flag;
            else if (key.Equals("LeagueAutoAcceptEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoAcceptEnabled = flag;
        }

        private static void Normalize(AppSettings result)
        {
            if (result == null) return;
            result.ThemeId = ThemeCatalog.Get(result.ThemeId).Id;
            result.PetStyleId = AnimalPetCatalog.Get(result.PetStyleId).Id;
            result.LeagueExitGameHotkey = Sanitize(result.LeagueExitGameHotkey).Trim();
            result.LeagueCloseLobbyHotkey = Sanitize(result.LeagueCloseLobbyHotkey).Trim();
        }

        private static void MigrateLegacySettings()
        {
            if (File.Exists(RuntimePaths.SettingsPath) || !File.Exists(LegacySettingsPath)) return;
            File.Copy(LegacySettingsPath, RuntimePaths.SettingsPath, false);
        }

        private static string Sanitize(string value)
        {
            return (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        }
    }
}
