using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using FACM.Pets;
using FACM.Theming;

namespace FACM.Services
{
    internal sealed class CloudSettingsSnapshot
    {
        public bool AutoUpdateEnabled { get; set; }
        public string ThemeId { get; set; }
        public string PetStyleId { get; set; }
        public bool AnimalPetEnabled { get; set; }
        public bool LeagueAutoApplyRecommended { get; set; }
        public string LeagueExitGameHotkey { get; set; }
        public string LeagueCloseLobbyHotkey { get; set; }
        public bool LeagueAutoHonorTeammateEnabled { get; set; }
        public bool LeagueAutoReturnLobbyEnabled { get; set; }
        public bool LeagueAutoMatchmakingEnabled { get; set; }
        public bool LeagueAutoAcceptEnabled { get; set; }
        public int LeagueAutoMatchmakingMinPartySize { get; set; }
        public int LeagueAutoMatchmakingStartDelayMs { get; set; }
        public int LeagueAutoAcceptDelayMs { get; set; }
        public string LeagueAutoMatchmakingStopPolicy { get; set; }
        public int LeagueAutoMatchmakingStopAfterMs { get; set; }
        public bool LeagueRuntimeCompanionPinned { get; set; }
        public bool LeagueRuntimeCompanionCollapsed { get; set; }
        public bool LeaguePersonalStatsEnabled { get; set; }
        public bool LeagueCloudRankingEnabled { get; set; }

        internal static CloudSettingsSnapshot Capture(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            return new CloudSettingsSnapshot
            {
                AutoUpdateEnabled = settings.AutoUpdateEnabled,
                ThemeId = ThemeCatalog.Get(settings.ThemeId).Id,
                PetStyleId = AnimalPetCatalog.Get(settings.PetStyleId).Id,
                AnimalPetEnabled = settings.AnimalPetEnabled,
                LeagueAutoApplyRecommended = settings.LeagueAutoApplyRecommended,
                LeagueExitGameHotkey = settings.LeagueExitGameHotkey ?? string.Empty,
                LeagueCloseLobbyHotkey = settings.LeagueCloseLobbyHotkey ?? string.Empty,
                LeagueAutoHonorTeammateEnabled = settings.LeagueAutoHonorTeammateEnabled,
                LeagueAutoReturnLobbyEnabled = settings.LeagueAutoReturnLobbyEnabled,
                LeagueAutoMatchmakingEnabled = settings.LeagueAutoMatchmakingEnabled,
                LeagueAutoAcceptEnabled = settings.LeagueAutoAcceptEnabled,
                LeagueAutoMatchmakingMinPartySize = settings.LeagueAutoMatchmakingMinPartySize,
                LeagueAutoMatchmakingStartDelayMs = settings.LeagueAutoMatchmakingStartDelayMs,
                LeagueAutoAcceptDelayMs = settings.LeagueAutoAcceptDelayMs,
                LeagueAutoMatchmakingStopPolicy = settings.LeagueAutoMatchmakingStopPolicy ?? "never",
                LeagueAutoMatchmakingStopAfterMs = settings.LeagueAutoMatchmakingStopAfterMs,
                LeagueRuntimeCompanionPinned = settings.LeagueRuntimeCompanionPinned,
                LeagueRuntimeCompanionCollapsed = settings.LeagueRuntimeCompanionCollapsed,
                LeaguePersonalStatsEnabled = settings.LeaguePersonalStatsEnabled,
                LeagueCloudRankingEnabled = settings.LeagueCloudRankingEnabled
            };
        }

        internal void ApplyTo(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            settings.AutoUpdateEnabled = AutoUpdateEnabled;
            settings.ThemeId = ThemeCatalog.Get(ThemeId).Id;
            settings.PetStyleId = AnimalPetCatalog.Get(PetStyleId).Id;
            settings.AnimalPetEnabled = AnimalPetEnabled;
            settings.LeagueAutoApplyRecommended = LeagueAutoApplyRecommended;
            settings.LeagueExitGameHotkey = LeagueExitGameHotkey ?? string.Empty;
            settings.LeagueCloseLobbyHotkey = LeagueCloseLobbyHotkey ?? string.Empty;
            settings.LeagueAutoHonorTeammateEnabled = LeagueAutoHonorTeammateEnabled;
            settings.LeagueAutoReturnLobbyEnabled = LeagueAutoReturnLobbyEnabled;
            settings.LeagueAutoMatchmakingEnabled = LeagueAutoMatchmakingEnabled;
            settings.LeagueAutoAcceptEnabled = LeagueAutoAcceptEnabled;
            settings.LeagueAutoMatchmakingMinPartySize = LeagueAutoMatchmakingMinPartySize;
            settings.LeagueAutoMatchmakingStartDelayMs = LeagueAutoMatchmakingStartDelayMs;
            settings.LeagueAutoAcceptDelayMs = LeagueAutoAcceptDelayMs;
            settings.LeagueAutoMatchmakingStopPolicy = LeagueAutoMatchmakingStopPolicy ?? "never";
            settings.LeagueAutoMatchmakingStopAfterMs = LeagueAutoMatchmakingStopAfterMs;
            settings.LeagueRuntimeCompanionPinned = LeagueRuntimeCompanionPinned;
            settings.LeagueRuntimeCompanionCollapsed = LeagueRuntimeCompanionCollapsed;
            settings.LeaguePersonalStatsEnabled = LeaguePersonalStatsEnabled;
            settings.LeagueCloudRankingEnabled = LeaguePersonalStatsEnabled && LeagueCloudRankingEnabled;
        }

        internal static CloudSettingsSnapshot Deserialize(object value)
        {
            var serializer = new JavaScriptSerializer();
            return serializer.ConvertToType<CloudSettingsSnapshot>(value);
        }

        internal static string CreateHash(CloudSettingsSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var serializer = new JavaScriptSerializer();
            var json = serializer.Serialize(snapshot);
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        internal static DateTime GetLocalSettingsWriteTimeUtc()
        {
            try
            {
                return File.Exists(RuntimePaths.SettingsPath)
                    ? File.GetLastWriteTimeUtc(RuntimePaths.SettingsPath)
                    : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var settings = AppSettings.ParseLines(new[]
            {
                "AutoUpdateEnabled=False",
                "ThemeId=midnight",
                "PetStyleId=default",
                "AnimalPetEnabled=True",
                "LeagueAutoMatchmakingEnabled=True",
                "LeagueAutoAcceptEnabled=True",
                "LeagueAutoMatchmakingMinPartySize=3",
                "LeagueAutoMatchmakingStartDelayMs=1200",
                "LeagueAutoAcceptDelayMs=800",
                "LeagueAutoMatchmakingStopPolicy=fixed",
                "LeagueAutoMatchmakingStopAfterMs=45000",
                "LeagueRuntimeCompanionPinned=False",
                "LeagueRuntimeCompanionCollapsed=True",
                "LeaguePersonalStatsEnabled=True",
                "LeagueCloudRankingEnabled=True"
            });
            var snapshot = Capture(settings);
            if (snapshot.AutoUpdateEnabled || !snapshot.AnimalPetEnabled ||
                !snapshot.LeagueAutoMatchmakingEnabled || snapshot.LeagueAutoMatchmakingMinPartySize != 3 ||
                snapshot.LeagueAutoMatchmakingStartDelayMs != 1200 || snapshot.LeagueAutoAcceptDelayMs != 800 ||
                snapshot.LeagueAutoMatchmakingStopAfterMs != 45000 || snapshot.LeagueRuntimeCompanionPinned ||
                !snapshot.LeagueRuntimeCompanionCollapsed || !snapshot.LeagueCloudRankingEnabled)
                throw new InvalidOperationException("Cloud settings snapshot capture drifted.");

            var roundTrip = new AppSettings();
            snapshot.ApplyTo(roundTrip);
            if (roundTrip.AutoUpdateEnabled || !roundTrip.AnimalPetEnabled ||
                !roundTrip.LeagueAutoMatchmakingEnabled || roundTrip.LeagueAutoMatchmakingMinPartySize != 3 ||
                roundTrip.LeagueAutoMatchmakingStartDelayMs != 1200 || roundTrip.LeagueAutoAcceptDelayMs != 800 ||
                roundTrip.LeagueAutoMatchmakingStopAfterMs != 45000 || roundTrip.LeagueRuntimeCompanionPinned ||
                !roundTrip.LeagueRuntimeCompanionCollapsed || !roundTrip.LeagueCloudRankingEnabled)
                throw new InvalidOperationException("Cloud settings snapshot apply drifted.");

            if (string.IsNullOrWhiteSpace(CreateHash(snapshot)))
                throw new InvalidOperationException("Cloud settings snapshot hash is empty.");
        }
    }
}