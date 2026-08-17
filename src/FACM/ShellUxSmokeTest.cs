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
            // validates the fixed Shell roots and the novice-facing League Hub information architecture.
            ShellMenuGroups.ValidateDefinitionForSmokeTest();
            LeagueHubNavigation.ValidateForSmokeTest();

            Require(LeagueHubNavigation.Views.Count == 6,
                "League Hub must expose four match detail views plus one recommendation center and one efficiency page.");
            Require(LeagueHubNavigation.Views[0].Id == LeagueHubNavigation.Dashboard,
                "League Hub must open from Overview/Dashboard.");
            Require(LeagueHubNavigation.ViewsForSection(LeagueHubUiTextKeys.SectionRecommend).Count == 1 &&
                    LeagueHubNavigation.ViewsForSection(LeagueHubUiTextKeys.SectionRecommend)[0].Id == LeagueHubNavigation.Recommendation,
                "League Hub recommendation must stay consolidated into one novice-facing surface.");
            Require(LeagueHubNavigation.ViewsForSection(LeagueHubUiTextKeys.SectionEfficiency).Count == 1 &&
                    LeagueHubNavigation.ViewsForSection(LeagueHubUiTextKeys.SectionEfficiency)[0].Id == LeagueHubNavigation.Efficiency,
                "League Hub efficiency page must remain reachable from the unified window.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
