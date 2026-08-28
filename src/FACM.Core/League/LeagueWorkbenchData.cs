namespace FACM.Core.League;

public enum LeagueWorkbenchDataState
{
    Unavailable,
    Partial,
    Ready
}

public sealed record LeagueWorkbenchAccount(
    string PuuId,
    long SummonerId,
    long AccountId,
    string GameName,
    string TagLine,
    string DisplayName,
    int SummonerLevel,
    int ProfileIconId)
{
    public string AccountName =>
        !string.IsNullOrWhiteSpace(GameName)
            ? string.IsNullOrWhiteSpace(TagLine) ? GameName : GameName + "#" + TagLine
            : DisplayName;
}

public sealed record LeagueWorkbenchQueue(
    int QueueId,
    string QueueName,
    string GameMode);

public sealed record LeagueWorkbenchLobbyMember(
    string PuuId,
    long SummonerId,
    string DisplayName,
    bool IsLocalPlayer);

public sealed record LeagueWorkbenchReadyCheck(
    string State,
    string PlayerResponse,
    int TimerMillisecondsLeft);

public sealed record LeagueWorkbenchDashboardSnapshot(
    LeagueWorkbenchDataState State,
    LeagueWorkbenchAccount? Account,
    LeagueWorkbenchQueue? Queue,
    IReadOnlyList<LeagueWorkbenchLobbyMember> LobbyMembers,
    LeagueWorkbenchReadyCheck? ReadyCheck,
    string Detail,
    DateTimeOffset UpdatedAtUtc)
{
    public static LeagueWorkbenchDashboardSnapshot Unavailable(string detail) =>
        new(
            LeagueWorkbenchDataState.Unavailable,
            null,
            null,
            Array.Empty<LeagueWorkbenchLobbyMember>(),
            null,
            detail,
            DateTimeOffset.UtcNow);
}

public sealed record LeagueWorkbenchRankedSummary(
    string QueueType,
    string Tier,
    string Division,
    int LeaguePoints,
    int Wins,
    int Losses)
{
    public int Games => Wins + Losses;
    public double WinRate => Games <= 0 ? 0d : Wins * 100d / Games;
}

public sealed record LeagueWorkbenchMatchSummary(
    long GameId,
    DateTimeOffset? GameCreation,
    int GameDurationSeconds,
    string GameMode,
    int QueueId,
    int ChampionId,
    string ChampionName,
    int Kills,
    int Deaths,
    int Assists,
    int CreepScore,
    bool Win,
    bool ParticipantResolved);

public sealed record LeagueWorkbenchPlayerSnapshot(
    LeagueWorkbenchDataState State,
    LeagueWorkbenchAccount? Account,
    LeagueWorkbenchRankedSummary? Ranked,
    IReadOnlyList<LeagueWorkbenchMatchSummary> RecentMatches,
    bool HasMoreMatches,
    string Detail,
    DateTimeOffset UpdatedAtUtc)
{
    public static LeagueWorkbenchPlayerSnapshot Unavailable(string detail) =>
        new(
            LeagueWorkbenchDataState.Unavailable,
            null,
            null,
            Array.Empty<LeagueWorkbenchMatchSummary>(),
            false,
            detail,
            DateTimeOffset.UtcNow);
}

public interface ILeagueWorkbenchDataSource
{
    Task<LeagueWorkbenchDashboardSnapshot> LoadDashboardAsync(CancellationToken cancellationToken = default);
    Task<LeagueWorkbenchPlayerSnapshot> LoadCurrentPlayerAsync(
        int startIndex = 0,
        int count = 10,
        CancellationToken cancellationToken = default);
}
