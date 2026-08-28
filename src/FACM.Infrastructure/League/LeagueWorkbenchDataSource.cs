using System.Text.Json;
using FACM.Core.League;
using FACM.Core.State;

namespace FACM.Infrastructure.League;

/// <summary>
/// Read-only LOL Workbench data source. All reads go through the process-wide
/// <see cref="ILeagueReadGateway"/> and live phase selection reuses the process-wide
/// <see cref="ILeagueGameflowReader"/>. It never creates another discovery/auth/polling owner.
/// Changed or unavailable LCU shapes fail soft and produce partial/unavailable snapshots.
/// </summary>
public sealed class LeagueWorkbenchDataSource : ILeagueWorkbenchDataSource
{
    internal const string CurrentSummonerPath = "/lol-summoner/v1/current-summoner";
    internal const string GameflowSessionPath = "/lol-gameflow/v1/session";
    internal const string LobbyPath = "/lol-lobby/v2/lobby";
    internal const string ReadyCheckPath = "/lol-matchmaking/v1/ready-check";
    internal const string RankedStatsPath = "/lol-ranked/v1/current-ranked-stats";
    internal const string ChampSelectSessionPath = "/lol-champ-select/v1/session";

    private readonly ILeagueReadGateway _gateway;
    private readonly ILeagueGameflowReader? _gameflow;

    public LeagueWorkbenchDataSource(ILeagueReadGateway gateway, ILeagueGameflowReader? gameflow = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _gameflow = gameflow;
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

    public async Task<LeagueWorkbenchLiveSnapshot> LoadLiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var phase = _gameflow?.Current;
        if (phase is null)
            return LeagueWorkbenchLiveSnapshot.Unavailable(string.Empty, "gameflow-unavailable");
        if (phase.ConnectionState != LeagueConnectionState.Connected)
            return LeagueWorkbenchLiveSnapshot.Unavailable(phase.Phase, "league-not-connected");

        if (phase.ProductState == LeagueProductState.ChampSelect)
        {
            var bytes = await _gateway.TryGetBytesAsync(ChampSelectSessionPath, cancellationToken).ConfigureAwait(false);
            return ParseChampSelect(bytes, phase.Phase);
        }

        if (phase.ProductState == LeagueProductState.InGame)
        {
            var bytes = await _gateway.TryGetBytesAsync(GameflowSessionPath, cancellationToken).ConfigureAwait(false);
            return ParseCurrentGame(bytes, phase.Phase);
        }

        return new LeagueWorkbenchLiveSnapshot(
            LeagueWorkbenchDataState.Partial,
            phase.Phase,
            0,
            null,
            0,
            string.Empty,
            0,
            string.Empty,
            0,
            string.Empty,
            0,
            false,
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<LeagueWorkbenchLivePlayer>(),
            "phase-has-no-live-session",
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

    internal static LeagueWorkbenchLiveSnapshot ParseChampSelect(byte[]? bytes, string phase)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
            return LeagueWorkbenchLiveSnapshot.Unavailable(phase, "champ-select-session-unavailable");

        var root = document.RootElement;
        var localCell = ReadInt(root, "localPlayerCellId");
        var players = new List<LeagueWorkbenchLivePlayer>();
        AppendChampSelectTeam(players, root, "myTeam", "ally", localCell);
        AppendChampSelectTeam(players, root, "theirTeam", "enemy", localCell);

        var allyBans = new List<int>();
        var enemyBans = new List<int>();
        if (TryGetObject(root, "bans") is JsonElement bans)
        {
            AppendInts(allyBans, bans, "myTeamBans");
            AppendInts(enemyBans, bans, "theirTeamBans");
        }

        var bench = new List<int>();
        AppendBenchChampionIds(bench, root);
        var timer = TryGetObject(root, "timer");
        var action = ResolveLocalAction(root, localCell);
        var queueId = ReadInt(root, "queueId");
        var queue = queueId > 0 ? new LeagueWorkbenchQueue(queueId, string.Empty, string.Empty) : null;
        var hasContent = players.Count > 0 || bench.Count > 0 || allyBans.Count > 0 || enemyBans.Count > 0 || queueId > 0;

        return new LeagueWorkbenchLiveSnapshot(
            hasContent ? LeagueWorkbenchDataState.Ready : LeagueWorkbenchDataState.Partial,
            phase,
            ReadLong(root, "gameId"),
            queue,
            0,
            string.Empty,
            localCell,
            timer is JsonElement timerElement ? ReadString(timerElement, "phase") : string.Empty,
            timer is JsonElement timerMilliseconds ? ReadInt(timerMilliseconds, "adjustedTimeLeftInPhase") : 0,
            action.Type,
            action.ChampionId,
            ReadBool(root, "benchEnabled"),
            allyBans,
            enemyBans,
            bench,
            players,
            hasContent ? "ready" : "partial",
            DateTimeOffset.UtcNow);
    }

    internal static LeagueWorkbenchLiveSnapshot ParseCurrentGame(byte[]? bytes, string phase)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
            return LeagueWorkbenchLiveSnapshot.Unavailable(phase, "current-game-session-unavailable");

        var root = document.RootElement;
        var gameData = TryGetObject(root, "gameData");
        if (gameData is not JsonElement data)
            return LeagueWorkbenchLiveSnapshot.Unavailable(phase, "current-game-data-unavailable");

        var queue = TryGetObject(data, "queue") is JsonElement queueElement
            ? new LeagueWorkbenchQueue(
                ReadInt(queueElement, "id"),
                FirstNonEmpty(ReadString(queueElement, "name"), ReadString(queueElement, "shortName")),
                ReadString(queueElement, "gameMode"))
            : null;
        var map = TryGetObject(root, "map");
        var players = new List<LeagueWorkbenchLivePlayer>();
        AppendCurrentGameTeam(players, data, "teamOne", "team-1");
        AppendCurrentGameTeam(players, data, "teamTwo", "team-2");
        var hasContent = players.Count > 0 || ReadLong(data, "gameId") > 0 || queue is not null;

        return new LeagueWorkbenchLiveSnapshot(
            hasContent ? LeagueWorkbenchDataState.Ready : LeagueWorkbenchDataState.Partial,
            FirstNonEmpty(ReadString(root, "phase"), phase),
            ReadLong(data, "gameId"),
            queue,
            map is JsonElement mapElement ? ReadInt(mapElement, "id") : 0,
            map is JsonElement mapNameElement
                ? FirstNonEmpty(ReadString(mapNameElement, "name"), ReadString(mapNameElement, "mapStringId"))
                : string.Empty,
            0,
            string.Empty,
            0,
            string.Empty,
            0,
            false,
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            players,
            hasContent ? "ready" : "partial",
            DateTimeOffset.UtcNow);
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

    private static void AppendChampSelectTeam(
        ICollection<LeagueWorkbenchLivePlayer> target,
        JsonElement root,
        string property,
        string side,
        int localCell)
    {
        if (!TryGetArray(root, property, out var members)) return;
        foreach (var member in members.EnumerateArray())
        {
            if (member.ValueKind != JsonValueKind.Object) continue;
            var cellId = ReadInt(member, "cellId");
            target.Add(new LeagueWorkbenchLivePlayer(
                side,
                cellId,
                cellId == localCell,
                ReadString(member, "puuid"),
                ReadLong(member, "summonerId"),
                ReadString(member, "gameName"),
                ReadString(member, "tagLine"),
                FirstNonEmpty(ReadString(member, "playerAlias"), ReadString(member, "internalName")),
                ReadString(member, "assignedPosition"),
                string.Empty,
                ReadInt(member, "championId"),
                ReadInt(member, "championPickIntent"),
                ReadInt(member, "spell1Id"),
                ReadInt(member, "spell2Id")));
        }
    }

    private static void AppendCurrentGameTeam(
        ICollection<LeagueWorkbenchLivePlayer> target,
        JsonElement gameData,
        string property,
        string side)
    {
        if (!TryGetArray(gameData, property, out var members)) return;
        foreach (var member in members.EnumerateArray())
        {
            if (member.ValueKind != JsonValueKind.Object) continue;
            target.Add(new LeagueWorkbenchLivePlayer(
                side,
                0,
                false,
                ReadString(member, "puuid"),
                ReadLong(member, "summonerId"),
                string.Empty,
                string.Empty,
                FirstNonEmpty(ReadString(member, "summonerName"), ReadString(member, "summonerInternalName")),
                ReadString(member, "selectedPosition"),
                ReadString(member, "selectedRole"),
                ReadInt(member, "championId"),
                0,
                0,
                0));
        }
    }

    private static (string Type, int ChampionId) ResolveLocalAction(JsonElement root, int localCell)
    {
        if (!TryGetArray(root, "actions", out var groups)) return (string.Empty, 0);
        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Array) continue;
            foreach (var action in group.EnumerateArray())
            {
                if (action.ValueKind != JsonValueKind.Object || ReadInt(action, "actorCellId") != localCell) continue;
                if (!ReadBool(action, "isInProgress")) continue;
                return (ReadString(action, "type"), ReadInt(action, "championId"));
            }
        }
        return (string.Empty, 0);
    }

    private static void AppendBenchChampionIds(ICollection<int> target, JsonElement root)
    {
        var seen = new HashSet<int>();
        if (TryGetArray(root, "benchChampions", out var champions))
        {
            foreach (var champion in champions.EnumerateArray())
            {
                if (champion.ValueKind != JsonValueKind.Object) continue;
                var id = ReadInt(champion, "championId");
                if (id > 0 && seen.Add(id)) target.Add(id);
            }
        }
        if (TryGetArray(root, "benchChampionIds", out var ids))
        {
            foreach (var value in ids.EnumerateArray())
            {
                var id = ReadIntValue(value);
                if (id > 0 && seen.Add(id)) target.Add(id);
            }
        }
    }

    private static void AppendInts(ICollection<int> target, JsonElement root, string property)
    {
        if (!TryGetArray(root, property, out var values)) return;
        foreach (var value in values.EnumerateArray())
        {
            var id = ReadIntValue(value);
            if (id > 0) target.Add(id);
        }
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
        return ReadIntValue(value);
    }

    private static int ReadIntValue(JsonElement value)
    {
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
