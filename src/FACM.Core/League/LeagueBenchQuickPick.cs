namespace FACM.Core.League;

public enum LeagueBenchSwapRoute
{
    Legacy,
    TeamBuilder
}

public enum LeagueBenchSwapStatus
{
    Success,
    SessionUnavailable,
    BenchDisabled,
    TargetUnavailable,
    WriteRejected,
    VerificationFailed
}

public sealed record LeagueBenchQuickPickState(
    bool SessionAvailable,
    bool BenchEnabled,
    int LocalPlayerCellId,
    int LocalChampionId,
    LeagueBenchSwapRoute SwapRoute,
    IReadOnlyList<int> ChampionIds)
{
    public static LeagueBenchQuickPickState Unavailable { get; } = new(
        false,
        false,
        0,
        0,
        LeagueBenchSwapRoute.Legacy,
        Array.Empty<int>());
}

public sealed record LeagueChampionIdentity(
    int ChampionId,
    string Name,
    string IconPath);

public sealed record LeagueBenchSwapResult(
    LeagueBenchSwapStatus Status,
    int ChampionId,
    int StatusCode,
    long ElapsedMilliseconds)
{
    public bool Success => Status == LeagueBenchSwapStatus.Success;
}

public interface ILeagueBenchQuickPickService
{
    Task<LeagueBenchQuickPickState> RefreshAsync(CancellationToken cancellationToken = default);
    void SetSwapRoute(LeagueBenchSwapRoute route);
    Task<IReadOnlyDictionary<int, LeagueChampionIdentity>> LoadChampionIdentitiesAsync(
        IReadOnlyCollection<int> championIds,
        CancellationToken cancellationToken = default);
    Task<byte[]?> LoadChampionIconAsync(int championId, CancellationToken cancellationToken = default);
    Task<LeagueBenchSwapResult> TrySwapAsync(int championId, CancellationToken cancellationToken = default);
}

public static class LeagueBenchQuickPickPolling
{
    public static TimeSpan ResolveDelay(bool benchActive, bool inGame, bool hidden)
    {
        if (hidden) return TimeSpan.FromSeconds(1);
        if (inGame) return TimeSpan.FromSeconds(5);
        return benchActive ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(750);
    }
}
