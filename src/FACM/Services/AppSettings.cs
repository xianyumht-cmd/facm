using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FACM.Pets;
using FACM.Theming;

namespace FACM.Services
{
    internal sealed class AppSettings
    {
        private static readonly string LegacySettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FACM",
            "settings.ini");

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
                result = ParseLines(File.ReadAllLines(RuntimePaths.SettingsPath));
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
                RuntimePaths.Initialize();
                File.WriteAllLines(RuntimePaths.SettingsPath, BuildLines());
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
