using System;
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
        public string LeagueExitGameHotkey { get; set; } = string.Empty;
        public string LeagueCloseLobbyHotkey { get; set; } = string.Empty;
        public string LeagueCredentialHotkey { get; set; } = string.Empty;

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

                foreach (var line in File.ReadAllLines(RuntimePaths.SettingsPath))
                    ApplyLine(result, line);
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to load settings", exception);
            }

            result.ThemeId = ThemeCatalog.Get(result.ThemeId).Id;
            result.PetStyleId = AnimalPetCatalog.Get(result.PetStyleId).Id;
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

        internal static void ApplyLineForSmokeTest(AppSettings settings, string line)
        {
            ApplyLine(settings, line);
        }

        internal string[] BuildLinesForSmokeTest()
        {
            return BuildLines();
        }

        private static void ApplyLine(AppSettings result, string line)
        {
            if (result == null || line == null) return;
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
            else if (key.Equals("LeagueExitGameHotkey", StringComparison.OrdinalIgnoreCase)) result.LeagueExitGameHotkey = Sanitize(value);
            else if (key.Equals("LeagueCloseLobbyHotkey", StringComparison.OrdinalIgnoreCase)) result.LeagueCloseLobbyHotkey = Sanitize(value);
            else if (key.Equals("LeagueCredentialHotkey", StringComparison.OrdinalIgnoreCase)) result.LeagueCredentialHotkey = Sanitize(value);
        }

        private string[] BuildLines()
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
                "LeagueExitGameHotkey=" + Sanitize(LeagueExitGameHotkey),
                "LeagueCloseLobbyHotkey=" + Sanitize(LeagueCloseLobbyHotkey),
                "LeagueCredentialHotkey=" + Sanitize(LeagueCredentialHotkey)
            };
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
