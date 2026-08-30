using System.Net;
using System.Text;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.State;
using FACM.Infrastructure.League;

internal static class LeagueDiagnosticsSmoke
{
    public static async Task RunAsync()
    {
        TestDiagnosticContext();
        await TestGatewayDiagnosticsAsync();
        await TestGameflowDiagnosticsAsync();
    }

    private static void TestDiagnosticContext()
    {
        True(LeagueDiagnosticContext.Current is null, "diagnostic context starts empty");
        using (LeagueDiagnosticContext.Begin("outer-correlation", "workbench", "refresh"))
        {
            Equal("outer-correlation", LeagueDiagnosticContext.Current!.CorrelationId, "outer correlation");
            using (LeagueDiagnosticContext.Begin("inner-correlation", "workbench", "dashboard"))
                Equal("dashboard", LeagueDiagnosticContext.Current!.Phase, "inner phase");
            Equal("refresh", LeagueDiagnosticContext.Current!.Phase, "context restores after nested scope");
        }
        True(LeagueDiagnosticContext.Current is null, "diagnostic context does not leak");
        Equal(
            "/lol-match-history/v1/products/lol/{redacted}/matches?begIndex=0&endIndex=9",
            LeagueEndpointRedactor.Redact("/lol-match-history/v1/products/lol/puuid-secret/matches?begIndex=0&endIndex=9"),
            "match history endpoint redaction");
    }

    private static async Task TestGatewayDiagnosticsAsync()
    {
        var successfulEvents = new List<LeagueHttpDiagnostic>();
        var successfulSource = new FakeSessionSource(CreateSession("success-secret"));
        using (var gateway = new LeagueHttpGateway(
                   successfulSource,
                   handlerFactory: () => new ResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"))
                   })),
                   diagnosticReporter: successfulEvents.Add))
        {
            using (LeagueDiagnosticContext.Begin("workbench-correlation", "workbench", "player"))
            {
                var body = await gateway.TryGetBytesAsync(
                    "/lol-match-history/v1/products/lol/puuid-secret/matches?begIndex=0&endIndex=9",
                    CancellationToken.None);
                Equal("ok", Encoding.UTF8.GetString(body!), "diagnostic success body");
            }
        }

        AssertPaired(successfulEvents, "success request");
        var successStart = successfulEvents.Single(item => item.Event == "started");
        var successEnd = successfulEvents.Single(item => item.Event == "completed");
        Equal("workbench-correlation", successEnd.CorrelationId, "gateway correlation propagation");
        Equal("player", successEnd.Phase, "gateway phase propagation");
        True(successEnd.Endpoint.Contains("{redacted}", StringComparison.Ordinal), "gateway endpoint is redacted");
        True(!successEnd.Endpoint.Contains("puuid-secret", StringComparison.Ordinal), "gateway endpoint does not contain PUUID");
        Equal(200, successEnd.StatusCode, "gateway success status");
        Equal("success", successEnd.Outcome, "gateway success outcome");
        Equal(0, successEnd.InFlightAtEnd, "gateway in-flight returns to zero");
        True(successEnd.MaxInFlightObserved >= successStart.InFlightAtStart, "gateway max in-flight is recorded");

        var failureEvents = new List<LeagueHttpDiagnostic>();
        using (var gateway = new LeagueHttpGateway(
                   new FakeSessionSource(CreateSession("failure-secret")),
                   handlerFactory: () => new ResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))),
                   diagnosticReporter: failureEvents.Add))
        {
            True(await gateway.TryGetBytesAsync("/lol-gameflow/v1/gameflow-phase", CancellationToken.None) is null, "gateway HTTP failure result");
        }
        AssertPaired(failureEvents, "HTTP failure request");
        Equal("http-failure", failureEvents.Single(item => item.Event == "completed").Outcome, "HTTP failure outcome");

        var timeoutEvents = new List<LeagueHttpDiagnostic>();
        var timeoutSource = new FakeSessionSource(CreateSession("timeout-secret"));
        using (var gateway = new LeagueHttpGateway(
                   timeoutSource,
                   requestTimeout: TimeSpan.FromMilliseconds(20),
                   handlerFactory: () => new DelayHandler(),
                   diagnosticReporter: timeoutEvents.Add))
        {
            True(await gateway.TryGetBytesAsync("/lol-gameflow/v1/gameflow-phase", CancellationToken.None) is null, "gateway timeout result");
        }
        AssertPaired(timeoutEvents, "timeout request");
        var timeoutEnd = timeoutEvents.Single(item => item.Event == "completed");
        Equal("timeout", timeoutEnd.Outcome, "gateway timeout outcome");
        True(timeoutEnd.SessionInvalidated, "gateway timeout invalidates session");
        Equal(1, timeoutSource.Invalidations, "gateway timeout invalidation count");

        var cancelledEvents = new List<LeagueHttpDiagnostic>();
        using (var gateway = new LeagueHttpGateway(
                   new FakeSessionSource(CreateSession("cancel-secret")),
                   requestTimeout: TimeSpan.FromSeconds(2),
                   handlerFactory: () => new DelayHandler(),
                   diagnosticReporter: cancelledEvents.Add))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
            await ThrowsAsync<OperationCanceledException>(
                () => gateway.TryGetBytesAsync("/lol-gameflow/v1/gameflow-phase", cancellation.Token),
                "caller cancellation propagation");
        }
        AssertPaired(cancelledEvents, "caller cancellation request");
        Equal("caller-cancelled", cancelledEvents.Single(item => item.Event == "completed").Outcome, "caller cancellation outcome");

        var concurrentEvents = new List<LeagueHttpDiagnostic>();
        using (var gateway = new LeagueHttpGateway(
                   new FakeSessionSource(CreateSession("concurrent-secret")),
                   handlerFactory: () => new ResponseHandler(async (_, cancellationToken) =>
                   {
                       await Task.Delay(25, cancellationToken);
                       return new HttpResponseMessage(HttpStatusCode.OK)
                       {
                           Content = new ByteArrayContent([1])
                       };
                   }),
                   diagnosticReporter: concurrentEvents.Add))
        {
            await Task.WhenAll(
                gateway.TryGetBytesAsync("/lol-gameflow/v1/gameflow-phase", CancellationToken.None),
                gateway.TryGetBytesAsync("/lol-gameflow/v1/session", CancellationToken.None));
        }
        True(concurrentEvents.Where(item => item.Event == "completed").Max(item => item.MaxInFlightObserved) >= 2, "gateway observes concurrent in-flight requests");
        True(concurrentEvents.Where(item => item.Event == "completed").All(item => item.InFlightAtEnd >= 0), "gateway in-flight never negative");
    }

    private static async Task TestGameflowDiagnosticsAsync()
    {
        var events = new List<LeagueGameflowDiagnostic>();
        var productState = new ProductStateStore();
        var gateway = new StaticReadGateway(Encoding.UTF8.GetBytes("\"ChampSelect\""));
        var sessions = new FakeSessionAccessor(LeagueConnectionState.Connected);
        using var monitor = new LeagueGameflowMonitor(
            gateway,
            sessions,
            productState,
            new PerformanceBudgetProvider(),
            diagnosticReporter: events.Add);

        var snapshot = await monitor.RefreshOnceAsync();
        Equal("ChampSelect", snapshot.Phase, "gameflow diagnostic phase");
        Equal(2, events.Count, "gameflow start/end pair");
        Equal(events[0].PollId, events[1].PollId, "gameflow poll pairing");
        Equal(events[0].CorrelationId, events[1].CorrelationId, "gameflow correlation pairing");
        Equal("success", events[1].Outcome, "gameflow success outcome");
        True(events[1].Changed == true, "gameflow changed flag");

        events.Clear();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(
            () => monitor.RefreshOnceAsync(cancellation.Token),
            "gameflow caller cancellation propagation");
        Equal(2, events.Count, "gameflow cancellation start/end pair");
        Equal("caller-cancelled", events[1].Outcome, "gameflow cancellation outcome");
    }

    private static void AssertPaired(IReadOnlyList<LeagueHttpDiagnostic> events, string name)
    {
        Equal(2, events.Count, name + " start/end count");
        Equal(events[0].RequestId, events[1].RequestId, name + " request pairing");
        Equal(events[0].CorrelationId, events[1].CorrelationId, name + " correlation pairing");
        Equal(0, events[1].InFlightAtEnd, name + " in-flight end");
        True(events[1].FinishedUtc >= events[1].StartedUtc, name + " timestamp order");
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

    private sealed class ResponseHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }

    private sealed class DelayHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class StaticReadGateway(byte[] response) : ILeagueReadGateway
    {
        public Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<byte[]?>(response);
        }
    }

    private sealed class FakeSessionAccessor(LeagueConnectionState state) : ILeagueSessionAccessor
    {
        public LeagueConnectionState State { get; } = state;
        public LeagueSessionDescriptor? Current => null;
    }
}
