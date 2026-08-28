namespace FACM.Core.League;

public enum LeagueConnectionState
{
    NotRunning,
    Connecting,
    Connected,
    Unavailable
}

public sealed record LeagueSessionDescriptor(
    int ProcessId,
    int Port,
    string Protocol,
    string Source,
    string? PlatformId,
    string? Region);

public interface ILeagueSessionAccessor
{
    LeagueConnectionState State { get; }
    LeagueSessionDescriptor? Current { get; }
}

public interface ILeagueReadGateway
{
    Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken);
}

public enum LeagueWriteCapability
{
    ApplyMySelection,
    CreatePerkPage,
    UpdatePerkPage,
    SetCurrentPerkPage,
    StartMatchmaking,
    AcceptReadyCheck,
    HonorPlayerV2,
    HonorPlayerLegacy,
    SubmitHonorBallotLegacy,
    PlayAgain,
    SetPresence,
    RestartClientUx,
    SwapBenchChampionLegacy,
    SwapBenchChampionTeamBuilder
}

public sealed record LeagueWriteCommand(LeagueWriteCapability Capability, long? ResourceId, string? Json);
public sealed record LeagueWriteResult(int StatusCode, byte[] Body)
{
    public bool IsSuccessStatusCode => StatusCode is >= 200 and <= 299;
}

public interface ILeagueWriteGateway
{
    Task<LeagueWriteResult?> ExecuteAsync(LeagueWriteCommand command, CancellationToken cancellationToken);
}

public sealed record LeagueWriteTarget(string Method, string Path);

public static class LeagueWriteTargetPolicy
{
    public static LeagueWriteTarget Resolve(LeagueWriteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Capability switch
        {
            LeagueWriteCapability.ApplyMySelection => new("PATCH", "/lol-champ-select/v1/session/my-selection"),
            LeagueWriteCapability.CreatePerkPage => new("POST", "/lol-perks/v1/pages"),
            LeagueWriteCapability.SetCurrentPerkPage => new("PUT", "/lol-perks/v1/currentpage"),
            LeagueWriteCapability.UpdatePerkPage when command.ResourceId is > 0 => new("PUT", "/lol-perks/v1/pages/" + command.ResourceId.Value),
            LeagueWriteCapability.UpdatePerkPage => throw new ArgumentException("UpdatePerkPage requires a positive resource ID.", nameof(command)),
            LeagueWriteCapability.StartMatchmaking => new("POST", "/lol-lobby/v2/lobby/matchmaking/search"),
            LeagueWriteCapability.AcceptReadyCheck => new("POST", "/lol-matchmaking/v1/ready-check/accept"),
            LeagueWriteCapability.HonorPlayerV2 => new("POST", "/lol-honor-v2/v1/honor-player"),
            LeagueWriteCapability.HonorPlayerLegacy => new("POST", "/lol-honor/v1/honor"),
            LeagueWriteCapability.SubmitHonorBallotLegacy => new("POST", "/lol-honor/v1/ballot"),
            LeagueWriteCapability.PlayAgain => new("POST", "/lol-lobby/v2/play-again"),
            LeagueWriteCapability.SetPresence => new("PUT", "/lol-chat/v1/me"),
            LeagueWriteCapability.RestartClientUx => new("POST", "/riotclient/kill-and-restart-ux"),
            LeagueWriteCapability.SwapBenchChampionLegacy when command.ResourceId is > 0 =>
                new("POST", "/lol-champ-select/v1/session/bench/swap/" + command.ResourceId.Value),
            LeagueWriteCapability.SwapBenchChampionLegacy =>
                throw new ArgumentException("SwapBenchChampionLegacy requires a positive champion ID.", nameof(command)),
            LeagueWriteCapability.SwapBenchChampionTeamBuilder when command.ResourceId is > 0 =>
                new("POST", "/lol-lobby-team-builder/champ-select/v1/session/bench/swap/" + command.ResourceId.Value),
            LeagueWriteCapability.SwapBenchChampionTeamBuilder =>
                throw new ArgumentException("SwapBenchChampionTeamBuilder requires a positive champion ID.", nameof(command)),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
    }

    public static bool Matches(LeagueWriteCommand command, string method, string path)
    {
        var expected = Resolve(command);
        return string.Equals(expected.Method, (method ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(expected.Path, NormalizePath(path), StringComparison.Ordinal);
    }

    private static string NormalizePath(string? path)
    {
        var value = (path ?? string.Empty).Trim();
        if (value.Length == 0) return "/";
        return value.StartsWith('/') ? value : "/" + value;
    }
}
