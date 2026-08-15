using System;
using System.Collections.Generic;
using System.Linq;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueHubViewDefinition
    {
        public LeagueHubViewDefinition(string id, string sectionKey, string textKey)
        {
            Id = id ?? string.Empty;
            SectionKey = sectionKey ?? string.Empty;
            TextKey = textKey ?? string.Empty;
        }

        public string Id { get; private set; }
        public string SectionKey { get; private set; }
        public string TextKey { get; private set; }
    }

    internal static class LeagueHubNavigation
    {
        public const string Dashboard = "dashboard";
        public const string Player = "player";
        public const string Live = "live";
        public const string Mayhem = "mayhem";
        public const string Advisor = "advisor";
        public const string Apply = "apply";
        public const string ItemSet = "item-set";
        public const string Efficiency = "efficiency";

        private static readonly IReadOnlyList<LeagueHubViewDefinition> Definitions = new[]
        {
            new LeagueHubViewDefinition(Dashboard, LeagueHubUiTextKeys.SectionMatch, LeagueHubUiTextKeys.Dashboard),
            new LeagueHubViewDefinition(Player, LeagueHubUiTextKeys.SectionMatch, UiTextKeys.LeaguePlayerMenu),
            new LeagueHubViewDefinition(Live, LeagueHubUiTextKeys.SectionMatch, UiTextKeys.LeagueLiveMenu),
            new LeagueHubViewDefinition(Mayhem, LeagueHubUiTextKeys.SectionMatch, UiTextKeys.MayhemRanking),
            new LeagueHubViewDefinition(Advisor, LeagueHubUiTextKeys.SectionRecommend, UiTextKeys.LeagueAdvisorMenu),
            new LeagueHubViewDefinition(Apply, LeagueHubUiTextKeys.SectionRecommend, LeagueBuildApplyUiTextKeys.Menu),
            new LeagueHubViewDefinition(ItemSet, LeagueHubUiTextKeys.SectionRecommend, LeagueItemSetUiTextKeys.Menu),
            new LeagueHubViewDefinition(Efficiency, LeagueHubUiTextKeys.SectionEfficiency, LeagueEfficiencyUiTextKeys.Menu)
        };

        public static IReadOnlyList<LeagueHubViewDefinition> Views
        {
            get { return Definitions; }
        }

        internal static void ValidateForSmokeTest()
        {
            if (Definitions.Count != 8)
                throw new InvalidOperationException("League Hub must expose exactly eight accepted views.");
            if (Definitions.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.SectionKey) || string.IsNullOrWhiteSpace(item.TextKey)))
                throw new InvalidOperationException("League Hub navigation contains an empty contract field.");
            if (Definitions.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != Definitions.Count)
                throw new InvalidOperationException("League Hub navigation contains duplicate view IDs.");

            var sections = Definitions.Select(item => item.SectionKey).Distinct(StringComparer.Ordinal).ToArray();
            var expected = new[]
            {
                LeagueHubUiTextKeys.SectionMatch,
                LeagueHubUiTextKeys.SectionRecommend,
                LeagueHubUiTextKeys.SectionEfficiency
            };
            if (sections.Length != expected.Length || expected.Any(key => !sections.Contains(key, StringComparer.Ordinal)))
                throw new InvalidOperationException("League Hub must keep exactly three novice-facing sections: match, recommendation and efficiency.");

            var defaults = LeagueHubText.DefaultsForSmokeTest();
            if (expected.Any(key => !defaults.ContainsKey(key)) || !defaults.ContainsKey(LeagueHubUiTextKeys.WindowTitle) || !defaults.ContainsKey(LeagueHubUiTextKeys.Title))
                throw new InvalidOperationException("League Hub UI text defaults are incomplete.");
        }
    }
}
