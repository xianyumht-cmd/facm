using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.Services
{
    internal sealed class GgmanPortableSettingsProfile
    {
        public int SchemaVersion { get; set; } = 1;
        public CloudSettingsSnapshot Settings { get; set; }
    }

    internal static class GgmanPortableSettingsStore
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 32768 };

        internal static GgmanPortableSettingsProfile Capture(AppSettings settings)
        {
            var selected = CloudSettingsSnapshot.Capture(settings);
            selected.LeaguePersonalStatsEnabled = true;
            selected.LeagueCloudRankingEnabled = false;
            return new GgmanPortableSettingsProfile { Settings = selected };
        }

        internal static string Serialize(GgmanPortableSettingsProfile data)
        {
            Validate(data);
            var json = Json.Serialize(data);
            if (Encoding.UTF8.GetByteCount(json) > 32768)
                throw new InvalidDataException("软件偏好数据超过云端限制。");
            return json;
        }

        internal static GgmanPortableSettingsProfile Deserialize(object raw)
        {
            var data = Json.ConvertToType<GgmanPortableSettingsProfile>(raw);
            Validate(data);
            return data;
        }

        internal static void Validate(GgmanPortableSettingsProfile value)
        {
            if (value == null || value.SchemaVersion != 1 || value.Settings == null)
                throw new InvalidDataException("云端 GGman 设置格式不受支持。");
            var s = value.Settings;
            if (s.LeagueExitGameHotkey == null || s.LeagueExitGameHotkey.Length > 100 ||
                s.LeagueCloseLobbyHotkey == null || s.LeagueCloseLobbyHotkey.Length > 100 ||
                s.LeagueAutoMatchmakingStopPolicy == null ||
                s.LeagueAutoMatchmakingStopPolicy.Length > 20 ||
                s.LeagueAutoMatchmakingMinPartySize < 1 ||
                s.LeagueAutoMatchmakingMinPartySize > 5 ||
                s.LeagueAutoMatchmakingStartDelayMs < 0 ||
                s.LeagueAutoMatchmakingStartDelayMs > 60000 ||
                s.LeagueAutoAcceptDelayMs < 0 || s.LeagueAutoAcceptDelayMs > 15000 ||
                s.LeagueAutoMatchmakingStopAfterMs < 1000 ||
                s.LeagueAutoMatchmakingStopAfterMs > 600000 ||
                (s.LeagueAutoMatchmakingStopPolicy != "never" &&
                 s.LeagueAutoMatchmakingStopPolicy != "fixed" &&
                 s.LeagueAutoMatchmakingStopPolicy != "estimated"))
                throw new InvalidDataException("软件偏好包含不合法的自动化参数。");
        }

        internal static string Restore(GgmanPortableSettingsProfile data, AppSettings settings)
        {
            Validate(data);
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            RuntimePaths.Initialize();
            var backupDir = Path.Combine(RuntimePaths.DataDirectory, "settings-backups");
            Directory.CreateDirectory(backupDir);
            var path = RuntimePaths.SettingsPath;
            if (!File.Exists(path)) throw new FileNotFoundException("没有找到本机软件配置。", path);
            var backup = Path.Combine(backupDir, "before-cloud-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
                Guid.NewGuid().ToString("N").Substring(0, 8) + ".ini");
            File.Copy(path, backup, false);
            var s = data.Settings;
            settings.AutoUpdateEnabled = s.AutoUpdateEnabled;
            settings.ThemeId = FACM.Theming.ThemeCatalog.Get(s.ThemeId).Id;
            settings.PetStyleId = FACM.Pets.AnimalPetCatalog.Get(s.PetStyleId).Id;
            settings.AnimalPetEnabled = s.AnimalPetEnabled;
            settings.LeagueAutoApplyRecommended = s.LeagueAutoApplyRecommended;
            settings.LeagueExitGameHotkey = s.LeagueExitGameHotkey;
            settings.LeagueCloseLobbyHotkey = s.LeagueCloseLobbyHotkey;
            settings.LeagueAutoHonorTeammateEnabled = s.LeagueAutoHonorTeammateEnabled;
            settings.LeagueAutoReturnLobbyEnabled = s.LeagueAutoReturnLobbyEnabled;
            settings.LeagueAutoMatchmakingEnabled = s.LeagueAutoMatchmakingEnabled;
            settings.LeagueAutoAcceptEnabled = s.LeagueAutoAcceptEnabled;
            settings.LeagueAutoMatchmakingMinPartySize = s.LeagueAutoMatchmakingMinPartySize;
            settings.LeagueAutoMatchmakingStartDelayMs = s.LeagueAutoMatchmakingStartDelayMs;
            settings.LeagueAutoAcceptDelayMs = s.LeagueAutoAcceptDelayMs;
            settings.LeagueAutoMatchmakingStopPolicy = s.LeagueAutoMatchmakingStopPolicy;
            settings.LeagueAutoMatchmakingStopAfterMs = s.LeagueAutoMatchmakingStopAfterMs;
            settings.LeagueRuntimeCompanionPinned = s.LeagueRuntimeCompanionPinned;
            settings.LeagueRuntimeCompanionCollapsed = s.LeagueRuntimeCompanionCollapsed;
            // Never rewrite local game paths, screen geometry, identity or privacy consent.
            settings.Save();
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new IOException("GGman 软件设置未能成功写入，请从本地备份恢复。");
            var expected = CloudSettingsSnapshot.CreateHash(CloudSettingsSnapshot.Capture(settings));
            var actual = CloudSettingsSnapshot.CreateHash(
                CloudSettingsSnapshot.Capture(AppSettings.Load()));
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new IOException("软件设置落盘验证失败；恢复前的配置已备份。");
            return backup;
        }

        internal static void ValidateForSmokeTest()
        {
            var app = new AppSettings
            {
                GamePath = @"E:\WeGameApps\英雄联盟",
                BallX = 400,
                LeagueRuntimeCompanionX = 900,
                LeaguePersonalStatsEnabled = true,
                LeagueCloudRankingEnabled = true,
                ThemeId = FACM.Theming.ThemeCatalog.DefaultThemeId,
                PetStyleId = FACM.Pets.AnimalPetCatalog.DefaultPetId
            };
            var local = Capture(app);
            var json = Serialize(local);
            if (json.Contains("WeGameApps") || json.Contains("BallX") ||
                json.Contains("CompanionX") || local.Settings.LeagueCloudRankingEnabled)
                throw new InvalidOperationException("Portable settings leaked machine or ranking preferences.");
            var remote = Deserialize(Json.DeserializeObject(json));
            if (remote.SchemaVersion != 1 || remote.Settings.ThemeId != local.Settings.ThemeId)
                throw new InvalidOperationException("Portable settings serialization drifted.");
        }
    }
}
