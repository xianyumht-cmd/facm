using System;
using System.Linq;
using FACM.League;

namespace FACM
{
    internal static class ShellUxSmokeTest
    {
        internal static void Validate()
        {
            // PerformanceContractSmokeTest runs before Application.EnableVisualStyles/Application.Run.
            // Keep this contract smoke pure: runtime menu objects are validated by MainForm, while CI
            // validates the fixed Shell roots, desktop-launcher composition and LOL helper information architecture.
            ShellMenuGroups.ValidateDefinitionForSmokeTest();
            DesktopLauncherEnhancer.ValidateDefinitionForSmokeTest();
            LeagueHubNavigation.ValidateForSmokeTest();

            Require(DesktopLauncherEnhancer.TileCount == 4,
                "Control center must expose four sparse desktop-style primary shortcuts; presence belongs inside LOL helper.");
            Require(LeagueHubNavigation.Views.Count == 7,
                "LOL helper must expose four match views plus recommendation, shortcuts and presence.");
            Require(LeagueHubNavigation.Views[0].Id == LeagueHubNavigation.Dashboard,
                "LOL helper must open from current status/dashboard.");
            Require(LeagueHubNavigation.ViewsForSection(LeagueHubUiTextKeys.SectionRecommend).Count == 1 &&
                    LeagueHubNavigation.ViewsForSection(LeagueHubUiTextKeys.SectionRecommend)[0].Id == LeagueHubNavigation.Recommendation,
                "LOL helper recommendation must stay consolidated into one surface.");

            var tools = LeagueHubNavigation.ViewsForSection(LeagueHubUiTextKeys.SectionEfficiency);
            Require(tools.Count == 2 &&
                    tools.Any(item => item.Id == LeagueHubNavigation.Efficiency) &&
                    tools.Any(item => item.Id == LeagueHubNavigation.Presence),
                "LOL helper tools must expose shortcuts and online status in the unified window.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
