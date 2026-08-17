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
        public const string Recommendation = "recommendation";
        public const string Efficiency = "efficiency";

        // Legacy view IDs remain stable for old code/tests, but are no longer novice-facing Hub tabs.
        public const string Advisor = "advisor";
        public const string Apply = "apply";
        public const string ItemSet = "item-set";

        private static readonly IReadOnlyList<LeagueHubViewDefinition> Definitions = new[]
        {
            new LeagueHubViewDefinition(Dashboard, LeagueHubUiTextKeys.SectionMatch, LeagueHubUiTextKeys.Dashboard),
            new LeagueHubViewDefinition(Player, LeagueHubUiTextKeys.SectionMatch, UiTextKeys.LeaguePlayerMenu),
            new LeagueHubViewDefinition(Live, LeagueHubUiTextKeys.SectionMatch, UiTextKeys.LeagueLiveMenu),
            new LeagueHubViewDefinition(Mayhem, LeagueHubUiTextKeys.SectionMatch, UiTextKeys.MayhemRanking),
            new LeagueHubViewDefinition(Recommendation, LeagueHubUiTextKeys.SectionRecommend, LeagueHubUiTextKeys.Recommendation),
            new LeagueHubViewDefinition(Efficiency, LeagueHubUiTextKeys.SectionEfficiency, LeagueEfficiencyUiTextKeys.Menu)
        };

        public static IReadOnlyList<LeagueHubViewDefinition> Views
        {
            get { return Definitions; }
        }

        public static IReadOnlyList<LeagueHubViewDefinition> ViewsForSection(string sectionKey)
        {
            return Definitions
                .Where(item => string.Equals(item.SectionKey, sectionKey, StringComparison.Ordinal))
                .ToArray();
        }

        internal static void ValidateForSmokeTest()
        {
            if (Definitions.Count != 6)
                throw new InvalidOperationException("League Hub must expose four match views plus one recommendation center and one efficiency view.");
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

            if (ViewsForSection(LeagueHubUiTextKeys.SectionRecommend).Count != 1 ||
                !string.Equals(ViewsForSection(LeagueHubUiTextKeys.SectionRecommend)[0].Id, Recommendation, StringComparison.Ordinal))
                throw new InvalidOperationException("Recommendation must be a single unified Hub surface.");

            var defaults = LeagueHubText.DefaultsForSmokeTest();
            if (expected.Any(key => !defaults.ContainsKey(key)) ||
                !defaults.ContainsKey(LeagueHubUiTextKeys.WindowTitle) ||
                !defaults.ContainsKey(LeagueHubUiTextKeys.Title) ||
                !defaults.ContainsKey(LeagueHubUiTextKeys.Recommendation))
                throw new InvalidOperationException("League Hub UI text defaults are incomplete.");

            foreach (var pair in LeagueRecommendationText.DefaultsForSmokeTest())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    throw new InvalidOperationException("League recommendation UI text contains an empty key/default.");
            }
        }
    }
}
