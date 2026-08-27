using System.Text;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.State;
using FACM.Core.Text;
using FACM.Infrastructure.League;

internal static class Gate8Smoke
{
    public static async Task RunAsync()
    {
        TestPhaseMapping();
        TestCadence();
        TestWorkbenchCatalogAndText();
        await TestSingleMonitorPublicationAsync();
    }

    private static void TestPhaseMapping()
    {
        Equal(LeagueProductState.NotRunning, Map(null, LeagueConnectionState.NotRunning, false).ProductState, "not-running product state");
        Equal(LeagueActivityLevel.None, Map(null, LeagueConnectionState.NotRunning, false).Activity, "not-running activity");
        Equal(LeagueProductState.Connecting, Map(null, LeagueConnectionState.Connecting, false).ProductState, "connecting product state");
        Equal(LeagueProductState.ClientError, Map(null, LeagueConnectionState.Unavailable, false).ProductState, "unavailable product state");
        Equal(LeagueProductState.ClientError, Map(null, LeagueConnectionState.Connected, false).ProductState, "connected read failure");

        Equal(LeagueProductState.Lobby, Map("None", LeagueConnectionState.Connected, true).ProductState, "connected idle mapping");
        Equal(LeagueProductState.Lobby, Map("Lobby", LeagueConnectionState.Connected, true).ProductState, "lobby mapping");
        Equal(LeagueProductState.Matchmaking, Map("Matchmaking", LeagueConnectionState.Connected, true).ProductState, "matchmaking mapping");
        Equal(LeagueProductState.ReadyCheck, Map("ReadyCheck", LeagueConnectionState.Connected, true).ProductState, "ready-check mapping");
        Equal(LeagueProductState.ChampSelect, Map("ChampSelect", LeagueConnectionState.Connected, true).ProductState, "champ-select mapping");
        Equal(LeagueActivityLevel.ChampSelect, Map("ChampSelect", LeagueConnectionState.Connected, true).Activity, "champ-select activity");

        foreach (var phase in new[] { "InProgress", "WatchInProgress", "Reconnect", "GameStart" })
        {
            var mapping = Map(phase, LeagueConnectionState.Connected, true);
            Equal(LeagueProductState.InGame, mapping.ProductState, "in-game mapping " + phase);
            Equal(LeagueActivityLevel.InGame, mapping.Activity, "in-game activity " + phase);
        }

        foreach (var phase in new[] { "WaitingForStats", "PreEndOfGame", "EndOfGame" })
        {
            Equal(LeagueProductState.PostGame, Map(phase, LeagueConnectionState.Connected, true).ProductState, "post-game mapping " + phase);
        }
    }

    private static void TestCadence()
    {
        Equal(TimeSpan.FromSeconds(10), Cadence(LeagueProductState.NotRunning, LeagueActivityLevel.None), "not-running cadence");
        Equal(TimeSpan.FromSeconds(10), Cadence(LeagueProductState.Connecting, LeagueActivityLevel.None), "connecting cadence");
        Equal(TimeSpan.FromSeconds(5), Cadence(LeagueProductState.Lobby, LeagueActivityLevel.Client), "lobby cadence");
        Equal(TimeSpan.FromSeconds(3), Cadence(LeagueProductState.Matchmaking, LeagueActivityLevel.Queueing), "matchmaking cadence");
        Equal(TimeSpan.FromSeconds(3), Cadence(LeagueProductState.ReadyCheck, LeagueActivityLevel.Queueing), "ready-check cadence");
        Equal(TimeSpan.FromSeconds(2), Cadence(LeagueProductState.ChampSelect, LeagueActivityLevel.ChampSelect), "champ-select cadence");
        Equal(TimeSpan.FromSeconds(10), Cadence(LeagueProductState.InGame, LeagueActivityLevel.InGame), "in-game cadence");
        Equal(TimeSpan.FromSeconds(5), Cadence(LeagueProductState.PostGame, LeagueActivityLevel.Client), "post-game cadence");
        Equal(TimeSpan.FromSeconds(10), Cadence(LeagueProductState.ClientError, LeagueActivityLevel.Client), "client-error cadence");
    }

    private static void TestWorkbenchCatalogAndText()
    {
        Equal(3, LeagueWorkbenchCatalog.Sections.Count, "Workbench section count");
        Equal(LeagueWorkbenchCatalog.Match, LeagueWorkbenchCatalog.Sections[0].Id, "Workbench match section");
        Equal(LeagueWorkbenchCatalog.Strategy, LeagueWorkbenchCatalog.Sections[1].Id, "Workbench strategy section");
        Equal(LeagueWorkbenchCatalog.Automation, LeagueWorkbenchCatalog.Sections[2].Id, "Workbench automation section");
        Equal(3, LeagueWorkbenchCatalog.Sections.Select(section => section.Id).Distinct(StringComparer.Ordinal).Count(), "Workbench unique section IDs");

        foreach (var section in LeagueWorkbenchCatalog.Sections)
        {
            True(!string.Equals(FoundationUiTextDefaults.Get(section.TitleTextKey), section.TitleTextKey, StringComparison.Ordinal), "Workbench title default " + section.Id);
            True(!string.Equals(FoundationUiTextDefaults.Get(section.DescriptionTextKey), section.DescriptionTextKey, StringComparison.Ordinal), "Workbench description default " + section.Id);
        }

        foreach (var key in new[]
        {
            UiTextKeys.LeagueWorkbenchStateLabel,
            UiTextKeys.LeagueWorkbenchBudgetLabel,
            UiTextKeys.LeagueStateNotRunning,
            UiTextKeys.LeagueStateConnecting,
            UiTextKeys.LeagueStateLobby,
            UiTextKeys.LeagueStateMatchmaking,
            UiTextKeys.LeagueStateReadyCheck,
            UiTextKeys.LeagueStateChampSelect,
            UiTextKeys.LeagueStateInGame,
            UiTextKeys.LeagueStatePostGame,
            UiTextKeys.LeagueStateClientError
        })
        {
            True(!string.Equals(FoundationUiTextDefaults.Get(key), key, StringComparison.Ordinal), "League state UI text default " + key);
        }
    }

    private static async Task TestSingleMonitorPublicationAsync()
    {
        var gateway = new FakeLeagueReadGateway { Next = Utf8("\"ChampSelect\"") };
        var sessions = new FakeLeagueSessionAccessor { State = LeagueConnectionState.Connected };
        var productState = new ProductStateStore(() => DateTimeOffset.Parse("2026-08-27T10:00:00Z"));
        var performance = new PerformanceBudgetProvider();
        var changed = 0;

        using var monitor = new LeagueGameflowMonitor(
            gateway,
            sessions,
            productState,
            performance,
            utcNow: () => DateTimeOffset.Parse("2026-08-27T10:00:01Z"));
        monitor.Changed += (_, _) => changed++;

        var first = await monitor.RefreshOnceAsync();
        Equal(LeagueProductState.ChampSelect, first.ProductState, "monitor champ-select snapshot");
        Equal(LeagueProductState.ChampSelect, productState.Current.League, "monitor Product State publication");
        Equal("champ-select", performance.Current.Name, "monitor Performance publication");
        Equal(1, changed, "monitor first event");
        var revisionAfterFirst = productState.Current.Revision;

        var duplicate = await monitor.RefreshOnceAsync();
        True(ReferenceEquals(first, duplicate), "equivalent gameflow must reuse current snapshot");
        Equal(1, changed, "equivalent gameflow event suppression");
        Equal(revisionAfterFirst, productState.Current.Revision, "equivalent Product State revision suppression");

        gateway.Next = Utf8("\"Matchmaking\"");
        var queueing = await monitor.RefreshOnceAsync();
        Equal(LeagueProductState.Matchmaking, queueing.ProductState, "monitor matchmaking snapshot");
        Equal("queueing", performance.Current.Name, "monitor queueing performance");
        Equal(2, changed, "monitor changed event after phase transition");

        gateway.Next = null;
        sessions.State = LeagueConnectionState.NotRunning;
        var stopped = await monitor.RefreshOnceAsync();
        Equal(LeagueProductState.NotRunning, stopped.ProductState, "monitor not-running snapshot");
        Equal("desktop", performance.Current.Name, "not-running desktop performance");
    }

    private static LeagueGameflowMapping Map(string? phase, LeagueConnectionState connection, bool succeeded) =>
        LeagueGameflowPhaseMapper.Map(phase, connection, succeeded);

    private static TimeSpan Cadence(LeagueProductState state, LeagueActivityLevel activity) =>
        LeagueGameflowCadence.Resolve(new LeagueGameflowMapping(LeagueConnectionState.Connected, string.Empty, state, activity));

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

    private sealed class FakeLeagueReadGateway : ILeagueReadGateway
    {
        public byte[]? Next { get; set; }
        public Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Next);
        }
    }

    private sealed class FakeLeagueSessionAccessor : ILeagueSessionAccessor
    {
        public LeagueConnectionState State { get; set; }
        public LeagueSessionDescriptor? Current => null;
    }
}
