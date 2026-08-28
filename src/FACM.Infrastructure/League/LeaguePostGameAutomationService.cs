using System.Globalization;
using System.Text.Json;
using FACM.Core.League;

namespace FACM.Infrastructure.League;

/// <summary>
/// FACM 4.0 post-game automation. Phase ownership stays with the one process-wide gameflow monitor;
/// the short ballot/verification waits below are scoped to one post-game action cycle only.
/// </summary>
public sealed class LeaguePostGameAutomationService : ILeaguePostGameAutomationService, IDisposable
{
    internal const string BallotPath = "/lol-honor-v2/v1/ballot";
    internal const string TeamChoicesPath = "/lol-honor-v2/v1/team-choices";
    internal const string CurrentSummonerPath = "/lol-summoner/v1/current-summoner";
    private static readonly TimeSpan BallotPollInterval = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan BallotWaitLimit = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan[] VerificationDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000),
        TimeSpan.FromMilliseconds(1500),
        TimeSpan.FromMilliseconds(2250)
    ];

    private readonly object _sync = new();
    private readonly ILeagueReadGateway _read;
    private readonly ILeagueWriteGateway _write;
    private readonly ILeagueGameflowObservationSource _gameflow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<int, int> _chooseIndex;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _cycleCancellation;
    private LeagueHonorAttemptStatus? _lastHonorStatus;
    private bool _autoHonor;
    private bool _autoReturnLobby;
    private bool _insidePostGame;
    private bool _cycleStarted;
    private bool _disposed;

    public LeaguePostGameAutomationService(
        ILeagueReadGateway read,
        ILeagueWriteGateway write,
        ILeagueGameflowObservationSource gameflow,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<int, int>? chooseIndex = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _gameflow = gameflow ?? throw new ArgumentNullException(nameof(gameflow));
        _delay = delay ?? Task.Delay;
        _chooseIndex = chooseIndex ?? (count => count <= 1 ? 0 : Random.Shared.Next(count));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _gameflow.Observed += OnGameflowObserved;
    }

    public event EventHandler? StatusChanged;

    public bool AutoHonorEnabled
    {
        get { lock (_sync) return _autoHonor; }
    }

    public bool AutoReturnLobbyEnabled
    {
        get { lock (_sync) return _autoReturnLobby; }
    }

    public LeagueHonorAttemptStatus? LastHonorStatus
    {
        get { lock (_sync) return _lastHonorStatus; }
    }

    public void Configure(bool autoHonor, bool autoReturnLobby)
    {
        LeagueGameflowSnapshot? current;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _autoHonor = autoHonor;
            _autoReturnLobby = autoReturnLobby;
            if (!_autoHonor && !_autoReturnLobby) CancelCycleLocked();
            current = _gameflow.Current;
        }

        if (current is not null) ObserveSnapshot(current);
    }

    private void OnGameflowObserved(object? sender, LeagueGameflowChangedEventArgs args) =>
        ObserveSnapshot(args.Current);

    private void ObserveSnapshot(LeagueGameflowSnapshot snapshot)
    {
        string phase;
        CancellationToken token;
        if (!TryBeginCycle(snapshot, out phase, out token)) return;
        _ = RunCycleSafelyAsync(phase, token);
    }

    internal async Task ObserveForSmokeTestAsync(
        LeagueGameflowSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        string phase;
        CancellationToken token;
        if (!TryBeginCycle(snapshot, out phase, out token)) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
        await RunCycleAsync(phase, linked.Token).ConfigureAwait(false);
    }

    private bool TryBeginCycle(
        LeagueGameflowSnapshot snapshot,
        out string phase,
        out CancellationToken token)
    {
        phase = snapshot.ConnectionState == LeagueConnectionState.Connected
            ? (snapshot.Phase ?? string.Empty).Trim()
            : string.Empty;
        token = CancellationToken.None;
        var postGame = IsPostGamePhase(phase);

        lock (_sync)
        {
            if (_disposed) return false;
            if (!postGame)
            {
                _insidePostGame = false;
                _cycleStarted = false;
                CancelCycleLocked();
                return false;
            }

            _insidePostGame = true;
            if (_cycleStarted || (!_autoHonor && !_autoReturnLobby)) return false;
            _cycleStarted = true;
            CancelCycleLocked();
            _cycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            token = _cycleCancellation.Token;
            return true;
        }
    }

    private async Task RunCycleSafelyAsync(string phase, CancellationToken cancellationToken)
    {
        try
        {
            await RunCycleAsync(phase, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Best-effort post-game automation must never destabilize the shared League runtime.
        }
    }

    private async Task RunCycleAsync(string initialPhase, CancellationToken cancellationToken)
    {
        bool honor;
        bool returnLobby;
        lock (_sync)
        {
            honor = _autoHonor;
            returnLobby = _autoReturnLobby;
        }

        if (honor)
        {
            var ballot = await WaitForBallotAsync(cancellationToken).ConfigureAwait(false);
            if (ballot is null)
                PublishStatus(CreateStatus(0, "skipped", "none", "ballot-timeout", 0, 0, null));
            else if (ballot.GameId <= 0)
                PublishStatus(CreateStatus(ballot.GameId, "skipped", "none", "invalid-game", 0, 0, null));
            else if (ballot.Votes <= 0)
                PublishStatus(CreateStatus(ballot.GameId, "skipped", "none", "no-votes", 0, 0, null));
            else
                await TryHonorOneAllyAsync(ballot, cancellationToken).ConfigureAwait(false);
        }

        if (!returnLobby) return;
        await _delay(honor ? TimeSpan.FromMilliseconds(350) : ResolveReturnDelay(initialPhase), cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsStillEnabledForReturn()) return;

        _ = await _write.ExecuteAsync(
            new LeagueWriteCommand(LeagueWriteCapability.PlayAgain, null, null),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<HonorBallot?> WaitForBallotAsync(CancellationToken cancellationToken)
    {
        var elapsed = TimeSpan.Zero;
        while (elapsed < BallotWaitLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsStillEnabledForHonor()) return null;

            var ballot = ParseBallot(await _read.TryGetBytesAsync(BallotPath, cancellationToken).ConfigureAwait(false));
            if (ballot is { GameId: > 0 }) return ballot;

            var remaining = BallotWaitLimit - elapsed;
            var delay = remaining < BallotPollInterval ? remaining : BallotPollInterval;
            if (delay <= TimeSpan.Zero) break;
            await _delay(delay, cancellationToken).ConfigureAwait(false);
            elapsed += delay;
        }
        return null;
    }

    private async Task TryHonorOneAllyAsync(HonorBallot ballot, CancellationToken cancellationToken)
    {
        var selfPuuid = await TryReadSelfPuuidAsync(cancellationToken).ConfigureAwait(false);
        var candidates = ballot.Allies
            .Where(item => !item.BotPlayer && !string.IsNullOrWhiteSpace(item.Puuid))
            .Where(item => string.IsNullOrEmpty(selfPuuid) || !string.Equals(item.Puuid, selfPuuid, StringComparison.Ordinal))
            .Where(item => !ballot.HonoredPuuids.Contains(item.Puuid, StringComparer.Ordinal))
            .GroupBy(item => item.Puuid, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (candidates.Count == 0)
        {
            PublishStatus(CreateStatus(ballot.GameId, "skipped", "none", "no-eligible-ally", 0, 0, null));
            return;
        }

        var index = _chooseIndex(candidates.Count);
        if (index < 0 || index >= candidates.Count) index = 0;
        var selected = candidates[index];

        if (selected.SummonerId > 0)
        {
            var v2 = await TryHonorV2Async(ballot, selected, cancellationToken).ConfigureAwait(false);
            if (v2 is not null)
            {
                PublishStatus(v2);
                return;
            }
        }

        PublishStatus(await TryHonorLegacyAsync(ballot, selected, cancellationToken).ConfigureAwait(false));
    }

    private async Task<LeagueHonorAttemptStatus?> TryHonorV2Async(
        HonorBallot ballot,
        HonorCandidate selected,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new
        {
            summonerId = selected.SummonerId,
            puuid = selected.Puuid,
            honorType = "HEART",
            gameId = ballot.GameId
        });
        var attempts = 1;
        var response = await _write.ExecuteAsync(
            new LeagueWriteCommand(LeagueWriteCapability.HonorPlayerV2, null, json),
            cancellationToken).ConfigureAwait(false);

        if (response is { StatusCode: 404 or 405 }) return null;

        var verification = await VerifyHonorAsync(ballot, selected, cancellationToken).ConfigureAwait(false);
        if (verification.State == "confirmed")
            return CreateStatus(ballot.GameId, "success", "v2", verification.Detail, response?.StatusCode ?? 0, attempts, selected);

        if (response?.IsSuccessStatusCode != true && verification.State == "not-applied")
        {
            await _delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            attempts++;
            response = await _write.ExecuteAsync(
                new LeagueWriteCommand(LeagueWriteCapability.HonorPlayerV2, null, json),
                cancellationToken).ConfigureAwait(false);
            verification = await VerifyHonorAsync(ballot, selected, cancellationToken).ConfigureAwait(false);
            if (verification.State == "confirmed")
                return CreateStatus(ballot.GameId, "success", "v2", verification.Detail + ";safe-retry", response?.StatusCode ?? 0, attempts, selected);
        }

        return CreateStatus(
            ballot.GameId,
            response?.IsSuccessStatusCode == true ? "unknown" : "failed",
            "v2",
            (response?.IsSuccessStatusCode == true ? "submitted-unverified:" : "submit-failed:") + verification.Detail,
            response?.StatusCode ?? 0,
            attempts,
            selected);
    }

    private async Task<LeagueHonorAttemptStatus> TryHonorLegacyAsync(
        HonorBallot ballot,
        HonorCandidate selected,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new { puuid = selected.Puuid, honorType = "HEART" });
        var honor = await _write.ExecuteAsync(
            new LeagueWriteCommand(LeagueWriteCapability.HonorPlayerLegacy, null, json),
            cancellationToken).ConfigureAwait(false);
        if (honor?.IsSuccessStatusCode != true)
            return CreateStatus(ballot.GameId, "failed", "legacy", "honor-submit-failed", honor?.StatusCode ?? 0, 1, selected);

        var submit = await _write.ExecuteAsync(
            new LeagueWriteCommand(LeagueWriteCapability.SubmitHonorBallotLegacy, null, null),
            cancellationToken).ConfigureAwait(false);
        if (submit?.IsSuccessStatusCode != true)
            return CreateStatus(ballot.GameId, "unknown", "legacy", "honor-sent-ballot-submit-failed", submit?.StatusCode ?? 0, 1, selected);

        var verification = await VerifyHonorAsync(ballot, selected, cancellationToken).ConfigureAwait(false);
        return CreateStatus(
            ballot.GameId,
            verification.State == "confirmed" ? "success" : "unknown",
            "legacy",
            verification.State == "confirmed" ? verification.Detail : "submitted-unverified:" + verification.Detail,
            submit.StatusCode,
            1,
            selected);
    }

    private async Task<HonorVerification> VerifyHonorAsync(
        HonorBallot before,
        HonorCandidate selected,
        CancellationToken cancellationToken)
    {
        var sameGameSeen = false;
        var targetStillEligible = false;
        var voteUnchanged = false;

        foreach (var delay in VerificationDelays)
        {
            await _delay(delay, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var choices = ParseTeamChoices(
                await _read.TryGetBytesAsync(TeamChoicesPath, cancellationToken).ConfigureAwait(false));
            if (choices.Any(value => string.Equals(value, selected.Puuid, StringComparison.Ordinal) ||
                                     (selected.SummonerId > 0 && string.Equals(
                                         value,
                                         selected.SummonerId.ToString(CultureInfo.InvariantCulture),
                                         StringComparison.Ordinal))))
                return new HonorVerification("confirmed", "team-choices-confirmed");

            var after = ParseBallot(await _read.TryGetBytesAsync(BallotPath, cancellationToken).ConfigureAwait(false));
            if (after is null || after.GameId != before.GameId) continue;
            sameGameSeen = true;
            if (after.HonoredPuuids.Contains(selected.Puuid, StringComparer.Ordinal))
                return new HonorVerification("confirmed", "ballot-honored-player-confirmed");
            targetStillEligible = after.Allies.Any(item =>
                item.SummonerId > 0 && item.SummonerId == selected.SummonerId ||
                string.Equals(item.Puuid, selected.Puuid, StringComparison.Ordinal));
            voteUnchanged = before.HasVoteCount && after.HasVoteCount && after.Votes == before.Votes;
            if (before.HasVoteCount && after.HasVoteCount && after.Votes < before.Votes)
                return new HonorVerification("confirmed", "ballot-vote-decreased");
        }

        return sameGameSeen && targetStillEligible && voteUnchanged
            ? new HonorVerification("not-applied", "same-game-ballot-unchanged")
            : new HonorVerification("unknown", "no-authoritative-confirmation");
    }

    private async Task<string?> TryReadSelfPuuidAsync(CancellationToken cancellationToken)
    {
        var bytes = await _read.TryGetBytesAsync(CurrentSummonerPath, cancellationToken).ConfigureAwait(false);
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return ReadScalarString(document.RootElement, "puuid");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static HonorBallot? ParseBallot(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var gameId = ReadInt64(root, "gameId");
            var hasVotes = false;
            var votes = 0;
            if (root.TryGetProperty("numVotes", out _))
            {
                hasVotes = true;
                votes = ReadInt32(root, "numVotes");
            }
            else if (root.TryGetProperty("votePool", out var votePool) &&
                     votePool.ValueKind == JsonValueKind.Object &&
                     votePool.TryGetProperty("votes", out _))
            {
                hasVotes = true;
                votes = ReadInt32(votePool, "votes");
            }

            var allies = new List<HonorCandidate>();
            JsonElement rows = default;
            if (root.TryGetProperty("eligibleAllies", out var modern) && modern.ValueKind == JsonValueKind.Array && modern.GetArrayLength() > 0)
                rows = modern;
            else if (root.TryGetProperty("eligiblePlayers", out var legacy) && legacy.ValueKind == JsonValueKind.Array)
                rows = legacy;

            if (rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    allies.Add(new HonorCandidate(
                        ReadScalarString(row, "puuid") ?? string.Empty,
                        ReadInt64Any(row, "summonerId", "summonerID"),
                        ReadBoolean(row, "botPlayer")));
                }
            }

            var honored = new List<string>();
            if (root.TryGetProperty("honoredPlayers", out var honoredRows) && honoredRows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in honoredRows.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    var puuid = ReadScalarString(row, "puuid");
                    if (!string.IsNullOrWhiteSpace(puuid) && !honored.Contains(puuid, StringComparer.Ordinal))
                        honored.Add(puuid);
                }
            }

            if (!hasVotes && allies.Count > 0) votes = 1;
            return new HonorBallot(gameId, votes, hasVotes, allies, honored);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<string> ParseTeamChoices(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return [];
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            var output = new List<string>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var value = item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString(),
                    JsonValueKind.Number => item.GetRawText(),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(value)) output.Add(value);
            }
            return output;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static bool IsPostGamePhase(string? phase) =>
        string.Equals(phase, "WaitingForStats", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(phase, "PreEndOfGame", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(phase, "EndOfGame", StringComparison.OrdinalIgnoreCase);

    internal static TimeSpan ResolveReturnDelay(string? phase)
    {
        if (string.Equals(phase, "WaitingForStats", StringComparison.OrdinalIgnoreCase)) return TimeSpan.FromSeconds(10);
        if (string.Equals(phase, "PreEndOfGame", StringComparison.OrdinalIgnoreCase)) return TimeSpan.FromMilliseconds(3250);
        return TimeSpan.FromMilliseconds(1575);
    }

    private bool IsStillEnabledForHonor()
    {
        lock (_sync) return !_disposed && _insidePostGame && _autoHonor;
    }

    private bool IsStillEnabledForReturn()
    {
        lock (_sync) return !_disposed && _insidePostGame && _autoReturnLobby;
    }

    private void PublishStatus(LeagueHonorAttemptStatus status)
    {
        EventHandler? handler;
        lock (_sync)
        {
            if (_disposed) return;
            _lastHonorStatus = status;
            handler = StatusChanged;
        }
        try { handler?.Invoke(this, EventArgs.Empty); }
        catch { }
    }

    private LeagueHonorAttemptStatus CreateStatus(
        long gameId,
        string state,
        string route,
        string detail,
        int httpStatus,
        int attempts,
        HonorCandidate? selected) =>
        new(
            gameId,
            state,
            route,
            detail,
            httpStatus,
            attempts,
            selected?.SummonerId ?? 0,
            MaskPuuid(selected?.Puuid),
            _utcNow());

    private static string MaskPuuid(string? puuid)
    {
        var value = (puuid ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;
        return "..." + (value.Length <= 8 ? value : value[^8..]);
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
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }

    private static long ReadInt64(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }

    private static long ReadInt64Any(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadInt64(source, name);
            if (value != 0) return value;
        }
        return 0;
    }

    private void CancelCycleLocked()
    {
        var cancellation = _cycleCancellation;
        _cycleCancellation = null;
        if (cancellation is null) return;
        try { cancellation.Cancel(); }
        catch { }
        cancellation.Dispose();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CancelCycleLocked();
        }
        _gameflow.Observed -= OnGameflowObserved;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    internal sealed record HonorCandidate(string Puuid, long SummonerId, bool BotPlayer);
    internal sealed record HonorBallot(
        long GameId,
        int Votes,
        bool HasVoteCount,
        IReadOnlyList<HonorCandidate> Allies,
        IReadOnlyList<string> HonoredPuuids);
    private sealed record HonorVerification(string State, string Detail);
}
