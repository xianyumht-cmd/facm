using System.Text;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.State;
using FACM.Infrastructure.League;

internal static class LeaguePostGameAutomationSmoke
{
    public static async Task RunAsync()
    {
        await V2HonorAndReturnLobbyRunOnceAsync();
        await LegacyHonorFallbackRemainsBoundedAsync();
        await IneligibleBallotNeverWritesHonorAsync();
        WriteTargetsRemainNarrow();
        VoteCompletionParserAcceptsLegacyFieldNames();
        PhaseDelaysMatchLegacyContract();
    }

    private static async Task V2HonorAndReturnLobbyRunOnceAsync()
    {
        var read = new FakeReadGateway();
        var write = new FakeWriteGateway();
        var gameflow = new FakeObservationSource();
        read.Set(LeaguePostGameAutomationService.BallotPath, """
            {
              "gameId": 9001,
              "numVotes": 1,
              "eligibleAllies": [
                { "puuid": "ALLY-1", "summonerId": 77, "botPlayer": false },
                { "puuid": "SELF", "summonerId": 88, "botPlayer": false }
              ],
              "honoredPlayers": []
            }
            """);
        read.Set(LeaguePostGameAutomationService.CurrentSummonerPath, "{\"puuid\":\"SELF\"}");
        read.Set(LeaguePostGameAutomationService.TeamChoicesPath, "[\"ALLY-1\"]");

        using var service = new LeaguePostGameAutomationService(
            read,
            write,
            gameflow,
            delay: (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            chooseIndex: _ => 0,
            utcNow: () => DateTimeOffset.Parse("2026-08-28T08:00:00Z"));
        service.Configure(autoHonor: true, autoReturnLobby: true);

        var postGame = Snapshot("EndOfGame", LeagueProductState.PostGame, LeagueActivityLevel.Client);
        await service.ObserveForSmokeTestAsync(postGame);

        Equal(2, write.Commands.Count, "V2 honor + play-again write count");
        Equal(LeagueWriteCapability.HonorPlayerV2, write.Commands[0].Capability, "V2 honor capability");
        Equal(LeagueWriteCapability.PlayAgain, write.Commands[1].Capability, "play-again capability");
        Equal("success", service.LastHonorStatus?.State, "V2 honor verification state");
        Equal("v2", service.LastHonorStatus?.Route, "V2 honor route");
        True(service.LastHonorStatus?.TargetPuuidSuffix.EndsWith("ALLY-1", StringComparison.Ordinal) == true,
            "honor status must expose only masked target suffix");

        await service.ObserveForSmokeTestAsync(postGame);
        Equal(2, write.Commands.Count, "same post-game cycle must not repeat writes");

        await service.ObserveForSmokeTestAsync(
            Snapshot("Lobby", LeagueProductState.Lobby, LeagueActivityLevel.Client));
        await service.ObserveForSmokeTestAsync(postGame);
        Equal(4, write.Commands.Count, "leaving post-game must allow exactly one later cycle");
    }

    private static async Task LegacyHonorFallbackRemainsBoundedAsync()
    {
        var read = new FakeReadGateway();
        var write = new FakeWriteGateway
        {
            ResponseFactory = command => command.Capability == LeagueWriteCapability.HonorPlayerV2
                ? new LeagueWriteResult(404, [])
                : new LeagueWriteResult(204, [])
        };
        var gameflow = new FakeObservationSource();
        read.Set(LeaguePostGameAutomationService.BallotPath, """
            {
              "gameId": 42,
              "votePool": { "votes": 1 },
              "eligiblePlayers": [
                { "puuid": "ALLY-LEGACY", "summonerID": 123, "botPlayer": false }
              ]
            }
            """);
        read.Set(LeaguePostGameAutomationService.CurrentSummonerPath, "{\"puuid\":\"SELF\"}");
        read.Set(LeaguePostGameAutomationService.TeamChoicesPath, "[123]");

        using var service = new LeaguePostGameAutomationService(
            read,
            write,
            gameflow,
            delay: (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            chooseIndex: _ => 0);
        service.Configure(autoHonor: true, autoReturnLobby: false);

        await service.ObserveForSmokeTestAsync(
            Snapshot("PreEndOfGame", LeagueProductState.PostGame, LeagueActivityLevel.Client));

        Equal(3, write.Commands.Count, "V2 404 fallback write count");
        Equal(LeagueWriteCapability.HonorPlayerV2, write.Commands[0].Capability, "fallback probe capability");
        Equal(LeagueWriteCapability.HonorPlayerLegacy, write.Commands[1].Capability, "legacy honor capability");
        Equal(LeagueWriteCapability.SubmitHonorBallotLegacy, write.Commands[2].Capability, "legacy ballot capability");
        Equal("success", service.LastHonorStatus?.State, "legacy fallback verification state");
        Equal("legacy", service.LastHonorStatus?.Route, "legacy fallback route");
    }

    private static async Task IneligibleBallotNeverWritesHonorAsync()
    {
        var read = new FakeReadGateway();
        var write = new FakeWriteGateway();
        var gameflow = new FakeObservationSource();
        read.Set(LeaguePostGameAutomationService.BallotPath, """
            {
              "gameId": 55,
              "numVotes": 1,
              "eligibleAllies": [
                { "puuid": "SELF", "summonerId": 1, "botPlayer": false },
                { "puuid": "BOT", "summonerId": 2, "botPlayer": true }
              ],
              "honoredPlayers": []
            }
            """);
        read.Set(LeaguePostGameAutomationService.CurrentSummonerPath, "{\"puuid\":\"SELF\"}");

        using var service = new LeaguePostGameAutomationService(
            read,
            write,
            gameflow,
            delay: (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            chooseIndex: _ => 0);
        service.Configure(autoHonor: true, autoReturnLobby: false);

        await service.ObserveForSmokeTestAsync(
            Snapshot("WaitingForStats", LeagueProductState.PostGame, LeagueActivityLevel.Client));

        Equal(0, write.Commands.Count, "self/bot-only ballot must not write honor");
        Equal("skipped", service.LastHonorStatus?.State, "ineligible ballot status");
        Equal("no-eligible-ally", service.LastHonorStatus?.Detail, "ineligible ballot detail");
    }

    private static void WriteTargetsRemainNarrow()
    {
        True(LeagueWriteTargetPolicy.Matches(
            new LeagueWriteCommand(LeagueWriteCapability.HonorPlayerV2, null, "{}"),
            "POST",
            "/lol-honor-v2/v1/honor-player"), "V2 honor target");
        True(LeagueWriteTargetPolicy.Matches(
            new LeagueWriteCommand(LeagueWriteCapability.HonorPlayerLegacy, null, "{}"),
            "POST",
            "/lol-honor/v1/honor"), "legacy honor target");
        True(LeagueWriteTargetPolicy.Matches(
            new LeagueWriteCommand(LeagueWriteCapability.SubmitHonorBallotLegacy, null, null),
            "POST",
            "/lol-honor/v1/ballot"), "legacy ballot target");
        True(LeagueWriteTargetPolicy.Matches(
            new LeagueWriteCommand(LeagueWriteCapability.PlayAgain, null, null),
            "POST",
            "/lol-lobby/v2/play-again"), "play-again target");
        True(!LeagueWriteTargetPolicy.Matches(
            new LeagueWriteCommand(LeagueWriteCapability.PlayAgain, null, null),
            "POST",
            "/lol-lobby/v2/lobby/matchmaking/search"), "play-again capability must not escape its target");
    }

    private static void PhaseDelaysMatchLegacyContract()
    {
        True(LeaguePostGameAutomationService.IsPostGamePhase("WaitingForStats"), "WaitingForStats phase");
        True(LeaguePostGameAutomationService.IsPostGamePhase("PreEndOfGame"), "PreEndOfGame phase");
        True(LeaguePostGameAutomationService.IsPostGamePhase("EndOfGame"), "EndOfGame phase");
        True(!LeaguePostGameAutomationService.IsPostGamePhase("Lobby"), "Lobby is not post-game");
        Equal(TimeSpan.FromSeconds(10), LeaguePostGameAutomationService.ResolveReturnDelay("WaitingForStats"), "WaitingForStats return delay");
        Equal(TimeSpan.FromMilliseconds(3250), LeaguePostGameAutomationService.ResolveReturnDelay("PreEndOfGame"), "PreEndOfGame return delay");
        Equal(TimeSpan.FromMilliseconds(1575), LeaguePostGameAutomationService.ResolveReturnDelay("EndOfGame"), "EndOfGame return delay");
    }

    private static void VoteCompletionParserAcceptsLegacyFieldNames()
    {
        var completion = LeaguePostGameAutomationService.ParseVoteCompletion(
            Encoding.UTF8.GetBytes("{ \"game_id\": 9001, \"full_team_vote\": true }"));
        True(completion is not null, "vote completion object parses");
        Equal(9001L, completion!.GameId, "vote completion game id");
        True(completion.FullTeamVote, "vote completion full team vote");
        True(LeaguePostGameAutomationService.ParseVoteCompletion(Encoding.UTF8.GetBytes("[]")) is null,
            "invalid vote completion shape is ignored");
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
        public Func<LeagueWriteCommand, LeagueWriteResult>? ResponseFactory { get; init; }

        public Task<LeagueWriteResult?> ExecuteAsync(LeagueWriteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            return Task.FromResult<LeagueWriteResult?>(
                ResponseFactory?.Invoke(command) ?? new LeagueWriteResult(204, []));
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
