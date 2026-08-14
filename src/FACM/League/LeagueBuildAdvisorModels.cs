using System;
using System.Collections.Generic;
using FACM.Performance;

namespace FACM.League
{
    internal sealed class LeagueBuildAdvisorSnapshot
    {
        public bool Connected { get; set; }
        public string Phase { get; set; }
        public LeagueActivityLevel Activity { get; set; }
        public string BudgetName { get; set; }
        public int QueueId { get; set; }
        public int ChampionId { get; set; }
        public string ChampionName { get; set; }
        public string Mode { get; set; }
        public string Position { get; set; }
        public string Source { get; set; }
        public string Version { get; set; }
        public string Status { get; set; }
        public bool FromCache { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public LeagueBuildRecommendation Recommendation { get; set; }
    }

    internal sealed class LeagueBuildRecommendation
    {
        public LeagueBuildRecommendation()
        {
            Rows = new List<LeagueBuildAdvisorRow>();
        }

        public string Tier { get; set; }
        public int Rank { get; set; }
        public double? WinRate { get; set; }
        public double? PickRate { get; set; }
        public double? BanRate { get; set; }
        public List<LeagueBuildAdvisorRow> Rows { get; private set; }
    }

    internal sealed class LeagueBuildAdvisorRow
    {
        public string Category { get; set; }
        public string Recommendation { get; set; }
        public string Evidence { get; set; }
    }

    internal sealed class LeagueBuildAdvisorCatalog
    {
        public LeagueBuildAdvisorCatalog()
        {
            Champions = new Dictionary<int, string>();
            Items = new Dictionary<int, string>();
            Spells = new Dictionary<int, string>();
            Perks = new Dictionary<int, string>();
        }

        public Dictionary<int, string> Champions { get; private set; }
        public Dictionary<int, string> Items { get; private set; }
        public Dictionary<int, string> Spells { get; private set; }
        public Dictionary<int, string> Perks { get; private set; }
    }
}
