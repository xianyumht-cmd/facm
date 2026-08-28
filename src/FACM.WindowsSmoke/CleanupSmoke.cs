using FACM.Core.Cleanup;
using FACM.Platform.Windows.Cleanup;

internal static class CleanupSmoke
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-cleanup-smoke-" + Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(root, "英雄联盟");
        var outside = Path.Combine(root, "outside.txt");
        Directory.CreateDirectory(gameRoot);
        try
        {
            var profile = CreateProfile();
            SeedGameTree(gameRoot);
            await File.WriteAllTextAsync(outside, "must-survive");

            var environment = new FakeCleanupEnvironment(gameRoot);
            var engine = new WindowsCleanupEngine(environment, profile);
            var service = new CleanupApplicationService(engine, engine);
            var plan = await service.PreviewAsync(gameRoot);

            True(plan.DeletableTargets.Count >= 4, "cleanup plan should expose configured targets");
            True(plan.Targets.All(target =>
                    !target.Path.Contains(Path.Combine("Game", "DATA"), StringComparison.OrdinalIgnoreCase)),
                "preserved DATA directory must never enter cleanup plan");
            True(plan.Targets.Any(target => target.Rule == CleanupRuleKind.LogFile &&
                                           target.Path.EndsWith("delete.log", StringComparison.OrdinalIgnoreCase)),
                "top-level configured log must enter cleanup plan");
            True(plan.Targets.All(target => !target.Path.EndsWith("keep.txt", StringComparison.OrdinalIgnoreCase)),
                "unconfigured files must not enter cleanup plan");

            try
            {
                await service.ExecuteConfirmedAsync(plan, confirmed: false);
                throw new InvalidOperationException("unconfirmed cleanup unexpectedly executed");
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("explicit confirmation", StringComparison.OrdinalIgnoreCase))
            {
            }
            True(File.Exists(Path.Combine(gameRoot, "Game", "delete.tmp")),
                "unconfirmed cleanup must not delete data");

            environment.RunningProcesses = ["LeagueClient"];
            try
            {
                await service.ExecuteConfirmedAsync(plan, confirmed: true);
                throw new InvalidOperationException("cleanup unexpectedly ran while League process was active");
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("仍在运行", StringComparison.Ordinal))
            {
            }
            True(File.Exists(Path.Combine(gameRoot, "Game", "delete.tmp")),
                "process guard must reject before deletion");
            environment.RunningProcesses = Array.Empty<string>();

            var malicious = new CleanupPlan(
                gameRoot,
                [new CleanupTarget(
                    outside,
                    "主体目录清理",
                    CleanupRuleKind.ContainerChild,
                    CleanupTargetKind.File,
                    1,
                    1,
                    0,
                    false,
                    "malicious test")]);
            var maliciousResult = await service.ExecuteConfirmedAsync(malicious, confirmed: true);
            True(maliciousResult.Failures.Count == 1, "execution-time allowlist must reject forged target");
            True(File.Exists(outside), "execution-time revalidation must leave unrelated file untouched");

            var result = await service.ExecuteConfirmedAsync(plan, confirmed: true);
            True(result.Failures.Count == 0, "valid configured cleanup should complete without failures");
            True(Directory.Exists(Path.Combine(gameRoot, "Game", "DATA")), "preserved DATA directory must survive execution");
            True(File.Exists(Path.Combine(gameRoot, "Game", "DATA", "keep.dat")), "preserved DATA file must survive execution");
            True(!File.Exists(Path.Combine(gameRoot, "Game", "delete.tmp")), "configured container child file should be deleted");
            True(!Directory.Exists(Path.Combine(gameRoot, "Game", "DeleteFolder")), "configured container child directory should be deleted");
            True(!Directory.Exists(Path.Combine(gameRoot, "Launcher", "AntiCheatExpert")), "configured extra directory should be deleted");
            True(!Directory.Exists(Path.Combine(gameRoot, "LeagueClient", "AntiCheatExpert")), "second configured extra directory should be deleted");
            True(!File.Exists(Path.Combine(gameRoot, "LeagueClient", "delete.log")), "configured top-level log should be deleted");
            True(File.Exists(Path.Combine(gameRoot, "LeagueClient", "keep.txt")), "non-log sibling must survive cleanup");
            True(File.Exists(outside), "unrelated file must survive valid cleanup");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static CleanupProfileSnapshot CreateProfile()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new CleanupProfileSnapshot(
            "FACM-Smoke-PF-" + suffix,
            "FACM-Smoke-PD-" + suffix,
            "Game",
            "Game",
            "DATA",
            [@"Launcher\AntiCheatExpert", @"LeagueClient\AntiCheatExpert"],
            "LeagueClient",
            "*.log",
            "FACM-Smoke-Registry-" + suffix,
            ["FACM-Smoke-Process-" + suffix],
            5);
    }

    private static void SeedGameTree(string gameRoot)
    {
        Directory.CreateDirectory(Path.Combine(gameRoot, "Game", "DATA"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "Game", "DeleteFolder"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "Launcher", "AntiCheatExpert"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "LeagueClient", "AntiCheatExpert"));
        File.WriteAllText(Path.Combine(gameRoot, "Game", "DATA", "keep.dat"), "keep");
        File.WriteAllText(Path.Combine(gameRoot, "Game", "delete.tmp"), "delete");
        File.WriteAllText(Path.Combine(gameRoot, "Game", "DeleteFolder", "nested.bin"), "delete");
        File.WriteAllText(Path.Combine(gameRoot, "Launcher", "AntiCheatExpert", "a.bin"), "delete");
        File.WriteAllText(Path.Combine(gameRoot, "LeagueClient", "AntiCheatExpert", "b.bin"), "delete");
        File.WriteAllText(Path.Combine(gameRoot, "LeagueClient", "delete.log"), "delete");
        File.WriteAllText(Path.Combine(gameRoot, "LeagueClient", "keep.txt"), "keep");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }

    private sealed class FakeCleanupEnvironment(string gameRoot) : ICleanupEnvironment
    {
        public IReadOnlyList<string> RunningProcesses { get; set; } = Array.Empty<string>();
        public bool IsAdministrator => true;
        public Task<string?> FindGameRootAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(gameRoot);
        public Task<string?> ResolveGameRootAsync(string selectedOrCandidatePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(gameRoot);
        public bool IsValidGameRoot(string path) =>
            string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(Path.Combine(gameRoot, "Game"));
        public IReadOnlyList<string> GetRunningRelatedProcesses() => RunningProcesses;
        public bool RestartElevatedForCleanup() => false;
    }
}
