using FACM.Core.Observability;
using FACM.Core.State;
using FACM.Infrastructure.Observability;

internal static class Gate5Smoke
{
    public static async Task RunAsync()
    {
        TestStateTransitionsAndSubscribers();
        TestConcurrentSnapshots();
        TestDiagnosticContractAndRedaction();
        await TestBoundedPhysicalSinkAsync();
    }

    private static void TestStateTransitionsAndSubscribers()
    {
        var tick = new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);
        var store = new ProductStateStore(() => tick = tick.AddMilliseconds(1));
        Equal(0L, store.Current.Revision, "initial revision");
        Equal(ApplicationProductState.Starting, store.Current.Application, "initial application state");
        Equal(LeagueProductState.NotRunning, store.Current.League, "initial league state");

        var events = new List<ProductStateChangedEventArgs>();
        store.Changed += (_, args) =>
        {
            // Reading the store from a subscriber proves callbacks run outside the writer lock.
            _ = store.Current;
            events.Add(args);
        };

        store.SetApplication(ApplicationProductState.Starting, "duplicate");
        Equal(0L, store.Current.Revision, "duplicate state must not increment revision");
        Equal(0, events.Count, "duplicate state must not publish event");

        store.SetApplication(ApplicationProductState.Ready, "shell-ready");
        Equal(1L, store.Current.Revision, "application revision");
        Equal("shell-ready", events.Single().Reason, "state change reason");
        Equal(ApplicationProductState.Starting, events[0].Previous.Application, "previous application state");
        Equal(ApplicationProductState.Ready, events[0].Current.Application, "current application state");

        var leagueStates = new[]
        {
            LeagueProductState.Connecting,
            LeagueProductState.Lobby,
            LeagueProductState.Matchmaking,
            LeagueProductState.ReadyCheck,
            LeagueProductState.ChampSelect,
            LeagueProductState.InGame,
            LeagueProductState.PostGame,
            LeagueProductState.ClientError,
            LeagueProductState.NotRunning
        };
        foreach (var state in leagueStates) store.SetLeague(state, state.ToString());
        Equal(1L + leagueStates.Length, store.Current.Revision, "league vocabulary revision count");
        Equal(LeagueProductState.NotRunning, store.Current.League, "league final state");
    }

    private static void TestConcurrentSnapshots()
    {
        var store = new ProductStateStore();
        var notifications = 0;
        store.Changed += (_, _) => Interlocked.Increment(ref notifications);

        Parallel.For(0, 100, index =>
        {
            store.SetEnvironment(new ProductEnvironmentState("dist-" + index, null, null), "parallel");
        });

        Equal(100L, store.Current.Revision, "parallel revision count");
        Equal(100, notifications, "parallel notification count");
        True(store.Current.Environment.DistributionDirectory.StartsWith("dist-", StringComparison.Ordinal), "parallel final snapshot");
    }

    private static void TestDiagnosticContractAndRedaction()
    {
        var diagnostic = DiagnosticEventFactory.Create(
            "league.refresh",
            "League.Runtime",
            42,
            DiagnosticResult.Failure,
            "token=abc; authorization=Bearer super secret|done",
            LeagueProductState.ClientError,
            "4.0.0",
            new Dictionary<string, string>
            {
                ["authToken"] = "very-secret",
                ["path"] = "C:\\safe",
                ["note"] = "password=s3cr3t; ok"
            },
            new DateTimeOffset(2026, 8, 27, 8, 30, 0, TimeSpan.Zero));

        Equal("league.refresh", diagnostic.ActionId, "diagnostic action id");
        Equal("League.Runtime", diagnostic.Module, "diagnostic module");
        Equal(42L, diagnostic.DurationMs, "diagnostic duration");
        Equal(DiagnosticResult.Failure, diagnostic.Result, "diagnostic result");
        Equal(LeagueProductState.ClientError, diagnostic.LeagueState, "diagnostic league state");
        Equal("4.0.0", diagnostic.ClientVersion, "diagnostic client version");
        Equal("[redacted]", diagnostic.Data["authToken"], "sensitive diagnostic key");
        Equal("C:\\safe", diagnostic.Data["path"], "safe diagnostic data");

        var serializedView = diagnostic.Reason + "|" + string.Join('|', diagnostic.Data.Values);
        foreach (var secret in new[] { "abc", "super secret", "very-secret", "s3cr3t" })
            True(!serializedView.Contains(secret, StringComparison.Ordinal), "diagnostic secret redaction: " + secret);
    }

    private static async Task TestBoundedPhysicalSinkAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-observability-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "facm4-events.jsonl");
        try
        {
            using var sink = new BoundedJsonLinesDiagnosticSink(path, 4096);
            var writes = Enumerable.Range(0, 24).Select(index => sink.WriteAsync(
                DiagnosticEventFactory.Create(
                    "smoke." + index,
                    "Gate5Smoke",
                    index,
                    DiagnosticResult.Success,
                    "token=top-secret; " + new string('x', 180),
                    LeagueProductState.Lobby,
                    "4.0.0",
                    new Dictionary<string, string>
                    {
                        ["authorization"] = "Bearer never-write-this",
                        ["sequence"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    })));
            await Task.WhenAll(writes);

            var rotated = path + ".1";
            True(File.Exists(path), "bounded sink current file");
            True(File.Exists(rotated), "bounded sink rotated file");
            True(new FileInfo(path).Length <= 4096, "bounded sink current size");
            True(new FileInfo(rotated).Length <= 4096, "bounded sink rotated size");

            var text = await File.ReadAllTextAsync(path) + await File.ReadAllTextAsync(rotated);
            foreach (var field in new[]
            {
                "\"timestampUtc\"", "\"actionId\"", "\"module\"", "\"durationMs\"",
                "\"result\"", "\"reason\"", "\"leagueState\"", "\"clientVersion\""
            })
                True(text.Contains(field, StringComparison.Ordinal), "structured field " + field);

            True(!text.Contains("top-secret", StringComparison.Ordinal), "free-text secret must not reach disk");
            True(!text.Contains("never-write-this", StringComparison.Ordinal), "sensitive data must not reach disk");
            True(text.Contains("[redacted]", StringComparison.Ordinal), "redaction marker on disk");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
