using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FACM.Core.League;

namespace FACM.Infrastructure.League;

/// <summary>
/// FACM 3.5.15 presence behavior rebuilt over the shared FACM 4.0 League gateway. Every apply is
/// explicitly user-directed, preserves unrelated presence metadata, performs one PUT only, and then
/// verifies readback without fighting the League client in a rewrite loop.
/// </summary>
public sealed class LeaguePresenceService : ILeaguePresenceService
{
    internal const string PresencePath = "/lol-chat/v1/me";
    internal static readonly TimeSpan DefaultFirstVerificationDelay = TimeSpan.FromMilliseconds(180);
    internal static readonly TimeSpan DefaultSettleVerificationDelay = TimeSpan.FromMilliseconds(320);

    private readonly ILeagueReadGateway _read;
    private readonly ILeagueWriteGateway _write;
    private readonly TimeSpan _firstVerificationDelay;
    private readonly TimeSpan _settleVerificationDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public LeaguePresenceService(
        ILeagueReadGateway read,
        ILeagueWriteGateway write,
        TimeSpan? firstVerificationDelay = null,
        TimeSpan? settleVerificationDelay = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _firstVerificationDelay = ClampDelay(firstVerificationDelay ?? DefaultFirstVerificationDelay);
        _settleVerificationDelay = ClampDelay(settleVerificationDelay ?? DefaultSettleVerificationDelay);
        _delay = delay ?? Task.Delay;
    }

    public async Task<LeaguePresenceSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var root = await ReadRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? LeaguePresenceSnapshot.Unavailable : ToSnapshot(root);
    }

    public async Task<LeaguePresenceApplyResult> ApplyAsync(
        LeaguePresenceMode mode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = await ReadRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return new LeaguePresenceApplyResult("unavailable", mode, null);

        ApplyMode(root, mode);
        var payload = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var response = await _write.ExecuteAsync(
            new LeagueWriteCommand(LeagueWriteCapability.SetPresence, null, payload),
            cancellationToken).ConfigureAwait(false);
        if (response is null || !response.IsSuccessStatusCode)
            return new LeaguePresenceApplyResult("write-failed", mode, null);

        if (_firstVerificationDelay > TimeSpan.Zero)
            await _delay(_firstVerificationDelay, cancellationToken).ConfigureAwait(false);
        var first = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!Matches(first, mode))
            return new LeaguePresenceApplyResult("overridden", mode, first);

        if (_settleVerificationDelay > TimeSpan.Zero)
            await _delay(_settleVerificationDelay, cancellationToken).ConfigureAwait(false);
        var settled = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!Matches(settled, mode))
            return new LeaguePresenceApplyResult("overridden", mode, settled);

        return new LeaguePresenceApplyResult("success", mode, settled);
    }

    internal string? BuildPayloadForSmokeTest(byte[]? currentPresence, LeaguePresenceMode mode)
    {
        var root = ParseRoot(currentPresence);
        if (root is null) return null;
        ApplyMode(root, mode);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    internal static bool MatchesForSmokeTest(LeaguePresenceSnapshot snapshot, LeaguePresenceMode mode) =>
        Matches(snapshot, mode);

    private async Task<JsonObject?> ReadRootAsync(CancellationToken cancellationToken)
    {
        var bytes = await _read.TryGetBytesAsync(PresencePath, cancellationToken).ConfigureAwait(false);
        return ParseRoot(bytes);
    }

    private static JsonObject? ParseRoot(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            return JsonNode.Parse(Encoding.UTF8.GetString(bytes)) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ApplyMode(JsonObject root, LeaguePresenceMode mode)
    {
        var currentAvailability = ReadString(root, "availability");
        switch (mode)
        {
            case LeaguePresenceMode.Online:
                root["availability"] = string.Equals(currentAvailability, "online", StringComparison.OrdinalIgnoreCase)
                    ? "online"
                    : "chat";
                SetGameStatus(root, "outOfGame");
                break;
            case LeaguePresenceMode.Away:
                root["availability"] = "away";
                SetGameStatus(root, "outOfGame");
                break;
            case LeaguePresenceMode.DoNotDisturb:
                root["availability"] = "dnd";
                SetGameStatus(root, "outOfGame");
                break;
            case LeaguePresenceMode.Mobile:
                root["availability"] = "mobile";
                SetGameStatus(root, "outOfGame");
                break;
            case LeaguePresenceMode.Offline:
                root["availability"] = "offline";
                SetGameStatus(root, "outOfGame");
                break;
            case LeaguePresenceMode.DisplayInGame:
                root["availability"] = "dnd";
                SetGameStatus(root, "inGame");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private static void SetGameStatus(JsonObject root, string gameStatus)
    {
        if (root["lol"] is not JsonObject lol)
        {
            lol = new JsonObject();
            root["lol"] = lol;
        }
        lol["gameStatus"] = gameStatus;
    }

    private static LeaguePresenceSnapshot ToSnapshot(JsonObject root)
    {
        var lol = root["lol"] as JsonObject;
        return new LeaguePresenceSnapshot(
            true,
            ReadString(root, "availability"),
            ReadString(lol, "gameStatus"),
            ReadString(root, "statusMessage"),
            ReadString(root, "name"));
    }

    private static bool Matches(LeaguePresenceSnapshot snapshot, LeaguePresenceMode mode)
    {
        if (!snapshot.Connected) return false;
        var availability = (snapshot.Availability ?? string.Empty).Trim();
        var gameStatus = (snapshot.GameStatus ?? string.Empty).Trim();
        return mode switch
        {
            LeaguePresenceMode.Online =>
                string.Equals(availability, "chat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(availability, "online", StringComparison.OrdinalIgnoreCase),
            LeaguePresenceMode.Away =>
                string.Equals(availability, "away", StringComparison.OrdinalIgnoreCase) && !IsInGame(gameStatus),
            LeaguePresenceMode.DoNotDisturb =>
                string.Equals(availability, "dnd", StringComparison.OrdinalIgnoreCase) && !IsInGame(gameStatus),
            LeaguePresenceMode.Mobile =>
                string.Equals(availability, "mobile", StringComparison.OrdinalIgnoreCase) && !IsInGame(gameStatus),
            LeaguePresenceMode.Offline =>
                string.Equals(availability, "offline", StringComparison.OrdinalIgnoreCase) && !IsInGame(gameStatus),
            LeaguePresenceMode.DisplayInGame =>
                IsInGame(gameStatus) && !string.Equals(availability, "offline", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsInGame(string value) =>
        string.Equals(value, "inGame", StringComparison.OrdinalIgnoreCase);

    private static string ReadString(JsonObject? source, string key)
    {
        if (source is null || source[key] is null) return string.Empty;
        try { return source[key]!.GetValue<string>() ?? string.Empty; }
        catch (InvalidOperationException) { return source[key]!.ToJsonString().Trim('"'); }
    }

    private static TimeSpan ClampDelay(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
