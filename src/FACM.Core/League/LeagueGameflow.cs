using FACM.Core.Performance;
using FACM.Core.State;

namespace FACM.Core.League;

public sealed record LeagueGameflowMapping(
    LeagueConnectionState ConnectionState,
    string Phase,
    LeagueProductState ProductState,
    LeagueActivityLevel Activity);

public static class LeagueGameflowPhaseMapper
{
    public static LeagueGameflowMapping Map(
        string? phase,
        LeagueConnectionState connectionState,
        bool phaseReadSucceeded)
    {
        var normalized = NormalizePhase(phase);
        if (connectionState == LeagueConnectionState.NotRunning)
            return Result(connectionState, normalized, LeagueProductState.NotRunning, LeagueActivityLevel.None);
        if (connectionState == LeagueConnectionState.Connecting)
            return Result(connectionState, normalized, LeagueProductState.Connecting, LeagueActivityLevel.None);
        if (connectionState == LeagueConnectionState.Unavailable)
            return Result(connectionState, normalized, LeagueProductState.ClientError, LeagueActivityLevel.Client);
        if (!phaseReadSucceeded)
            return Result(connectionState, normalized, LeagueProductState.ClientError, LeagueActivityLevel.Client);

        if (EqualsPhase(normalized, "Matchmaking"))
            return Result(connectionState, normalized, LeagueProductState.Matchmaking, LeagueActivityLevel.Queueing);
        if (EqualsPhase(normalized, "ReadyCheck"))
            return Result(connectionState, normalized, LeagueProductState.ReadyCheck, LeagueActivityLevel.Queueing);
        if (EqualsPhase(normalized, "ChampSelect"))
            return Result(connectionState, normalized, LeagueProductState.ChampSelect, LeagueActivityLevel.ChampSelect);
        if (IsInGame(normalized))
            return Result(connectionState, normalized, LeagueProductState.InGame, LeagueActivityLevel.InGame);
        if (IsPostGame(normalized))
            return Result(connectionState, normalized, LeagueProductState.PostGame, LeagueActivityLevel.Client);

        // Connected idle/unknown phases are client-side states. The public Product State vocabulary
        // intentionally uses Lobby as the safe connected-idle bucket rather than exposing LCU internals.
        return Result(connectionState, normalized, LeagueProductState.Lobby, LeagueActivityLevel.Client);
    }

    private static bool IsInGame(string phase) =>
        EqualsPhase(phase, "InProgress") ||
        EqualsPhase(phase, "WatchInProgress") ||
        EqualsPhase(phase, "Reconnect") ||
        EqualsPhase(phase, "GameStart");

    private static bool IsPostGame(string phase) =>
        EqualsPhase(phase, "WaitingForStats") ||
        EqualsPhase(phase, "PreEndOfGame") ||
        EqualsPhase(phase, "EndOfGame");

    private static bool EqualsPhase(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePhase(string? phase) => (phase ?? string.Empty).Trim().Trim('"');

    private static LeagueGameflowMapping Result(
        LeagueConnectionState connectionState,
        string phase,
        LeagueProductState productState,
        LeagueActivityLevel activity) =>
        new(connectionState, phase, productState, activity);
}

public static class LeagueGameflowCadence
{
    public static TimeSpan Resolve(LeagueGameflowMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        return mapping.ProductState switch
        {
            LeagueProductState.ChampSelect => TimeSpan.FromSeconds(2),
            LeagueProductState.Matchmaking or LeagueProductState.ReadyCheck => TimeSpan.FromSeconds(3),
            LeagueProductState.InGame => TimeSpan.FromSeconds(10),
            LeagueProductState.NotRunning or LeagueProductState.Connecting or LeagueProductState.ClientError => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(5)
        };
    }
}

public sealed record LeagueGameflowSnapshot(
    DateTimeOffset TimestampUtc,
    LeagueConnectionState ConnectionState,
    string Phase,
    LeagueProductState ProductState,
    LeagueActivityLevel Activity);

public sealed class LeagueGameflowChangedEventArgs(
    LeagueGameflowSnapshot? previous,
    LeagueGameflowSnapshot current) : EventArgs
{
    public LeagueGameflowSnapshot? Previous { get; } = previous;
    public LeagueGameflowSnapshot Current { get; } = current ?? throw new ArgumentNullException(nameof(current));
}

public interface ILeagueGameflowReader
{
    LeagueGameflowSnapshot? Current { get; }
    event EventHandler<LeagueGameflowChangedEventArgs>? Changed;
}
