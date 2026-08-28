using System.Text.Json;
using FACM.Core.League;

namespace FACM.Infrastructure.League;

/// <summary>
/// Read-only LOL Workbench data source. All reads go through the process-wide
/// <see cref="ILeagueReadGateway"/> so FACM 4.0 keeps exactly one discovery/auth/session owner.
/// Changed or unavailable LCU shapes fail soft and produce partial/unavailable snapshots.
/// </summary>
public sealed class LeagueWorkbenchDataSource : ILeagueWorkbenchDataSource
{
    internal const string CurrentSummonerPath = "/lol-summoner/v1/current-summoner";
    internal const string GameflowSessionPath = "/lol-gameflow/v1/session";
    internal const string LobbyPath = "/lol-lobby/v2/lobby";
    internal const string ReadyCheckPath = "/lol-matchmaking/v1/ready-check";
    internal const string RankedStatsPath = "/lol-ranked/v1/current-ranked-stats";

    private readonly ILeagueReadGateway _gateway;

    public LeagueWorkbenchDataSource(ILeagueReadGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public async Task<LeagueWorkbenchDashboardSnapshot> LoadDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var account = ParseAccount(await _gateway.TryGetBytesAsync(CurrentSummonerPath, cancellationToken).ConfigureAwait(false));
        cancellationToken.ThrowIfCancellationRequested();

        var gameflow = ParseDocument(await _gateway.TryGetBytesAsync(GameflowSessionPath, cancellationToken).ConfigureAwait(false));
        cancellationToken.ThrowIfCancellationRequested();
        var queue = gameflow is null ? null : ParseQueue(gameflow.RootElement);

        var lobby = ParseDocument(await _gateway.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false));
        cancellationToken.ThrowIfCancellationRequested();
        var lobbyMembers = lobby is null
            ? Array.Empty<LeagueWorkbenchLobbyMember>()
            : ParseLobbyMembers(lobby.RootElement, account);

        var readyCheck = ParseReadyCheck(
            await _gateway.TryGetBytesAsync(ReadyCheckPath, cancellationToken).ConfigureAwait(false));

        gameflow?.Dispose();
        lobby?.Dispose();

        var hasAny = account is not null || queue is not null || lobbyMembers.Count > 0 || readyCheck is not null;
        if (!hasAny) return LeagueWorkbenchDashboardSnapshot.Unavailable("league-unavailable");

        var state = account is not null && queue is not null
            ? LeagueWorkbenchDataState.Ready
            : LeagueWorkbenchDataState.Partial;
        return new LeagueWorkbenchDashboardSnapshot(
            state,
            account,
            queue,
            lobbyMembers,
            readyCheck,
            state == LeagueWorkbenchDataState.Ready ? "ready" : "partial",
            DateTimeOffset.UtcNow);
    }

    public async Task<LeagueWorkbenchPlayerSnapshot> LoadCurrentPlayerAsync(
        int startIndex = 0,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        startIndex = Math.Max(0, startIndex);
        count = Math.Clamp(count, 1, 20);

        var account = ParseAccount(await _gateway.TryGetBytesAsync(CurrentSummonerPath, cancellationToken).ConfigureAwait(false));
        if (account is null || string.IsNullOrWhiteSpace(account.PuuId))
            return LeagueWorkbenchPlayerSnapshot.Unavailable("current-player-unavailable");

        cancellationToken.ThrowIfCancellationRequested();
        var ranked = ParseRankedSummary(
            await _gateway.TryGetBytesAsync(RankedStatsPath, cancellationToken).ConfigureAwait(false));

        cancellationToken.ThrowIfCancellationRequested();
        var endIndex = startIndex + count - 1;
        var matchPath = "/lol-match-history/v1/products/lol/" + Uri.EscapeDataString(account.PuuId) +
                        "/matches?begIndex=" + startIndex + "&endIndex=" + endIndex;
        var matches = ParseMatches(
            await _gateway.TryGetBytesAsync(matchPath, cancellationToken).ConfigureAwait(false),
            account,
            count);

        var state = ranked is not null || matches.Items.Count > 0
            ? LeagueWorkbenchDataState.Ready
            : LeagueWorkbenchDataState.Partial;
        return new LeagueWorkbenchPlayerSnapshot(
            state,
            account,
            ranked,
            matches.Items,
            matches.HasMore,
            state == LeagueWorkbenchDataState.Ready ? "ready" : "partial",
            DateTimeOffset.UtcNow);
    }

    internal static LeagueWorkbenchAccount? ParseAccount(byte[]? bytes)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = document.RootElement;
        var puuId = ReadString(root, "puuid");
        var gameName = ReadString(root, "gameName");
        var displayName = ReadString(root, "displayName");
        if (string.IsNullOrWhiteSpace(puuId) && string.IsNullOrWhiteSpace(gameName) && string.IsNullOrWhiteSpace(displayName))
            return null;

        return new LeagueWorkbenchAccount(
            puuId,
            ReadLong(root, "summonerId"),
            ReadLong(root, "accountId"),
            gameName,
            ReadString(root, "tagLine"),
            displayName,
            ReadInt(root, "summonerLevel"),
            ReadInt(root, "profileIconId"));
    }

    internal static LeagueWorkbenchQueue? ParseQueue(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        var gameData = TryGetObject(root, "gameData");
        var queue = gameData is JsonElement gameDataElement
            ? TryGetObject(gameDataElement, "queue")
            : null;
        if (queue is JsonElement queueElement)
        {
            var id = ReadInt(queueElement, "id");
            var name = FirstNonEmpty(ReadString(queueElement, "name"), ReadString(queueElement, "shortName"));
            var mode = ReadString(queueElement, "gameMode");
            if (id > 0 || !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(mode))
                return new LeagueWorkbenchQueue(id, name, mode);
        }

        var fallbackId = gameData is JsonElement fallbackGameData ? ReadInt(fallbackGameData, "queueId") : 0;
        var fallbackMode = gameData is JsonElement fallbackModeData ? ReadString(fallbackModeData, "gameMode") : string.Empty;
        return fallbackId > 0 || !string.IsNullOrWhiteSpace(fallbackMode)
            ? new LeagueWorkbenchQueue(fallbackId, string.Empty, fallbackMode)
            : null;
    }

    internal static IReadOnlyList<LeagueWorkbenchLobbyMember> ParseLobbyMembers(
        JsonElement root,
        LeagueWorkbenchAccount? account)
    {
        if (!TryGetArray(root, "members", out var members)) return Array.Empty<LeagueWorkbenchLobbyMember>();
        var result = new List<LeagueWorkbenchLobbyMember>();
        foreach (var member in members.EnumerateArray())
        {
            if (member.ValueKind != JsonValueKind.Object) continue;
            var puuId = FirstNonEmpty(ReadString(member, "puuid"), ReadString(member, "puuId"));
            var summonerId = ReadLong(member, "summonerId");
            var displayName = FirstNonEmpty(
                ReadString(member, "gameName"),
                ReadString(member, "summonerName"),
                ReadString(member, "displayName"));
            var isLocal = account is not null &&
                          ((!string.IsNullOrWhiteSpace(account.PuuId) && string.Equals(account.PuuId, puuId, StringComparison.OrdinalIgnoreCase)) ||
                           (account.SummonerId > 0 && account.SummonerId == summonerId));
            result.Add(new LeagueWorkbenchLobbyMember(puuId, summonerId, displayName, isLocal));
        }
        return result;
    }

    internal static LeagueWorkbenchReadyCheck? ParseReadyCheck(byte[]? bytes)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = document.RootElement;
        var state = ReadString(root, "state");
        var response = ReadString(root, "playerResponse");
        var timer = ReadInt(root, "timer");
        if (timer <= 0) timer = ReadInt(root, "timerMillisecondsLeft");
        if (string.IsNullOrWhiteSpace(state) && string.IsNullOrWhiteSpace(response) && timer <= 0) return null;
        return new LeagueWorkbenchReadyCheck(state, response, timer);
    }

    internal static LeagueWorkbenchRankedSummary? ParseRankedSummary(byte[]? bytes)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = document.RootElement;

        var candidates = new List<JsonElement>();
        if (TryGetArray(root, "queues", out var queues)) candidates.AddRange(queues.EnumerateArray());
        if (TryGetObject(root, "queueMap") is JsonElement queueMap)
        {
            foreach (var property in queueMap.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object) candidates.Add(property.Value);
            }
        }

        JsonElement? selected = null;
        foreach (var candidate in candidates)
        {
            if (candidate.ValueKind != JsonValueKind.Object) continue;
            var type = FirstNonEmpty(ReadString(candidate, "queueType"), ReadString(candidate, "queue"));
            if (string.Equals(type, "RANKED_SOLO_5x5", StringComparison.OrdinalIgnoreCase))
            {
                selected = candidate;
                break;
            }
            if (selected is null && HasRankedData(candidate)) selected = candidate;
        }

        if (selected is null && HasRankedData(root)) selected = root;
        if (selected is not JsonElement value) return null;

        return new LeagueWorkbenchRankedSummary(
            FirstNonEmpty(ReadString(value, "queueType"), ReadString(value, "queue")),
            ReadString(value, "tier"),
            FirstNonEmpty(ReadString(value, "division"), ReadString(value, "rank")),
            ReadInt(value, "leaguePoints"),
            ReadInt(value, "wins"),
            ReadInt(value, "losses"));
    }

    internal static (IReadOnlyList<LeagueWorkbenchMatchSummary> Items, bool HasMore) ParseMatches(
        byte[]? bytes,
        LeagueWorkbenchAccount account,
        int requestedCount)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
            return (Array.Empty<LeagueWorkbenchMatchSummary>(), false);

        var root = document.RootElement;
        var gamesRoot = TryGetObject(root, "games");
        if (gamesRoot is not JsonElement gamesObject || !TryGetArray(gamesObject, "games", out var games))
            return (Array.Empty<LeagueWorkbenchMatchSummary>(), false);

        var result = new List<LeagueWorkbenchMatchSummary>();
        foreach (var game in games.EnumerateArray())
        {
            if (game.ValueKind != JsonValueKind.Object) continue;
            result.Add(ParseMatch(game, account));
        }
        return (result, requestedCount > 0 && result.Count >= requestedCount);
    }

    private static LeagueWorkbenchMatchSummary ParseMatch(JsonElement game, LeagueWorkbenchAccount account)
    {
        var participantId = ResolveParticipantId(game, account);
        JsonElement? participant = null;
        if (TryGetArray(game, "participants", out var participants))
        {
            foreach (var candidate in participants.EnumerateArray())
            {
                if (candidate.ValueKind == JsonValueKind.Object && ReadInt(candidate, "participantId") == participantId)
                {
                    participant = candidate;
                    break;
                }
            }
        }

        var stats = participant is JsonElement participantElement ? TryGetObject(participantElement, "stats") : null;
        var championId = participant is JsonElement championElement ? ReadInt(championElement, "championId") : 0;
        return new LeagueWorkbenchMatchSummary(
            ReadLong(game, "gameId"),
            ReadCreation(game),
            ReadInt(game, "gameDuration"),
            ReadString(game, "gameMode"),
            ReadInt(game, "queueId"),
            championId,
            string.Empty,
            stats is JsonElement statsElement ? ReadInt(statsElement, "kills") : 0,
            stats is JsonElement deathsElement ? ReadInt(deathsElement, "deaths") : 0,
            stats is JsonElement assistsElement ? ReadInt(assistsElement, "assists") : 0,
            stats is JsonElement csElement ? ReadInt(csElement, "totalMinionsKilled") + ReadInt(csElement, "neutralMinionsKilled") : 0,
            stats is JsonElement winElement && ReadBool(winElement, "win"),
            participant is not null);
    }

    private static int ResolveParticipantId(JsonElement game, LeagueWorkbenchAccount account)
    {
        if (!TryGetArray(game, "participantIdentities", out var identities)) return 0;
        foreach (var identity in identities.EnumerateArray())
        {
            if (identity.ValueKind != JsonValueKind.Object || TryGetObject(identity, "player") is not JsonElement player) continue;
            var puuId = ReadString(player, "puuid");
            if (!string.IsNullOrWhiteSpace(account.PuuId) && string.Equals(account.PuuId, puuId, StringComparison.OrdinalIgnoreCase))
                return ReadInt(identity, "participantId");
            if (account.SummonerId > 0 && ReadLong(player, "summonerId") == account.SummonerId)
                return ReadInt(identity, "participantId");
        }
        return 0;
    }

    private static bool HasRankedData(JsonElement value) =>
        !string.IsNullOrWhiteSpace(ReadString(value, "tier")) ||
        ReadInt(value, "wins") > 0 ||
        ReadInt(value, "losses") > 0 ||
        ReadInt(value, "leaguePoints") > 0;

    private static DateTimeOffset? ReadCreation(JsonElement game)
    {
        var milliseconds = ReadLong(game, "gameCreation");
        if (milliseconds > 0)
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); }
            catch (ArgumentOutOfRangeException) { }
        }

        var text = ReadString(game, "gameCreationDate");
        return DateTimeOffset.TryParse(text, out var parsed) ? parsed : null;
    }

    private static JsonDocument? ParseDocument(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try { return JsonDocument.Parse(bytes); }
        catch (JsonException) { return null; }
    }

    private static JsonElement? TryGetObject(JsonElement source, string key) =>
        source.ValueKind == JsonValueKind.Object &&
        source.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static bool TryGetArray(JsonElement source, string key, out JsonElement array)
    {
        if (source.ValueKind == JsonValueKind.Object &&
            source.TryGetProperty(key, out array) &&
            array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }
        array = default;
        return false;
    }

    private static string ReadString(JsonElement source, string key)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(key, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static int ReadInt(JsonElement source, string key)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(key, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : 0;
    }

    private static long ReadLong(JsonElement source, string key)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(key, out var value)) return 0L;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number) ? number : 0L;
    }

    private static bool ReadBool(JsonElement source, string key)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(key, out var value)) return false;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result) && result;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
