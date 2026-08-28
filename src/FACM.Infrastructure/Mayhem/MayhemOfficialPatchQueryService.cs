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

    public MayhemOfficialPatchQueryService(
        IMayhemQueryService inner,
        IMayhemOfficialPatchService official)
        : this(inner, official, ownsInner: false, ownsOfficial: false)
    {
    }

    private MayhemOfficialPatchQueryService(
        IMayhemQueryService inner,
        IMayhemOfficialPatchService official,
        bool ownsInner,
        bool ownsOfficial)
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

        MayhemOfficialPatchMerger.Apply(result, official, HasFullRankingState(result), result.Query);
        progress?.Report("国服版本校验完成");
        return result;
    }

    private static bool HasFullRankingState(MayhemChampionResult result) =>
        !string.IsNullOrWhiteSpace(result.RankingPatch) ||
        result.SourceNote.Contains("平衡：ARAMMayhem 完整状态", StringComparison.Ordinal);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsInner && _inner is IDisposable innerDisposable) innerDisposable.Dispose();
        if (_ownsOfficial && _official is IDisposable officialDisposable) officialDisposable.Dispose();
    }
}
