using System;
using System.Collections.Generic;
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
            ValidatePollingBudget();
            ValidateCancellation();
            if (!LeagueLiveUiBridge.HasTrayAccessForSmokeTest())
                throw new InvalidOperationException("League Live tray bridge lost MainForm tray access.");
        }

        private static void ValidateChampSelectAndCurrentGameParsing()
        {
            var champSelect = "{\"gameId\":123456,\"queueId\":420,\"localPlayerCellId\":1," +
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
            Require(select.Players.Count == 3, "Champ Select teams did not parse expected rows.");
            Require(select.Players.Exists(row => row.IsLocalPlayer && row.PuuId == "local-puuid" && row.ChampionId == 22 && row.ChampionPickIntent == 99), "Champ Select local player was not resolved by localPlayerCellId.");
            Require(select.LocalActionType == "pick" && select.LocalActionChampionId == 99, "Champ Select active local action did not parse.");
            Require(api.Paths.Count == 2 && api.Paths[0] == LeagueDashboardPhaseService.PhasePath && api.Paths[1] == LeagueLiveDataService.ChampSelectSessionPath, "Champ Select refresh must remain one phase request plus one session request.");
            Require(!api.Paths.Exists(path => path.IndexOf("match-history", StringComparison.OrdinalIgnoreCase) >= 0), "League Live must not fan out into match history.");

            api.Phase = "InProgress";
            api.Paths.Clear();
            var game = service.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(game != null && game.Activity == LeagueActivityLevel.InGame && budgets.Current.Name == "in-game", "League Live did not enter in-game performance budget.");
            Require(game.GameId == 123456 && game.MapId == 11 && game.QueueId == 420, "Current Game identifiers did not parse.");
            Require(game.MapName == "Summoner's Rift" && game.GameMode == "Classic" && game.QueueName == "Ranked Solo", "Current Game map/mode/queue did not parse.");
            Require(game.Players.Count == 2, "Current Game teams did not parse.");
            Require(game.Players.Exists(row => row.IsLocalPlayer && row.PuuId == "local-puuid" && row.ChampionId == 22), "Current Game did not retain the local player identity learned during Champ Select.");
            Require(api.Paths.Count == 2 && api.Paths[0] == LeagueDashboardPhaseService.PhasePath && api.Paths[1] == LeagueLiveDataService.GameflowSessionPath, "In-game refresh must remain one phase request plus one gameflow-session request.");
            Require(!api.Paths.Exists(path => path.IndexOf("match-history", StringComparison.OrdinalIgnoreCase) >= 0), "In-game League Live must perform zero history/scouting requests.");
        }

        private static void ValidatePollingBudget()
        {
            Require(LeagueLivePolling.ResolveDelay(LeagueActivityLevel.ChampSelect, false) >= TimeSpan.FromSeconds(2), "Champ Select visible polling became too aggressive.");
            Require(LeagueLivePolling.ResolveDelay(LeagueActivityLevel.InGame, false) >= TimeSpan.FromSeconds(10), "In-game visible polling must remain low frequency.");
            Require(LeagueLivePolling.ResolveDelay(LeagueActivityLevel.Client, false) >= TimeSpan.FromSeconds(5), "Client polling became too aggressive.");
            Require(LeagueLivePolling.ResolveDelay(LeagueActivityLevel.ChampSelect, true) >= TimeSpan.FromSeconds(10), "Minimized League Live must throttle polling.");
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
    }
}
