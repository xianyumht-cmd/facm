namespace FACM.Core.Settings;

public enum SettingsLoadOrigin
{
    ExistingV2,
    MigratedLegacy,
    Defaults,
    RecoveredLastKnownGood,
    RecoveryDefaults
}

public enum SettingsSectionOwner
{
    Environment,
    Online,
    Appearance,
    Pets,
    League
}

public sealed class Settings2Document
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public EnvironmentSettings Environment { get; set; } = new();
    public OnlineSettings Online { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public PetSettings Pets { get; set; } = new();
    public LeagueSettings League { get; set; } = new();

    public static Settings2Document CreateDefault() => new();
}

public sealed class EnvironmentSettings
{
    public string GamePath { get; set; } = string.Empty;
}

public sealed class OnlineSettings
{
    public bool AutoUpdateEnabled { get; set; } = true;
    public string LastAnnouncementId { get; set; } = string.Empty;
}

public sealed class AppearanceSettings
{
    public string ThemeId { get; set; } = LegacySettingsSnapshot.DefaultThemeId;
}

public sealed class PetSettings
{
    public int BallX { get; set; } = int.MinValue;
    public int BallY { get; set; } = int.MinValue;
    public string StyleId { get; set; } = LegacySettingsSnapshot.DefaultPetId;
    public bool Enabled { get; set; }
}

public sealed class LeagueSettings
{
    public bool AutoApplyRecommended { get; set; }
    public string ExitGameHotkey { get; set; } = string.Empty;
    public string CloseLobbyHotkey { get; set; } = string.Empty;
    public bool AutoHonorTeammateEnabled { get; set; }
    public bool AutoReturnLobbyEnabled { get; set; }
    public bool AutoMatchmakingEnabled { get; set; }
    public bool AutoAcceptEnabled { get; set; }
}

public sealed record Settings2LoadResult(Settings2Document Settings, SettingsLoadOrigin Origin);

public interface ISettings2Repository
{
    Task<Settings2LoadResult> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Settings2Document settings, CancellationToken cancellationToken = default);
}

public static class Settings2Ownership
{
    public static SettingsSectionOwner GetOwner(string sectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        return sectionName.Trim().ToLowerInvariant() switch
        {
            "environment" => SettingsSectionOwner.Environment,
            "online" => SettingsSectionOwner.Online,
            "appearance" => SettingsSectionOwner.Appearance,
            "pets" => SettingsSectionOwner.Pets,
            "league" => SettingsSectionOwner.League,
            _ => throw new ArgumentOutOfRangeException(nameof(sectionName), sectionName, "Unknown Settings 2.0 section.")
        };
    }
}

public static class Settings2Migration
{
    public static Settings2Document FromLegacy(LegacySettingsSnapshot legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        var document = new Settings2Document
        {
            Environment = new EnvironmentSettings
            {
                GamePath = legacy.GamePath
            },
            Online = new OnlineSettings
            {
                AutoUpdateEnabled = legacy.AutoUpdateEnabled,
                LastAnnouncementId = legacy.LastAnnouncementId
            },
            Appearance = new AppearanceSettings
            {
                ThemeId = legacy.ThemeId
            },
            Pets = new PetSettings
            {
                BallX = legacy.BallX,
                BallY = legacy.BallY,
                StyleId = legacy.PetStyleId,
                Enabled = legacy.AnimalPetEnabled
            },
            League = new LeagueSettings
            {
                AutoApplyRecommended = legacy.LeagueAutoApplyRecommended,
                ExitGameHotkey = legacy.LeagueExitGameHotkey,
                CloseLobbyHotkey = legacy.LeagueCloseLobbyHotkey,
                AutoHonorTeammateEnabled = legacy.LeagueAutoHonorTeammateEnabled,
                AutoReturnLobbyEnabled = legacy.LeagueAutoReturnLobbyEnabled,
                AutoMatchmakingEnabled = legacy.LeagueAutoMatchmakingEnabled,
                AutoAcceptEnabled = legacy.LeagueAutoAcceptEnabled
            }
        };
        Settings2Validator.ThrowIfInvalid(document);
        return document;
    }
}

public sealed record SettingsValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public static class Settings2Validator
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

    public static SettingsValidationResult Validate(Settings2Document? settings)
    {
        var errors = new List<string>();
        if (settings is null)
        {
            errors.Add("settings document is null");
            return new SettingsValidationResult(false, errors);
        }

        if (settings.SchemaVersion != Settings2Document.CurrentSchemaVersion)
            errors.Add($"unsupported schema version: {settings.SchemaVersion}");

        var environment = settings.Environment;
        var online = settings.Online;
        var appearance = settings.Appearance;
        var pets = settings.Pets;
        var league = settings.League;

        if (environment is null) errors.Add("environment section is missing");
        else ValidateSingleLine(environment.GamePath, 4096, "environment.gamePath", errors);

        if (online is null) errors.Add("online section is missing");
        else ValidateSingleLine(online.LastAnnouncementId, 512, "online.lastAnnouncementId", errors);

        if (appearance is null) errors.Add("appearance section is missing");
        else if (string.IsNullOrWhiteSpace(appearance.ThemeId) || !KnownThemeIds.Contains(appearance.ThemeId))
            errors.Add("appearance.themeId is unsupported");

        if (pets is null) errors.Add("pets section is missing");
        else if (string.IsNullOrWhiteSpace(pets.StyleId) || !KnownPetIds.Contains(pets.StyleId))
            errors.Add("pets.styleId is unsupported");

        if (league is null) errors.Add("league section is missing");
        else
        {
            ValidateSingleLine(league.ExitGameHotkey, 128, "league.exitGameHotkey", errors);
            ValidateSingleLine(league.CloseLobbyHotkey, 128, "league.closeLobbyHotkey", errors);
        }

        return new SettingsValidationResult(errors.Count == 0, errors);
    }

    public static void ThrowIfInvalid(Settings2Document? settings)
    {
        var result = Validate(settings);
        if (!result.IsValid)
            throw new InvalidDataException("Invalid FACM Settings 2.0: " + string.Join("; ", result.Errors));
    }

    private static void ValidateSingleLine(string? value, int maxLength, string name, List<string> errors)
    {
        if (value is null)
        {
            errors.Add(name + " is null");
            return;
        }
        if (value.Length > maxLength) errors.Add(name + " is too long");
        if (value.Contains('\r') || value.Contains('\n')) errors.Add(name + " must be single-line");
    }
}
