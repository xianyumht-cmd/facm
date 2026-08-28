using System.Text.Json;
using FACM.Core.League;

namespace FACM.Infrastructure.League;

/// <summary>
/// FACM 4.0 matchmaking automation. It never owns a phase polling loop: every evaluation is driven
/// by the one process-wide LeagueGameflowMonitor heartbeat and all writes go through narrow capabilities.
/// </summary>
public sealed class LeagueMatchmakingAutomationService : ILeagueMatchmakingAutomationService, IDisposable
{
    internal const string LobbyPath = "/lol-lobby/v2/lobby";
    internal const string SearchStatePath = "/lol-matchmaking/v1/search";

    private readonly object _sync = new();
    private readonly ILeagueReadGateway _read;
    private readonly ILeagueWriteGateway _write;
    private readonly ILeagueGameflowObservationSource _gameflow;
    private readonly SemaphoreSlim _evaluationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _autoSearch;
    private bool _autoAccept;
    private string _lastPhase = string.Empty;
    private string? _lastSearchFingerprint;
    private bool _acceptAttemptedThisReadyCheck;
    private bool _disposed;

    public LeagueMatchmakingAutomationService(
        ILeagueReadGateway read,
        ILeagueWriteGateway write,
        ILeagueGameflowObservationSource gameflow)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _gameflow = gameflow ?? throw new ArgumentNullException(nameof(gameflow));
        _gameflow.Observed += OnGameflowObserved;
    }

    public bool AutoSearchEnabled
    {
        get { lock (_sync) return _autoSearch; }
    }

    public bool AutoAcceptEnabled
    {
        get { lock (_sync) return _autoAccept; }
    }

    public void Configure(bool autoSearch, bool autoAccept)
    {
        LeagueGameflowSnapshot? current;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!autoSearch && _autoSearch) _lastSearchFingerprint = null;
            if (!autoAccept && _autoAccept) _acceptAttemptedThisReadyCheck = false;
            _autoSearch = autoSearch;
            _autoAccept = autoAccept;
            current = _gameflow.Current;
        }

        // If settings become enabled while we are already in Lobby/ReadyCheck, consume the current
        // shared snapshot once. This still does not create a second poll owner.
        if (current is not null && PrepareObservation(current))
            _ = EvaluateObservedSafelyAsync(current);
    }

    private void OnGameflowObserved(object? sender, LeagueGameflowChangedEventArgs args)
    {
        if (!PrepareObservation(args.Current)) return;
        _ = EvaluateObservedSafelyAsync(args.Current);
    }

    private bool PrepareObservation(LeagueGameflowSnapshot snapshot)
    {
        lock (_sync)
        {
            if (_disposed) return false;
            var phase = snapshot.ConnectionState == LeagueConnectionState.Connected
                ? (snapshot.Phase ?? string.Empty).Trim()
                : string.Empty;

            if (!string.Equals(phase, "Lobby", StringComparison.OrdinalIgnoreCase))
                _lastSearchFingerprint = null;
            if (!string.Equals(phase, "ReadyCheck", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_lastPhase, "ReadyCheck", StringComparison.OrdinalIgnoreCase))
                _acceptAttemptedThisReadyCheck = false;

            _lastPhase = phase;
            return (string.Equals(phase, "Lobby", StringComparison.OrdinalIgnoreCase) && _autoSearch) ||
                   (string.Equals(phase, "ReadyCheck", StringComparison.OrdinalIgnoreCase) && _autoAccept);
        }
    }

    private async Task EvaluateObservedSafelyAsync(LeagueGameflowSnapshot snapshot)
    {
        try
        {
            await EvaluateObservationAsync(snapshot, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            // Automation is best-effort. A malformed Tencent response or transient LCU error must
            // never destabilize the shared League runtime or FACM shell.
        }
    }

    internal async Task EvaluateForSmokeTestAsync(
        LeagueGameflowSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!PrepareObservation(snapshot)) return;
        await EvaluateObservationAsync(snapshot, cancellationToken, waitForGate: true).ConfigureAwait(false);
    }

    private async Task EvaluateObservationAsync(
        LeagueGameflowSnapshot snapshot,
        CancellationToken cancellationToken,
        bool waitForGate = false)
    {
        if (waitForGate)
        {
            await _evaluationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (!await _evaluationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            // Another heartbeat evaluation is still in flight. The shared monitor will publish the
            // next observation; do not build a queue or another timer here.
            return;
        }

        try
        {
            if (string.Equals(snapshot.Phase, "Lobby", StringComparison.OrdinalIgnoreCase))
                await EvaluateLobbyAsync(cancellationToken).ConfigureAwait(false);
            else if (string.Equals(snapshot.Phase, "ReadyCheck", StringComparison.OrdinalIgnoreCase))
                await EvaluateReadyCheckAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _evaluationGate.Release();
        }
    }

    private async Task EvaluateLobbyAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_disposed || !_autoSearch || !string.Equals(_lastPhase, "Lobby", StringComparison.OrdinalIgnoreCase))
                return;
        }

        var bytes = await _read.TryGetBytesAsync(LobbyPath, cancellationToken).ConfigureAwait(false);
        var lobby = ParseLobby(bytes);
        if (lobby is null || !lobby.IsEligible) return;

        lock (_sync)
        {
            if (_disposed || !_autoSearch || !string.Equals(_lastPhase, "Lobby", StringComparison.OrdinalIgnoreCase))
                return;
            if (string.Equals(_lastSearchFingerprint, lobby.Fingerprint, StringComparison.Ordinal)) return;
            // One write attempt for a stable lobby membership. We intentionally mark before writing,
            // matching 3.5's protection against repeated matchmaking POST storms.
            _lastSearchFingerprint = lobby.Fingerprint;
        }

        _ = await _write.ExecuteAsync(
            new LeagueWriteCommand(LeagueWriteCapability.StartMatchmaking, null, null),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EvaluateReadyCheckAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_disposed || !_autoAccept ||
                !string.Equals(_lastPhase, "ReadyCheck", StringComparison.OrdinalIgnoreCase) ||
                _acceptAttemptedThisReadyCheck)
                return;
        }

        var bytes = await _read.TryGetBytesAsync(SearchStatePath, cancellationToken).ConfigureAwait(false);
        var response = ParseReadyCheckResponse(bytes);
        if (IsFinalResponse(response))
        {
            lock (_sync) _acceptAttemptedThisReadyCheck = true;
            return;
        }

        lock (_sync)
        {
            if (_disposed || !_autoAccept ||
                !string.Equals(_lastPhase, "ReadyCheck", StringComparison.OrdinalIgnoreCase) ||
                _acceptAttemptedThisReadyCheck)
                return;
            // Missing/partial search data must not block ReadyCheck accept. Mark first so one ready
            // check can never produce repeated accept writes.
            _acceptAttemptedThisReadyCheck = true;
        }

        _ = await _write.ExecuteAsync(
            new LeagueWriteCommand(LeagueWriteCapability.AcceptReadyCheck, null, null),
            cancellationToken).ConfigureAwait(false);
    }

    internal static LeagueLobbyEligibility? ParseLobby(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("localMember", out var local) ||
                local.ValueKind != JsonValueKind.Object)
                return null;

            var canStart = ReadBoolean(root, "canStartActivity");
            var isLeader = ReadBoolean(local, "isLeader");
            var queueId = 0;
            if (root.TryGetProperty("gameConfig", out var game) && game.ValueKind == JsonValueKind.Object)
                queueId = ReadInt32(game, "queueId");

            var members = new List<string>();
            var realMemberCount = 0;
            if (root.TryGetProperty("members", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var member in rows.EnumerateArray())
                {
                    if (member.ValueKind != JsonValueKind.Object ||
                        ReadBoolean(member, "isBot") || ReadBoolean(member, "isSpectator"))
                        continue;
                    realMemberCount++;
                    var id = ReadScalarString(member, "puuid");
                    if (string.IsNullOrWhiteSpace(id)) id = ReadScalarString(member, "summonerId");
                    if (!string.IsNullOrWhiteSpace(id)) members.Add(id.Trim());
                }
            }

            return new LeagueLobbyEligibility(canStart, isLeader, queueId, realMemberCount, members);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? ParseReadyCheckResponse(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("readyCheck", out var ready) ||
                ready.ValueKind != JsonValueKind.Object)
                return null;
            return ReadScalarString(ready, "playerResponse");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsFinalResponse(string? response) =>
        string.Equals(response, "Accepted", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(response, "Declined", StringComparison.OrdinalIgnoreCase);

    private static bool ReadBoolean(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static int ReadInt32(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        return 0;
    }

    private static string? ReadScalarString(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _gameflow.Observed -= OnGameflowObserved;
        _lifetime.Cancel();
        _lifetime.Dispose();
        // Do not dispose _evaluationGate here: an in-flight heartbeat may still be unwinding and
        // releasing it after cancellation. The service is process-scoped, so retaining this tiny
        // managed semaphore until GC avoids a shutdown race without retaining external resources.
    }
}

internal sealed record LeagueLobbyEligibility(
    bool CanStartActivity,
    bool IsLeader,
    int QueueId,
    int RealMemberCount,
    IReadOnlyList<string> MemberIds)
{
    public bool IsEligible => CanStartActivity && IsLeader && RealMemberCount > 0;

    public string Fingerprint
    {
        get
        {
            var members = MemberIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var memberPart = members.Length > 0 ? string.Join(",", members) : "count:" + RealMemberCount;
            return "queue:" + QueueId + "|members:" + memberPart;
        }
    }
}
