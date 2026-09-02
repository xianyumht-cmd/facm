using System.Globalization;

namespace FACM.Core.Settings;

public sealed class LegacySettingsSnapshot
{
    public const string DefaultThemeId = "glass-blue";
    public const string DefaultPetId = "greenfly";

    public int BallX { get; set; } = int.MinValue;
    public int BallY { get; set; } = int.MinValue;
    public string GamePath { get; set; } = string.Empty;
    public bool AutoUpdateEnabled { get; set; } = true;
    public string LastAnnouncementId { get; set; } = string.Empty;
    public string ThemeId { get; set; } = DefaultThemeId;
    public string PetStyleId { get; set; } = DefaultPetId;
    public bool AnimalPetEnabled { get; set; }
    public bool LeagueAutoApplyRecommended { get; set; }
    public string LeagueExitGameHotkey { get; set; } = string.Empty;
    public string LeagueCloseLobbyHotkey { get; set; } = string.Empty;
    public bool LeagueAutoHonorTeammateEnabled { get; set; }
    public bool LeagueAutoReturnLobbyEnabled { get; set; }
    public bool LeagueAutoMatchmakingEnabled { get; set; }
    public bool LeagueAutoAcceptEnabled { get; set; }
}

public static class LegacySettingsCodec
{
    private static readonly HashSet<string> KnownThemeIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "glass-blue", "obsidian-gold", "neon-cyber", "cloud-light", "brutalist-grid",
        "holo-spectrum", "mono-emerald", "rgb-tactical", "aurora-night", "sunset-synthwave"
    };

    private static readonly HashSet<string> KnownPetIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "greenfly", "bee", "real-bee", "dragonfly", "butterfly", "moth", "vpet",
        "cat", "dog", "spider", "ant", "greyfly", "wasp", "bird"
    };

    public static LegacySettingsSnapshot Parse(IEnumerable<string>? lines)
    {
        var result = new LegacySettingsSnapshot();
        if (lines is not null)
        {
            foreach (var line in lines) ApplyLine(result, line);
        }
        Normalize(result);
        return result;
    }

    public static IReadOnlyList<string> Serialize(LegacySettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        return
        [
            LegacySettingsContract.BallX + "=" + settings.BallX.ToString(CultureInfo.InvariantCulture),
            LegacySettingsContract.BallY + "=" + settings.BallY.ToString(CultureInfo.InvariantCulture),
            LegacySettingsContract.GamePath + "=" + Sanitize(settings.GamePath),
            LegacySettingsContract.AutoUpdateEnabled + "=" + settings.AutoUpdateEnabled,
            LegacySettingsContract.LastAnnouncementId + "=" + Sanitize(settings.LastAnnouncementId),
            LegacySettingsContract.ThemeId + "=" + settings.ThemeId,
            LegacySettingsContract.PetStyleId + "=" + settings.PetStyleId,
            LegacySettingsContract.AnimalPetEnabled + "=" + settings.AnimalPetEnabled,
            LegacySettingsContract.LeagueAutoApplyRecommended + "=" + settings.LeagueAutoApplyRecommended,
            LegacySettingsContract.LeagueExitGameHotkey + "=" + Sanitize(settings.LeagueExitGameHotkey),
            LegacySettingsContract.LeagueCloseLobbyHotkey + "=" + Sanitize(settings.LeagueCloseLobbyHotkey),
            LegacySettingsContract.LeagueAutoHonorTeammateEnabled + "=" + settings.LeagueAutoHonorTeammateEnabled,
            LegacySettingsContract.LeagueAutoReturnLobbyEnabled + "=" + settings.LeagueAutoReturnLobbyEnabled,
            LegacySettingsContract.LeagueAutoMatchmakingEnabled + "=" + settings.LeagueAutoMatchmakingEnabled,
            LegacySettingsContract.LeagueAutoAcceptEnabled + "=" + settings.LeagueAutoAcceptEnabled
        ];
    }

    private static void ApplyLine(LegacySettingsSnapshot result, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var separator = line.IndexOf('=');
        if (separator <= 0) return;
        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();
        if (key.Equals(LegacySettingsContract.BallX, StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) result.BallX = x;
        else if (key.Equals(LegacySettingsContract.BallY, StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)) result.BallY = y;
        else if (key.Equals(LegacySettingsContract.GamePath, StringComparison.OrdinalIgnoreCase)) result.GamePath = value;
        else if (key.Equals(LegacySettingsContract.AutoUpdateEnabled, StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var autoUpdate)) result.AutoUpdateEnabled = autoUpdate;
        else if (key.Equals(LegacySettingsContract.LastAnnouncementId, StringComparison.OrdinalIgnoreCase)) result.LastAnnouncementId = value;
        else if (key.Equals(LegacySettingsContract.ThemeId, StringComparison.OrdinalIgnoreCase)) result.ThemeId = value;
        else if (key.Equals(LegacySettingsContract.PetStyleId, StringComparison.OrdinalIgnoreCase)) result.PetStyleId = value;
        else if (key.Equals(LegacySettingsContract.AnimalPetEnabled, StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var animalPet)) result.AnimalPetEnabled = animalPet;
        else if (key.Equals(LegacySettingsContract.LeagueAutoApplyRecommended, StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var autoApply)) result.LeagueAutoApplyRecommended = autoApply;
        else if (key.Equals(LegacySettingsContract.LeagueExitGameHotkey, StringComparison.OrdinalIgnoreCase)) result.LeagueExitGameHotkey = value;
        else if (key.Equals(LegacySettingsContract.LeagueCloseLobbyHotkey, StringComparison.OrdinalIgnoreCase)) result.LeagueCloseLobbyHotkey = value;
        else if (key.Equals(LegacySettingsContract.LeagueAutoHonorTeammateEnabled, StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var honor)) result.LeagueAutoHonorTeammateEnabled = honor;
        else if (key.Equals(LegacySettingsContract.LeagueAutoReturnLobbyEnabled, StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var returnLobby)) result.LeagueAutoReturnLobbyEnabled = returnLobby;
        else if (key.Equals(LegacySettingsContract.LeagueAutoMatchmakingEnabled, StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var matchmaking)) result.LeagueAutoMatchmakingEnabled = matchmaking;
        else if (key.Equals(LegacySettingsContract.LeagueAutoAcceptEnabled, StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var accept)) result.LeagueAutoAcceptEnabled = accept;
    }

    private static void Normalize(LegacySettingsSnapshot result)
    {
        var themeId = result.ThemeId ?? string.Empty;
        var petStyleId = result.PetStyleId ?? string.Empty;
        result.ThemeId = KnownThemeIds.Contains(themeId) ? themeId : LegacySettingsSnapshot.DefaultThemeId;
        result.PetStyleId = KnownPetIds.Contains(petStyleId) ? petStyleId : LegacySettingsSnapshot.DefaultPetId;
        result.GamePath = Sanitize(result.GamePath);
        result.LastAnnouncementId = Sanitize(result.LastAnnouncementId);
        result.LeagueExitGameHotkey = Sanitize(result.LeagueExitGameHotkey).Trim();
        result.LeagueCloseLobbyHotkey = Sanitize(result.LeagueCloseLobbyHotkey).Trim();
    }

    private static string Sanitize(string? value) => (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
}
