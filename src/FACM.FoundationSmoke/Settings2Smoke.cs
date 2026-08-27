using FACM.Core.Settings;
using FACM.Infrastructure.Settings;

internal static class Settings2Smoke
{
    public static async Task RunAsync()
    {
        await MigratesAllLegacyKeysAndPreservesLegacyAsync();
        await RejectsCorruptionAndUnsupportedVersionAsync();
        await RejectsInvalidSettingsBeforeWriteAsync();
        await FailedAtomicWritePreservesExistingAsync();
        await CreatesValidatedDefaultsWithoutLegacyAsync();
    }

    private static async Task MigratesAllLegacyKeysAndPreservesLegacyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-settings2-smoke", Guid.NewGuid().ToString("N"));
        var legacyPath = Path.Combine(root, "settings.ini");
        var v2Path = Path.Combine(root, "settings.v2.json");
        var legacyText = string.Join(Environment.NewLine,
        [
            "BallX=101",
            "BallY=-202",
            "GamePath=C:\\Games\\League",
            "AutoUpdateEnabled=False",
            "LastAnnouncementId=notice-42",
            "ThemeId=obsidian-gold",
            "PetStyleId=vpet",
            "AnimalPetEnabled=True",
            "LeagueAutoApplyRecommended=True",
            "LeagueExitGameHotkey=Ctrl+F9",
            "LeagueCloseLobbyHotkey=Ctrl+F10",
            "LeagueAutoHonorTeammateEnabled=True",
            "LeagueAutoReturnLobbyEnabled=True",
            "LeagueAutoMatchmakingEnabled=True",
            "LeagueAutoAcceptEnabled=True"
        ]);

        var files = new MemorySettings2FileStore();
        files.Seed(legacyPath, legacyText);
        var repository = new Settings2Repository(v2Path, legacyPath, files);

        var migrated = await repository.LoadAsync();
        Equal(SettingsLoadOrigin.MigratedLegacy, migrated.Origin, "legacy migration origin");
        Equal(Settings2Document.CurrentSchemaVersion, migrated.Settings.SchemaVersion, "schema version");
        Equal("C:\\Games\\League", migrated.Settings.Environment.GamePath, "GamePath");
        True(!migrated.Settings.Online.AutoUpdateEnabled, "AutoUpdateEnabled");
        Equal("notice-42", migrated.Settings.Online.LastAnnouncementId, "LastAnnouncementId");
        Equal("obsidian-gold", migrated.Settings.Appearance.ThemeId, "ThemeId");
        Equal(101, migrated.Settings.Pets.BallX, "BallX");
        Equal(-202, migrated.Settings.Pets.BallY, "BallY");
        Equal("vpet", migrated.Settings.Pets.StyleId, "PetStyleId");
        True(migrated.Settings.Pets.Enabled, "AnimalPetEnabled");
        True(migrated.Settings.League.AutoApplyRecommended, "LeagueAutoApplyRecommended");
        Equal("Ctrl+F9", migrated.Settings.League.ExitGameHotkey, "LeagueExitGameHotkey");
        Equal("Ctrl+F10", migrated.Settings.League.CloseLobbyHotkey, "LeagueCloseLobbyHotkey");
        True(migrated.Settings.League.AutoHonorTeammateEnabled, "LeagueAutoHonorTeammateEnabled");
        True(migrated.Settings.League.AutoReturnLobbyEnabled, "LeagueAutoReturnLobbyEnabled");
        True(migrated.Settings.League.AutoMatchmakingEnabled, "LeagueAutoMatchmakingEnabled");
        True(migrated.Settings.League.AutoAcceptEnabled, "LeagueAutoAcceptEnabled");
        Equal(legacyText, files.Get(legacyPath), "legacy settings must remain byte-for-byte unchanged in fake store");
        True(files.Exists(v2Path), "settings.v2.json should be created after migration");

        var secondLoad = await repository.LoadAsync();
        Equal(SettingsLoadOrigin.ExistingV2, secondLoad.Origin, "second load must use Settings 2.0");
        Equal("notice-42", secondLoad.Settings.Online.LastAnnouncementId, "v2 round-trip announcement");
    }

    private static async Task RejectsCorruptionAndUnsupportedVersionAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-settings2-smoke", Guid.NewGuid().ToString("N"));
        var legacyPath = Path.Combine(root, "settings.ini");
        var corruptPath = Path.Combine(root, "corrupt.json");
        var newerPath = Path.Combine(root, "newer.json");
        var files = new MemorySettings2FileStore();

        files.Seed(corruptPath, "{ not-json");
        await ThrowsAsync<InvalidDataException>(() => new Settings2Repository(corruptPath, legacyPath, files).LoadAsync(), "corrupt JSON");
        Equal("{ not-json", files.Get(corruptPath), "corrupt file must not be overwritten");

        files.Seed(newerPath, "{\"schemaVersion\":99}");
        await ThrowsAsync<InvalidDataException>(() => new Settings2Repository(newerPath, legacyPath, files).LoadAsync(), "unsupported schema");
        Equal("{\"schemaVersion\":99}", files.Get(newerPath), "unsupported-version file must not be overwritten");
    }

    private static async Task RejectsInvalidSettingsBeforeWriteAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-settings2-smoke", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.v2.json");
        var legacyPath = Path.Combine(root, "settings.ini");
        var files = new MemorySettings2FileStore();
        var repository = new Settings2Repository(path, legacyPath, files);
        await repository.SaveAsync(Settings2Document.CreateDefault());
        var before = files.Get(path);
        var writesBefore = files.SuccessfulWrites;

        var invalid = Settings2Document.CreateDefault();
        invalid.Appearance.ThemeId = "not-a-theme";
        await ThrowsAsync<InvalidDataException>(() => repository.SaveAsync(invalid), "invalid theme");
        Equal(writesBefore, files.SuccessfulWrites, "invalid settings must fail before file write");
        Equal(before, files.Get(path), "invalid settings must preserve existing file");
    }

    private static async Task FailedAtomicWritePreservesExistingAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-settings2-smoke", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.v2.json");
        var legacyPath = Path.Combine(root, "settings.ini");
        var files = new MemorySettings2FileStore();
        var repository = new Settings2Repository(path, legacyPath, files);
        await repository.SaveAsync(Settings2Document.CreateDefault());
        var before = files.Get(path);

        var changed = Settings2Document.CreateDefault();
        changed.Online.AutoUpdateEnabled = false;
        files.FailWrites = true;
        await ThrowsAsync<IOException>(() => repository.SaveAsync(changed), "simulated atomic replace failure");
        Equal(before, files.Get(path), "failed atomic write must preserve previous settings");
    }

    private static async Task CreatesValidatedDefaultsWithoutLegacyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-settings2-smoke", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.v2.json");
        var legacyPath = Path.Combine(root, "settings.ini");
        var files = new MemorySettings2FileStore();
        var repository = new Settings2Repository(path, legacyPath, files);
        var loaded = await repository.LoadAsync();
        Equal(SettingsLoadOrigin.Defaults, loaded.Origin, "defaults origin");
        Equal(LegacySettingsSnapshot.DefaultThemeId, loaded.Settings.Appearance.ThemeId, "default theme");
        Equal(LegacySettingsSnapshot.DefaultPetId, loaded.Settings.Pets.StyleId, "default pet");
        True(loaded.Settings.Online.AutoUpdateEnabled, "default auto-update");
        True(files.Exists(path), "validated defaults should be persisted");
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action, string name) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(name + ": expected " + typeof(TException).Name);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }

    private sealed class MemorySettings2FileStore : ISettings2FileStore
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public bool FailWrites { get; set; }
        public int SuccessfulWrites { get; private set; }

        public void Seed(string path, string content) => _files[Normalize(path)] = content;
        public string Get(string path) => _files[Normalize(path)];
        public bool Exists(string path) => _files.ContainsKey(Normalize(path));

        public Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Get(path));
        }

        public Task<IReadOnlyList<string>> ReadLinesAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<string> lines = Get(path)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n');
            return Task.FromResult(lines);
        }

        public Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailWrites) throw new IOException("planned atomic write failure");
            _files[Normalize(path)] = content;
            SuccessfulWrites++;
            return Task.CompletedTask;
        }

        private static string Normalize(string path) => Path.GetFullPath(path);
    }
}
