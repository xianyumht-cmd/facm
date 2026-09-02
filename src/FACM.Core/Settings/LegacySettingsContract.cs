namespace FACM.Core.Settings;

/// <summary>
/// Frozen settings.ini contract derived from production FACM 3.5.15 AppSettings.BuildLines().
/// Keep the key names and ordering stable while 4.0 remains migration-compatible with 3.5.15.
/// </summary>
public static class LegacySettingsContract
{
    public const string ProductionBaseline = "FACM 3.5.15";

    public const string BallX = "BallX";
    public const string BallY = "BallY";
    public const string GamePath = "GamePath";
    public const string AutoUpdateEnabled = "AutoUpdateEnabled";
    public const string LastAnnouncementId = "LastAnnouncementId";
    public const string ThemeId = "ThemeId";
    public const string PetStyleId = "PetStyleId";
    public const string AnimalPetEnabled = "AnimalPetEnabled";
    public const string LeagueAutoApplyRecommended = "LeagueAutoApplyRecommended";
    public const string LeagueExitGameHotkey = "LeagueExitGameHotkey";
    public const string LeagueCloseLobbyHotkey = "LeagueCloseLobbyHotkey";
    public const string LeagueAutoHonorTeammateEnabled = "LeagueAutoHonorTeammateEnabled";
    public const string LeagueAutoReturnLobbyEnabled = "LeagueAutoReturnLobbyEnabled";
    public const string LeagueAutoMatchmakingEnabled = "LeagueAutoMatchmakingEnabled";
    public const string LeagueAutoAcceptEnabled = "LeagueAutoAcceptEnabled";

    public const int KeyCount = 15;

    public static IReadOnlyList<string> OrderedKeys { get; } =
    [
        BallX,
        BallY,
        GamePath,
        AutoUpdateEnabled,
        LastAnnouncementId,
        ThemeId,
        PetStyleId,
        AnimalPetEnabled,
        LeagueAutoApplyRecommended,
        LeagueExitGameHotkey,
        LeagueCloseLobbyHotkey,
        LeagueAutoHonorTeammateEnabled,
        LeagueAutoReturnLobbyEnabled,
        LeagueAutoMatchmakingEnabled,
        LeagueAutoAcceptEnabled
    ];
}
