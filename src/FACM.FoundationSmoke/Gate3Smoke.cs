using System.Net;
using System.Text;
using FACM.Core.League;
using FACM.Core.Online;
using FACM.Core.Runtime;
using FACM.Infrastructure.League;
using FACM.Infrastructure.Online;

internal static class Gate3Smoke
{
    public static async Task RunAsync()
    {
        TestLeagueSessionParsers();
        TestRuntimePathLayout();
        TestUpdateDecisionCompatibility();
        await TestLeagueHttpGatewayAsync();
        await TestManifestSourceAsync();
    }

    private static void TestLeagueSessionParsers()
    {
        True(LeagueTransportSessionParser.TryParseLockfile(
            "LeagueClientUx:1234:54321:secret-token:https",
            out var lockfile),
            "lockfile parser");
        Equal(1234, lockfile!.Descriptor.ProcessId, "lockfile pid");
        Equal(54321, lockfile.Descriptor.Port, "lockfile port");
        Equal("lockfile", lockfile.Descriptor.Source, "lockfile source");
        Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:secret-token")), lockfile.CreateBasicAuthorizationParameter(), "basic auth");
        True(!lockfile.ToString().Contains("secret-token", StringComparison.Ordinal), "session diagnostics must hide token");

        var commandLine = "LeagueClientUx.exe --app-port=32123 --remoting-auth-token=\"abc:def\" --app-pid=88 --rso_platform_id=HN1 --region=HN";
        True(LeagueTransportSessionParser.TryParseCommandLine(commandLine, out var command), "command-line parser");
        Equal(88, command!.Descriptor.ProcessId, "command pid");
        Equal(32123, command.Descriptor.Port, "command port");
        Equal("HN1", command.Descriptor.PlatformId, "command platform");
        Equal("HN", command.Descriptor.Region, "command region");
        True(!LeagueTransportSessionParser.TryParseLockfile("broken", out _), "broken lockfile rejection");
    }

    private static void TestRuntimePathLayout()
    {
        var distribution = Path.Combine(Path.GetTempPath(), "facm4-dist", "FACM.App.exe");
        var selfExtract = Path.Combine(Path.GetTempPath(), ".net", "FACM.App", "random");
        var layout = RuntimePathLayout.From(new FakeExecutablePaths(distribution, selfExtract));
        Equal(Path.GetFullPath(Path.GetDirectoryName(distribution)!), layout.DistributionDirectory, "distribution directory");
        Equal(Path.Combine(layout.DistributionDirectory, "settings.ini"), layout.SettingsPath, "settings distribution path");
        True(!layout.SettingsPath.StartsWith(Path.GetFullPath(selfExtract), StringComparison.OrdinalIgnoreCase), "settings must ignore self-extract base directory");
        Equal(Path.Combine(layout.DistributionDirectory, "runtime", "updates"), layout.UpdatesDirectory, "updates directory");
    }

    private static void TestUpdateDecisionCompatibility()
    {
        var forceFlag = new UpdateManifestSnapshot(
            true, "v4.0.0", "3.0.0", true,
            "https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0/FACM.exe",
            new string('A', 64), string.Empty, string.Empty);
        var forced = UpdateDecisionService.Evaluate(new Version(3, 5, 15), forceFlag);
        True(forced.UpdateAvailable && forced.ForceUpdateRequired, "force_update flag must force an available update even above minimum");

        var minimum = forceFlag with { ForceUpdate = false, MinimumVersion = "3.6.0" };
        var belowMinimum = UpdateDecisionService.Evaluate(new Version(3, 5, 15), minimum);
        True(belowMinimum.ForceUpdateRequired, "below minimum must force available update");
        Equal(new Version(4, 0, 0), UpdateDecisionService.ParseVersion("V4.0.0"), "leading v version parser");
    }

    private static async Task TestLeagueHttpGatewayAsync()
    {
        var session = CreateSession("gateway-secret");
        var source = new FakeSessionSource(session);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"))
        });

        using (var gateway = new LeagueHttpGateway(source, handlerFactory: () => handler))
        {
            var body = await gateway.TryGetBytesAsync("/lol-gameflow/v1/gameflow-phase", CancellationToken.None);
            Equal("ok", Encoding.UTF8.GetString(body!), "LCU read body");
            Equal("/lol-gameflow/v1/gameflow-phase", handler.LastPath, "LCU read relative path");
            Equal("Basic " + session.CreateBasicAuthorizationParameter(), handler.LastAuthorization, "LCU auth header");

            var selection = new LeagueWriteCommand(LeagueWriteCapability.ApplyMySelection, null, "{}");
            var result = await gateway.ExecuteAsync(selection, CancellationToken.None);
            True(result!.IsSuccessStatusCode, "LCU write success");
            Equal("PATCH", handler.LastMethod, "LCU write method");
            Equal("/lol-champ-select/v1/session/my-selection", handler.LastPath, "LCU writer capability target");

            await ThrowsAsync<ArgumentException>(
                () => gateway.ExecuteAsync(new LeagueWriteCommand(LeagueWriteCapability.UpdatePerkPage, 0, "{}"), CancellationToken.None),
                "invalid perk id must fail before transport");
        }

        var unauthorizedSource = new FakeSessionSource(CreateSession("unauthorized"));
        var unauthorizedHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using (var gateway = new LeagueHttpGateway(unauthorizedSource, handlerFactory: () => unauthorizedHandler))
        {
            var result = await gateway.TryGetBytesAsync("/lol-gameflow/v1/gameflow-phase", CancellationToken.None);
            True(result is null, "401 read should return unavailable");
            Equal(1, unauthorizedSource.Invalidations, "401 must invalidate shared session source");
        }

        await ThrowsAsync<ArgumentException>(async () =>
        {
            using var gateway = new LeagueHttpGateway(new FakeSessionSource(CreateSession("x")), handlerFactory: () => new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
            _ = await gateway.TryGetBytesAsync("https://example.invalid/not-lcu", CancellationToken.None);
        }, "absolute read URL must be rejected");
    }

    private static async Task TestManifestSourceAsync()
    {
        var validJson = "{\"enabled\":true,\"version\":\"4.0.0\",\"minimum_version\":\"3.0.0\",\"force_update\":false,\"download_url\":\"https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0/FACM.exe\",\"sha256\":\"" + new string('A', 64) + "\",\"release_notes\":\"test\",\"published_at\":\"2026-08-27\"}";
        using (var source = new HttpUpdateManifestSource(new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(validJson))
        })))
        {
            var manifest = await source.GetAsync(CancellationToken.None);
            Equal("4.0.0", manifest!.Version, "manifest version");
            True(HttpUpdateManifestSource.IsValidManifest(manifest), "valid manifest policy");
        }

        var invalid = new UpdateManifestSnapshot(true, "4.0.0", "3.0.0", false,
            "https://example.invalid/facm.exe", new string('A', 64), string.Empty, string.Empty);
        True(!HttpUpdateManifestSource.IsValidManifest(invalid), "non-GitHub release URL rejection");
        True(!HttpUpdateManifestSource.IsValidManifest(invalid with
        {
            DownloadUrl = "https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0/FACM.exe",
            Sha256 = "ABC"
        }), "short hash rejection");

        using (var oversized = new HttpUpdateManifestSource(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1025]) }),
            maxMetadataBytes: 1024))
        {
            await ThrowsAsync<InvalidDataException>(() => oversized.GetAsync(CancellationToken.None), "metadata size limit");
        }

        using (var timeout = new HttpUpdateManifestSource(new DelayHandler(), timeout: TimeSpan.FromMilliseconds(25)))
        {
            await ThrowsAsync<OperationCanceledException>(() => timeout.GetAsync(CancellationToken.None), "metadata timeout cancellation");
        }
    }

    private static LeagueTransportSession CreateSession(string password) => new(
        new LeagueSessionDescriptor(77, 29999, "https", "smoke", "HN1", "HN"), password);

    private static async Task ThrowsAsync<T>(Func<Task> action, string name) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException(name + ": expected " + typeof(T).Name);
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

    private sealed class FakeExecutablePaths(string executablePath, string baseDirectory) : IExecutablePathProvider
    {
        public string ExecutablePath { get; } = executablePath;
        public string BaseDirectory { get; } = baseDirectory;
    }

    private sealed class FakeSessionSource(LeagueTransportSession session) : ILeagueTransportSessionSource
    {
        private LeagueTransportSession? _session = session;
        public int Invalidations { get; private set; }
        public LeagueTransportSession? GetSession(bool forceRefresh = false) => _session;
        public void Invalidate(LeagueTransportSession expected)
        {
            if (_session is null || !_session.Matches(expected)) return;
            Invalidations++;
            _session = null;
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public string LastMethod { get; private set; } = string.Empty;
        public string LastPath { get; private set; } = string.Empty;
        public string LastAuthorization { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method.Method;
            LastPath = request.RequestUri?.PathAndQuery ?? string.Empty;
            LastAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class StaticHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class DelayHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
