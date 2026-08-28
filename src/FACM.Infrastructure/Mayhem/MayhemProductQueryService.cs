using FACM.Core.League;
using FACM.Core.Mayhem;

namespace FACM.Infrastructure.Mayhem;

/// <summary>
/// Product-facing Mayhem query pipeline. Presentation consumes only IMayhemQueryService while this
/// composition keeps public web reads, official patch validation, ARAM balance, build/augment
/// enrichment and LCU-first localization behind the Infrastructure boundary.
/// </summary>
public sealed class MayhemProductQueryService : IMayhemQueryService, IDisposable
{
    private readonly MayhemCachedPublicDataTransport _publicData;
    private readonly MayhemOfficialPatchQueryService _baseQuery;
    private readonly MayhemAugmentEnrichmentService _augments;
    private readonly MayhemBuildDetailsService _build;
    private readonly MayhemBaseAramBalanceService _baseBalance;
    private readonly MayhemDecisionLocalizationService _localization;
    private bool _disposed;

    public MayhemProductQueryService(string runtimeCacheDirectory, ILeagueReadGateway? leagueGateway)
    {
        if (string.IsNullOrWhiteSpace(runtimeCacheDirectory))
            throw new ArgumentException("Runtime cache directory is required.", nameof(runtimeCacheDirectory));

        _publicData = new MayhemCachedPublicDataTransport(runtimeCacheDirectory);
        _baseQuery = new MayhemOfficialPatchQueryService();
        _augments = new MayhemAugmentEnrichmentService(_publicData);
        _build = new MayhemBuildDetailsService(_publicData);
        _baseBalance = new MayhemBaseAramBalanceService(_publicData);
        _localization = new MayhemDecisionLocalizationService(leagueGateway, _publicData);
    }

    public async Task<MayhemChampionResult> QueryAsync(
        string input,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _baseQuery.QueryAsync(input, progress, cancellationToken).ConfigureAwait(false);
        if (!result.Success) return result;

        progress?.Report("正在读取强化符文、出装与基础 ARAM 平衡…");
        await Task.WhenAll(
            _augments.EnrichAsync(result, cancellationToken),
            _build.EnrichAsync(result, cancellationToken),
            _baseBalance.EnrichAsync(result, cancellationToken)).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("正在整理英雄、技能、装备和强化数据…");
        await _localization.EnrichAsync(result, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("查询完成");
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _baseQuery.Dispose();
        _publicData.Dispose();
    }
}
