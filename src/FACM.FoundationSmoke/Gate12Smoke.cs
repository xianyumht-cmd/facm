using System.Text.Json;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.Release;
using FACM.Core.State;

internal static class Gate12Smoke
{
    private static readonly string[] MandatoryExternalEvidenceIds =
    [
        "compat.non-admin-uac-cancel",
        "security.defender-smartscreen",
        "compat.windows-10-1809",
        "compat.windows-10-22h2",
        "compat.windows-11",
        "display.real-mixed-dpi-multimonitor",
        "accessibility.real-machine",
        "migration.settings-3.5.15-to-4.0",
        "update.interrupted-replacement-rollback",
        "release.final-signature-package"
    ];

    public static async Task RunAsync()
    {
        VerifyPerformanceBudgets();
        VerifyGameflowCadence();
        VerifyReleaseEvaluator();
        await VerifyRepositoryEvidenceAsync();
    }

    private static void VerifyPerformanceBudgets()
    {
        AssertBudget(PerformancePolicy.Desktop, "desktop", 4, 2, 2, 2, 20, 15, true, true, true);
        AssertBudget(PerformancePolicy.Client, "league-client", 3, 2, 2, 2, 12, 20, true, true, true);
        AssertBudget(PerformancePolicy.Queueing, "queueing", 2, 1, 1, 1, 4, 30, false, false, false);
        AssertBudget(PerformancePolicy.ChampSelect, "champ-select", 2, 1, 1, 1, 0, 45, false, false, false);
        AssertBudget(PerformancePolicy.InGame, "in-game", 1, 1, 1, 1, 0, 60, false, false, false);
        AssertBudget(PerformancePolicy.Background, "background", 1, 1, 1, 1, 0, 60, false, false, false);

        True(PerformancePolicy.IsNoMoreAggressiveThan(PerformancePolicy.Client, PerformancePolicy.Desktop), "client <= desktop");
        True(PerformancePolicy.IsNoMoreAggressiveThan(PerformancePolicy.Queueing, PerformancePolicy.Client), "queueing <= client");
        True(PerformancePolicy.IsNoMoreAggressiveThan(PerformancePolicy.ChampSelect, PerformancePolicy.Queueing), "champ-select <= queueing");
        True(PerformancePolicy.IsNoMoreAggressiveThan(PerformancePolicy.InGame, PerformancePolicy.ChampSelect), "in-game <= champ-select");
        True(PerformancePolicy.IsNoMoreAggressiveThan(PerformancePolicy.Background, PerformancePolicy.Desktop), "background <= desktop");
    }

    private static void VerifyGameflowCadence()
    {
        Equal(TimeSpan.FromSeconds(2), Cadence(LeagueProductState.ChampSelect), "ChampSelect cadence");
        Equal(TimeSpan.FromSeconds(3), Cadence(LeagueProductState.Matchmaking), "Matchmaking cadence");
        Equal(TimeSpan.FromSeconds(3), Cadence(LeagueProductState.ReadyCheck), "ReadyCheck cadence");
        Equal(TimeSpan.FromSeconds(10), Cadence(LeagueProductState.InGame), "InGame cadence");
        Equal(TimeSpan.FromSeconds(5), Cadence(LeagueProductState.Lobby), "connected idle cadence");
        Equal(TimeSpan.FromSeconds(5), Cadence(LeagueProductState.PostGame), "post-game cadence");
        Equal(TimeSpan.FromSeconds(10), Cadence(LeagueProductState.NotRunning), "not-running cadence");
        Equal(TimeSpan.FromSeconds(10), Cadence(LeagueProductState.Connecting), "connecting cadence");
        Equal(TimeSpan.FromSeconds(10), Cadence(LeagueProductState.ClientError), "client-error cadence");
    }

    private static void VerifyReleaseEvaluator()
    {
        var candidate = new ReleaseCandidateIdentity
        {
            HeadSha = new string('a', 40),
            ArtifactId = 1,
            ArtifactDigest = "sha256:" + new string('b', 64),
            ArtifactSizeBytes = 1
        };
        var ready = new ReleaseEvidenceDocument
        {
            Candidate = candidate,
            Items =
            [
                new ReleaseEvidenceItem
                {
                    Id = "required.pass",
                    Category = "smoke",
                    RequiredForRelease = true,
                    Status = ReleaseEvidenceStatus.Passed,
                    Evidence = "deterministic proof"
                }
            ]
        };
        True(ReleaseEvidenceEvaluator.Evaluate(ready).ReleaseReady, "all required passed => release ready");

        ready.Items.Add(new ReleaseEvidenceItem
        {
            Id = "required.blocked",
            Category = "smoke",
            RequiredForRelease = true,
            Status = ReleaseEvidenceStatus.Blocked,
            Notes = "real-machine evidence missing"
        });
        var blocked = ReleaseEvidenceEvaluator.Evaluate(ready);
        True(!blocked.ReleaseReady, "blocked required evidence => release blocked");
        True(blocked.BlockingIds.Contains("required.blocked", StringComparer.Ordinal), "blocking id surfaced");

        ready.Items[1].Status = ReleaseEvidenceStatus.NotRun;
        True(!ReleaseEvidenceEvaluator.Evaluate(ready).ReleaseReady, "not-run required evidence => release blocked");
        ready.Items[1].Status = ReleaseEvidenceStatus.Failed;
        True(!ReleaseEvidenceEvaluator.Evaluate(ready).ReleaseReady, "failed required evidence => release blocked");
    }

    private static async Task VerifyRepositoryEvidenceAsync()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "evidence", "facm4-release-evidence.json");
        True(File.Exists(path), "release evidence matrix exists");
        var json = await File.ReadAllTextAsync(path);
        var document = JsonSerializer.Deserialize<ReleaseEvidenceDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("Release evidence matrix deserialized to null.");
        var summary = ReleaseEvidenceEvaluator.Evaluate(document);
        var byId = document.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);

        foreach (var id in MandatoryExternalEvidenceIds)
        {
            True(byId.TryGetValue(id, out var item), "mandatory external evidence missing: " + id);
            True(item!.RequiredForRelease, "mandatory external evidence must be required: " + id);
        }

        var required = document.Items.Where(item => item.RequiredForRelease).ToArray();
        Equal(required.All(item => item.Status == ReleaseEvidenceStatus.Passed), summary.ReleaseReady, "derived release-ready state");
        foreach (var item in required)
        {
            var isBlocking = summary.BlockingIds.Contains(item.Id, StringComparer.Ordinal);
            Equal(item.Status != ReleaseEvidenceStatus.Passed, isBlocking, "blocking membership: " + item.Id);
        }
    }

    private static TimeSpan Cadence(LeagueProductState state) => LeagueGameflowCadence.Resolve(
        new LeagueGameflowMapping(
            state == LeagueProductState.NotRunning ? LeagueConnectionState.NotRunning : LeagueConnectionState.Connected,
            state.ToString(),
            state,
            state switch
            {
                LeagueProductState.ChampSelect => LeagueActivityLevel.ChampSelect,
                LeagueProductState.Matchmaking or LeagueProductState.ReadyCheck => LeagueActivityLevel.Queueing,
                LeagueProductState.InGame => LeagueActivityLevel.InGame,
                LeagueProductState.NotRunning => LeagueActivityLevel.None,
                _ => LeagueActivityLevel.Client
            }));

    private static void AssertBudget(
        PerformanceBudget actual,
        string name,
        int network,
        int image,
        int disk,
        int cpu,
        int history,
        int pollSeconds,
        bool prefetch,
        bool maintenance,
        bool visual)
    {
        Equal(name, actual.Name, name + " name");
        Equal(network, actual.NetworkConcurrency, name + " network");
        Equal(image, actual.ImageDecodeConcurrency, name + " image");
        Equal(disk, actual.DiskIoConcurrency, name + " disk");
        Equal(cpu, actual.BackgroundCpuConcurrency, name + " cpu");
        Equal(history, actual.MatchHistoryPrefetchCount, name + " history");
        Equal(TimeSpan.FromSeconds(pollSeconds), actual.NonCriticalPollInterval, name + " poll");
        Equal(prefetch, actual.AllowBackgroundPrefetch, name + " prefetch");
        Equal(maintenance, actual.AllowMaintenanceWork, name + " maintenance");
        Equal(visual, actual.AllowVisualEnhancements, name + " visual");
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
}
