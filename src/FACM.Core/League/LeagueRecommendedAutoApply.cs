namespace FACM.Core.League;

public sealed record LeagueRecommendedAutoApplyStatus(
    string State,
    string Detail,
    string Fingerprint,
    DateTimeOffset UpdatedAtUtc)
{
    public static LeagueRecommendedAutoApplyStatus Disabled() =>
        new("disabled", "disabled", string.Empty, DateTimeOffset.UtcNow);
}

public sealed class LeagueRecommendedAutoApplyStatusChangedEventArgs(
    LeagueRecommendedAutoApplyStatus status) : EventArgs
{
    public LeagueRecommendedAutoApplyStatus Status { get; } =
        status ?? throw new ArgumentNullException(nameof(status));
}

/// <summary>
/// Process-scoped automation boundary for FACM's recommended League setup. Implementations must
/// consume the shared gameflow heartbeat rather than own a second phase polling loop.
/// </summary>
public interface ILeagueRecommendedAutoApplyService
{
    bool Enabled { get; }
    LeagueRecommendedAutoApplyStatus LastStatus { get; }
    event EventHandler<LeagueRecommendedAutoApplyStatusChangedEventArgs>? StatusChanged;
    void Configure(bool enabled);
}
