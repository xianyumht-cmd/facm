using FACM.Core.Mayhem;

namespace FACM.Infrastructure.Mayhem;

/// <summary>
/// Product query composition that keeps the 3.5 visible 5.5-second budget while running the base
/// ranking/build query and Tencent official patch validation in parallel.
/// </summary>
public sealed class MayhemOfficialPatchQueryService : IMayhemQueryService, IDisposable
{
    private readonly IMayhemQueryService _inner;
    private readonly IMayhemOfficialPatchService _official;
    private readonly bool _ownsInner;
    private readonly bool _ownsOfficial;
    private bool _disposed;

    public MayhemOfficialPatchQueryService()
        : this(new MayhemQueryService(), new TencentMayhemOfficialPatchService(), ownsInner: true, ownsOfficial: true)
    {
    }

    internal MayhemOfficialPatchQueryService(
        IMayhemQueryService inner,
        IMayhemOfficialPatchService official,
        bool ownsInner = false,
        bool ownsOfficial = false)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _official = official ?? throw new ArgumentNullException(nameof(official));
        _ownsInner = ownsInner;
        _ownsOfficial = ownsOfficial;
    }

    public async Task<MayhemChampionResult> QueryAsync(
        string input,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(MayhemQueryService.OverallBudget);
        var token = overall.Token;

        var officialTask = _official.FetchLatestAsync(token);
        MayhemChampionResult result;
        try
        {
            result = await _inner.QueryAsync(input, progress, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MayhemChampionResult
            {
                Query = (input ?? string.Empty).Trim(),
                ErrorMessage = "查询超过 5.5 秒，已返回前仍未得到可用结果。"
            };
        }

        if (!result.Success)
        {
            overall.Cancel();
            return result;
        }

        progress?.Report("正在校验国服当前版本…");
        MayhemOfficialPatchSnapshot? official = null;
        try
        {
            official = await officialTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        ApplyOfficialPatch(result, official, HasFullRankingState(result), result.Query);
        progress?.Report("国服版本校验完成");
        return result;
    }

    internal static void ApplyOfficialPatchForSmoke(
        MayhemChampionResult result,
        MayhemOfficialPatchSnapshot? official,
        bool fullStateFetched,
        string query) => ApplyOfficialPatch(result, official, fullStateFetched, query);

    private static void ApplyOfficialPatch(
        MayhemChampionResult result,
        MayhemOfficialPatchSnapshot? official,
        bool fullStateFetched,
        string query)
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

    private static bool HasFullRankingState(MayhemChampionResult result) =>
        !string.IsNullOrWhiteSpace(result.RankingPatch) ||
        result.SourceNote.Contains("平衡：ARAMMayhem 完整状态", StringComparison.Ordinal);

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsInner && _inner is IDisposable innerDisposable) innerDisposable.Dispose();
        if (_ownsOfficial && _official is IDisposable officialDisposable) officialDisposable.Dispose();
    }
}
