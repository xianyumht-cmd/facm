using System.Text;
using System.Text.Json.Nodes;
using FACM.Core.League;
using FACM.Infrastructure.League;

internal static class LeaguePresenceSmoke
{
    public static async Task RunAsync()
    {
        PayloadPreservesUnrelatedFields();
        await OneUserIntentProducesOneWriteAsync();
        await DisplayInGameVerifiesAsync();
        await ClientOverrideDoesNotCreateRewriteLoopAsync();
        NarrowWriteTargetOnly();
    }

    private static void PayloadPreservesUnrelatedFields()
    {
        var read = new FakeReadGateway();
        var write = new FakeWriteGateway(read);
        var service = CreateService(read, write);
        var payload = service.BuildPayloadForSmokeTest(read.Current, LeaguePresenceMode.Away);
        var root = JsonNode.Parse(payload!)!.AsObject();
        Equal("away", root["availability"]!.GetValue<string>(), "away availability");
        Equal("keep-me", root["statusMessage"]!.GetValue<string>(), "preserve statusMessage");
        Equal("preserve", root["customRoot"]!.GetValue<string>(), "preserve custom root");
        var lol = root["lol"]!.AsObject();
        Equal("outOfGame", lol["gameStatus"]!.GetValue<string>(), "away clears displayed in-game state");
        Equal("Gold", lol["rankedLeagueName"]!.GetValue<string>(), "preserve unrelated lol metadata");
    }

    private static async Task OneUserIntentProducesOneWriteAsync()
    {
        var read = new FakeReadGateway();
        var write = new FakeWriteGateway(read);
        var service = CreateService(read, write);
        var result = await service.ApplyAsync(LeaguePresenceMode.Offline);
        Equal("success", result.Status, "offline apply status");
        Equal(1, write.Commands.Count, "one presence click must write exactly once");
        Equal("offline", result.Observed?.Availability, "offline readback");
    }

    private static async Task DisplayInGameVerifiesAsync()
    {
        var read = new FakeReadGateway();
        var write = new FakeWriteGateway(read);
        var service = CreateService(read, write);
        var result = await service.ApplyAsync(LeaguePresenceMode.DisplayInGame);
        Equal("success", result.Status, "display-in-game status");
        Equal(1, write.Commands.Count, "display-in-game one write");
        Equal("inGame", result.Observed?.GameStatus, "display-in-game readback");
    }

    private static async Task ClientOverrideDoesNotCreateRewriteLoopAsync()
    {
        var read = new FakeReadGateway { OverrideOnSecondPostWriteRead = true };
        var write = new FakeWriteGateway(read);
        var service = CreateService(read, write);
        var result = await service.ApplyAsync(LeaguePresenceMode.Away);
        Equal("overridden", result.Status, "client override status");
        Equal(1, write.Commands.Count, "client override must not trigger rewrite loop");
    }

    private static void NarrowWriteTargetOnly()
    {
        var command = new LeagueWriteCommand(LeagueWriteCapability.SetPresence, null, "{}");
        True(LeagueWriteTargetPolicy.Matches(command, "PUT", "/lol-chat/v1/me"), "presence target allowlist");
        True(!LeagueWriteTargetPolicy.Matches(command, "POST", "/lol-chat/v1/me"), "presence wrong method rejected");
        True(!LeagueWriteTargetPolicy.Matches(command, "PUT", "/lol-chat/v1/me?force=true"), "presence query escape rejected");
        True(!LeagueWriteTargetPolicy.Matches(command, "PUT", "/lol-champ-select/v1/session/my-selection"), "presence cannot escape into champ-select");
    }

    private static LeaguePresenceService CreateService(FakeReadGateway read, FakeWriteGateway write) =>
        new(
            read,
            write,
            firstVerificationDelay: TimeSpan.Zero,
            settleVerificationDelay: TimeSpan.Zero,
            delay: (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

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
        private byte[] _current = Encoding.UTF8.GetBytes(
            "{\"availability\":\"chat\",\"name\":\"Tester\",\"statusMessage\":\"keep-me\",\"customRoot\":\"preserve\",\"lol\":{\"gameStatus\":\"outOfGame\",\"rankedLeagueName\":\"Gold\"}}");
        private bool _written;
        private int _postWriteReads;

        public bool OverrideOnSecondPostWriteRead { get; init; }
        public byte[] Current => _current;

        public Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(resourceKey, LeaguePresenceService.PresencePath, StringComparison.Ordinal))
                return Task.FromResult<byte[]?>(null);
            if (_written)
            {
                _postWriteReads++;
                if (OverrideOnSecondPostWriteRead && _postWriteReads >= 2)
                {
                    _current = Encoding.UTF8.GetBytes(
                        "{\"availability\":\"chat\",\"name\":\"Tester\",\"statusMessage\":\"keep-me\",\"customRoot\":\"preserve\",\"lol\":{\"gameStatus\":\"outOfGame\",\"rankedLeagueName\":\"Gold\"}}");
                }
            }
            return Task.FromResult<byte[]?>(_current);
        }

        public void AcceptWrite(string json)
        {
            _written = true;
            _current = Encoding.UTF8.GetBytes(json);
        }
    }

    private sealed class FakeWriteGateway(FakeReadGateway read) : ILeagueWriteGateway
    {
        public List<LeagueWriteCommand> Commands { get; } = [];

        public Task<LeagueWriteResult?> ExecuteAsync(LeagueWriteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            if (command.Capability == LeagueWriteCapability.SetPresence && command.Json is not null)
                read.AcceptWrite(command.Json);
            return Task.FromResult<LeagueWriteResult?>(new LeagueWriteResult(200, []));
        }
    }
}
