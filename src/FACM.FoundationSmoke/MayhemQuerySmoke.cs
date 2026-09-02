using FACM.Core.Mayhem;
using FACM.Infrastructure.Mayhem;

internal static class MayhemQuerySmoke
{
    public static async Task RunAsync()
    {
        ValidateAliasCompatibility();
        ValidateNormalizationAndSlugGrammar();
        ValidateQueryBudgets();
        ValidateResultContract();
        await ValidateEmptyQueryIsLocalAsync();
        await MayhemAugmentSmoke.RunAsync();
        await MayhemBuildDetailsSmoke.RunAsync();
        await MayhemBaseBalanceSmoke.RunAsync();
        await MayhemLocalizationSmoke.RunAsync();
    }

    private static void ValidateAliasCompatibility()
    {
        Require(MayhemChampionAliases.TryResolve("寒冰", out var ashe) && ashe == "ashe",
            "3.5 alias 寒冰 -> ashe was not preserved.");
        Require(MayhemChampionAliases.TryResolve("滑板鞋", out var kalista) && kalista == "kalista",
            "3.5 alias 滑板鞋 -> kalista was not preserved.");
        Require(MayhemChampionAliases.TryResolve("VN", out var vayne) && vayne == "vayne",
            "ASCII alias lookup must remain case-insensitive.");
        Require(MayhemChampionAliases.TryResolve("Jarvan IV", out var jarvan) && jarvan == "jarvan-iv",
            "ASCII champion names must remain slug-compatible.");
        Require(!MayhemChampionAliases.TryResolve("完全未知的中文英雄", out _),
            "Unknown non-ASCII text must not be guessed into an external URL slug.");
    }

    private static void ValidateNormalizationAndSlugGrammar()
    {
        Require(MayhemChampionAliases.Normalize(" Kai'Sa ") == "kaisa",
            "Champion normalization changed from the 3.5 grammar.");
        Require(MayhemChampionAliases.Slugify("Dr. Mundo") == "dr-mundo",
            "Champion slugification changed from the 3.5 grammar.");
        Require(MayhemChampionAliases.Slugify("Aurelion__Sol") == "aurelion-sol",
            "Repeated separators were not collapsed.");
    }

    private static void ValidateQueryBudgets()
    {
        Require(MayhemQueryService.CacheDuration == TimeSpan.FromMinutes(10),
            "Mayhem query cache must preserve the 3.5 10-minute lifetime.");
        Require(MayhemQueryService.OverallBudget == TimeSpan.FromSeconds(5.5),
            "Mayhem base query must preserve the 3.5 5.5-second overall budget.");
    }

    private static void ValidateResultContract()
    {
        var result = new MayhemChampionResult
        {
            ChampionSlug = "ashe",
            ChampionName = "Ashe",
            Rank = 3,
            Tier = "S+",
            WinRate = 55.2,
            CoreItems = ["A", "B"],
            Augments = ["X"]
        };
        Require(result.Success, "A populated Mayhem result should be successful.");
        result.ErrorMessage = "blocked";
        Require(!result.Success, "Mayhem error state must override populated result fields.");
    }

    private static async Task ValidateEmptyQueryIsLocalAsync()
    {
        using var service = new MayhemQueryService();
        var result = await service.QueryAsync("   ");
        Require(!result.Success && result.ErrorMessage.Contains("请输入英雄", StringComparison.Ordinal),
            "Empty Mayhem query must fail locally before any public-source request.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
