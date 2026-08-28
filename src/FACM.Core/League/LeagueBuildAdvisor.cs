namespace FACM.Core.League;

public enum LeagueBuildAdvisorState
{
    Unavailable,
    WaitingChampion,
    UnsupportedMode,
    WaitingChampSelect,
    Ready,
    InGameCache,
    InGameNoCache,
    ProviderUnavailable,
    Timeout
}

public sealed record LeagueBuildAdvisorRow(
    string Category,
    string Recommendation,
    string Evidence);

public sealed record LeagueBuildRecommendation(
    string Tier,
    int Rank,
    double? WinRate,
    double? PickRate,
    double? BanRate,
    IReadOnlyList<LeagueBuildAdvisorRow> Rows);

public sealed record LeagueBuildAdvisorSnapshot(
    LeagueBuildAdvisorState State,
    string Phase,
    int QueueId,
    int ChampionId,
    string ChampionName,
    string Mode,
    string Position,
    string Source,
    string Version,
    bool FromCache,
    LeagueBuildRecommendation? Recommendation,
    string Detail,
    DateTimeOffset UpdatedAtUtc)
{
    public static LeagueBuildAdvisorSnapshot Unavailable(string phase, string detail) =>
        new(
            LeagueBuildAdvisorState.Unavailable,
            phase,
            0,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            "OP.GG Global",
            string.Empty,
            false,
            null,
            detail,
            DateTimeOffset.UtcNow);
}

public interface ILeagueBuildAdvisorService
{
    Task<LeagueBuildAdvisorSnapshot> RefreshAsync(
        bool force = false,
        CancellationToken cancellationToken = default);
}
