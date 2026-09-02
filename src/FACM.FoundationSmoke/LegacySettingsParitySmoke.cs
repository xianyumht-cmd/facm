using FACM.Core.Settings;

internal static class LegacySettingsParitySmoke
{
    private static readonly string[] Production3515Keys =
    [
        "BallX",
        "BallY",
        "GamePath",
        "AutoUpdateEnabled",
        "LastAnnouncementId",
        "ThemeId",
        "PetStyleId",
        "AnimalPetEnabled",
        "LeagueAutoApplyRecommended",
        "LeagueExitGameHotkey",
        "LeagueCloseLobbyHotkey",
        "LeagueAutoHonorTeammateEnabled",
        "LeagueAutoReturnLobbyEnabled",
        "LeagueAutoMatchmakingEnabled",
        "LeagueAutoAcceptEnabled"
    ];

    public static Task RunAsync()
    {
        Require(LegacySettingsContract.ProductionBaseline == "FACM 3.5.15",
            "Legacy settings contract lost the production 3.5.15 baseline marker.");
        Require(LegacySettingsContract.KeyCount == Production3515Keys.Length,
            "Frozen legacy settings key count no longer matches production 3.5.15.");
        Require(LegacySettingsContract.OrderedKeys.SequenceEqual(Production3515Keys, StringComparer.Ordinal),
            "Frozen legacy settings key ordering no longer matches production 3.5.15 AppSettings.BuildLines().");

        var snapshot = new LegacySettingsSnapshot
        {
            BallX = 101,
            BallY = -202,
            GamePath = @"C:\Games\League",
            AutoUpdateEnabled = false,
            LastAnnouncementId = "notice-42",
            ThemeId = "obsidian-gold",
            PetStyleId = "vpet",
            AnimalPetEnabled = true,
            LeagueAutoApplyRecommended = true,
            LeagueExitGameHotkey = "Ctrl+F9",
            LeagueCloseLobbyHotkey = "Ctrl+F10",
            LeagueAutoHonorTeammateEnabled = true,
            LeagueAutoReturnLobbyEnabled = true,
            LeagueAutoMatchmakingEnabled = true,
            LeagueAutoAcceptEnabled = true
        };
        var serialized = LegacySettingsCodec.Serialize(snapshot);
        var serializedKeys = serialized
            .Select(line => line[..line.IndexOf('=')])
            .ToArray();
        Require(serialized.Count == LegacySettingsContract.KeyCount,
            "Legacy settings serializer no longer emits exactly the frozen key count.");
        Require(serializedKeys.SequenceEqual(Production3515Keys, StringComparer.Ordinal),
            "Legacy settings serializer key ordering drifted from production 3.5.15.");

        var parsed = LegacySettingsCodec.Parse(serialized);
        Require(parsed.BallX == snapshot.BallX && parsed.BallY == snapshot.BallY,
            "Legacy position values failed round-trip parity.");
        Require(parsed.GamePath == snapshot.GamePath && !parsed.AutoUpdateEnabled,
            "Legacy environment/online values failed round-trip parity.");
        Require(parsed.ThemeId == snapshot.ThemeId && parsed.PetStyleId == snapshot.PetStyleId && parsed.AnimalPetEnabled,
            "Legacy personalization values failed round-trip parity.");
        Require(parsed.LeagueAutoApplyRecommended &&
                parsed.LeagueExitGameHotkey == snapshot.LeagueExitGameHotkey &&
                parsed.LeagueCloseLobbyHotkey == snapshot.LeagueCloseLobbyHotkey &&
                parsed.LeagueAutoHonorTeammateEnabled &&
                parsed.LeagueAutoReturnLobbyEnabled &&
                parsed.LeagueAutoMatchmakingEnabled &&
                parsed.LeagueAutoAcceptEnabled,
            "Legacy League settings failed round-trip parity.");
        return Task.CompletedTask;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
