using System.Net;
using System.Text;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.State;
using FACM.Infrastructure.League;

internal static class LeagueReliabilitySmoke
{
    public static async Task RunAsync()
    {
        TestExpected404Classification();
        await TestGatewayClassificationAsync();
        await TestGameflowObserverBoundaryAsync();
        await TestAutoAcceptFailureBoundaryAsync();
    }

    private static void TestExpected404Classification()
    {
        Equal(
            League404Classification.ExpectedUnavailable,
            LeagueEndpointAvailabilityPolicy.Classify404(
                "/lol-matchmaking/v1/ready-check",
                "None",
                LeagueConnectionState.Connected),
            "ready-check 404 outside ReadyCheck is expected unavailable");
        Equal(
            League404Classification.UnexpectedFailure,
            LeagueEndpointAvailabilityPolicy.Classify404(
                "/lol-matchmaking/v1/ready-check",
                "ReadyCheck",
                LeagueConnectionState.Connected),
            "ready-check 404 during ReadyCheck is unexpected");
        Equal(
            League404Classification.UnexpectedFailure,
            LeagueEndpointAvailabilityPolicy.Classify404(
                "/unknown/endpoint",
                "None",
                LeagueConnectionState.Connected),
            "unknown 404 remains failure");
    }

    private static async Task TestGatewayClassificationAsync()
    {
        var events = new List<LeagueHttpDiagnostic>();
        var observed = new LeagueGameflowSnapshot(
            DateTimeOffset.UtcNow,
            LeagueConnectionState.Connected,
            "None",
            LeagueProductState.Lobby,
            LeagueActivityLevel.Client);
        using var gateway = new LeagueHttpGateway(
            new FakeSessionSource(),
            handlerFactory: () => new ResponseHandler(
                (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))),
            diagnosticReporter: events.Add,
            gameflowProvider: () => observed);

        True(await gateway.TryGetBytesAsync("/lol-matchmaking/v1/ready-check", CancellationToken.None) is null,
            "expected unavailable gateway response is null");
        var completed = events.Single(item => item.Event == "completed");
        Equal("expected-unavailable", completed.Outcome, "gateway expected unavailable outcome");
        Equal(nameof(League404Classification.ExpectedUnavailable), completed.NotFoundClassification,
            "gateway expected unavailable classification");
        Equal("None", completed.GameflowPhase, "gateway records raw gameflow phase");
    }

    private static async Task TestGameflowObserverBoundaryAsync()
    {
        var monitor = new LeagueGameflowMonitor(
            new StaticReadGateway(Encoding.UTF8.GetBytes("\"None\"")),
            new FakeSessionAccessor(),
            new ProductStateStore(),
            new PerformanceBudgetProvider());
        var observed = 0;
        monitor.Changed += (_, _) => throw new InvalidOperationException("synthetic UI observer failure");
        monitor.Observed += (_, _) => observed++;
        using (monitor)
        {
            var snapshot = await monitor.RefreshOnceAsync();
            Equal("None", snapshot.Phase, "gameflow snapshot remains usable after observer failure");
            Equal(1, observed, "healthy observer still runs after failed observer");
        }
    }

    private static async Task TestAutoAcceptFailureBoundaryAsync()
    {
        var diagnostics = new List<LeagueAutomationDiagnostic>();
        var gameflow = new FakeObservationSource();
        var read = new StaticReadGateway(Encoding.UTF8.GetBytes(
            "{ \"readyCheck\": { \"state\": \"InProgress\", \"playerResponse\": \"None\" } }"));
        var write = new ThrowingWriteGateway();
        using var service = new LeagueMatchmakingAutomationService(read, write, gameflow, diagnostics.Add);
        service.Configure(autoSearch: false, autoAccept: true);

        await service.EvaluateObservedSafelyForSmokeTestAsync(new LeagueGameflowSnapshot(
            DateTimeOffset.UtcNow,
            LeagueConnectionState.Connected,
            "ReadyCheck",
            LeagueProductState.ReadyCheck,
            LeagueActivityLevel.Queueing));

        True(diagnostics.Any(item => item.Event == "evaluation-complete" && item.Outcome == "failure"),
            "Auto Accept failure is contained and reported");
        True(diagnostics.Any(item => item.Event == "evaluation-complete" && item.ExceptionType.Contains(nameof(InvalidOperationException), StringComparison.Ordinal)),
            "Auto Accept failure includes exception type");
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

    private sealed class FakeSessionSource : ILeagueTransportSessionSource
    {
        private readonly LeagueTransportSession _session = new(
            new LeagueSessionDescriptor(77, 29999, "https", "smoke", "HN1", "HN"),
            "smoke-secret");

        public LeagueTransportSession? GetSession(bool forceRefresh = false) => _session;
        public void Invalidate(LeagueTransportSession expected) { }
    }

    private sealed class ResponseHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }

    private sealed class StaticReadGateway(byte[] response) : ILeagueReadGateway
    {
        public Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<byte[]?>(response);
        }
    }

    private sealed class ThrowingWriteGateway : ILeagueWriteGateway
    {
        public Task<LeagueWriteResult?> ExecuteAsync(LeagueWriteCommand command, CancellationToken cancellationToken) =>
            Task.FromException<LeagueWriteResult?>(new InvalidOperationException("synthetic Auto Accept write failure"));
    }

    private sealed class FakeSessionAccessor : ILeagueSessionAccessor
    {
        public LeagueConnectionState State => LeagueConnectionState.Connected;
        public LeagueSessionDescriptor? Current => null;
    }

    private sealed class FakeObservationSource : ILeagueGameflowObservationSource
    {
        public LeagueGameflowSnapshot? Current { get; private set; }
        public event EventHandler<LeagueGameflowChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public event EventHandler<LeagueGameflowChangedEventArgs>? Observed
        {
            add { }
            remove { }
        }
    }
}
