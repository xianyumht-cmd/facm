using System;
using System.Linq;
using FACM.League;
using FACM.AppHost.Modules;

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
            Require(SettingsModule.ShouldShowFirstUseForSmokeTest(false, false, false),
                "A genuinely fresh install must receive first-use guidance.");
            Require(!SettingsModule.ShouldShowFirstUseForSmokeTest(true, false, false) &&
                    !SettingsModule.ShouldShowFirstUseForSmokeTest(false, true, false) &&
                    !SettingsModule.ShouldShowFirstUseForSmokeTest(false, false, true),
                "Migrated, existing and recoverable settings must never be treated as first use.");

            foreach (var contextual in new[] { false, true })
            {
                var compactHeight = DesktopLauncherEnhancer.ResolveLauncherHeightForSmokeTest(contextual, false);
                var welcomeHeight = DesktopLauncherEnhancer.ResolveLauncherHeightForSmokeTest(contextual, true);
                var start = DesktopLauncherEnhancer.ResolveLauncherTopForSmokeTest(contextual, true);
                var footer = DesktopLauncherEnhancer.ResolveFooterTopForSmokeTest(contextual, true);
                Require(welcomeHeight - compactHeight == 122 &&
                        start - DesktopLauncherEnhancer.ResolveLauncherTopForSmokeTest(contextual, false) == 122 &&
                        footer - DesktopLauncherEnhancer.ResolveFooterTopForSmokeTest(contextual, false) == 122,
                    "First-use orientation must reserve height without moving or clipping existing launcher rows.");
                Require(start + 151 <= footer + 2 && footer + 38 < welcomeHeight,
                    "First-use orientation layout overlaps the launcher footer or window edge.");
            }

            LeagueHubNavigation.ValidateForSmokeTest();

            Require(DesktopLauncherEnhancer.TileCount == 4,
                "Control center must expose four sparse desktop-style primary shortcuts; directory/status belongs inside cleanup and repair.");
            Require(DesktopLauncherEnhancer.LauncherColumns == 2,
                "Floating-ball launcher must place its four primary routes in two compact rows.");
            Require(LeagueHubNavigation.Views.Count == 9,
                "LOL helper must expose five match/profile views plus recommendation, shortcuts, game repair and presence.");
            Require(LeagueHubNavigation.Views[0].Id == LeagueHubNavigation.Dashboard,
                "LOL helper must open from current status/dashboard.");
            Require(LeagueHubNavigation.ViewsForSection(LeagueHubUiTextKeys.SectionMatch).Any(
                    item => item.Id == LeagueHubNavigation.Profile),
                "LOL helper match section must expose the My GGman personal-stats surface.");
            Require(LeagueHubNavigation.ViewsForSection(LeagueHubUiTextKeys.SectionRecommend).Count == 1 &&
                    LeagueHubNavigation.ViewsForSection(LeagueHubUiTextKeys.SectionRecommend)[0].Id == LeagueHubNavigation.Recommendation,
                "LOL helper recommendation must stay consolidated into one surface.");

            var tools = LeagueHubNavigation.ViewsForSection(LeagueHubUiTextKeys.SectionEfficiency);
            Require(tools.Count == 3 &&
                    tools.Any(item => item.Id == LeagueHubNavigation.Efficiency) &&
                    tools.Any(item => item.Id == LeagueHubNavigation.Repair) &&
                    tools.Any(item => item.Id == LeagueHubNavigation.Presence),
                "LOL helper tools must expose shortcuts, game repair and online status in the unified window.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
