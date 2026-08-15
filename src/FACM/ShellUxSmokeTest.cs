using System;
using FACM.League;

namespace FACM
{
    internal static class ShellUxSmokeTest
    {
        internal static void Validate()
        {
            // PerformanceContractSmokeTest runs before Application.EnableVisualStyles/Application.Run.
            // Keep this contract smoke pure: runtime menu objects are validated by MainForm, while CI
            // validates the stable five-root definition and the single-entry League Hub structure.
            ShellMenuGroups.ValidateDefinitionForSmokeTest();
            LeagueHubNavigation.ValidateForSmokeTest();

            Require(LeagueHubNavigation.Views.Count == 8, "League Hub accepted-view count changed unexpectedly.");
            Require(LeagueHubNavigation.Views[0].Id == LeagueHubNavigation.Dashboard, "League Hub must open from Overview/Dashboard.");
            Require(LeagueHubNavigation.Views[LeagueHubNavigation.Views.Count - 1].Id == LeagueHubNavigation.Efficiency,
                "League Hub efficiency page must remain reachable from the unified window.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
