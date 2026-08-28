namespace FACM.Core.League;

public sealed record LeagueHonorAttemptStatus(
    long GameId,
    string State,
    string Route,
    string Detail,
    int HttpStatus,
    int Attempts,
    long TargetSummonerId,
    string TargetPuuidSuffix,
    DateTimeOffset CompletedAtUtc);

/// <summary>
/// User-facing control boundary for post-game automation. The implementation consumes the shared
/// gameflow heartbeat; callers only enable the two approved behaviors and observe sanitized status.
/// </summary>
public interface ILeaguePostGameAutomationService
{
    bool AutoHonorEnabled { get; }
    bool AutoReturnLobbyEnabled { get; }
    LeagueHonorAttemptStatus? LastHonorStatus { get; }
    event EventHandler? StatusChanged;
    void Configure(bool autoHonor, bool autoReturnLobby);
}
