namespace FACM.Core.Mayhem;

public sealed class MayhemTopChampion
{
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public double? WinRate { get; set; }
    public string Tier { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
}

public sealed class MayhemAugmentRow
{
    public string Id { get; set; } = string.Empty;
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
    public double? WinRate { get; set; }
    public double? PickRate { get; set; }
    public int? Games { get; set; }
    public string Description { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
}

public sealed class MayhemDecisionRoute
{
    public string Title { get; set; } = string.Empty;
    public string AugmentName { get; set; } = string.Empty;
    public string Hint { get; set; } = string.Empty;
    public double Score { get; set; }
}

public sealed class MayhemBuildItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
}

public sealed class MayhemBuildPath
{
    public int Rank { get; set; }
    public List<MayhemBuildItem> Items { get; set; } = [];
}

public sealed class MayhemSkillPriority
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
}

public sealed class MayhemRuneRecommendation
{
    public string PrimaryTree { get; set; } = string.Empty;
    public string Keystone { get; set; } = string.Empty;
    public List<string> PrimaryRunes { get; set; } = [];
    public string SecondaryTree { get; set; } = string.Empty;
    public List<string> SecondaryRunes { get; set; } = [];
    public List<string> StatShards { get; set; } = [];
    public double? WinRate { get; set; }
    public double? PickRate { get; set; }
    public int? Games { get; set; }

    public bool HasLocalizedContent =>
        !string.IsNullOrWhiteSpace(PrimaryTree) ||
        !string.IsNullOrWhiteSpace(Keystone) ||
        PrimaryRunes.Count > 0 ||
        !string.IsNullOrWhiteSpace(SecondaryTree) ||
        SecondaryRunes.Count > 0 ||
        StatShards.Count > 0;
}

/// <summary>
/// Platform-neutral FACM 3.5 ARAM Mayhem query result. Rendering and clipboard/file operations
/// deliberately stay outside Core so WinUI can reuse the same data without carrying WinForms/GDI.
/// </summary>
public sealed class MayhemChampionResult
{
    public string Query { get; set; } = string.Empty;
    public int ChampionId { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public string ChampionSlug { get; set; } = string.Empty;
    public string Patch { get; set; } = string.Empty;
    public string RankingPatch { get; set; } = string.Empty;
    public int? Rank { get; set; }
    public string Tier { get; set; } = string.Empty;
    public double? WinRate { get; set; }
    public double? PickRate { get; set; }
    public int? SampleSize { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public string BalanceSummary { get; set; } = string.Empty;
    public string MayhemBalanceSummary { get; set; } = string.Empty;
    public string BaseBalanceSummary { get; set; } = string.Empty;
    public string BaseBalancePatch { get; set; } = string.Empty;
    public string BaseBalanceStatus { get; set; } = string.Empty;
    public string BaseBalanceErrorClass { get; set; } = string.Empty;
    public bool BaseBalanceComplete { get; set; }
    public string SkillOrder { get; set; } = string.Empty;
    public List<string> CoreItems { get; set; } = [];
    public List<MayhemBuildPath> CoreBuilds { get; set; } = [];
    public List<MayhemBuildItem> StarterItems { get; set; } = [];
    public List<MayhemBuildItem> BootItems { get; set; } = [];
    public List<MayhemBuildItem> SummonerSpells { get; set; } = [];
    public List<MayhemSkillPriority> SkillPriority { get; set; } = [];
    public MayhemRuneRecommendation? RuneRecommendation { get; set; }
    public string BuildSourceStatus { get; set; } = string.Empty;
    public string BuildSourceRoute { get; set; } = string.Empty;
    public bool BuildSourceStale { get; set; }
    public List<string> Augments { get; set; } = [];
    public List<MayhemAugmentRow> AugmentRows { get; set; } = [];
    public List<MayhemDecisionRoute> AugmentRoutes { get; set; } = [];
    public List<MayhemTopChampion> TopTen { get; set; } = [];
    public string ChampionIconUrl { get; set; } = string.Empty;
    public string ChampionSplashUrl { get; set; } = string.Empty;
    public Dictionary<string, string> SkillIconUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CoreItemIconUrls { get; set; } = [];
    public List<string> AugmentIconUrls { get; set; } = [];
    public string SourceUrl { get; set; } = string.Empty;
    public string RankingSourceUrl { get; set; } = string.Empty;
    public string AugmentSourceUrl { get; set; } = string.Empty;
    public string AugmentSourceRoute { get; set; } = string.Empty;
    public bool AugmentSourceStale { get; set; }
    public string SourceNote { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;

    public bool Success => string.IsNullOrWhiteSpace(ErrorMessage) && !string.IsNullOrWhiteSpace(ChampionSlug);
}

public interface IMayhemQueryService
{
    Task<MayhemChampionResult> QueryAsync(
        string input,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
