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
        public const string Repair = "repair";
        public const string Presence = "presence";

        // Legacy view IDs remain stable for old code/tests, but are no longer novice-facing Hub tabs.
        public const string Advisor = "advisor";
        public const string Apply = "apply";
        public const string ItemSet = "item-set";

        private static readonly IReadOnlyList<LeagueHubViewDefinition> Definitions = new[]
        {
            new LeagueHubViewDefinition(Dashboard, LeagueHubUiTextKeys.SectionMatch, LeagueHubUiTextKeys.Dashboard),
            new LeagueHubViewDefinition(Player, LeagueHubUiTextKeys.SectionMatch, LeagueHubUiTextKeys.Player),
            new LeagueHubViewDefinition(Live, LeagueHubUiTextKeys.SectionMatch, LeagueHubUiTextKeys.Live),
            new LeagueHubViewDefinition(Mayhem, LeagueHubUiTextKeys.SectionMatch, LeagueHubUiTextKeys.Mayhem),
            new LeagueHubViewDefinition(Recommendation, LeagueHubUiTextKeys.SectionRecommend, LeagueHubUiTextKeys.Recommendation),
            new LeagueHubViewDefinition(Efficiency, LeagueHubUiTextKeys.SectionEfficiency, LeagueHubUiTextKeys.Efficiency),
            new LeagueHubViewDefinition(Repair, LeagueHubUiTextKeys.SectionEfficiency, LeagueHubUiTextKeys.Repair),
            new LeagueHubViewDefinition(Presence, LeagueHubUiTextKeys.SectionEfficiency, LeagueHubUiTextKeys.Presence)
        };

        private static readonly IReadOnlyDictionary<string, string[]> Related =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { Dashboard, new[] { Player, Live, Mayhem, Recommendation } },
                { Player, new[] { Live, Recommendation, Mayhem, Dashboard } },
                { Live, new[] { Recommendation, Mayhem, Repair, Player } },
                { Mayhem, new[] { Recommendation, Live, Player, Dashboard } },
                { Recommendation, new[] { Mayhem, Live, Efficiency, Player } },
                { Efficiency, new[] { Repair, Dashboard, Recommendation, Presence } },
                { Repair, new[] { Efficiency, Live, Dashboard, Presence } },
                { Presence, new[] { Efficiency, Repair, Dashboard, Player } }
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

        public static IReadOnlyList<LeagueHubViewDefinition> RelatedViews(string viewId)
        {
            string[] ids;
            if (string.IsNullOrWhiteSpace(viewId) || !Related.TryGetValue(viewId, out ids) || ids == null)
                return new LeagueHubViewDefinition[0];

            return ids
                .Select(id => Definitions.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal)))
                .Where(item => item != null)
                .ToArray();
        }

        internal static void ValidateForSmokeTest()
        {
            if (Definitions.Count != 8)
                throw new InvalidOperationException("LOL helper must expose four match views, recommendation, shortcuts, game repair and presence.");
            if (Definitions.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.SectionKey) || string.IsNullOrWhiteSpace(item.TextKey)))
                throw new InvalidOperationException("LOL helper navigation contains an empty contract field.");
            if (Definitions.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != Definitions.Count)
                throw new InvalidOperationException("LOL helper navigation contains duplicate view IDs.");

            var sections = Definitions.Select(item => item.SectionKey).Distinct(StringComparer.Ordinal).ToArray();
            var expected = new[]
            {
                LeagueHubUiTextKeys.SectionMatch,
                LeagueHubUiTextKeys.SectionRecommend,
                LeagueHubUiTextKeys.SectionEfficiency
            };
            if (sections.Length != expected.Length || expected.Any(key => !sections.Contains(key, StringComparer.Ordinal)))
                throw new InvalidOperationException("LOL helper must keep exactly three plain-language sections: match, recommendation and tools.");

            if (ViewsForSection(LeagueHubUiTextKeys.SectionRecommend).Count != 1 ||
                !string.Equals(ViewsForSection(LeagueHubUiTextKeys.SectionRecommend)[0].Id, Recommendation, StringComparison.Ordinal))
                throw new InvalidOperationException("Recommendation must stay as one unified surface.");

            var tools = ViewsForSection(LeagueHubUiTextKeys.SectionEfficiency);
            if (tools.Count != 3 ||
                !tools.Any(item => string.Equals(item.Id, Efficiency, StringComparison.Ordinal)) ||
                !tools.Any(item => string.Equals(item.Id, Repair, StringComparison.Ordinal)) ||
                !tools.Any(item => string.Equals(item.Id, Presence, StringComparison.Ordinal)))
                throw new InvalidOperationException("Tools must expose shortcuts, game repair and online status inside the LOL helper.");

            var knownIds = new HashSet<string>(Definitions.Select(item => item.Id), StringComparer.Ordinal);
            foreach (var pair in Related)
            {
                if (!knownIds.Contains(pair.Key))
                    throw new InvalidOperationException("LOL workbench context map has an unknown source view: " + pair.Key);
                if (pair.Value == null || pair.Value.Length == 0 || pair.Value.Length > 4)
                    throw new InvalidOperationException("LOL workbench context links must contain one to four actions.");
                if (pair.Value.Any(string.IsNullOrWhiteSpace) || pair.Value.Any(id => !knownIds.Contains(id)))
                    throw new InvalidOperationException("LOL workbench context map contains an unknown target view.");
                if (pair.Value.Any(id => string.Equals(id, pair.Key, StringComparison.Ordinal)))
                    throw new InvalidOperationException("LOL workbench context map cannot link a view to itself.");
                if (pair.Value.Distinct(StringComparer.Ordinal).Count() != pair.Value.Length)
                    throw new InvalidOperationException("LOL workbench context map contains duplicate targets.");
            }
            if (Related.Count != Definitions.Count)
                throw new InvalidOperationException("Every novice-facing LOL workbench view must expose contextual next actions.");

            var defaults = LeagueHubText.DefaultsForSmokeTest();
            if (expected.Any(key => !defaults.ContainsKey(key)) ||
                !defaults.ContainsKey(LeagueHubUiTextKeys.WindowTitle) ||
                !defaults.ContainsKey(LeagueHubUiTextKeys.Title) ||
                !defaults.ContainsKey(LeagueHubUiTextKeys.Repair) ||
                !defaults.ContainsKey(LeagueHubUiTextKeys.Presence) ||
                !defaults.ContainsKey(LeagueHubUiTextKeys.Recommendation) ||
                !defaults.ContainsKey(LeagueHubUiTextKeys.ContextTitle) ||
                !defaults.ContainsKey(LeagueHubUiTextKeys.ContextHint))
                throw new InvalidOperationException("LOL helper UI text defaults are incomplete.");

            foreach (var pair in LeagueRecommendationText.DefaultsForSmokeTest())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    throw new InvalidOperationException("League recommendation UI text contains an empty key/default.");
            }
        }
    }
}
