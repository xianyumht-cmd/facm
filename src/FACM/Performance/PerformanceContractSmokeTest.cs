using System;

namespace FACM.Performance
{
    internal static class PerformanceContractSmokeTest
    {
        public static int Run()
        {
            try
            {
                Validate();
                FACM.ShellUxSmokeTest.Validate();
                FACM.LeagueDashboardSmokeTest.Validate();
                FACM.League.LeaguePlayerSmokeTest.Validate();
                FACM.League.LeagueLiveSmokeTest.Validate();
                FACM.League.LeagueBuildAdvisorSmokeTest.Validate();
                FACM.League.LeagueBuildApplySmokeTest.Validate();
                FACM.League.LeaguePresenceSmokeTest.Validate();
                FACM.League.LeagueItemSetUiTextSmokeTest.Validate();
                FACM.League.LeagueItemSetSmokeTest.Validate();
                FACM.League.LeagueAutoApplySmokeTest.Validate();
                FACM.League.LeagueEfficiencySmokeTest.Validate();
                FACM.League.LeaguePostGameAutomationSmokeTest.Validate();
                FACM.League.LeagueMatchmakingAutomationSmokeTest.Validate();
                Console.WriteLine("FACM performance contract smoke passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 4;
            }
        }

        private static void Validate()
        {
            var desktop = Resolve(LeagueActivityLevel.None, true);
            var client = Resolve(LeagueActivityLevel.Client, true);
            var queueing = Resolve(LeagueActivityLevel.Queueing, true);
            var champSelect = Resolve(LeagueActivityLevel.ChampSelect, true);
            var inGame = Resolve(LeagueActivityLevel.InGame, true);
            var background = Resolve(LeagueActivityLevel.None, false);
            var hiddenInGame = Resolve(LeagueActivityLevel.InGame, false);

            Require(desktop.NetworkConcurrency <= 4, "Desktop network concurrency exceeded the contract ceiling.");
            Require(desktop.ImageDecodeConcurrency <= 2, "Desktop image decode concurrency exceeded the contract ceiling.");
            Require(desktop.BackgroundCpuConcurrency <= 2, "Desktop background CPU concurrency exceeded the contract ceiling.");
            Require(desktop.MatchHistoryPrefetchCount <= 20, "Desktop match prefetch exceeded the contract ceiling.");

            Require(PerformancePolicy.IsNoMoreAggressiveThan(client, desktop), "League client budget must not exceed desktop budget.");
            Require(PerformancePolicy.IsNoMoreAggressiveThan(queueing, client), "Queueing budget must not exceed client budget.");
            Require(PerformancePolicy.IsNoMoreAggressiveThan(champSelect, queueing), "Champ Select budget must not exceed queueing budget.");
            Require(PerformancePolicy.IsNoMoreAggressiveThan(inGame, champSelect), "In-game budget must not exceed Champ Select budget.");
            Require(PerformancePolicy.IsNoMoreAggressiveThan(background, desktop), "Background budget must not exceed desktop budget.");

            Require(inGame.NetworkConcurrency == 1, "In-game network concurrency must be one.");
            Require(inGame.ImageDecodeConcurrency == 1, "In-game image decode concurrency must be one.");
            Require(inGame.DiskIoConcurrency == 1, "In-game disk concurrency must be one.");
            Require(inGame.BackgroundCpuConcurrency == 1, "In-game background CPU concurrency must be one.");
            Require(inGame.MatchHistoryPrefetchCount == 0, "In-game match prefetch must be disabled.");
            Require(!inGame.AllowBackgroundPrefetch, "In-game background prefetch must be disabled.");
            Require(!inGame.AllowMaintenanceWork, "In-game maintenance work must be disabled.");
            Require(!inGame.AllowVisualEnhancements, "In-game visual enhancements must be disabled.");
            Require(inGame.NonCriticalPollInterval >= TimeSpan.FromSeconds(60), "In-game non-critical polling must be throttled.");
            Require(hiddenInGame.Name == inGame.Name, "In-game state must take precedence over window visibility.");

            Require(FACM.League.LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("PATCH", "/lol-champ-select/v1/session/my-selection"),
                "Gate 2 transport blocked its summoner-spell write endpoint.");
            Require(FACM.League.LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-perks/v1/pages/"),
                "Gate 2 transport blocked rune-page creation.");
            Require(FACM.League.LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("PUT", "/lol-perks/v1/pages/77"),
                "Gate 2 transport blocked an owned rune-page update.");
            Require(FACM.League.LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("PUT", "/lol-perks/v1/currentpage"),
                "Gate 2 transport blocked current rune-page selection.");
            Require(!FACM.League.LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-matchmaking/v1/ready-check/accept"),
                "Gate 2 transport must hard-block auto accept; Gate 7 uses a separate minimal writer.");
            Require(!FACM.League.LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("PATCH", "/lol-champ-select/v1/session/actions/1"),
                "Gate 2 transport must hard-block pick/ban action writes.");
            Require(!FACM.League.LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-champ-select/v1/session/bench/swap/55"),
                "Gate 2 transport must not absorb the legacy manual bench-swap capability.");
            Require(!FACM.League.LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-lobby-team-builder/champ-select/v1/session/bench/swap/55"),
                "Gate 2 transport must not absorb the Team Builder manual bench-swap capability.");
            Require(!FACM.League.LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("PUT", "/lol-chat/v1/me"),
                "Gate 2 transport must not absorb the user-directed presence write capability.");
            Require(!FACM.League.LeagueClientWriteApiClient.IsAllowedTargetForSmokeTest("PUT", "/lol-perks/v1/pages/77?force=true"),
                "Gate 2 transport must reject rune-page paths with query-string escape hatches.");

            Require(FACM.League.LeaguePresenceWriteApiClient.IsAllowedTargetForSmokeTest("PUT", "/lol-chat/v1/me"),
                "Presence writer blocked its exact user-status endpoint.");
            Require(!FACM.League.LeaguePresenceWriteApiClient.IsAllowedTargetForSmokeTest("PUT", "/lol-chat/v1/me?force=true"),
                "Presence writer accepted a query-string escape hatch.");
            Require(!FACM.League.LeaguePresenceWriteApiClient.IsAllowedTargetForSmokeTest("PATCH", "/lol-chat/v1/me"),
                "Presence writer accepted a non-PUT method.");

            Require(FACM.League.LeagueBenchSwapWriteApiClient.IsValidChampionIdForSmokeTest(55),
                "Bench writer blocked a valid champion id.");
            Require(!FACM.League.LeagueBenchSwapWriteApiClient.IsValidChampionIdForSmokeTest(0),
                "Bench writer accepted an invalid champion id.");
            Require(FACM.League.LeagueBenchSwapWriteApiClient.BuildPathForSmokeTest(55, FACM.League.LeagueBenchSwapRoute.Legacy) == "/lol-champ-select/v1/session/bench/swap/55",
                "Legacy bench writer must remain fenced to its dedicated bench/swap endpoint.");
            Require(FACM.League.LeagueBenchSwapWriteApiClient.BuildPathForSmokeTest(55, FACM.League.LeagueBenchSwapRoute.TeamBuilder) == "/lol-lobby-team-builder/champ-select/v1/session/bench/swap/55",
                "Team Builder bench writer must remain fenced to its dedicated bench/swap endpoint.");

            var provider = new PerformanceBudgetProvider();
            var changes = 0;
            provider.BudgetChanged += delegate { changes++; };
            Require(provider.Current.Name == "desktop", "Provider must start with the desktop budget.");
            provider.UpdateLeagueActivity(LeagueActivityLevel.ChampSelect);
            Require(provider.Current.Name == "champ-select", "Provider did not enter Champ Select budget.");
            provider.UpdateLeagueActivity(LeagueActivityLevel.InGame);
            Require(provider.Current.Name == "in-game", "Provider did not enter in-game budget.");
            provider.UpdateUiVisibility(false);
            Require(provider.Current.Name == "in-game", "Hiding FACM must not relax in-game budget.");
            provider.UpdateLeagueActivity(LeagueActivityLevel.None);
            Require(provider.Current.Name == "background", "Hidden idle FACM must use background budget.");
            provider.UpdateUiVisibility(true);
            Require(provider.Current.Name == "desktop", "Visible idle FACM must return to desktop budget.");
            Require(changes == 4, "Unexpected PerformanceBudgetProvider transition count: " + changes);
        }

        private static PerformanceBudget Resolve(LeagueActivityLevel activity, bool visible)
        {
            return PerformancePolicy.Resolve(new PerformanceContext(activity, visible));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
