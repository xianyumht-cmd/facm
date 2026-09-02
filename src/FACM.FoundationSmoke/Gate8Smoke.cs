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
        await TestWorkbenchDataSourceAsync();
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
        Equal(TimeSpan.FromSeconds(3), Cadence(LeagueProductState.NotRunning, LeagueActivityLevel.None), "not-running cadence");
        Equal(TimeSpan.FromSeconds(3), Cadence(LeagueProductState.Connecting, LeagueActivityLevel.None), "connecting cadence");
        Equal(TimeSpan.FromSeconds(5), Cadence(LeagueProductState.Lobby, LeagueActivityLevel.Client), "lobby cadence");
        Equal(TimeSpan.FromSeconds(3), Cadence(LeagueProductState.Matchmaking, LeagueActivityLevel.Queueing), "matchmaking cadence");
        Equal(TimeSpan.FromSeconds(3), Cadence(LeagueProductState.ReadyCheck, LeagueActivityLevel.Queueing), "ready-check cadence");
        Equal(TimeSpan.FromSeconds(2), Cadence(LeagueProductState.ChampSelect, LeagueActivityLevel.ChampSelect), "champ-select cadence");
        Equal(TimeSpan.FromSeconds(10), Cadence(LeagueProductState.InGame, LeagueActivityLevel.InGame), "in-game cadence");
        Equal(TimeSpan.FromSeconds(5), Cadence(LeagueProductState.PostGame, LeagueActivityLevel.Client), "post-game cadence");
        Equal(TimeSpan.FromSeconds(3), Cadence(LeagueProductState.ClientError, LeagueActivityLevel.Client), "client-error cadence");
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

    private static async Task TestWorkbenchDataSourceAsync()
    {
        var gateway = new FakeLeagueReadGateway();
        gateway.Responses[LeagueWorkbenchDataSource.CurrentSummonerPath] = Utf8("""
            {"puuid":"PUUID-1","summonerId":42,"accountId":9,"gameName":"FACM","tagLine":"CN1","displayName":"FACM","summonerLevel":88,"profileIconId":123}
            """);
        gateway.Responses[LeagueWorkbenchDataSource.GameflowSessionPath] = Utf8("""
            {"gameData":{"queue":{"id":450,"name":"ARAM","gameMode":"ARAM"}}}
            """);
        gateway.Responses[LeagueWorkbenchDataSource.LobbyPath] = Utf8("""
            {"members":[{"puuid":"PUUID-1","summonerId":42,"gameName":"FACM"},{"puuid":"PUUID-2","summonerId":43,"gameName":"Teammate"}]}
            """);
        gateway.Responses[LeagueWorkbenchDataSource.ReadyCheckPath] = Utf8("""
            {"state":"InProgress","playerResponse":"Accepted","timer":8000}
            """);
        gateway.Responses[LeagueWorkbenchDataSource.RankedStatsPath] = Utf8("""
            {"queues":[{"queueType":"RANKED_SOLO_5x5","tier":"GOLD","division":"II","leaguePoints":55,"wins":12,"losses":8}]}
            """);
        gateway.Responses["/lol-match-history/v1/products/lol/PUUID-1/matches?begIndex=0&endIndex=9"] = Utf8("""
            {"games":{"gameCount":1,"games":[{"gameId":101,"gameCreation":1700000000000,"gameDuration":1800,"gameMode":"CLASSIC","queueId":420,"participantIdentities":[{"participantId":3,"player":{"puuid":"PUUID-1","summonerId":42}}],"participants":[{"participantId":3,"championId":99,"stats":{"kills":10,"deaths":2,"assists":7,"totalMinionsKilled":150,"neutralMinionsKilled":12,"win":true}}]}]}}
            """);

        var noPhaseSource = new LeagueWorkbenchDataSource(gateway);
        var dashboard = await noPhaseSource.LoadDashboardAsync();
        Equal(LeagueWorkbenchDataState.Ready, dashboard.State, "workbench dashboard ready");
        Equal("FACM#CN1", dashboard.Account?.AccountName, "workbench account name");
        Equal(450, dashboard.Queue?.QueueId, "workbench queue id");
        Equal(2, dashboard.LobbyMembers.Count, "workbench lobby count");
        True(dashboard.LobbyMembers[0].IsLocalPlayer, "workbench local lobby member");
        Equal("Accepted", dashboard.ReadyCheck?.PlayerResponse, "workbench ready-check response");

        var player = await noPhaseSource.LoadCurrentPlayerAsync();
        Equal(LeagueWorkbenchDataState.Ready, player.State, "workbench player ready");
        Equal("GOLD", player.Ranked?.Tier, "workbench ranked tier");
        Equal(55, player.Ranked?.LeaguePoints ?? 0, "workbench ranked lp");
        Equal(1, player.RecentMatches.Count, "workbench match count");
        Equal(99, player.RecentMatches[0].ChampionId, "workbench champion id");
        Equal(10, player.RecentMatches[0].Kills, "workbench kills");
        Equal(162, player.RecentMatches[0].CreepScore, "workbench creep score");
        True(player.RecentMatches[0].Win, "workbench win");
        True(player.RecentMatches[0].ParticipantResolved, "workbench participant resolution");
        True(!player.HasMoreMatches, "workbench finite page");

        var gameflow = new FakeGameflowReader
        {
            Current = new LeagueGameflowSnapshot(
                DateTimeOffset.Parse("2026-08-28T07:00:00Z"),
                LeagueConnectionState.Connected,
                "ChampSelect",
                LeagueProductState.ChampSelect,
                LeagueActivityLevel.ChampSelect)
        };
        gateway.Responses[LeagueWorkbenchDataSource.ChampSelectSessionPath] = Utf8("""
            {"gameId":202,"queueId":450,"localPlayerCellId":1,"benchEnabled":true,"timer":{"phase":"BAN_PICK","adjustedTimeLeftInPhase":12345},"bans":{"myTeamBans":[11],"theirTeamBans":[22]},"benchChampions":[{"championId":33}],"myTeam":[{"cellId":1,"puuid":"PUUID-1","summonerId":42,"gameName":"FACM","tagLine":"CN1","assignedPosition":"middle","championId":99,"spell1Id":4,"spell2Id":14}],"theirTeam":[{"cellId":6,"puuid":"PUUID-9","summonerId":99,"gameName":"Enemy","championPickIntent":55}],"actions":[[{"actorCellId":1,"isInProgress":true,"type":"pick","championId":99}]]}
            """);
        var liveSource = new LeagueWorkbenchDataSource(gateway, gameflow);
        var champSelect = await liveSource.LoadLiveAsync();
        Equal(LeagueWorkbenchDataState.Ready, champSelect.State, "workbench champ-select ready");
        Equal(2, champSelect.Players.Count, "workbench champ-select players");
        True(champSelect.Players[0].IsLocalPlayer, "workbench champ-select local player");
        Equal(33, champSelect.BenchChampionIds[0], "workbench champ-select bench");
        Equal(11, champSelect.AllyBans[0], "workbench ally ban");
        Equal(22, champSelect.EnemyBans[0], "workbench enemy ban");
        Equal("pick", champSelect.LocalActionType, "workbench local action");
        Equal(99, champSelect.LocalActionChampionId, "workbench local action champion");
        Equal(LeagueBenchSwapRoute.Legacy, champSelect.BenchSwapRoute, "workbench legacy bench route");

        gateway.Responses[LeagueWorkbenchDataSource.ChampSelectSessionPath] = Utf8("""
            {"isLegacyChampSelect":false,"localPlayerCellId":1,"benchEnabled":true,"myTeam":[{"cellId":1,"championId":99}]}
            """);
        gateway.Responses[LeagueWorkbenchDataSource.TeamBuilderChampSelectSessionPath] = Utf8("""
            {"localPlayerCellId":1,"benchEnabled":true,"myTeam":[{"cellId":1,"championId":99}],"benchChampionIds":[44]}
            """);
        var teamBuilderChampSelect = await liveSource.LoadLiveAsync();
        Equal(LeagueBenchSwapRoute.TeamBuilder, teamBuilderChampSelect.BenchSwapRoute,
            "workbench Team Builder route");
        Equal(44, teamBuilderChampSelect.BenchChampionIds[0], "workbench Team Builder bench routing");

        gameflow.Current = new LeagueGameflowSnapshot(
            DateTimeOffset.Parse("2026-08-28T07:01:00Z"),
            LeagueConnectionState.Connected,
            "InProgress",
            LeagueProductState.InGame,
            LeagueActivityLevel.InGame);
        gateway.Responses[LeagueWorkbenchDataSource.GameflowSessionPath] = Utf8("""
            {"phase":"InProgress","map":{"id":11,"name":"Summoner's Rift"},"gameData":{"gameId":303,"queue":{"id":420,"name":"Ranked Solo","gameMode":"CLASSIC"},"teamOne":[{"puuid":"PUUID-1","summonerId":42,"summonerName":"FACM","selectedPosition":"MIDDLE","selectedRole":"SOLO","championId":99}],"teamTwo":[{"puuid":"PUUID-9","summonerId":99,"summonerName":"Enemy","championId":55}]}}
            """);
        var inGame = await liveSource.LoadLiveAsync();
        Equal(LeagueWorkbenchDataState.Ready, inGame.State, "workbench in-game ready");
        Equal(303L, inGame.GameId, "workbench in-game game id");
        Equal(11, inGame.MapId, "workbench in-game map id");
        Equal(420, inGame.Queue?.QueueId ?? 0, "workbench in-game queue");
        Equal(2, inGame.Players.Count, "workbench in-game players");

        var unavailable = await new LeagueWorkbenchDataSource(new FakeLeagueReadGateway()).LoadDashboardAsync();
        Equal(LeagueWorkbenchDataState.Unavailable, unavailable.State, "workbench fail-soft unavailable");
        var noLivePhase = await noPhaseSource.LoadLiveAsync();
        Equal(LeagueWorkbenchDataState.Unavailable, noLivePhase.State, "workbench live requires shared phase owner");
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
        public Dictionary<string, byte[]> Responses { get; } = new(StringComparer.Ordinal);

        public Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Responses.TryGetValue(resourceKey, out var response) ? response : Next);
        }
    }

    private sealed class FakeLeagueSessionAccessor : ILeagueSessionAccessor
    {
        public LeagueConnectionState State { get; set; }
        public LeagueSessionDescriptor? Current => null;
    }

    private sealed class FakeGameflowReader : ILeagueGameflowReader
    {
        public LeagueGameflowSnapshot? Current { get; set; }
        public event EventHandler<LeagueGameflowChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
