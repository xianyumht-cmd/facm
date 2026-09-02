using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FACM.Core.League;
using FACM.Infrastructure.League;
using FACM.Platform.Windows.League;

internal static class LeagueLcuAuditSmoke
{
    private const string EvidenceRoot = @"D:\project2\ggman-live-lcu-guide-audit-20260901";

    private static readonly string[] EndpointPaths =
    [
        "/lol-gameflow/v1/gameflow-phase",
        "/lol-gameflow/v1/session",
        "/lol-lobby/v2/lobby",
        "/lol-summoner/v1/current-summoner",
        "/lol-champ-select/v1/session",
        "/lol-lobby-team-builder/champ-select/v1/session",
        "/lol-game-data/assets/v1/champion-summary.json",
        "/lol-game-data/assets/v1/items.json",
        "/lol-game-data/assets/v1/summoner-spells.json",
        "/lol-game-data/assets/v1/perks.json",
        "/lol-game-data/assets/v1/cherry-augments.json",
        "/lol-game-data/assets/v1/champions/497.json",
        "/lol-game-data/assets/v1/champion-icons/497.png"
    ];

    public static async Task RunLiveAsync()
    {
        Directory.CreateDirectory(EvidenceRoot);
        var discoveryEvents = new List<LeagueSessionDiscoveryDiagnostic>();
        using var source = new WindowsLeagueTransportSessionSource(
            diagnosticReporter: discoveryEvents.Add,
            discoveryTimeout: TimeSpan.FromSeconds(3));
        var session = await source.GetSessionAsync().ConfigureAwait(false);
        for (var attempt = 1; session is null && attempt <= 3; attempt++)
        {
            // The live client can briefly expose an unreadable process snapshot while its UX
            // process is healthy. Reuse this same source for bounded retries; never create a
            // second discovery owner or retain command-line credentials.
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            session = await source.GetSessionAsync(forceRefresh: true).ConfigureAwait(false);
        }
        if (session is null)
        {
            await WriteEvidenceAsync(new
            {
                capturedAtUtc = DateTimeOffset.UtcNow,
                outcome = "no-session",
                discovery = discoveryEvents.Select(ToDiscoverySummary).ToArray(),
                endpoints = Array.Empty<object>()
            }).ConfigureAwait(false);
            Console.WriteLine("LIVE_LCU_AUDIT outcome=no-session");
            return;
        }

        var httpEvents = new List<LeagueHttpDiagnostic>();
        using var gateway = new LeagueHttpGateway(
            source,
            diagnosticReporter: httpEvents.Add);
        var endpoints = new List<object>();
        foreach (var path in EndpointPaths)
        {
            var start = httpEvents.Count;
            var bytes = await gateway.TryGetBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
            var completed = httpEvents
                .Skip(start)
                .LastOrDefault(item => item.Event == "completed");
            endpoints.Add(SummarizeEndpoint(path, bytes, completed));
        }

        var ux = FindLeagueClientUx();
        var audit = new
        {
            capturedAtUtc = DateTimeOffset.UtcNow,
            outcome = "success",
            session = new
            {
                source = session.Descriptor.Source,
                processId = session.Descriptor.ProcessId,
                port = session.Descriptor.Port,
                protocol = session.Descriptor.Protocol,
                platformIdPresent = !string.IsNullOrWhiteSpace(session.Descriptor.PlatformId),
                regionPresent = !string.IsNullOrWhiteSpace(session.Descriptor.Region)
            },
            leagueClientUx = ux,
            discovery = discoveryEvents.Select(ToDiscoverySummary).ToArray(),
            endpoints
        };
        await WriteEvidenceAsync(audit).ConfigureAwait(false);
        var phase = endpoints
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault(item => Equals(item.GetValueOrDefault("path"), "/lol-gameflow/v1/gameflow-phase"))
            ?.GetValueOrDefault("phase");
        Console.WriteLine($"LIVE_LCU_AUDIT outcome=success;phase={phase ?? "unknown"};endpointCount={endpoints.Count};evidence={EvidenceRoot}");
    }

    public static async Task ObserveChampSelectLiveAsync()
    {
        Directory.CreateDirectory(EvidenceRoot);
        var outputPath = Path.Combine(EvidenceRoot, "champselect-observation.jsonl");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var cancellationToken = lifetime.Token;
        var discoveryEvents = new List<LeagueSessionDiscoveryDiagnostic>();
        using var source = new WindowsLeagueTransportSessionSource(
            diagnosticReporter: discoveryEvents.Add,
            discoveryTimeout: TimeSpan.FromSeconds(3));
        var session = await DiscoverWithRetryAsync(source, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            await File.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(new
                {
                    capturedAtUtc = DateTimeOffset.UtcNow,
                    kind = "observer-finish",
                    outcome = "no-session",
                    discovery = discoveryEvents.Select(ToDiscoverySummary).ToArray()
                }) + Environment.NewLine,
                cancellationToken).ConfigureAwait(false);
            Console.WriteLine("LIVE_CHAMPSELECT_OBSERVER outcome=no-session");
            return;
        }

        var httpEvents = new List<LeagueHttpDiagnostic>();
        using var gateway = new LeagueHttpGateway(source, diagnosticReporter: httpEvents.Add);
        using var writer = new StreamWriter(outputPath, false, new System.Text.UTF8Encoding(false));
        var observedChampSelect = false;
        var observedAssetIds = new HashSet<int>();
        var sampleCount = 0;
        await WriteJsonLineAsync(writer, new
        {
            capturedAtUtc = DateTimeOffset.UtcNow,
            kind = "observer-start",
            outcome = "started",
            evidence = outputPath
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var sampleUtc = DateTimeOffset.UtcNow;
                var phaseCallStart = httpEvents.Count;
                var phaseBytes = await gateway.TryGetBytesAsync(
                    "/lol-gameflow/v1/gameflow-phase",
                    cancellationToken).ConfigureAwait(false);
                var phaseDiagnostic = LastCompleted(httpEvents, phaseCallStart);
                var phase = ParsePhase(phaseBytes);
                var currentSession = source.Current;
                object? champSelect = null;
                object? assets = null;

                if (string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
                {
                    observedChampSelect = true;
                    var primary = await ReadEndpointAsync(
                        gateway,
                        httpEvents,
                        "/lol-champ-select/v1/session",
                        cancellationToken).ConfigureAwait(false);
                    var teamBuilder = await ReadEndpointAsync(
                        gateway,
                        httpEvents,
                        "/lol-lobby-team-builder/champ-select/v1/session",
                        cancellationToken).ConfigureAwait(false);
                    champSelect = new
                    {
                        primary = SummarizeChampSelect(primary.Bytes, primary.Diagnostic, "legacy"),
                        teamBuilder = SummarizeChampSelect(teamBuilder.Bytes, teamBuilder.Diagnostic, "team-builder")
                    };

                    foreach (var championId in ExtractCandidateChampionIds(primary.Bytes, teamBuilder.Bytes))
                    {
                        if (!observedAssetIds.Add(championId)) continue;
                        var detail = await ReadEndpointAsync(
                            gateway,
                            httpEvents,
                            "/lol-game-data/assets/v1/champions/" + championId.ToString(CultureInfo.InvariantCulture) + ".json",
                            cancellationToken).ConfigureAwait(false);
                        var icon = await ReadEndpointAsync(
                            gateway,
                            httpEvents,
                            "/lol-game-data/assets/v1/champion-icons/" + championId.ToString(CultureInfo.InvariantCulture) + ".png",
                            cancellationToken).ConfigureAwait(false);
                        assets = new
                        {
                            championId,
                            detail = SummarizeJson(detail.Bytes, detail.Diagnostic),
                            icon = SummarizeBinary(icon.Bytes, icon.Diagnostic)
                        };
                        break;
                    }
                }

                var record = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["capturedAtUtc"] = sampleUtc,
                    ["kind"] = "sample",
                    ["sample"] = ++sampleCount,
                    ["phase"] = phase,
                    ["phaseRequest"] = SummarizeRequest(phaseBytes, phaseDiagnostic),
                    ["session"] = currentSession is null
                        ? null
                        : new
                        {
                            source = currentSession.Source,
                            processId = currentSession.ProcessId,
                            port = currentSession.Port,
                            protocol = currentSession.Protocol
                        },
                    ["champSelect"] = champSelect,
                    ["assets"] = assets
                };
                await WriteJsonLineAsync(writer, record, cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(
                    string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase)
                        ? TimeSpan.FromMilliseconds(500)
                        : TimeSpan.FromSeconds(1),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        await WriteJsonLineAsync(writer, new
        {
            capturedAtUtc = DateTimeOffset.UtcNow,
            kind = "observer-finish",
            outcome = observedChampSelect ? "window-complete-after-champselect" : "timeout-no-champselect",
            sampleCount,
            discovery = discoveryEvents.Select(ToDiscoverySummary).ToArray()
        }, CancellationToken.None).ConfigureAwait(false);
        await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"LIVE_CHAMPSELECT_OBSERVER outcome={(observedChampSelect ? "window-complete-after-champselect" : "timeout-no-champselect")};samples={sampleCount};evidence={outputPath}");
    }

    private static async Task<LeagueTransportSession?> DiscoverWithRetryAsync(
        WindowsLeagueTransportSessionSource source,
        CancellationToken cancellationToken)
    {
        var session = await source.GetSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        for (var attempt = 1; session is null && attempt <= 3; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            session = await source.GetSessionAsync(forceRefresh: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        return session;
    }

    private static async Task<(byte[]? Bytes, LeagueHttpDiagnostic? Diagnostic)> ReadEndpointAsync(
        LeagueHttpGateway gateway,
        List<LeagueHttpDiagnostic> events,
        string path,
        CancellationToken cancellationToken)
    {
        var start = events.Count;
        var bytes = await gateway.TryGetBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return (bytes, LastCompleted(events, start));
    }

    private static LeagueHttpDiagnostic? LastCompleted(List<LeagueHttpDiagnostic> events, int start) =>
        events.Skip(start).LastOrDefault(item => item.Event == "completed");

    private static async Task WriteJsonLineAsync(StreamWriter writer, object value, CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static object SummarizeRequest(byte[]? bytes, LeagueHttpDiagnostic? diagnostic) => new
    {
        status = diagnostic?.StatusCode,
        outcome = diagnostic?.Outcome ?? "missing-diagnostic",
        durationMs = diagnostic?.DurationMs,
        bodyBytes = bytes?.Length ?? 0
    };

    private static string ParsePhase(byte[]? bytes) =>
        bytes is null || bytes.Length == 0
            ? string.Empty
            : System.Text.Encoding.UTF8.GetString(bytes).Trim().Trim('"');

    private static object SummarizeBinary(byte[]? bytes, LeagueHttpDiagnostic? diagnostic) => new
    {
        status = diagnostic?.StatusCode,
        outcome = diagnostic?.Outcome ?? "missing-diagnostic",
        durationMs = diagnostic?.DurationMs,
        bodyBytes = bytes?.Length ?? 0,
        content = bytes is { Length: > 0 } ? "binary" : "empty"
    };

    private static object SummarizeJson(byte[]? bytes, LeagueHttpDiagnostic? diagnostic)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["status"] = diagnostic?.StatusCode,
            ["outcome"] = diagnostic?.Outcome ?? "missing-diagnostic",
            ["durationMs"] = diagnostic?.DurationMs,
            ["bodyBytes"] = bytes?.Length ?? 0
        };
        if (bytes is null || bytes.Length == 0) return result;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            result["rootKind"] = document.RootElement.ValueKind.ToString();
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                result["topLevelKeys"] = document.RootElement.EnumerateObject().Select(item => item.Name).OrderBy(item => item).ToArray();
        }
        catch (JsonException)
        {
            result["rootKind"] = "invalid-json";
        }
        return result;
    }

    private static object SummarizeChampSelect(
        byte[]? bytes,
        LeagueHttpDiagnostic? diagnostic,
        string route)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["route"] = route,
            ["status"] = diagnostic?.StatusCode,
            ["outcome"] = diagnostic?.Outcome ?? "missing-diagnostic",
            ["durationMs"] = diagnostic?.DurationMs,
            ["bodyBytes"] = bytes?.Length ?? 0
        };
        if (bytes is null || bytes.Length == 0) return result;

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            result["rootKind"] = root.ValueKind.ToString();
            if (root.ValueKind != JsonValueKind.Object) return result;
            result["topLevelKeys"] = root.EnumerateObject().Select(item => item.Name).OrderBy(item => item).ToArray();
            result["localPlayerCellId"] = ReadInt(root, "localPlayerCellId");
            result["queueId"] = ReadInt(root, "queueId");
            result["gameIdType"] = ValueType(root, "gameId");
            result["benchEnabledType"] = ValueType(root, "benchEnabled");
            result["isLegacyChampSelectType"] = ValueType(root, "isLegacyChampSelect");
            result["myTeamCount"] = ArrayLength(root, "myTeam");
            result["theirTeamCount"] = ArrayLength(root, "theirTeam");
            result["benchChampionsCount"] = ArrayLength(root, "benchChampions");
            result["myTeamBansCount"] = NestedArrayLength(root, "bans", "myTeamBans");
            result["theirTeamBansCount"] = NestedArrayLength(root, "bans", "theirTeamBans");
            result["localPlayer"] = SummarizeLocalPlayer(root);
            result["localActions"] = SummarizeLocalActions(root, ReadInt(root, "localPlayerCellId"));
            result["benchChampionIds"] = ReadBenchChampionIds(root);
        }
        catch (JsonException)
        {
            result["rootKind"] = "invalid-json";
        }
        return result;
    }

    private static object? SummarizeLocalPlayer(JsonElement root)
    {
        var localCell = ReadInt(root, "localPlayerCellId");
        foreach (var property in new[] { "myTeam", "theirTeam" })
        {
            if (!TryGetArray(root, property, out var members)) continue;
            foreach (var member in members.EnumerateArray())
            {
                if (member.ValueKind != JsonValueKind.Object || ReadInt(member, "cellId") != localCell) continue;
                return new
                {
                    side = property,
                    cellId = localCell,
                    championId = ReadInt(member, "championId"),
                    championPickIntent = ReadInt(member, "championPickIntent"),
                    spell1Id = ReadInt(member, "spell1Id"),
                    spell2Id = ReadInt(member, "spell2Id"),
                    keys = member.EnumerateObject().Select(item => item.Name).OrderBy(item => item).ToArray()
                };
            }
        }
        return null;
    }

    private static IReadOnlyList<object> SummarizeLocalActions(JsonElement root, int localCell)
    {
        var actions = new List<object>();
        if (!TryGetArray(root, "actions", out var groups)) return actions;
        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Array) continue;
            foreach (var action in group.EnumerateArray())
            {
                if (action.ValueKind != JsonValueKind.Object || ReadInt(action, "actorCellId") != localCell) continue;
                actions.Add(new
                {
                    actorCellId = localCell,
                    type = ReadString(action, "type"),
                    championId = ReadInt(action, "championId"),
                    isInProgress = ReadBool(action, "isInProgress"),
                    isCompleted = ReadBool(action, "isCompleted"),
                    pickTurn = ReadInt(action, "pickTurn"),
                    keys = action.EnumerateObject().Select(item => item.Name).OrderBy(item => item).ToArray()
                });
            }
        }
        return actions;
    }

    private static IReadOnlyList<int> ReadBenchChampionIds(JsonElement root)
    {
        var ids = new HashSet<int>();
        if (TryGetArray(root, "benchChampions", out var champions))
            foreach (var item in champions.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object && ReadInt(item, "championId") > 0)
                    ids.Add(ReadInt(item, "championId"));
        if (TryGetArray(root, "benchChampionIds", out var values))
            foreach (var item in values.EnumerateArray())
                if (ReadIntValue(item) > 0) ids.Add(ReadIntValue(item));
        return ids.OrderBy(item => item).ToArray();
    }

    private static IReadOnlyList<int> ExtractCandidateChampionIds(params byte[]?[] payloads)
    {
        var ids = new HashSet<int>();
        foreach (var bytes in payloads)
        {
            if (bytes is null || bytes.Length == 0) continue;
            try
            {
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                var localCell = ReadInt(root, "localPlayerCellId");
                if (TryGetArray(root, "myTeam", out var team))
                    foreach (var member in team.EnumerateArray())
                        if (member.ValueKind == JsonValueKind.Object && ReadInt(member, "cellId") == localCell)
                        {
                            AddIfPositive(ids, ReadInt(member, "championId"));
                            AddIfPositive(ids, ReadInt(member, "championPickIntent"));
                        }
                if (TryGetArray(root, "actions", out var groups))
                    foreach (var group in groups.EnumerateArray())
                        if (group.ValueKind == JsonValueKind.Array)
                            foreach (var action in group.EnumerateArray())
                                if (action.ValueKind == JsonValueKind.Object && ReadInt(action, "actorCellId") == localCell)
                                    AddIfPositive(ids, ReadInt(action, "championId"));
            }
            catch (JsonException)
            {
            }
        }
        return ids.OrderBy(value => value).ToArray();
    }

    private static void AddIfPositive(ISet<int> target, int value)
    {
        if (value > 0) target.Add(value);
    }

    private static int ArrayLength(JsonElement root, string property) =>
        TryGetArray(root, property, out var value) ? value.GetArrayLength() : 0;

    private static int NestedArrayLength(JsonElement root, string parent, string property) =>
        root.TryGetProperty(parent, out var value) && value.ValueKind == JsonValueKind.Object
            ? ArrayLength(value, property)
            : 0;

    private static string ValueType(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) ? value.ValueKind.ToString() : "Missing";

    private static int ReadIntValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    private static int ReadInt(JsonElement source, string key) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(key, out var value)
            ? value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0
            : 0;

    private static long ReadLong(JsonElement source, string key) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(key, out var value)
            ? value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0
            : 0;

    private static bool ReadBool(JsonElement source, string key) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(key, out var value) &&
        value.ValueKind is JsonValueKind.True;

    private static string ReadString(JsonElement source, string key) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool TryGetArray(JsonElement source, string key, out JsonElement array)
    {
        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty(key, out array) &&
            array.ValueKind == JsonValueKind.Array) return true;
        array = default;
        return false;
    }

    private static async Task WriteEvidenceAsync(object value)
    {
        var path = Path.Combine(EvidenceRoot, "lobby-audit.json");
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json + Environment.NewLine).ConfigureAwait(false);
    }

    private static object ToDiscoverySummary(LeagueSessionDiscoveryDiagnostic item) => new
    {
        item.Event,
        item.Source,
        item.ProcessId,
        item.Port,
        item.Outcome,
        item.DurationMs,
        item.CacheHit,
        item.NegativeCacheHit,
        item.JoinedExistingDiscovery,
        item.Reason
    };

    private static Dictionary<string, object?> SummarizeEndpoint(
        string path,
        byte[]? bytes,
        LeagueHttpDiagnostic? completed)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = path,
            ["status"] = completed?.StatusCode,
            ["outcome"] = completed?.Outcome ?? "missing-diagnostic",
            ["durationMs"] = completed?.DurationMs,
            ["bodyBytes"] = bytes?.Length ?? 0
        };

        if (bytes is null || bytes.Length == 0) return result;
        if (path.EndsWith("gameflow-phase", StringComparison.Ordinal))
        {
            var phase = System.Text.Encoding.UTF8.GetString(bytes).Trim().Trim('"');
            result["rootKind"] = "string";
            result["phase"] = phase;
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            result["rootKind"] = document.RootElement.ValueKind.ToString();
            AddShape(result, document.RootElement, path);
        }
        catch (JsonException)
        {
            result["rootKind"] = "invalid-json";
        }

        return result;
    }

    private static void AddShape(Dictionary<string, object?> result, JsonElement root, string path)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            result["topLevelKeys"] = root.EnumerateObject().Select(item => item.Name).OrderBy(item => item).ToArray();
            AddArrayCount(result, root, "members", "membersCount");
            AddArrayCount(result, root, "myTeam", "myTeamCount");
            AddArrayCount(result, root, "theirTeam", "theirTeamCount");
            AddArrayCount(result, root, "benchChampions", "benchChampionsCount");
            AddNestedArrayCount(result, root, "bans", "myTeamBans", "myTeamBansCount");
            AddNestedArrayCount(result, root, "bans", "theirTeamBans", "theirTeamBansCount");
            AddSafeScalarType(result, root, "queueId");
            AddSafeScalarType(result, root, "localPlayerCellId");
            AddSafeScalarType(result, root, "benchEnabled");
            AddSafeScalarType(result, root, "isLegacyChampSelect");
            AddSafeScalarType(result, root, "version");
            AddSafeScalarType(result, root, "gameVersion");
            return;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            result["arrayCount"] = root.GetArrayLength();
            var firstObject = root.EnumerateArray().FirstOrDefault(item => item.ValueKind == JsonValueKind.Object);
            if (firstObject.ValueKind == JsonValueKind.Object)
                result["firstObjectKeys"] = firstObject.EnumerateObject().Select(item => item.Name).OrderBy(item => item).ToArray();
        }
    }

    private static void AddArrayCount(Dictionary<string, object?> result, JsonElement root, string property, string output)
    {
        if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array)
            result[output] = value.GetArrayLength();
    }

    private static void AddNestedArrayCount(
        Dictionary<string, object?> result,
        JsonElement root,
        string parent,
        string property,
        string output)
    {
        if (root.TryGetProperty(parent, out var value) && value.ValueKind == JsonValueKind.Object)
            AddArrayCount(result, value, property, output);
    }

    private static void AddSafeScalarType(Dictionary<string, object?> result, JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value))
            result[property + "Type"] = value.ValueKind.ToString();
    }

    private static object FindLeagueClientUx()
    {
        try
        {
            var process = Process.GetProcessesByName("LeagueClientUx")
                .FirstOrDefault(item => item.Responding && !string.IsNullOrWhiteSpace(item.MainModule?.FileName));
            if (process is null) return new { present = false };
            using (process)
            {
                var file = process.MainModule?.FileName ?? string.Empty;
                var version = File.Exists(file)
                    ? FileVersionInfo.GetVersionInfo(file).FileVersion ?? string.Empty
                    : string.Empty;
                return new { present = true, fileVersion = version };
            }
        }
        catch
        {
            return new { present = true, fileVersion = string.Empty };
        }
    }
}
