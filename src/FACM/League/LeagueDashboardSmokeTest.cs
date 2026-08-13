using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Performance;

namespace FACM
{
    internal static class LeagueDashboardSmokeTest
    {
        public static int Run()
        {
            try
            {
                Validate().GetAwaiter().GetResult();
                Console.WriteLine("FACM League Dashboard smoke passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 4;
            }
        }

        private static async Task Validate()
        {
            Require(LeagueGameflowActivityMapper.Map(null, false) == LeagueActivityLevel.None, "Disconnected mapping failed.");
            Require(LeagueGameflowActivityMapper.Map("Lobby", true) == LeagueActivityLevel.Client, "Lobby mapping failed.");
            Require(LeagueGameflowActivityMapper.Map("Matchmaking", true) == LeagueActivityLevel.Queueing, "Matchmaking mapping failed.");
            Require(LeagueGameflowActivityMapper.Map("ReadyCheck", true) == LeagueActivityLevel.Queueing, "ReadyCheck mapping failed.");
            Require(LeagueGameflowActivityMapper.Map("ChampSelect", true) == LeagueActivityLevel.ChampSelect, "ChampSelect mapping failed.");
            Require(LeagueGameflowActivityMapper.Map("InProgress", true) == LeagueActivityLevel.InGame, "InProgress mapping failed.");
            Require(LeagueGameflowActivityMapper.Map("WatchInProgress", true) == LeagueActivityLevel.InGame, "Watch mapping failed.");
            Require(LeagueGameflowActivityMapper.Map("Reconnect", true) == LeagueActivityLevel.InGame, "Reconnect mapping failed.");
            Require(LeagueGameflowActivityMapper.Map("GameStart", true) == LeagueActivityLevel.InGame, "GameStart mapping failed.");

            var budgets = new PerformanceBudgetProvider();
            var api = new FakeLeagueClientApi();
            var phaseService = new LeagueDashboardPhaseService(api, budgets);
            var phase = await phaseService.RefreshAsync(CancellationToken.None);
            Require(phase.Connected && phase.Phase == "ChampSelect", "Phase read failed.");
            Require(budgets.Current.Name == "champ-select", "Performance budget was not driven by Gameflow.");

            var details = new LeagueDashboardDetailsService(api, budgets);
            var snapshot = await details.LoadAsync(phase, CancellationToken.None);
            Require(snapshot.AccountName == "FACM测试#CN1", "Summoner name parsing failed.");
            Require(snapshot.SummonerLevel == 88, "Summoner level parsing failed.");
            Require(snapshot.PlatformId == "HN1", "Platform id parsing failed.");
            Require(snapshot.PlatformName == "测试区服", "Platform name parsing failed.");
            Require(api.MaxConcurrent <= budgets.Current.NetworkConcurrency, "Dashboard exceeded current network budget.");

            budgets.UpdateLeagueActivity(LeagueActivityLevel.InGame);
            api.ResetConcurrency();
            var inGamePhase = new LeagueDashboardPhaseState { Connected = true, Phase = "InProgress", Activity = LeagueActivityLevel.InGame, BudgetName = budgets.Current.Name };
            await details.LoadAsync(inGamePhase, CancellationToken.None);
            Require(api.MaxConcurrent == 1, "In-game Dashboard details must be sequential.");

            var module = new LeagueDashboardModule(new LeagueClientModule(), new PerformanceModule());
            Require(Contains(module.Dependencies, LeagueClientModule.ModuleId), "Dashboard must depend on LeagueClient.");
            Require(Contains(module.Dependencies, PerformanceModule.ModuleId), "Dashboard must depend on Performance.");
            Require(LeagueDashboardUiBridge.HasTrayAccessForSmokeTest(), "Dashboard tray bridge lost the MainForm tray contract.");
        }

        private static bool Contains(IReadOnlyList<string> values, string expected)
        {
            foreach (var value in values) if (string.Equals(value, expected, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeLeagueClientApi : ILeagueClientApi
        {
            private int _concurrent;
            private int _maxConcurrent;
            public int MaxConcurrent { get { return _maxConcurrent; } }

            public void ResetConcurrency()
            {
                _concurrent = 0;
                _maxConcurrent = 0;
            }

            public async Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                var current = Interlocked.Increment(ref _concurrent);
                while (true)
                {
                    var observed = _maxConcurrent;
                    if (current <= observed || Interlocked.CompareExchange(ref _maxConcurrent, current, observed) == observed) break;
                }
                try
                {
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                    if (path == LeagueDashboardPhaseService.PhasePath) return Utf8("\"ChampSelect\"");
                    if (path == LeagueDashboardDetailsService.SummonerPath)
                        return Utf8("{\"gameName\":\"FACM测试\",\"tagLine\":\"CN1\",\"summonerLevel\":88,\"profileIconId\":123}");
                    if (path == LeagueDashboardDetailsService.SessionPath)
                        return Utf8("{\"map\":{\"platformId\":\"HN1\",\"platformName\":\"测试区服\"}}");
                    return null;
                }
                finally
                {
                    Interlocked.Decrement(ref _concurrent);
                }
            }

            private static byte[] Utf8(string value) { return Encoding.UTF8.GetBytes(value); }
        }
    }
}
