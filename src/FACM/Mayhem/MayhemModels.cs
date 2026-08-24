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
        public string IconUrl { get; set; }
    }

    internal sealed class MayhemAugmentRow
    {
        public string Id { get; set; }
        public int Rank { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public string Rarity { get; set; }
        public double? WinRate { get; set; }
        public double? PickRate { get; set; }
        public int? Games { get; set; }
        public string Description { get; set; }
        public string IconUrl { get; set; }
    }

    internal sealed class MayhemDecisionRoute
    {
        public string Title { get; set; }
        public string AugmentName { get; set; }
        public string Hint { get; set; }
        public double Score { get; set; }
    }

    internal sealed class MayhemBuildItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string IconUrl { get; set; }
    }

    internal sealed class MayhemBuildPath
    {
        public int Rank { get; set; }
        public List<MayhemBuildItem> Items { get; set; } = new List<MayhemBuildItem>();
    }

    internal sealed class MayhemSkillPriority
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string IconUrl { get; set; }
    }

    internal sealed class MayhemChampionResult
    {
        public string Query { get; set; }
        public string ChampionName { get; set; }
        public string ChampionSlug { get; set; }
        public string Patch { get; set; }
        public string RankingPatch { get; set; }
        public int? Rank { get; set; }
        public string Tier { get; set; }
        public double? WinRate { get; set; }
        public double? PickRate { get; set; }
        public string BalanceSummary { get; set; }
        public string MayhemBalanceSummary { get; set; }
        public string BaseBalanceSummary { get; set; }
        public string BaseBalancePatch { get; set; }
        public string BaseBalanceStatus { get; set; }
        public string BaseBalanceErrorClass { get; set; }
        public bool BaseBalanceComplete { get; set; }
        public string SkillOrder { get; set; }
        public List<string> CoreItems { get; set; } = new List<string>();
        public List<MayhemBuildPath> CoreBuilds { get; set; } = new List<MayhemBuildPath>();
        public List<MayhemBuildItem> StarterItems { get; set; } = new List<MayhemBuildItem>();
        public List<MayhemBuildItem> BootItems { get; set; } = new List<MayhemBuildItem>();
        public List<MayhemBuildItem> SummonerSpells { get; set; } = new List<MayhemBuildItem>();
        public List<MayhemSkillPriority> SkillPriority { get; set; } = new List<MayhemSkillPriority>();
        public string BuildSourceStatus { get; set; }
        public string BuildSourceRoute { get; set; }
        public bool BuildSourceStale { get; set; }
        public List<string> Augments { get; set; } = new List<string>();
        public List<MayhemAugmentRow> AugmentRows { get; set; } = new List<MayhemAugmentRow>();
        public List<MayhemDecisionRoute> AugmentRoutes { get; set; } = new List<MayhemDecisionRoute>();
        public List<MayhemTopChampion> TopTen { get; set; } = new List<MayhemTopChampion>();
        public string ChampionIconUrl { get; set; }
        public string ChampionSplashUrl { get; set; }
        public Dictionary<string, string> SkillIconUrls { get; set; } = new Dictionary<string, string>();
        public List<string> CoreItemIconUrls { get; set; } = new List<string>();
        public List<string> AugmentIconUrls { get; set; } = new List<string>();
        public string SourceUrl { get; set; }
        public string RankingSourceUrl { get; set; }
        public string AugmentSourceUrl { get; set; }
        public string AugmentSourceRoute { get; set; }
        public bool AugmentSourceStale { get; set; }
        public string SourceNote { get; set; }
        public string ErrorMessage { get; set; }
    }
}
