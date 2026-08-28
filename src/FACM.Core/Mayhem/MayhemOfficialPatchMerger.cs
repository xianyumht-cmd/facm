namespace FACM.Core.Mayhem;

/// <summary>
/// Pure merge policy for official CN patch facts. Stale ranking values are never presented as the
/// current patch: when the ranking patch differs from Tencent's verified patch, old numeric balance
/// text is suppressed and replaced with an explicit syncing/official-change message.
/// </summary>
public static class MayhemOfficialPatchMerger
{
    public static void Apply(
        MayhemChampionResult result,
        MayhemOfficialPatchSnapshot? official,
        bool fullStateFetched,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (official is null)
        {
            if (fullStateFetched && string.IsNullOrWhiteSpace(result.BalanceSummary))
                result.BalanceSummary = "当前版本未发现英雄专属平衡修正。";
            SetOfficialSourceNote(result, validated: false);
            return;
        }

        result.Patch = official.Patch;
        var changes = official.FindChampionChanges(result.ChampionName, query);
        var rankingPatch = result.RankingPatch;
        var fullStateCurrent = fullStateFetched &&
                               !string.IsNullOrWhiteSpace(rankingPatch) &&
                               PatchesMatch(rankingPatch, official.Patch);

        if (fullStateFetched && !string.IsNullOrWhiteSpace(rankingPatch) && !fullStateCurrent)
        {
            result.RankingPatch = string.Empty;
            result.BalanceSummary = changes.Count > 0
                ? "国服 " + official.Patch + " 本版本官方改动（完整当前状态同步中）：" + string.Join(" · ", changes)
                : "国服 " + official.Patch + " 平衡状态正在同步，暂不展示 " + rankingPatch + " 的旧数值。";
            SetOfficialSourceNote(result, validated: true);
            return;
        }

        if (fullStateCurrent)
        {
            if (string.IsNullOrWhiteSpace(result.BalanceSummary))
                result.BalanceSummary = "当前版本：无英雄专属修正。";
            SetOfficialSourceNote(result, validated: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(result.BalanceSummary) && changes.Count > 0)
            result.BalanceSummary = "国服 " + official.Patch + " 本版本官方改动（非完整当前状态）：" + string.Join(" · ", changes);

        SetOfficialSourceNote(result, validated: true);
    }

    private static bool PatchesMatch(string first, string second) =>
        Version.TryParse(first, out var left) && Version.TryParse(second, out var right) && left.Equals(right);

    private static void SetOfficialSourceNote(MayhemChampionResult result, bool validated)
    {
        var marker = validated ? "国服版本：腾讯官网已校验" : "国服版本：本次未校验";
        var note = result.SourceNote ?? string.Empty;
        if (note.Contains("国服版本：等待腾讯校验层", StringComparison.Ordinal))
        {
            result.SourceNote = note.Replace("国服版本：等待腾讯校验层", marker, StringComparison.Ordinal);
            return;
        }

        var parts = note.Split('；', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("国服版本：", StringComparison.Ordinal))
            .ToList();
        parts.Add(marker);
        result.SourceNote = string.Join("；", parts);
    }
}
