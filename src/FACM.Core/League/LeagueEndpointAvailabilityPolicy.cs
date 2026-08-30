using FACM.Core.State;

namespace FACM.Core.League;

public enum League404Classification
{
    ExpectedUnavailable,
    UnexpectedFailure
}

/// <summary>
/// Classifies only the known optional/session-shaped LCU 404s. Unknown endpoints remain failures.
/// A 404 for an endpoint belonging to the currently observed gameflow phase is unexpected; the same
/// response outside that phase is a normal unavailable feature state.
/// </summary>
public static class LeagueEndpointAvailabilityPolicy
{
    public static League404Classification Classify404(
        string endpoint,
        string? gameflowPhase,
        LeagueConnectionState connectionState)
    {
        if (connectionState != LeagueConnectionState.Connected)
            return IsKnownOptionalEndpoint(endpoint)
                ? League404Classification.ExpectedUnavailable
                : League404Classification.UnexpectedFailure;

        var phase = Normalize(gameflowPhase);
        if (string.IsNullOrWhiteSpace(phase))
            return IsKnownOptionalEndpoint(endpoint)
                ? League404Classification.ExpectedUnavailable
                : League404Classification.UnexpectedFailure;

        return EndpointBelongsToPhase(endpoint, phase)
            ? League404Classification.UnexpectedFailure
            : IsKnownOptionalEndpoint(endpoint)
                ? League404Classification.ExpectedUnavailable
                : League404Classification.UnexpectedFailure;
    }

    public static bool IsKnownOptionalEndpoint(string endpoint)
    {
        var path = NormalizeEndpoint(endpoint);
        return path is
            "/lol-gameflow/v1/session" or
            "/lol-lobby/v2/lobby" or
            "/lol-matchmaking/v1/ready-check" or
            "/lol-matchmaking/v1/search" or
            "/lol-champ-select/v1/session" or
            "/lol-lobby-team-builder/champ-select/v1/session" or
            "/lol-end-of-game/v1/eog-stats-block" or
            "/lol-presence/v1/presences";
    }

    private static bool EndpointBelongsToPhase(string endpoint, string phase)
    {
        var path = NormalizeEndpoint(endpoint);
        if (path == "/lol-gameflow/v1/session")
            return phase is "Matchmaking" or "ReadyCheck" or "ChampSelect" or "InProgress" or
                "WatchInProgress" or "Reconnect" or "GameStart" or "WaitingForStats" or
                "PreEndOfGame" or "EndOfGame";
        if (path == "/lol-lobby/v2/lobby")
            return phase is "Lobby" or "Matchmaking" or "ReadyCheck" or "ChampSelect";
        if (path == "/lol-matchmaking/v1/ready-check")
            return phase == "ReadyCheck";
        if (path == "/lol-matchmaking/v1/search")
            return phase == "Matchmaking";
        if (path is "/lol-champ-select/v1/session" or "/lol-lobby-team-builder/champ-select/v1/session")
            return phase == "ChampSelect";
        if (path == "/lol-end-of-game/v1/eog-stats-block")
            return phase is "WaitingForStats" or "PreEndOfGame" or "EndOfGame";
        return false;
    }

    private static string Normalize(string? phase) => (phase ?? string.Empty).Trim().Trim('"');

    private static string NormalizeEndpoint(string? endpoint)
    {
        var value = (endpoint ?? string.Empty).Trim();
        return value.StartsWith('/') ? value : "/" + value;
    }
}
