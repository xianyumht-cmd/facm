namespace FACM.Core.League;

/// <summary>
/// User-facing control boundary for matchmaking automation. The implementation must consume the
/// shared gameflow owner; callers only enable or disable the two approved behaviors.
/// </summary>
public interface ILeagueMatchmakingAutomationService
{
    bool AutoSearchEnabled { get; }
    bool AutoAcceptEnabled { get; }
    void Configure(bool autoSearch, bool autoAccept);
}
