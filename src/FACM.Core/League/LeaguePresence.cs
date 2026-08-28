namespace FACM.Core.League;

/// <summary>
/// User-directed League chat presence modes carried forward from FACM 3.5.15.
/// </summary>
public enum LeaguePresenceMode
{
    Online,
    Away,
    DoNotDisturb,
    Mobile,
    Offline,
    DisplayInGame
}

public sealed record LeaguePresenceSnapshot(
    bool Connected,
    string Availability,
    string GameStatus,
    string StatusMessage,
    string DisplayName)
{
    public static LeaguePresenceSnapshot Unavailable { get; } = new(false, string.Empty, string.Empty, string.Empty, string.Empty);
}

public sealed record LeaguePresenceApplyResult(
    string Status,
    LeaguePresenceMode Mode,
    LeaguePresenceSnapshot? Observed)
{
    public bool Succeeded => string.Equals(Status, "success", StringComparison.Ordinal);
}

/// <summary>
/// Explicit user intent only. Implementations must not own a background rewrite/poll loop.
/// </summary>
public interface ILeaguePresenceService
{
    Task<LeaguePresenceSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    Task<LeaguePresenceApplyResult> ApplyAsync(
        LeaguePresenceMode mode,
        CancellationToken cancellationToken = default);
}
