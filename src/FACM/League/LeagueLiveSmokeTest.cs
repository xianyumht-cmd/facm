using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;

namespace FACM.League
{
    internal static class LeagueLiveSmokeTest
    {
        public static void Validate()
        {
            ValidateChampSelectAndCurrentGameParsing();
            ValidateCurrentMayhemBenchShape();
            ValidateTeamBuilderFallback();
            ValidateBenchQuickPick();
            ValidatePollingBudget();
            ValidateCancellation();
            if (!LeagueLiveUiBridge.HasTrayAccessForSmokeTest())
                throw new InvalidOperationException("League Live tray bridge lost MainForm tray access.");
        }

        private static void ValidateChampSelectAndCurrentGameParsing()
        {
            var champSelect = "{\"gameId\":123456,\"queueId\":420,\"localPlayerCellId\":1,\"benchEnabled\":true,\"benchChampionIds\":[266,55]," +
                "\"timer\":{\"phase\":\"BAN_PICK\",\"adjustedTimeLeftInPhase\":25000,\"totalTimeInPhase\":30000}," +
                "\"bans\":{\"myTeamBans\":[11,22],\"theirTeamBans\":[33]}," +
                "\"myTeam\":[" +
                "{\"cellId\":0,\"championId\":10,\"championPickIntent\":0,\"gameName\":\"Ally\",\"tagLine\":\"CQ100\",\"puuid\":\"ally-puuid\",\"assignedPosition\":\"TOP\",\"spell1Id\":4,\"spell2Id\":12,\"summonerId\":100}," +
                "{\"cellId\":1,\"championId\":22,\"championPickIntent\":99,\"gameName\":\"Me\",\"tagLine\":\"CQ100\",\"puuid\":\"local-puuid\",\"assignedPosition\":\"BOTTOM\",\"spell1Id\":4,\"spell2Id\":7,\"summonerId\":101}]," +
                "\"theirTeam\":[{\"cellId\":5,\"championId\":55,\"championPickIntent\":0,\"assignedPosition\":\"MIDDLE\",\"summonerId\":201}]," +
                "\"actions\":[[{\"actorCellId\":1,\"championId\":99,\"completed\":false,\"isInProgress\":true,\"type\":\"pick\"}]]}";

            var currentGame = "{\"phase\":\"InProgress\",\"map\":{\"id\":11,\"name\":\"Summoner's Rift\",\"gameMode\":\"CLASSIC\",\"gameModeName\":\"Classic\"}," +
                "\"gameData\":{\"gameId\":123456,\"queue\":{\"id\":420,\"name\":\"Ranked Solo\",\"gameMode\":\"CLASSIC\"}," +
                "\"teamOne\":[{\"championId\":22,\"puuid\":\"local-puuid\",\"summonerId\":101,\"summonerName\":\"Me\",\"selectedPosition\":\"BOTTOM\",\"selectedRole\":\"DUO_CARRY\"}]," +
                "\"teamTwo\":[{\"championId\":55,\"puuid\":\"enemy-puuid\",\"summonerId\":201,\"summonerName\":\"Enemy\",\"selectedPosition\":\"MIDDLE\",\"selectedRole\":\"SOLO\"}]}}";

            var api = new FixtureApi("ChampSelect", champSelect, currentGame);
            var budgets = new PerformanceBudgetProvider();
            var service = new LeagueLiveDataService(api, budgets);

            var select = service.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(select != null && select.Connected, "League Live Champ Select did not connect.");
            Require(select.Activity == LeagueActivityLevel.ChampSelect && budgets.Current.Name == "champ-select", "League Live did not preserve Champ Select performance budget.");
            Require(select.GameId == 123456 && select.QueueId == 420 && select.LocalPlayerCellId == 1, "Champ Select session identity did not parse.");
            Require(select.TimerPhase == "BAN_PICK" && select.TimerMillisecondsLeft == 25000, "Champ Select timer did not parse.");
            Require(select.AllyBans.Count == 2 && select.EnemyBans.Count == 1, "Champ Select bans did not parse.");
            Require(select.BenchEnabled && select.BenchChampionIds.SequenceEqual(new[] { 266, 55 }), "Legacy benchChampionIds fallback did not parse.");
            Require(select.BenchSwapRoute == LeagueBenchSwapRoute.Legacy, "Missing isLegacyChampSelect must default to the legacy route.");
            Require(select.Players.Count == 3, "Champ Select teams did not parse expected rows.");
            Require(select.Players.Exists(row => row.IsLocalPlayer && row.PuuId == "local-puuid" && row.ChampionId == 22 && row.ChampionPickIntent == 99), "Champ Select local player was not resolved by localPlayerCellId.");
            Require(select.LocalActionType == "pick" && select.LocalActionChampionId == 99, "Champ Select active local action did not parse.");
            Require(api.Paths.Count == 2 && api.Paths[0] == LeagueDashboardPhaseService.PhasePath && api.Paths[1] == LeagueLiveDataService.ChampSelectSessionPath, "Champ Select refresh must remain one phase request plus one session request.");
            Require(!api.Paths.Exists(path => path.IndexOf("match-history", StringComparison.OrdinalIgnoreCase) >= 0), "League Live must not fan out into match history.");

            var bench = service.ParseBenchState(Encoding.UTF8.GetBytes(champSelect));
            Require(bench.SessionAvailable && bench.BenchEnabled, "Bench state did not retain availability flags.");
            Require(bench.LocalPlayerCellId == 1 && bench.LocalChampionId == 22, "Bench state did not resolve the local champion.");
            Require(bench.ChampionIds.SequenceEqual(new[] { 266, 55 }), "Legacy bench ids did not preserve client order.");

            api.Phase = "InProgress";
            api.Paths.Clear();
            var game = service.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(game != null && game.Activity == LeagueActivityLevel.InGame && budgets.Current.Name == "in-game", "League Live did not enter in-game performance budget.");
            Require(game.GameId == 123456 && game.MapId == 11 && game.QueueId == 420, "Current Game identifiers did not parse.");
            Require(game.MapName == "Summoner's Rift" && game.GameMode == "Classic" && game.QueueName == "Ranked Solo", "Current Game map/mode/queue did not parse.");
            Require(game.Players.Count == 2, "Current Game teams did not parse.");
            Require(game.Players.Exists(row => row.IsLocalPlayer && row.PuuId == "local-puuid" && row.ChampionId == 22), "Current Game did not retain the local player identity learned during Champ Select.");
            Require(api.Paths.Count == 2 && api.Paths[0] == LeagueDashboardPhaseService.PhasePath && api.Paths[1] == LeagueLiveDataService.GameflowSessionPath, "In-game refresh must remain one phase request plus one gameflow-session request.");
        }

        private static void ValidateCurrentMayhemBenchShape()
        {
            var mayhem = "{\"gameId\":500861493625,\"queueId\":2400,\"localPlayerCellId\":2,\"benchEnabled\":true," +
                         "\"isLegacyChampSelect\":false,\"benchChampionIds\":[]," +
                         "\"benchChampions\":[{\"championId\":266},{\"championId\":55},{\"championId\":266}]," +
                         "\"myTeam\":[{\"cellId\":2,\"championId\":58}]}";
            var service = new LeagueLiveDataService(new FixtureApi("ChampSelect", mayhem, "{}"), new PerformanceBudgetProvider());
            var state = service.ParseBenchState(Encoding.UTF8.GetBytes(mayhem));
            Require(state.SessionAvailable && state.BenchEnabled, "Mayhem bench session was not recognized.");
            Require(state.ChampionIds.SequenceEqual(new[] { 266, 55 }), "Mayhem benchChampions did not parse/deduplicate in client order.");
            Require(state.LocalChampionId == 58, "Mayhem local champion did not parse.");
            Require(state.SwapRoute == LeagueBenchSwapRoute.TeamBuilder, "Mayhem isLegacyChampSelect=false did not select Team Builder route.");
            Require(service.LastBenchSwapRouteForQuickPick == LeagueBenchSwapRoute.TeamBuilder, "Observed Mayhem route was not retained for the click path.");
        }

        private static void ValidateTeamBuilderFallback()
        {
            var generic = "{\"localPlayerCellId\":1,\"benchEnabled\":true,\"isLegacyChampSelect\":false,\"benchChampions\":[],\"myTeam\":[{\"cellId\":1,\"championId\":34}]}";
            var teamBuilder = "{\"localPlayerCellId\":1,\"benchEnabled\":true,\"benchChampions\":[{\"championId\":55},{\"championId\":99}],\"myTeam\":[{\"cellId\":1,\"championId\":34}]}";
            var api = new TeamBuilderFallbackApi(generic, teamBuilder);
            var service = new LeagueLiveDataService(api, new PerformanceBudgetProvider());
            var state = service.RefreshBenchAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(state.SessionAvailable && state.BenchEnabled, "Team Builder fallback did not return a bench session.");
            Require(state.ChampionIds.SequenceEqual(new[] { 55, 99 }), "Team Builder fallback did not recover bench champions.");
            Require(state.SwapRoute == LeagueBenchSwapRoute.TeamBuilder, "Team Builder fallback lost its write route.");
            Require(api.GenericReads == 1 && api.TeamBuilderReads == 1, "Team Builder fallback must be narrow and bounded to one extra GET.");
        }

        private static void ValidateBenchQuickPick()
        {
            Require(LeagueBenchSwapWriteApiClient.IsValidChampionIdForSmokeTest(55), "Bench writer rejected a valid champion id.");
            Require(!LeagueBenchSwapWriteApiClient.IsValidChampionIdForSmokeTest(0), "Bench writer accepted champion id zero.");
            Require(
                LeagueBenchSwapWriteApiClient.BuildPathForSmokeTest(55, LeagueBenchSwapRoute.Legacy) == "/lol-champ-select/v1/session/bench/swap/55",
                "Legacy bench writer path drifted.");
            Require(
                LeagueBenchSwapWriteApiClient.BuildPathForSmokeTest(55, LeagueBenchSwapRoute.TeamBuilder) == "/lol-lobby-team-builder/champ-select/v1/session/bench/swap/55",
                "Team Builder bench writer path drifted.");

            var successApi = new BenchFixtureApi(10, new[] { 22, 55 }, false);
            var successWriter = new BenchFixtureWriter(successApi) { StatusCode = 204, ApplySwap = true };
            var successLive = new LeagueLiveDataService(successApi, new PerformanceBudgetProvider());
            successLive.RefreshBenchAsync(CancellationToken.None).GetAwaiter().GetResult();
            var successService = new LeagueBenchQuickPickService(successLive, successWriter);
            var readsBeforeClick = successApi.SessionReads;
            var success = successService.TrySwapAsync(55, CancellationToken.None).GetAwaiter().GetResult();
            Require(success.Success && success.Status == LeagueBenchSwapStatus.Success, "Bench swap did not verify a successful local champion change.");
            Require(successWriter.Calls == 1 && successWriter.LastChampionId == 55, "One bench click must produce exactly one swap POST.");
            Require(successWriter.LastRoute == LeagueBenchSwapRoute.TeamBuilder, "Observed Team Builder route was not used by the UI-compatible click path.");
            Require(successApi.LocalChampionId == 55, "Successful bench fixture did not settle on the target champion.");
            Require(successApi.SessionReads > readsBeforeClick, "Successful swap must read back the settled local champion.");

            var rejectedApi = new BenchFixtureApi(10, new[] { 55 }, false);
            var rejectedWriter = new BenchFixtureWriter(rejectedApi) { StatusCode = 409, ApplySwap = false };
            var rejectedLive = new LeagueLiveDataService(rejectedApi, new PerformanceBudgetProvider());
            rejectedLive.RefreshBenchAsync(CancellationToken.None).GetAwaiter().GetResult();
            var rejectedService = new LeagueBenchQuickPickService(rejectedLive, rejectedWriter);
            var rejectedReadsBeforeClick = rejectedApi.SessionReads;
            var rejected = rejectedService.TrySwapAsync(55, CancellationToken.None).GetAwaiter().GetResult();
            Require(rejected.Status == LeagueBenchSwapStatus.TargetUnavailable && rejected.StatusCode == 409, "Stale Team Builder bench write did not surface as unavailable.");
            Require(rejectedWriter.Calls == 1, "Rejected bench write must not retry the POST.");
            Require(rejectedApi.SessionReads == rejectedReadsBeforeClick, "Race-sensitive click path must not pre-read before a rejected POST.");

            var verifyApi = new BenchFixtureApi(10, new[] { 55 }, true);
            var verifyWriter = new BenchFixtureWriter(verifyApi) { StatusCode = 204, ApplySwap = false };
            var verifyLive = new LeagueLiveDataService(verifyApi, new PerformanceBudgetProvider());
            verifyLive.RefreshBenchAsync(CancellationToken.None).GetAwaiter().GetResult();
            var verifyService = new LeagueBenchQuickPickService(verifyLive, verifyWriter);
            var verify = verifyService.TrySwapAsync(55, CancellationToken.None).GetAwaiter().GetResult();
            Require(verify.Status == LeagueBenchSwapStatus.VerificationFailed, "2xx without a settled champion change must not be reported as success.");
            Require(verifyWriter.Calls == 1, "Verification failure must never retry the swap POST.");
            Require(verifyWriter.LastRoute == LeagueBenchSwapRoute.Legacy, "Legacy session did not keep the legacy writer route.");

            Require(LeagueBenchQuickPickText.DefaultsForSmokeTest().Count >= 10, "Bench quick-pick UI text defaults are incomplete.");
        }

        private static void ValidatePollingBudget()
        {
            Require(LeagueLivePolling.ResolveDelay(LeagueActivityLevel.ChampSelect, false) >= TimeSpan.FromSeconds(2), "Normal Champ Select live polling became too aggressive.");
            Require(LeagueLivePolling.ResolveDelay(LeagueActivityLevel.InGame, false) >= TimeSpan.FromSeconds(10), "In-game visible polling must remain low frequency.");
            Require(LeagueLivePolling.ResolveDelay(LeagueActivityLevel.Client, false) >= TimeSpan.FromSeconds(5), "Client live polling became too aggressive.");
            Require(LeagueLivePolling.ResolveDelay(LeagueActivityLevel.ChampSelect, true) >= TimeSpan.FromSeconds(10), "Minimized League Live must throttle normal polling.");

            var quick = LeagueBenchQuickPickPolling.ResolveDelay(true, LeagueActivityLevel.ChampSelect, false);
            Require(quick >= TimeSpan.FromMilliseconds(90) && quick <= TimeSpan.FromMilliseconds(125), "Active bench quick-pick polling must stay inside its bounded low-latency window.");
            Require(LeagueBenchQuickPickPolling.ResolveDelay(false, LeagueActivityLevel.Client, false) >= TimeSpan.FromMilliseconds(750), "Inactive bench probing became too aggressive.");
            Require(LeagueBenchQuickPickPolling.ResolveDelay(true, LeagueActivityLevel.InGame, false) >= TimeSpan.FromSeconds(5), "Bench probing must throttle in game.");
            Require(LeagueBenchQuickPickPolling.ResolveDelay(true, LeagueActivityLevel.ChampSelect, true) >= TimeSpan.FromSeconds(1), "Minimized bench probing must throttle.");
        }

        private static void ValidateCancellation()
        {
            var api = new FixtureApi("ChampSelect", "{}", "{}");
            var service = new LeagueLiveDataService(api, new PerformanceBudgetProvider());
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try
                {
                    service.RefreshAsync(cancellation.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Canceled League Live request unexpectedly completed.");
                }
                catch (OperationCanceledException)
                {
                    // Expected: form-close cancellation must stop queued live work.
                }
            }
            Require(api.Paths.Count == 0, "Canceled League Live refresh must not start LCU requests.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FixtureApi : ILeagueClientApi
        {
            private readonly byte[] _champSelect;
            private readonly byte[] _currentGame;

            public FixtureApi(string phase, string champSelect, string currentGame)
            {
                Phase = phase;
                _champSelect = Encoding.UTF8.GetBytes(champSelect ?? string.Empty);
                _currentGame = Encoding.UTF8.GetBytes(currentGame ?? string.Empty);
                Paths = new List<string>();
            }

            public string Phase { get; set; }
            public List<string> Paths { get; private set; }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                if (string.Equals(path, LeagueDashboardPhaseService.PhasePath, StringComparison.Ordinal))
                    return Task.FromResult(Encoding.UTF8.GetBytes("\"" + (Phase ?? string.Empty) + "\""));
                if (string.Equals(path, LeagueLiveDataService.ChampSelectSessionPath, StringComparison.Ordinal))
                    return Task.FromResult(_champSelect);
                if (string.Equals(path, LeagueLiveDataService.GameflowSessionPath, StringComparison.Ordinal))
                    return Task.FromResult(_currentGame);
                return Task.FromResult<byte[]>(null);
            }
        }

        private sealed class TeamBuilderFallbackApi : ILeagueClientApi
        {
            private readonly byte[] _generic;
            private readonly byte[] _teamBuilder;

            public TeamBuilderFallbackApi(string generic, string teamBuilder)
            {
                _generic = Encoding.UTF8.GetBytes(generic ?? string.Empty);
                _teamBuilder = Encoding.UTF8.GetBytes(teamBuilder ?? string.Empty);
            }

            public int GenericReads { get; private set; }
            public int TeamBuilderReads { get; private set; }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(path, LeagueLiveDataService.ChampSelectSessionPath, StringComparison.Ordinal))
                {
                    GenericReads++;
                    return Task.FromResult(_generic);
                }
                if (string.Equals(path, LeagueLiveDataService.TeamBuilderChampSelectSessionPath, StringComparison.Ordinal))
                {
                    TeamBuilderReads++;
                    return Task.FromResult(_teamBuilder);
                }
                return Task.FromResult<byte[]>(null);
            }
        }

        private sealed class BenchFixtureApi : ILeagueClientApi
        {
            private readonly bool _legacy;

            public BenchFixtureApi(int localChampionId, IEnumerable<int> benchChampionIds, bool legacy)
            {
                LocalChampionId = localChampionId;
                BenchChampionIds = new List<int>(benchChampionIds ?? new int[0]);
                _legacy = legacy;
            }

            public int LocalChampionId { get; set; }
            public List<int> BenchChampionIds { get; private set; }
            public int SessionReads { get; private set; }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(path, LeagueLiveDataService.ChampSelectSessionPath, StringComparison.Ordinal) &&
                    !string.Equals(path, LeagueLiveDataService.TeamBuilderChampSelectSessionPath, StringComparison.Ordinal))
                    return Task.FromResult<byte[]>(null);

                SessionReads++;
                var benchObjects = string.Join(",", BenchChampionIds.Select(id => "{\"championId\":" + id + "}"));
                var json = "{\"localPlayerCellId\":1,\"benchEnabled\":true,\"isLegacyChampSelect\":" + (_legacy ? "true" : "false") + "," +
                           "\"benchChampions\":[" + benchObjects + "]," +
                           "\"myTeam\":[{\"cellId\":1,\"championId\":" + LocalChampionId + "}]}";
                return Task.FromResult(Encoding.UTF8.GetBytes(json));
            }

            public void Apply(int championId)
            {
                LocalChampionId = championId;
                BenchChampionIds.Remove(championId);
            }
        }

        private sealed class BenchFixtureWriter : ILeagueBenchSwapWriteApi
        {
            private readonly BenchFixtureApi _api;

            public BenchFixtureWriter(BenchFixtureApi api)
            {
                _api = api;
            }

            public int Calls { get; private set; }
            public int LastChampionId { get; private set; }
            public LeagueBenchSwapRoute LastRoute { get; private set; }
            public int StatusCode { get; set; }
            public bool ApplySwap { get; set; }

            public Task<LeagueClientWriteResponse> TrySwapAsync(int championId, LeagueBenchSwapRoute route, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                LastChampionId = championId;
                LastRoute = route;
                if (StatusCode >= 200 && StatusCode <= 299 && ApplySwap) _api.Apply(championId);
                return Task.FromResult(new LeagueClientWriteResponse
                {
                    StatusCode = StatusCode,
                    Body = new byte[0]
                });
            }
        }
    }
}
