using System.Diagnostics;
using System.Text.Json;
using FACM.Core.League;
using FACM.Platform.Windows.League;

internal static class LeagueSessionDiscoverySmoke
{
    public static async Task RunAsync()
    {
        await VerifyLockfilePreferredAsync();
        await VerifyCommandLineFallbackAsync();
        await VerifySingleFlightAsync();
        await VerifyNegativeCacheAsync();
        await VerifyAsyncBoundaryAsync();
        await VerifyDiagnosticsRedactionAsync();
    }

    public static async Task RunLiveAsync()
    {
        var sessionEvents = new List<LeagueSessionDiscoveryDiagnostic>();
        using var source = new WindowsLeagueTransportSessionSource(
            diagnosticReporter: sessionEvents.Add,
            discoveryTimeout: TimeSpan.FromSeconds(3));
        var session = await source.GetSessionAsync();
        var finish = sessionEvents.LastOrDefault(item => item.Event == "discovery-finish");
        Console.WriteLine("LIVE_DISCOVERY " +
                          (session is null ? "session=null" :
                           $"source={session.Descriptor.Source};pid={session.Descriptor.ProcessId};port={session.Descriptor.Port}") +
                          (finish is null ? string.Empty :
                           $";outcome={finish.Outcome};durationMs={finish.DurationMs}"));

        if (session is null) return;

        var httpEvents = new List<LeagueHttpDiagnostic>();
        using var gateway = new FACM.Infrastructure.League.LeagueHttpGateway(
            source,
            diagnosticReporter: httpEvents.Add);
        var body = await gateway.TryGetBytesAsync("/lol-gameflow/v1/gameflow-phase", CancellationToken.None);
        var completed = httpEvents.LastOrDefault(item => item.Event == "completed");
        Console.WriteLine("LIVE_HTTP " +
                          (completed is null ? "completed=missing" :
                           $"outcome={completed.Outcome};status={completed.StatusCode};durationMs={completed.DurationMs}") +
                          (body is null ? string.Empty : ";bodyBytes=" + body.Length));
    }

    private static Task VerifyLockfilePreferredAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            var executable = Path.Combine(root, "LeagueClientUx.exe");
            File.WriteAllText(Path.Combine(root, "lockfile"), "LeagueClientUx:101:32123:lock-secret:https");
            var provider = new SnapshotProvider(
                new LeagueProcessSnapshot(101, executable, "LeagueClientUx.exe --app-port=65061 --remoting-auth-token=fallback"));
            var discovery = new ProcessLockfileLeagueSessionDiscovery(provider);
            var result = discovery.Discover();

            Equal("lockfile", result.Source, "valid lockfile must win over command line");
            Equal("lockfile-success", result.Outcome, "valid lockfile outcome");
            Equal(32123, result.Port, "lockfile port");
            Equal("lockfile", result.Session!.Descriptor.Source, "lockfile session source");
            return Task.CompletedTask;
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static Task VerifyCommandLineFallbackAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            var executable = Path.Combine(root, "LeagueClientUx.exe");
            File.WriteAllText(Path.Combine(root, "lockfile"), string.Empty);
            var token = "fallback-secret:with-colon";
            var commandLine = "LeagueClientUx.exe --app-port=32124 --remoting-auth-token=\"" + token +
                              "\" --app-pid=202 --rso_platform_id=HN1 --region=HN";
            var provider = new SnapshotProvider(new LeagueProcessSnapshot(202, executable, commandLine));
            var result = new ProcessLockfileLeagueSessionDiscovery(provider).Discover();

            Equal("process-command-line", result.Source, "empty lockfile command-line source");
            Equal("process-fallback-success", result.Outcome, "empty lockfile fallback outcome");
            Equal(202, result.ProcessId, "fallback process id");
            Equal(32124, result.Port, "fallback port");
            Equal("HN1", result.Session!.Descriptor.PlatformId, "fallback platform");
            return Task.CompletedTask;
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static async Task VerifySingleFlightAsync()
    {
        var discovery = new DelayedDiscovery(CreateSession(303, 32125), TimeSpan.FromMilliseconds(80));
        using var source = new WindowsLeagueTransportSessionSource(
            discovery,
            retryInterval: TimeSpan.FromMilliseconds(500),
            discoveryTimeout: TimeSpan.FromSeconds(2));

        var sessions = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => source.GetSessionAsync()));
        True(sessions.All(session => session is not null), "four concurrent sessions resolve");
        Equal(1, discovery.Calls, "four concurrent sessions use one real discovery");
        True(sessions.All(session => ReferenceEquals(session, sessions[0])), "single-flight shares result");
    }

    private static async Task VerifyNegativeCacheAsync()
    {
        var discovery = new DelayedDiscovery(null, TimeSpan.FromMilliseconds(80));
        using var source = new WindowsLeagueTransportSessionSource(
            discovery,
            retryInterval: TimeSpan.FromMilliseconds(500),
            discoveryTimeout: TimeSpan.FromSeconds(2));
        using var gateway = new FACM.Infrastructure.League.LeagueHttpGateway(source);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            gateway.TryGetBytesAsync("/lol-gameflow/v1/session", CancellationToken.None)));
        True(results.All(result => result is null), "no-session gateway results");
        Equal(1, discovery.Calls, "no-session gateway batch uses one discovery");
        True(await gateway.TryGetBytesAsync("/lol-gameflow/v1/session", CancellationToken.None) is null,
            "negative cache still returns no-session");
        Equal(1, discovery.Calls, "negative cache prevents immediate rescan");
    }

    private static async Task VerifyAsyncBoundaryAsync()
    {
        var discovery = new DelayedDiscovery(null, TimeSpan.FromMilliseconds(800));
        using var source = new WindowsLeagueTransportSessionSource(
            discovery,
            retryInterval: TimeSpan.FromMilliseconds(500),
            discoveryTimeout: TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        var pending = source.GetSessionAsync();
        var callerElapsed = stopwatch.Elapsed;
        True(callerElapsed < TimeSpan.FromMilliseconds(250), "discovery does not synchronously occupy caller");
        True(await pending is null, "delayed discovery result");
    }

    private static Task VerifyDiagnosticsRedactionAsync()
    {
        var events = new List<LeagueSessionDiscoveryDiagnostic>();
        var secret = "telemetry-secret";
        var commandLine = "LeagueClientUx.exe --app-port=32126 --remoting-auth-token=\"" + secret +
                          "\" --app-pid=404";
        var root = CreateTempDirectory();
        var provider = new SnapshotProvider(new LeagueProcessSnapshot(
            404,
            Path.Combine(root, "LeagueClientUx.exe"),
            commandLine));
        try
        {
            var discovery = new ProcessLockfileLeagueSessionDiscovery(provider);
            using var source = new WindowsLeagueTransportSessionSource(
                discovery,
                diagnosticReporter: events.Add);
            _ = source.GetSession();

            var telemetry = JsonSerializer.Serialize(events);
            True(!telemetry.Contains(secret, StringComparison.Ordinal), "token absent from discovery telemetry");
            True(events.Any(item => item.Event == "discovery-finish" &&
                                   item.Source == "process-command-line" &&
                                   item.Outcome == "process-fallback-success"),
                "fallback finish diagnostic");
            True(events.Where(item => item.Event == "discovery-finish").All(item => item.ProcessId == 404 && item.Port == 32126),
                "diagnostics retain only process and port identity");
            return Task.CompletedTask;
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static LeagueTransportSession CreateSession(int processId, int port) => new(
        new LeagueSessionDescriptor(processId, port, "https", "smoke", "HN1", "HN"),
        "smoke-secret");

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "facm4-league-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
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

    private sealed class SnapshotProvider(params LeagueProcessSnapshot[] snapshots) : ILeagueProcessSnapshotProvider
    {
        public IReadOnlyList<LeagueProcessSnapshot> GetProcesses(string processName) =>
            string.Equals(processName, "LeagueClientUx", StringComparison.OrdinalIgnoreCase)
                ? snapshots
                : Array.Empty<LeagueProcessSnapshot>();
    }

    private sealed class DelayedDiscovery(LeagueTransportSession? session, TimeSpan delay) : ILeagueSessionDiscovery
    {
        public int Calls { get; private set; }

        public LeagueTransportSession? TryDiscover()
        {
            Calls++;
            Thread.Sleep(delay);
            return session;
        }
    }
}
