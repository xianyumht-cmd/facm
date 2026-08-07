using System.Collections.Generic;

namespace FACM.Mayhem
{
    internal sealed class MayhemTopChampion
    {
        public int Rank { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public double? WinRate { get; set; }
        public string Tier { get; set; }
    }

    internal sealed class MayhemChampionResult
    {
        public string Query { get; set; }
        public string ChampionName { get; set; }
        public string ChampionSlug { get; set; }
        public string Patch { get; set; }
        public int? Rank { get; set; }
        public string Tier { get; set; }
        public double? WinRate { get; set; }
        public double? PickRate { get; set; }
        public string BalanceSummary { get; set; }
        public string SkillOrder { get; set; }
        public List<string> CoreItems { get; set; } = new List<string>();
        public List<string> Augments { get; set; } = new List<string>();
        public List<MayhemTopChampion> TopTen { get; set; } = new List<MayhemTopChampion>();
        public string SourceUrl { get; set; }
        public string SourceNote { get; set; }
        public string ErrorMessage { get; set; }
    }
}
