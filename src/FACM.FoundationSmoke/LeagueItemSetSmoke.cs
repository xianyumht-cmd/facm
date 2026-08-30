using System.Text;
using FACM.Core.League;
using FACM.Infrastructure.League;

internal static class LeagueItemSetSmoke
{
    public static async Task RunAsync()
    {
        TestRecipeAndTargetRules();
        await TestPrepareApplyAndOwnershipAsync();
    }

    private static void TestRecipeAndTargetRules()
    {
        Equal(3004, LeagueItemSetService.RestoreRecipe(3042), "item-set restores Muramana recipe");
        Equal(3003, LeagueItemSetService.RestoreRecipe(3040), "item-set restores Seraph recipe");
        Equal(3119, LeagueItemSetService.RestoreRecipe(3121), "item-set restores Winter approach recipe");
        Equal(1234, LeagueItemSetService.RestoreRecipe(1234), "item-set preserves ordinary item");

        var files = new FakeFileSystem();
        var standardRoot = Path.GetFullPath(@"C:\Riot Games\League of Legends");
        files.Directories.Add(standardRoot);
        True(
            LeagueItemSetService.TryResolveTargetDirectory(standardRoot, files, out var standardTarget, out var standardLayout),
            "item-set standard layout");
        Equal("standard-install", standardLayout, "item-set standard layout name");
        True(standardTarget.EndsWith(Path.Combine("Config", "Global", "Recommended"), StringComparison.OrdinalIgnoreCase), "item-set standard target suffix");

        var tencentRoot = Path.GetFullPath(@"C:\Tencent\League of Legends\LeagueClient");
        var tencentGame = Path.GetFullPath(Path.Combine(Directory.GetParent(tencentRoot)!.FullName, "Game"));
        files.Directories.Add(tencentRoot);
        files.Directories.Add(tencentGame);
        True(
            LeagueItemSetService.TryResolveTargetDirectory(tencentRoot, files, out var tencentTarget, out var tencentLayout),
            "item-set Tencent layout");
        Equal("tencent-sibling-game", tencentLayout, "item-set Tencent layout name");
        True(tencentTarget.StartsWith(tencentGame, StringComparison.OrdinalIgnoreCase), "item-set Tencent target stays under Game");

        True(!LeagueItemSetService.TryResolveTargetDirectory("relative\\League", files, out _, out _), "item-set rejects relative install path");
    }

    private static async Task TestPrepareApplyAndOwnershipAsync()
    {
        var live = CreateLive(99, 420);
        var workbench = new FakeWorkbench { Live = live };
        var lcu = new FakeLeagueReadGateway();
        var opgg = new FakeOpggBuildSource();
        var files = new FakeFileSystem();
        var installRoot = Path.GetFullPath(@"C:\Riot Games\League of Legends");
        files.Directories.Add(installRoot);
        lcu.Responses[LeagueItemSetService.InstallDirPath] = Utf8(System.Text.Json.JsonSerializer.Serialize(installRoot));

        var snapshot = new LeagueBuildAdvisorSnapshot(
            LeagueBuildAdvisorState.Ready,
            "ChampSelect",
            420,
            99,
            "Lux",
            "ranked",
            "mid",
            "OP.GG Global",
            "14.17",
            false,
            new LeagueBuildRecommendation("T1", 3, 0.52, 0.11, 0.04, Array.Empty<LeagueBuildAdvisorRow>()),
            "ready",
            DateTimeOffset.Parse("2026-08-28T08:30:00Z"));
        var buildPath = LeagueBuildAdvisorService.BuildPath(99, "ranked", "mid", "14.17");
        opgg.Responses[buildPath] = Utf8("""
            {"data":{
              "starter_items":[{"ids":[1056,3042],"pick_rate":0.5}],
              "boots":[{"ids":[3020]}],
              "prism_items":[{"ids":[223005]}],
              "core_items":[{"ids":[3089],"pick_rate":0.3}],
              "last_items":[{"ids":[3135]}]
            }}
            """);

        using var service = new LeagueItemSetService(workbench, lcu, opgg, files);
        var plan = await service.PrepareAsync(snapshot);
        True(plan is not null, "item-set prepare returns plan");
        True(plan!.Uid.StartsWith(LeagueItemSetService.FilePrefix, StringComparison.Ordinal), "item-set FACM4 uid ownership");
        Equal(5, plan.Blocks.Count, "item-set block count");
        Equal(6, plan.ItemCount, "item-set item count");
        Equal(1, opgg.Paths.Count, "item-set prepare OP.GG request count");

        True(
            LeagueItemSetService.TryResolveTargetDirectory(installRoot, files, out var targetDirectory, out _),
            "item-set target resolves before apply");
        var old4 = Path.Combine(targetDirectory, "facm4-old.json");
        var legacy3 = Path.Combine(targetDirectory, "facm1-legacy.json");
        files.Directories.Add(targetDirectory);
        files.Files[old4] = "{}";
        files.Files[legacy3] = "{}";

        var result = await service.ApplyAsync(plan);
        Equal(LeagueItemSetApplyState.Success, result.State, "item-set apply success");
        Equal(1, result.RemovedOldFiles, "item-set only removes old FACM4 file");
        True(!files.Files.ContainsKey(old4), "item-set removed FACM4 owned old file");
        True(files.Files.ContainsKey(legacy3), "item-set preserves legacy FACM3 file");
        True(files.Files.TryGetValue(Path.Combine(targetDirectory, result.FileName), out var written), "item-set destination exists");
        True(written!.Contains("3004", StringComparison.Ordinal), "item-set writes restored recipe id");
        True(!written.Contains("\"3042\"", StringComparison.Ordinal), "item-set does not write transformed recipe id");
        Equal(1, files.DurableCommitCount, "item-set one durable commit");

        workbench.Live = CreateLive(55, 420);
        var writesBeforeBlocked = files.WriteCount;
        var blocked = await service.ApplyAsync(plan);
        Equal(LeagueItemSetApplyState.Blocked, blocked.State, "item-set champion-change block");
        Equal("champion-changed", blocked.Detail, "item-set champion-change reason");
        Equal(writesBeforeBlocked, files.WriteCount, "item-set blocked apply performs no file write");

        workbench.Live = CreateLive(99, 420);
        var unsafePlan = plan with { Uid = "facm4-../escape" };
        var failed = await service.ApplyAsync(unsafePlan);
        Equal(LeagueItemSetApplyState.Failed, failed.State, "item-set unsafe file name fails");
        Equal("invalid-owned-file-name", failed.Detail, "item-set unsafe file name reason");
    }

    private static LeagueWorkbenchLiveSnapshot CreateLive(int championId, int queueId)
    {
        var player = new LeagueWorkbenchLivePlayer(
            "ally",
            1,
            true,
            "PUUID-1",
            42,
            "FACM",
            "CN1",
            "FACM",
            "MIDDLE",
            "SOLO",
            championId,
            0,
            4,
            14);
        return new LeagueWorkbenchLiveSnapshot(
            LeagueWorkbenchDataState.Ready,
            "ChampSelect",
            303,
            new LeagueWorkbenchQueue(queueId, "Ranked Solo", "CLASSIC"),
            11,
            "Summoner's Rift",
            1,
            "BAN_PICK",
            12000,
            "pick",
            championId,
            false,
            LeagueBenchSwapRoute.Legacy,
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            [player],
            "ready",
            DateTimeOffset.Parse("2026-08-28T08:30:00Z"));
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }

    private sealed class FakeWorkbench : ILeagueWorkbenchDataSource
    {
        public LeagueWorkbenchLiveSnapshot Live { get; set; } = LeagueWorkbenchLiveSnapshot.Unavailable(string.Empty, "unset");

        public Task<LeagueWorkbenchDashboardSnapshot> LoadDashboardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LeagueWorkbenchDashboardSnapshot.Unavailable("not-used"));

        public Task<LeagueWorkbenchPlayerSnapshot> LoadCurrentPlayerAsync(
            int startIndex = 0,
            int count = 10,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LeagueWorkbenchPlayerSnapshot.Unavailable("not-used"));

        public Task<LeagueWorkbenchLiveSnapshot> LoadLiveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Live);
        }
    }

    private sealed class FakeLeagueReadGateway : ILeagueReadGateway
    {
        public Dictionary<string, byte[]> Responses { get; } = new(StringComparer.Ordinal);

        public Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Responses.TryGetValue(resourceKey, out var value) ? value : null);
        }
    }

    private sealed class FakeOpggBuildSource : IOpggBuildSource
    {
        public Dictionary<string, byte[]> Responses { get; } = new(StringComparer.Ordinal);
        public List<string> Paths { get; } = [];

        public Task<byte[]?> TryGetBytesAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(path);
            return Task.FromResult(Responses.TryGetValue(path, out var value) ? value : null);
        }
    }

    private sealed class FakeFileSystem : ILeagueItemSetFileSystem
    {
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int WriteCount { get; private set; }
        public int DurableCommitCount { get; private set; }

        public bool DirectoryExists(string path) => Directories.Contains(Path.GetFullPath(path));
        public bool FileExists(string path) => Files.ContainsKey(Path.GetFullPath(path));

        public void CreateDirectory(string path) => Directories.Add(Path.GetFullPath(path));

        public void WriteAllText(string path, string content)
        {
            WriteCount++;
            Files[Path.GetFullPath(path)] = content;
        }

        public string ReadAllText(string path) => Files[Path.GetFullPath(path)];

        public string[] GetFiles(string directory, string pattern)
        {
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var prefix = pattern.EndsWith("*.json", StringComparison.OrdinalIgnoreCase)
                ? pattern[..^6]
                : pattern;
            return Files.Keys
                .Where(path => path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                .Where(path => string.Equals(Path.GetDirectoryName(path), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        public void MoveFile(string source, string destination)
        {
            source = Path.GetFullPath(source);
            destination = Path.GetFullPath(destination);
            Files[destination] = Files[source];
            Files.Remove(source);
            if (destination.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) DurableCommitCount++;
        }

        public void ReplaceFile(string source, string destination, string backup)
        {
            source = Path.GetFullPath(source);
            destination = Path.GetFullPath(destination);
            backup = Path.GetFullPath(backup);
            Files[backup] = Files[destination];
            Files[destination] = Files[source];
            Files.Remove(source);
            DurableCommitCount++;
        }

        public void DeleteFile(string path) => Files.Remove(Path.GetFullPath(path));
    }
}
