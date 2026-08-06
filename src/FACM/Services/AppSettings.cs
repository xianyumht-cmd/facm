using System;
using System.Globalization;
using System.IO;

namespace FACM.Services
{
    internal sealed class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FACM",
            "settings.ini");

        public int BallX { get; set; } = int.MinValue;
        public int BallY { get; set; } = int.MinValue;
        public string GamePath { get; set; } = string.Empty;
        public bool AutoUpdateEnabled { get; set; } = true;
        public string LastAnnouncementId { get; set; } = string.Empty;

        public static AppSettings Load()
        {
            var result = new AppSettings();
            try
            {
                if (!File.Exists(SettingsPath)) return result;
                foreach (var line in File.ReadAllLines(SettingsPath))
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
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to load settings", exception);
            }
            return result;
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var lines = new[]
                {
                    "BallX=" + BallX.ToString(CultureInfo.InvariantCulture),
                    "BallY=" + BallY.ToString(CultureInfo.InvariantCulture),
                    "GamePath=" + Sanitize(GamePath),
                    "AutoUpdateEnabled=" + AutoUpdateEnabled,
                    "LastAnnouncementId=" + Sanitize(LastAnnouncementId)
                };
                File.WriteAllLines(SettingsPath, lines);
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to save settings", exception);
            }
        }

        private static string Sanitize(string value)
        {
            return (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
        }
    }
}
