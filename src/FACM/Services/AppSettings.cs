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
        public string PetStyleId { get; set; } = PetCatalog.DefaultPetId;

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
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0) continue;
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
                    else if (key.Equals("PetStyleId", StringComparison.OrdinalIgnoreCase)) result.PetStyleId = PetCatalog.Get(value).Id;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to load settings", exception);
            }

            result.ThemeId = ThemeCatalog.Get(result.ThemeId).Id;
            result.PetStyleId = PetCatalog.Get(result.PetStyleId).Id;
            return result;
        }

        public void Save()
        {
            try
            {
                RuntimePaths.Initialize();
                var lines = new[]
                {
                    "BallX=" + BallX.ToString(CultureInfo.InvariantCulture),
                    "BallY=" + BallY.ToString(CultureInfo.InvariantCulture),
                    "GamePath=" + Sanitize(GamePath),
                    "AutoUpdateEnabled=" + AutoUpdateEnabled,
                    "LastAnnouncementId=" + Sanitize(LastAnnouncementId),
                    "ThemeId=" + ThemeCatalog.Get(ThemeId).Id,
                    "PetStyleId=" + PetCatalog.Get(PetStyleId).Id
                };
                File.WriteAllLines(RuntimePaths.SettingsPath, lines);
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to save settings", exception);
            }
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
