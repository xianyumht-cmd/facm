using FACM.Core.Desktop;
using FACM.Core.State;

namespace FACM.Core.League;

/// <summary>
/// The process-level, read-only Bench context consumed by the desktop surface. It is deliberately
/// independent of the detailed League Workbench so the compact surface can react even when that
/// page has never been opened.
/// </summary>
public sealed record LeagueBenchRuntimeSnapshot(
    LeagueProductState ProductState,
    string Phase,
    long ContextGeneration,
    bool SessionAvailable,
    bool BenchEnabled,
    int LocalChampionId,
    LeagueBenchSwapRoute SwapRoute,
    IReadOnlyList<int> ChampionIds,
    bool IsLatched,
    DateTimeOffset UpdatedAtUtc,
    string SourceOwner,
    string SourceFreshness)
{
    public bool IsChampSelect => ProductState == LeagueProductState.ChampSelect;

    public int CandidateCount => ChampionIds.Count(id => id > 0);

    public bool HasActionableCandidates => IsChampSelect &&
                                           BenchEnabled &&
                                           CandidateCount > 0;

    public static LeagueBenchRuntimeSnapshot Unavailable { get; } = new(
        LeagueProductState.NotRunning,
        string.Empty,
        0,
        false,
        false,
        0,
        LeagueBenchSwapRoute.Legacy,
        Array.Empty<int>(),
        false,
        DateTimeOffset.MinValue,
        "LeagueBenchRuntimeObserver",
        "no-observation");
}

public sealed class LeagueBenchRuntimeChangedEventArgs(
    LeagueBenchRuntimeSnapshot? previous,
    LeagueBenchRuntimeSnapshot current,
    string reason) : EventArgs
{
    public LeagueBenchRuntimeSnapshot? Previous { get; } = previous;
    public LeagueBenchRuntimeSnapshot Current { get; } = current ?? throw new ArgumentNullException(nameof(current));
    public string Reason { get; } = reason ?? string.Empty;
}

public interface ILeagueBenchRuntimeState
{
    LeagueBenchRuntimeSnapshot Current { get; }
    event EventHandler<LeagueBenchRuntimeChangedEventArgs>? Changed;
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A low-noise surface decision record. The App maps this to the bounded JSONL diagnostic sink;
/// the fields intentionally contain only state and ownership facts, never LCU credentials.
/// </summary>
public sealed record LeagueBenchSurfaceEvaluation(
    string Phase,
    long ContextGeneration,
    bool BenchEnabled,
    int CandidateCount,
    string CurrentSurface,
    bool IsLatched,
    string Decision,
    string SourceOwner,
    string SourceFreshness);

public static class LeagueBenchStripInteractionPolicy
{
    public static bool SuppressOutsideDismissal(FacmSurfaceMode mode) =>
        mode == FacmSurfaceMode.ChampSelectStrip;

    public static bool SuppressCollapse(FacmSurfaceMode mode) =>
        mode == FacmSurfaceMode.ChampSelectStrip;

    public static bool PreserveAfterCandidateClick(FacmSurfaceMode mode) =>
        mode == FacmSurfaceMode.ChampSelectStrip;

    public static bool PreserveAfterHandleClick(FacmSurfaceMode mode) =>
        mode == FacmSurfaceMode.ChampSelectStrip;
}
