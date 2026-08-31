namespace FACM.Core.League;

public enum LeagueBenchCandidateActionState
{
    Actionable,
    Busy,
    Unavailable
}

/// <summary>
/// One authoritative user-facing identity for an already observed Bench candidate. The champion
/// id remains available for the existing write path, but it is deliberately not the primary label.
/// </summary>
public sealed record LeagueBenchCandidate(
    int ChampionId,
    string DisplayName,
    string PortraitSource,
    LeagueBenchCandidateActionState ActionState)
{
    public bool IsActionable => ActionState == LeagueBenchCandidateActionState.Actionable;

    public string AccessibleName => DisplayName + " · Swap";
}

public static class LeagueBenchCandidatePresentation
{
    public static IReadOnlyList<LeagueBenchCandidate> Create(
        IEnumerable<int> championIds,
        IReadOnlyDictionary<int, LeagueChampionIdentity>? identities = null)
    {
        ArgumentNullException.ThrowIfNull(championIds);

        return championIds
            .Where(id => id > 0)
            .Distinct()
            .Select(id =>
            {
                LeagueChampionIdentity? identity = null;
                if (identities is not null)
                    identities.TryGetValue(id, out identity);
                var name = string.IsNullOrWhiteSpace(identity?.Name) ? "Unknown champion" : identity.Name.Trim();
                var portrait = identity?.IconPath?.Trim() ?? string.Empty;
                return new LeagueBenchCandidate(
                    id,
                    name,
                    portrait,
                    LeagueBenchCandidateActionState.Actionable);
            })
            .ToArray();
    }
}

public static class LeagueBenchSwapStripPolicy
{
    public const double HeightDip = 56d;
    public const double PortraitTileDip = 44d;
    public const double MaximumWidthDip = 600d;
    public const double MinimumWidthDip = 280d;

    public static bool IsEligible(LeagueWorkbenchLiveSnapshot? live)
    {
        if (live is null || live.State == LeagueWorkbenchDataState.Unavailable)
            return false;
        if (!string.Equals(live.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!live.BenchEnabled)
            return false;

        return live.BenchChampionIds.Any(id => id > 0);
    }

    public static int CountActionableCandidates(LeagueWorkbenchLiveSnapshot? live) =>
        live is null
            ? 0
            : live.BenchChampionIds.Where(id => id > 0).Distinct().Count();

    public static double ResolveWidthDip(int candidateCount)
    {
        var count = Math.Max(1, candidateCount);
        const double handleDip = 42d;
        const double statusDip = 92d;
        const double collapseDip = 34d;
        const double horizontalPaddingDip = 20d;
        const double gapDip = 6d;
        var contentWidth = handleDip + statusDip + collapseDip + horizontalPaddingDip +
                           count * PortraitTileDip + Math.Max(0, count - 1) * gapDip;
        return Math.Clamp(contentWidth, MinimumWidthDip, MaximumWidthDip);
    }
}

/// <summary>
/// Keeps manual dismissal local to one Champ Select/Bench context. A new context or a materially
/// changed candidate list is allowed to auto-show the strip again.
/// </summary>
public sealed class LeagueBenchContextDismissal
{
    private long _generation;
    private long? _dismissedGeneration;

    public long Generation => _generation;

    public void BeginNewContext()
    {
        _generation++;
        _dismissedGeneration = null;
    }

    public void ResetForMaterialCandidateChange() => _dismissedGeneration = null;

    public void DismissCurrentContext() => _dismissedGeneration = _generation;

    public bool CanAutoShow(bool hasActionableCandidates) =>
        hasActionableCandidates && _dismissedGeneration != _generation;
}
