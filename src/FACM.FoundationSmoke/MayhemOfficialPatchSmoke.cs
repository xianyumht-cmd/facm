using FACM.Core.Mayhem;
using FACM.Infrastructure.Mayhem;

internal static class MayhemOfficialPatchSmoke
{
    public static async Task RunAsync()
    {
        ValidateChampionChangeLookup();
        ValidateStaleRankingIsSuppressed();
        ValidateCurrentPatchNoSpecificCorrection();
        ValidateUnavailableOfficialSourceDegradesCleanly();
        await ValidateCompositeQueryAsync();
    }

    private static void ValidateChampionChangeLookup()
    {
        var snapshot = Official("15.18", new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["寒冰射手 艾希"] = ["造成伤害 +5% → +3%"]
        });
        var changes = snapshot.FindChampionChanges("艾希", "寒冰");
        Require(changes.Count == 1 && changes[0].Contains("+3%", StringComparison.Ordinal),
            "Official patch champion lookup lost 3.5 normalized-name matching.");
    }

    private static void ValidateStaleRankingIsSuppressed()
    {
        var result = BaseResult("15.17");
        result.BalanceSummary = "造成伤害 +5%";
        var official = Official("15.18", new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["艾希"] = ["造成伤害 +5% → +3%"]
        });

        MayhemOfficialPatchMerger.Apply(result, official, fullStateFetched: true, "艾希");

        Require(result.Patch == "15.18", "Official patch did not become the visible CN patch.");
        Require(result.RankingPatch.Length == 0, "Stale ranking patch was still presented as current.");
        Require(result.BalanceSummary.Contains("完整当前状态同步中", StringComparison.Ordinal) &&
                result.BalanceSummary.Contains("+3%", StringComparison.Ordinal),
            "Stale ranking values were not replaced by the verified official change.");
        Require(result.SourceNote.Contains("腾讯官网已校验", StringComparison.Ordinal),
            "Verified Tencent source was not surfaced in the source note.");
    }

    private static void ValidateCurrentPatchNoSpecificCorrection()
    {
        var result = BaseResult("15.18");
        result.BalanceSummary = string.Empty;
        MayhemOfficialPatchMerger.Apply(result, Official("15.18"), fullStateFetched: true, "艾希");
        Require(result.BalanceSummary == "当前版本：无英雄专属修正。",
            "Current full-state ranking did not preserve the no-specific-correction state.");
    }

    private static void ValidateUnavailableOfficialSourceDegradesCleanly()
    {
        var result = BaseResult("15.18");
        result.BalanceSummary = string.Empty;
        MayhemOfficialPatchMerger.Apply(result, null, fullStateFetched: true, "艾希");
        Require(result.BalanceSummary == "当前版本未发现英雄专属平衡修正。",
            "Unavailable official source did not retain the 3.5 full-state fallback.");
        Require(result.SourceNote.Contains("本次未校验", StringComparison.Ordinal),
            "Unavailable Tencent source was incorrectly reported as verified.");
    }

    private static async Task ValidateCompositeQueryAsync()
    {
        var inner = new FakeQuery { Result = BaseResult("15.18") };
        var official = new FakeOfficial { Snapshot = Official("15.18") };
        using var service = new MayhemOfficialPatchQueryService(inner, official);
        var result = await service.QueryAsync("艾希");

        Require(inner.Calls == 1 && official.Calls == 1,
            "Composite Mayhem query did not invoke base and official sources exactly once.");
        Require(result.Success && result.Patch == "15.18",
            "Composite Mayhem query did not merge the official patch into a successful base result.");
        Require(result.SourceNote.Contains("腾讯官网已校验", StringComparison.Ordinal),
            "Composite Mayhem query did not surface verified source state.");
    }

    private static MayhemChampionResult BaseResult(string rankingPatch) => new()
    {
        Query = "艾希",
        ChampionName = "艾希",
        ChampionSlug = "ashe",
        RankingPatch = rankingPatch,
        SourceNote = "排行：Hexdata 国内优先；攻略：OP.GG 已补充；平衡：ARAMMayhem 完整状态；国服版本：等待腾讯校验层"
    };

    private static MayhemOfficialPatchSnapshot Official(
        string patch,
        Dictionary<string, List<string>>? changes = null) => new()
    {
        Patch = patch,
        SourceUrl = "https://lol.qq.com/gicp/news/410/37092739.html",
        ChampionChanges = changes ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeQuery : IMayhemQueryService
    {
        public MayhemChampionResult Result { get; set; } = BaseResult("15.18");
        public int Calls { get; private set; }

        public Task<MayhemChampionResult> QueryAsync(
            string input,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeOfficial : IMayhemOfficialPatchService
    {
        public MayhemOfficialPatchSnapshot? Snapshot { get; set; }
        public int Calls { get; private set; }

        public Task<MayhemOfficialPatchSnapshot?> FetchLatestAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Snapshot);
        }
    }
}
