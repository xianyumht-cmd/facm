using System.Diagnostics;
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
