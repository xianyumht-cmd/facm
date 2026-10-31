using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FACM.Pets;
using FACM.League;
using FACM.Theming;

namespace FACM.Services
{
    internal sealed class AppSettings
    {
        private const int MoveFileReplaceExisting = 0x1;
        private const int MoveFileWriteThrough = 0x8;
        private static readonly object SettingsWriteSync = new object();
        internal static event Action<AppSettings> Saved;
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
        public bool LeagueChampionSpellPresetsEnabled { get; set; } = false;
        public string LeagueChampionSpellPresets { get; set; } = string.Empty;
        public string LeagueChampionRunePresets { get; set; } = string.Empty;
        public string LeagueExitGameHotkey { get; set; } = string.Empty;
        public string LeagueCloseLobbyHotkey { get; set; } = string.Empty;
        public bool LeagueAutoHonorTeammateEnabled { get; set; } = false;
        public string LeagueAutoHonorStrategy { get; set; } = LeagueHonorStrategy.PreferLobbyMember;
        public bool LeagueAutoReturnLobbyEnabled { get; set; } = false;
        public bool LeagueAutoMatchmakingEnabled { get; set; } = false;
        public bool LeagueAutoMatchmakingWaitForInvitees { get; set; } = true;
        public bool LeagueAutoMatchmakingRoomNotices { get; set; } = false;
        public bool LeagueAutoAcceptEnabled { get; set; } = false;
        public bool LeagueAutoHandleInvitationsEnabled { get; set; } = false;
        public bool LeagueSkipInvitationsWhenAway { get; set; } = false;
        public string LeagueInvitationStrategies { get; set; } = "{}";
        public bool LeagueAutoSkipLeaderEnabled { get; set; } = false;
        public bool LeagueAutoReconnectEnabled { get; set; } = false;
        public bool LeagueAutoAramTeamSideEnabled { get; set; } = false;
        public bool LeagueAutoAramTeamSideVisibleToTeam { get; set; } = false;
        public bool LeagueAutoLoginStatusMessageEnabled { get; set; } = false;
        public string LeagueAutoLoginStatusMessage { get; set; } = string.Empty;
        public bool LeagueAutoLoginRankedStatusEnabled { get; set; } = false;
        public string LeagueAutoLoginRankedQueue { get; set; } = "RANKED_SOLO_5x5";
        public string LeagueAutoLoginRankedTier { get; set; } = "CHALLENGER";
        public string LeagueAutoLoginRankedDivision { get; set; } = string.Empty;
        public bool LeagueAutoReplyEnabled { get; set; } = false;
        public bool LeagueAutoReplyAwayOnly { get; set; } = false;
        public string LeagueAutoReplyText { get; set; } = "";
        public bool LeagueLockOfflineStatusEnabled { get; set; } = false;
        public bool LeagueRespawnOverlayEnabled { get; set; } = false;
        public string LeagueDynamicMessageOptions { get; set; } = string.Empty;
        public string LeagueQuickMessagePresets { get; set; } = string.Empty;
        public string LeagueQuickMessageHotkeys { get; set; } = string.Empty;
        public int LeagueQuickMessageIntervalMs { get; set; } = 500;
        public bool LeagueAutoPickEnabled { get; set; } = false;
        public bool LeagueAutoBanEnabled { get; set; } = false;
        public int LeagueAutoPickChampionId { get; set; } = 0;
        public int LeagueAutoBanChampionId { get; set; } = 0;
        public string LeagueAutoSelectProfiles { get; set; } = string.Empty;
        public int LeagueAutoMatchmakingMinPartySize { get; set; } = 1;
        public int LeagueAutoMatchmakingStartDelayMs { get; set; } = 0;
        public int LeagueAutoAcceptDelayMs { get; set; } = 0;
        public string LeagueAutoMatchmakingStopPolicy { get; set; } = "never";
        public int LeagueAutoMatchmakingStopAfterMs { get; set; } = 120000;
        public int LeagueRuntimeCompanionX { get; set; } = int.MinValue;
        public int LeagueRuntimeCompanionY { get; set; } = int.MinValue;
        public bool LeagueRuntimeCompanionPinned { get; set; } = true;
        public bool LeagueRuntimeCompanionCollapsed { get; set; } = false;
        public bool LeaguePersonalStatsEnabled { get; set; } = true;
        public bool LeagueCloudRankingEnabled { get; set; } = false;
        public bool AutoConfigSyncEnabled { get; set; } = true;
        public bool GameAutoRestoreEnabled { get; set; } = true;

        internal static bool HasLegacySettingsForFirstUse()
        {
            return File.Exists(LegacySettingsPath);
        }

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
            var committed = false;
            try
            {
                lock (SettingsWriteSync)
                {
                    RuntimePaths.Initialize();
                    WriteLinesAtomically(RuntimePaths.SettingsPath, BuildLines());
                    AppSettingsRecovery.SaveLastKnownGood(RuntimePaths.SettingsRecoveryPath, this);
                    committed = true;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to save settings", exception);
            }
            if (!committed) return;
            var saved = Saved;
            if (saved == null) return;
            try { saved(this); }
            catch (Exception error)
            {
                AppLog.Info("Settings saved, notification deferred: " + error.GetType().Name);
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
                "LeagueChampionSpellPresetsEnabled=" + LeagueChampionSpellPresetsEnabled,
                "LeagueChampionSpellPresets=" + Sanitize(LeagueChampionSpellPresets),
                "LeagueChampionRunePresets=" + Sanitize(LeagueChampionRunePresets),
                "LeagueExitGameHotkey=" + Sanitize(LeagueExitGameHotkey),
                "LeagueCloseLobbyHotkey=" + Sanitize(LeagueCloseLobbyHotkey),
                "LeagueAutoHonorTeammateEnabled=" + LeagueAutoHonorTeammateEnabled,
                "LeagueAutoHonorStrategy=" + LeagueHonorStrategy.Normalize(LeagueAutoHonorStrategy),
                "LeagueAutoReturnLobbyEnabled=" + LeagueAutoReturnLobbyEnabled,
                "LeagueAutoMatchmakingEnabled=" + LeagueAutoMatchmakingEnabled,
                "LeagueAutoMatchmakingWaitForInvitees=" + LeagueAutoMatchmakingWaitForInvitees,
                "LeagueAutoMatchmakingRoomNotices=" + LeagueAutoMatchmakingRoomNotices,
                "LeagueAutoAcceptEnabled=" + LeagueAutoAcceptEnabled,
                "LeagueAutoHandleInvitationsEnabled=" + LeagueAutoHandleInvitationsEnabled,
                "LeagueSkipInvitationsWhenAway=" + LeagueSkipInvitationsWhenAway,
                "LeagueInvitationStrategies=" + LeagueInvitationPolicies.NormalizeJson(LeagueInvitationStrategies),
                "LeagueAutoSkipLeaderEnabled=" + LeagueAutoSkipLeaderEnabled,
                "LeagueAutoReconnectEnabled=" + LeagueAutoReconnectEnabled,
                "LeagueAutoAramTeamSideEnabled=" + LeagueAutoAramTeamSideEnabled,
                "LeagueAutoAramTeamSideVisibleToTeam=" + LeagueAutoAramTeamSideVisibleToTeam,
                "LeagueAutoLoginStatusMessageEnabled=" + LeagueAutoLoginStatusMessageEnabled,
                "LeagueAutoLoginStatusMessage=" + Sanitize(LeagueLoginStatusRestoreController.NormalizeSavedMessage(LeagueAutoLoginStatusMessage)),
                "LeagueAutoLoginRankedStatusEnabled=" + LeagueAutoLoginRankedStatusEnabled,
                "LeagueAutoLoginRankedQueue=" + Sanitize(LeagueAutoLoginRankedQueue),
                "LeagueAutoLoginRankedTier=" + Sanitize(LeagueAutoLoginRankedTier),
                "LeagueAutoLoginRankedDivision=" + Sanitize(LeagueAutoLoginRankedDivision),
                "LeagueAutoReplyEnabled=" + LeagueAutoReplyEnabled,
                "LeagueAutoReplyAwayOnly=" + LeagueAutoReplyAwayOnly,
                "LeagueAutoReplyText=" + Sanitize(LeagueAutoReplyController.NormalizeText(LeagueAutoReplyText)),
                "LeagueLockOfflineStatusEnabled=" + LeagueLockOfflineStatusEnabled,
                "LeagueRespawnOverlayEnabled=" + LeagueRespawnOverlayEnabled,
                "LeagueDynamicMessageOptions=" + FACM.League.LeagueDynamicMessageOptions.Save(FACM.League.LeagueDynamicMessageOptions.Parse(LeagueDynamicMessageOptions)),
                "LeagueQuickMessagePresets=" + FACM.League.LeagueQuickMessagePresets.NormalizeJson(LeagueQuickMessagePresets),
                "LeagueQuickMessageHotkeys=" + FACM.League.LeagueQuickMessageHotkeys.NormalizeJson(LeagueQuickMessageHotkeys),
                "LeagueQuickMessageIntervalMs=" + Math.Max(0, Math.Min(5000, LeagueQuickMessageIntervalMs)).ToString(CultureInfo.InvariantCulture),
                "LeagueAutoPickEnabled=" + LeagueAutoPickEnabled,
                "LeagueAutoBanEnabled=" + LeagueAutoBanEnabled,
                "LeagueAutoPickChampionId=" + LeagueAutoPickChampionId.ToString(CultureInfo.InvariantCulture),
                "LeagueAutoBanChampionId=" + LeagueAutoBanChampionId.ToString(CultureInfo.InvariantCulture),
                "LeagueAutoSelectProfiles=" + (LeagueAutoSelectProfiles ?? string.Empty),
                "LeagueAutoMatchmakingMinPartySize=" + LeagueAutoMatchmakingMinPartySize.ToString(CultureInfo.InvariantCulture),
                "LeagueAutoMatchmakingStartDelayMs=" + LeagueAutoMatchmakingStartDelayMs.ToString(CultureInfo.InvariantCulture),
                "LeagueAutoAcceptDelayMs=" + LeagueAutoAcceptDelayMs.ToString(CultureInfo.InvariantCulture),
                "LeagueAutoMatchmakingStopPolicy=" + Sanitize(LeagueAutoMatchmakingStopPolicy),
                "LeagueAutoMatchmakingStopAfterMs=" + LeagueAutoMatchmakingStopAfterMs.ToString(CultureInfo.InvariantCulture),
                "LeagueRuntimeCompanionX=" + LeagueRuntimeCompanionX.ToString(CultureInfo.InvariantCulture),
                "LeagueRuntimeCompanionY=" + LeagueRuntimeCompanionY.ToString(CultureInfo.InvariantCulture),
                "LeagueRuntimeCompanionPinned=" + LeagueRuntimeCompanionPinned,
                "LeagueRuntimeCompanionCollapsed=" + LeagueRuntimeCompanionCollapsed,
                "LeaguePersonalStatsEnabled=" + LeaguePersonalStatsEnabled,
                "LeagueCloudRankingEnabled=" + LeagueCloudRankingEnabled,
                "AutoConfigSyncEnabled=" + AutoConfigSyncEnabled,
                "GameAutoRestoreEnabled=" + GameAutoRestoreEnabled
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

        internal static void ValidateRuntimeCompanionPreferencesForSmokeTest()
        {
            var settings = ParseLines(new[]
            {
                "LeagueRuntimeCompanionX=-1250",
                "LeagueRuntimeCompanionY=240",
                "LeagueRuntimeCompanionPinned=False",
                "LeagueRuntimeCompanionCollapsed=True"
            });
            if (settings.LeagueRuntimeCompanionX != -1250 || settings.LeagueRuntimeCompanionY != 240 ||
                settings.LeagueRuntimeCompanionPinned || !settings.LeagueRuntimeCompanionCollapsed)
                throw new InvalidOperationException("Runtime Companion settings parsing drifted.");

            var lines = settings.BuildLines();
            if (Array.IndexOf(lines, "LeagueRuntimeCompanionX=-1250") < 0 ||
                Array.IndexOf(lines, "LeagueRuntimeCompanionY=240") < 0 ||
                Array.IndexOf(lines, "LeagueRuntimeCompanionPinned=False") < 0 ||
                Array.IndexOf(lines, "LeagueRuntimeCompanionCollapsed=True") < 0)
                throw new InvalidOperationException("Runtime Companion settings serialization drifted.");
        }

        internal static void ValidatePersonalStatsPreferencesForSmokeTest()
        {
            var enabled = ParseLines(new[]
            {
                "LeaguePersonalStatsEnabled=True",
                "LeagueCloudRankingEnabled=True"
            });
            if (!enabled.LeaguePersonalStatsEnabled || !enabled.LeagueCloudRankingEnabled)
                throw new InvalidOperationException("Personal stats settings parsing drifted.");

            var disabled = ParseLines(new[]
            {
                "LeaguePersonalStatsEnabled=False",
                "LeagueCloudRankingEnabled=True"
            });
            if (disabled.LeaguePersonalStatsEnabled || disabled.LeagueCloudRankingEnabled)
                throw new InvalidOperationException("Cloud ranking must fail closed when personal stats are disabled.");

            var lines = disabled.BuildLines();
            if (Array.IndexOf(lines, "LeaguePersonalStatsEnabled=False") < 0 ||
                Array.IndexOf(lines, "LeagueCloudRankingEnabled=False") < 0)
                throw new InvalidOperationException("Personal stats settings serialization drifted.");
        }

        internal static void ValidateMatchmakingStopPreferencesForSmokeTest()
        {
            var settings = ParseLines(new[]
            {
                "LeagueAutoMatchmakingStopPolicy=FIXED",
                "LeagueAutoMatchmakingStopAfterMs=42000"
            });
            if (!string.Equals(settings.LeagueAutoMatchmakingStopPolicy, "fixed", StringComparison.Ordinal) ||
                settings.LeagueAutoMatchmakingStopAfterMs != 42000)
                throw new InvalidOperationException("Matchmaking stop settings parsing drifted.");

            var invalid = ParseLines(new[]
            {
                "LeagueAutoMatchmakingStopPolicy=unknown",
                "LeagueAutoMatchmakingStopAfterMs=-5"
            });
            if (!string.Equals(invalid.LeagueAutoMatchmakingStopPolicy, "never", StringComparison.Ordinal) ||
                invalid.LeagueAutoMatchmakingStopAfterMs != 1000)
                throw new InvalidOperationException("Matchmaking stop settings fail-closed normalization drifted.");
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
            else if (key.Equals("LeagueChampionSpellPresetsEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueChampionSpellPresetsEnabled = flag;
            else if (key.Equals("LeagueChampionSpellPresets", StringComparison.OrdinalIgnoreCase) && value.Length <= 4096) result.LeagueChampionSpellPresets = value;
            else if (key.Equals("LeagueChampionRunePresets", StringComparison.OrdinalIgnoreCase) && value.Length <= 7500) result.LeagueChampionRunePresets = value;
            else if (key.Equals("LeagueExitGameHotkey", StringComparison.OrdinalIgnoreCase)) result.LeagueExitGameHotkey = value;
            else if (key.Equals("LeagueCloseLobbyHotkey", StringComparison.OrdinalIgnoreCase)) result.LeagueCloseLobbyHotkey = value;
            else if (key.Equals("LeagueAutoHonorTeammateEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoHonorTeammateEnabled = flag;
            else if (key.Equals("LeagueAutoHonorStrategy", StringComparison.OrdinalIgnoreCase)) result.LeagueAutoHonorStrategy = LeagueHonorStrategy.Normalize(value);
            else if (key.Equals("LeagueAutoReturnLobbyEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoReturnLobbyEnabled = flag;
            else if (key.Equals("LeagueAutoMatchmakingEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoMatchmakingEnabled = flag;
            else if (key.Equals("LeagueAutoMatchmakingWaitForInvitees", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoMatchmakingWaitForInvitees = flag;
            else if (key.Equals("LeagueAutoMatchmakingRoomNotices", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoMatchmakingRoomNotices = flag;
            else if (key.Equals("LeagueAutoAcceptEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoAcceptEnabled = flag;
            else if (key.Equals("LeagueAutoHandleInvitationsEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoHandleInvitationsEnabled = flag;
            else if (key.Equals("LeagueSkipInvitationsWhenAway", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueSkipInvitationsWhenAway = flag;
            else if (key.Equals("LeagueInvitationStrategies", StringComparison.OrdinalIgnoreCase) && value.Length <= 800) result.LeagueInvitationStrategies = LeagueInvitationPolicies.NormalizeJson(value);
            else if (key.Equals("LeagueAutoSkipLeaderEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoSkipLeaderEnabled = flag;
            else if (key.Equals("LeagueAutoReconnectEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoReconnectEnabled = flag;
            else if (key.Equals("LeagueAutoAramTeamSideEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoAramTeamSideEnabled = flag;
            else if (key.Equals("LeagueAutoAramTeamSideVisibleToTeam", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoAramTeamSideVisibleToTeam = flag;
            else if (key.Equals("LeagueAutoLoginStatusMessageEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoLoginStatusMessageEnabled = flag;
            else if (key.Equals("LeagueAutoLoginStatusMessage", StringComparison.OrdinalIgnoreCase) && value.Length <= LeaguePresenceService.MaximumStatusMessageLength) result.LeagueAutoLoginStatusMessage = LeagueLoginStatusRestoreController.NormalizeSavedMessage(value);
            else if (key.Equals("LeagueAutoLoginRankedStatusEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoLoginRankedStatusEnabled = flag;
            else if (key.Equals("LeagueAutoLoginRankedQueue", StringComparison.OrdinalIgnoreCase) && value.Length <= 32) result.LeagueAutoLoginRankedQueue = value;
            else if (key.Equals("LeagueAutoLoginRankedTier", StringComparison.OrdinalIgnoreCase) && value.Length <= 20) result.LeagueAutoLoginRankedTier = value;
            else if (key.Equals("LeagueAutoLoginRankedDivision", StringComparison.OrdinalIgnoreCase) && value.Length <= 4) result.LeagueAutoLoginRankedDivision = value;
            else if (key.Equals("LeagueAutoReplyEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoReplyEnabled = flag;
            else if (key.Equals("LeagueAutoReplyAwayOnly", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoReplyAwayOnly = flag;
            else if (key.Equals("LeagueAutoReplyText", StringComparison.OrdinalIgnoreCase)) result.LeagueAutoReplyText = LeagueAutoReplyController.NormalizeText(value);
            else if (key.Equals("LeagueLockOfflineStatusEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueLockOfflineStatusEnabled = flag;
            else if (key.Equals("LeagueRespawnOverlayEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueRespawnOverlayEnabled = flag;
            else if (key.Equals("LeagueDynamicMessageOptions", StringComparison.OrdinalIgnoreCase)) result.LeagueDynamicMessageOptions = FACM.League.LeagueDynamicMessageOptions.Save(FACM.League.LeagueDynamicMessageOptions.Parse(value));
            else if (key.Equals("LeagueQuickMessagePresets", StringComparison.OrdinalIgnoreCase)) result.LeagueQuickMessagePresets = FACM.League.LeagueQuickMessagePresets.NormalizeJson(value);
            else if (key.Equals("LeagueQuickMessageHotkeys", StringComparison.OrdinalIgnoreCase)) result.LeagueQuickMessageHotkeys = FACM.League.LeagueQuickMessageHotkeys.NormalizeJson(value);
            else if (key.Equals("LeagueQuickMessageIntervalMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out number)) result.LeagueQuickMessageIntervalMs = Math.Max(0, Math.Min(5000, number));
            else if (key.Equals("LeagueAutoPickEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoPickEnabled = flag;
            else if (key.Equals("LeagueAutoBanEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueAutoBanEnabled = flag;
            else if (key.Equals("LeagueAutoPickChampionId", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) result.LeagueAutoPickChampionId = Math.Max(0, Math.Min(10000, number));
            else if (key.Equals("LeagueAutoBanChampionId", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) result.LeagueAutoBanChampionId = Math.Max(0, Math.Min(10000, number));
            else if (key.Equals("LeagueAutoSelectProfiles", StringComparison.OrdinalIgnoreCase) && value.Length <= FACM.League.LeagueAutoSelectProfiles.MaxSerializedLength) result.LeagueAutoSelectProfiles = value;
            else if (key.Equals("LeagueAutoMatchmakingMinPartySize", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) result.LeagueAutoMatchmakingMinPartySize = number;
            else if (key.Equals("LeagueAutoMatchmakingStartDelayMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) result.LeagueAutoMatchmakingStartDelayMs = number;
            else if (key.Equals("LeagueAutoAcceptDelayMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) result.LeagueAutoAcceptDelayMs = number;
            else if (key.Equals("LeagueAutoMatchmakingStopPolicy", StringComparison.OrdinalIgnoreCase)) result.LeagueAutoMatchmakingStopPolicy = value;
            else if (key.Equals("LeagueAutoMatchmakingStopAfterMs", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) result.LeagueAutoMatchmakingStopAfterMs = number;
            else if (key.Equals("LeagueRuntimeCompanionX", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) result.LeagueRuntimeCompanionX = number;
            else if (key.Equals("LeagueRuntimeCompanionY", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) result.LeagueRuntimeCompanionY = number;
            else if (key.Equals("LeagueRuntimeCompanionPinned", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueRuntimeCompanionPinned = flag;
            else if (key.Equals("LeagueRuntimeCompanionCollapsed", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueRuntimeCompanionCollapsed = flag;
            else if (key.Equals("LeaguePersonalStatsEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeaguePersonalStatsEnabled = flag;
            else if (key.Equals("LeagueCloudRankingEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.LeagueCloudRankingEnabled = flag;
            else if (key.Equals("AutoConfigSyncEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.AutoConfigSyncEnabled = flag;
            else if (key.Equals("GameAutoRestoreEnabled", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out flag)) result.GameAutoRestoreEnabled = flag;
        }

        private static void Normalize(AppSettings result)
        {
            if (result == null) return;
            result.ThemeId = ThemeCatalog.Get(result.ThemeId).Id;
            result.PetStyleId = AnimalPetCatalog.Get(result.PetStyleId).Id;
            result.LeagueExitGameHotkey = Sanitize(result.LeagueExitGameHotkey).Trim();
            result.LeagueCloseLobbyHotkey = Sanitize(result.LeagueCloseLobbyHotkey).Trim();
            result.LeagueAutoHonorStrategy = LeagueHonorStrategy.Normalize(result.LeagueAutoHonorStrategy);
            result.LeagueInvitationStrategies = LeagueInvitationPolicies.NormalizeJson(result.LeagueInvitationStrategies);
            string rankedQueue;
            string rankedTier;
            string rankedDivision;
            if (!LeaguePresenceService.TryNormalizeRankedStatusForSmokeTest(
                result.LeagueAutoLoginRankedQueue, result.LeagueAutoLoginRankedTier,
                result.LeagueAutoLoginRankedDivision, out rankedQueue, out rankedTier, out rankedDivision))
            {
                result.LeagueAutoLoginRankedStatusEnabled = false;
                rankedQueue = "RANKED_SOLO_5x5";
                rankedTier = "CHALLENGER";
                rankedDivision = string.Empty;
            }
            result.LeagueAutoLoginRankedQueue = rankedQueue;
            result.LeagueAutoLoginRankedTier = rankedTier;
            result.LeagueAutoLoginRankedDivision = rankedDivision;
            if ((result.LeagueChampionSpellPresets ?? string.Empty).Length > 4096)
                result.LeagueChampionSpellPresets = string.Empty;
            if ((result.LeagueChampionRunePresets ?? string.Empty).Length > 7500)
                result.LeagueChampionRunePresets = string.Empty;
            // Both automatic writers must not race to replace the same spell slots.
            if (result.LeagueChampionSpellPresetsEnabled) result.LeagueAutoApplyRecommended = false;
            result.LeagueAutoMatchmakingMinPartySize = Math.Max(1, Math.Min(5, result.LeagueAutoMatchmakingMinPartySize));
            result.LeagueAutoMatchmakingStartDelayMs = Math.Max(0, Math.Min(60000, result.LeagueAutoMatchmakingStartDelayMs));
            result.LeagueAutoAcceptDelayMs = Math.Max(0, Math.Min(15000, result.LeagueAutoAcceptDelayMs));
            result.LeagueAutoMatchmakingStopPolicy = NormalizeMatchmakingStopPolicy(result.LeagueAutoMatchmakingStopPolicy);
            result.LeagueAutoMatchmakingStopAfterMs = Math.Max(1000, Math.Min(600000, result.LeagueAutoMatchmakingStopAfterMs));
            if (!result.LeaguePersonalStatsEnabled) result.LeagueCloudRankingEnabled = false;
        }

        private static string NormalizeMatchmakingStopPolicy(string value)
        {
            var normalized = Sanitize(value).Trim().ToLowerInvariant();
            if (normalized == "fixed" || normalized == "estimated") return normalized;
            return "never";
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
