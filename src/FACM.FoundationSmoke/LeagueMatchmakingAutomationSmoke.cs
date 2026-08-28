using System.Text;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.State;
using FACM.Infrastructure.League;

internal static class LeagueMatchmakingAutomationSmoke
{
    public static async Task RunAsync()
    {
        await AutoSearchUsesEligibilityAndFingerprintAsync();
        await AutoAcceptIsOneShotPerReadyCheckAsync();
        await FinalReadyCheckResponseWinsAsync();
        WriteTargetsRemainNarrow();
    }

    private static async Task AutoSearchUsesEligibilityAndFingerprintAsync()
    {
        var read = new FakeReadGateway();
        var write = new FakeWriteGateway();
        var gameflow = new FakeObservationSource();
        using var service = new LeagueMatchmakingAutomationService(read, write, gameflow);
        service.Configure(autoSearch: true, autoAccept: false);

        read.Set(LeagueMatchmakingAutomationService.LobbyPath, """
            {
              "canStartActivity": true,
              "localMember": { "isLeader": true },
              "gameConfig": { "queueId": 420 },
              "members": [
                { "puuid": "b", "isBot": false, "isSpectator": false },
                { "puuid": "a", "isBot": false, "isSpectator": false }
              ]
            }
            """);

        var lobby = Snapshot("Lobby", LeagueProductState.Lobby, LeagueActivityLevel.Client);
        await service.EvaluateForSmokeTestAsync(lobby);
        Equal(1, write.Commands.Count, "eligible leader should start matchmaking once");
        Equal(LeagueWriteCapability.StartMatchmaking, write.Commands[0].Capability, "search capability");

        await service.EvaluateForSmokeTestAsync(lobby);
        Equal(1, write.Commands.Count, "stable lobby fingerprint must not repeat search");

        read.Set(LeagueMatchmakingAutomationService.LobbyPath, """
            {
              "canStartActivity": true,
              "localMember": { "isLeader": true },
              "gameConfig": { "queueId": 420 },
              "members": [
                { "puuid": "a", "isBot": false, "isSpectator": false },
                { "puuid": "c", "isBot": false, "isSpectator": false }
              ]
            }
            """);
        await service.EvaluateForSmokeTestAsync(lobby);
        Equal(2, write.Commands.Count, "membership change should allow one new search attempt");

        read.Set(LeagueMatchmakingAutomationService.LobbyPath, """
            {
              "canStartActivity": true,
              "localMember": { "isLeader": false },
              "gameConfig": { "queueId": 420 },
              "members": [{ "puuid": "z", "isBot": false, "isSpectator": false }]
            }
            """);
        await service.EvaluateForSmokeTestAsync(Snapshot("None", LeagueProductState.Lobby, LeagueActivityLevel.Client));
        await service.EvaluateForSmokeTestAsync(lobby);
        Equal(2, write.Commands.Count, "non-leader lobby must not start matchmaking");
    }

    private static async Task AutoAcceptIsOneShotPerReadyCheckAsync()
    {
        var read = new FakeReadGateway();
        var write = new FakeWriteGateway();
        var gameflow = new FakeObservationSource();
        using var service = new LeagueMatchmakingAutomationService(read, write, gameflow);
        service.Configure(autoSearch: false, autoAccept: true);
        read.Set(LeagueMatchmakingAutomationService.SearchStatePath, """
            { "readyCheck": { "state": "InProgress", "playerResponse": "None" } }
            """);

        var ready = Snapshot("ReadyCheck", LeagueProductState.ReadyCheck, LeagueActivityLevel.Queueing);
        await service.EvaluateForSmokeTestAsync(ready);
        Equal(1, write.Commands.Count, "ready check should accept once");
        Equal(LeagueWriteCapability.AcceptReadyCheck, write.Commands[0].Capability, "accept capability");

        await service.EvaluateForSmokeTestAsync(ready);
        Equal(1, write.Commands.Count, "same ready check must not repeat accept");

        await service.EvaluateForSmokeTestAsync(Snapshot("Lobby", LeagueProductState.Lobby, LeagueActivityLevel.Client));
        await service.EvaluateForSmokeTestAsync(ready);
        Equal(2, write.Commands.Count, "leaving ReadyCheck must reset one-shot state");
    }

    private static async Task FinalReadyCheckResponseWinsAsync()
    {
        var read = new FakeReadGateway();
        var write = new FakeWriteGateway();
        var gameflow = new FakeObservationSource();
        using var service = new LeagueMatchmakingAutomationService(read, write, gameflow);
        service.Configure(autoSearch: false, autoAccept: true);
        read.Set(LeagueMatchmakingAutomationService.SearchStatePath, """
            { "readyCheck": { "state": "InProgress", "playerResponse": "Declined" } }
            """);

        await service.EvaluateForSmokeTestAsync(
            Snapshot("ReadyCheck", LeagueProductState.ReadyCheck, LeagueActivityLevel.Queueing));
        Equal(0, write.Commands.Count, "explicit final ready-check response must never be reversed");
    }

    private static void WriteTargetsRemainNarrow()
    {
        var search = new LeagueWriteCommand(LeagueWriteCapability.StartMatchmaking, null, null);
        var accept = new LeagueWriteCommand(LeagueWriteCapability.AcceptReadyCheck, null, null);
        True(LeagueWriteTargetPolicy.Matches(search, "POST", "/lol-lobby/v2/lobby/matchmaking/search"), "search path allowlist");
        True(LeagueWriteTargetPolicy.Matches(accept, "POST", "/lol-matchmaking/v1/ready-check/accept"), "accept path allowlist");
        True(!LeagueWriteTargetPolicy.Matches(accept, "POST", "/lol-matchmaking/v1/ready-check/decline"), "decline must not be reachable through accept capability");
    }

    private static LeagueGameflowSnapshot Snapshot(
        string phase,
        LeagueProductState productState,
        LeagueActivityLevel activity) =>
        new(DateTimeOffset.UtcNow, LeagueConnectionState.Connected, phase, productState, activity);

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }

    private sealed class FakeReadGateway : ILeagueReadGateway
    {
        private readonly Dictionary<string, byte[]> _responses = new(StringComparer.Ordinal);

        public void Set(string path, string json) => _responses[path] = Encoding.UTF8.GetBytes(json);

        public Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _responses.TryGetValue(resourceKey, out var value);
            return Task.FromResult<byte[]?>(value);
        }
    }

    private sealed class FakeWriteGateway : ILeagueWriteGateway
    {
        public List<LeagueWriteCommand> Commands { get; } = [];

        public Task<LeagueWriteResult?> ExecuteAsync(LeagueWriteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            return Task.FromResult<LeagueWriteResult?>(new LeagueWriteResult(204, []));
        }
    }

    private sealed class FakeObservationSource : ILeagueGameflowObservationSource
    {
        public LeagueGameflowSnapshot? Current { get; private set; }
        public event EventHandler<LeagueGameflowChangedEventArgs>? Changed;
        public event EventHandler<LeagueGameflowChangedEventArgs>? Observed;

        public void Publish(LeagueGameflowSnapshot snapshot)
        {
            var previous = Current;
            Current = snapshot;
            var args = new LeagueGameflowChangedEventArgs(previous, snapshot);
            Changed?.Invoke(this, args);
            Observed?.Invoke(this, args);
        }
    }
}
