using System.Diagnostics;
using System.Text.Json;
using FACM.Core.League;

namespace FACM.Infrastructure.League;

/// <summary>
/// Manual-only ARAM / ARAM Mayhem bench quick-pick transaction migrated from FACM 3.5.15.
/// It reuses the process-wide authenticated League gateway. A user click produces at most one POST;
/// success is proven only by bounded read-back and the POST is never retried automatically.
/// </summary>
public sealed class LeagueBenchQuickPickService : ILeagueBenchQuickPickService, IDisposable
{
    public const string ChampSelectSessionPath = "/lol-champ-select/v1/session";
    public const string TeamBuilderChampSelectSessionPath = "/lol-lobby-team-builder/champ-select/v1/session";
    public const string ChampionIconPathPrefix = "/lol-game-data/assets/v1/champion-icons/";

    private const int MaxChampionIconBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan[] VerificationDelays =
    [
        TimeSpan.FromMilliseconds(35),
        TimeSpan.FromMilliseconds(70),
        TimeSpan.FromMilliseconds(140)
    ];

    private readonly ILeagueReadGateway _reader;
    private readonly ILeagueWriteGateway _writer;
    private readonly SemaphoreSlim _swapGate = new(1, 1);
    private readonly object _iconSync = new();
    private readonly Dictionary<int, byte[]> _iconCache = [];
    private int _lastRoute = (int)LeagueBenchSwapRoute.Legacy;
    private bool _disposed;

    public LeagueBenchQuickPickService(ILeagueReadGateway reader, ILeagueWriteGateway writer)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async Task<LeagueBenchQuickPickState> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var genericBytes = await _reader.TryGetBytesAsync(ChampSelectSessionPath, cancellationToken).ConfigureAwait(false);
        var state = ParseBenchState(genericBytes, forceRoute: null);
        var shouldTryTeamBuilder = !state.SessionAvailable ||
                                   (state.BenchEnabled &&
                                    state.SwapRoute == LeagueBenchSwapRoute.TeamBuilder &&
                                    state.ChampionIds.Count == 0);
        if (shouldTryTeamBuilder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var teamBuilderBytes = await _reader.TryGetBytesAsync(
                TeamBuilderChampSelectSessionPath,
                cancellationToken).ConfigureAwait(false);
            var teamBuilder = ParseBenchState(teamBuilderBytes, LeagueBenchSwapRoute.TeamBuilder);
            if (teamBuilder.SessionAvailable)
            {
                RememberRoute(LeagueBenchSwapRoute.TeamBuilder);
                return teamBuilder;
            }
        }

        if (state.SessionAvailable) RememberRoute(state.SwapRoute);
        return state;
    }

    public async Task<byte[]?> LoadChampionIconAsync(
        int championId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (championId <= 0) return null;

        lock (_iconSync)
        {
            if (_iconCache.TryGetValue(championId, out var cached)) return cached;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await _reader.TryGetBytesAsync(
            ChampionIconPathPrefix + championId + ".png",
            cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0 || bytes.Length > MaxChampionIconBytes) return null;

        lock (_iconSync)
        {
            if (!_iconCache.TryGetValue(championId, out var cached))
            {
                cached = bytes;
                _iconCache[championId] = cached;
            }
            return cached;
        }
    }

    public async Task<LeagueBenchSwapResult> TrySwapAsync(
        int championId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (championId <= 0) throw new ArgumentOutOfRangeException(nameof(championId));

        await _swapGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var route = (LeagueBenchSwapRoute)Volatile.Read(ref _lastRoute);
            var capability = route == LeagueBenchSwapRoute.TeamBuilder
                ? LeagueWriteCapability.SwapBenchChampionTeamBuilder
                : LeagueWriteCapability.SwapBenchChampionLegacy;
            var response = await _writer.ExecuteAsync(
                new LeagueWriteCommand(capability, championId, null),
                cancellationToken).ConfigureAwait(false);

            if (response is null)
                return Result(LeagueBenchSwapStatus.SessionUnavailable, championId, 0, stopwatch);
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode is 404 or 409
                    ? LeagueBenchSwapStatus.TargetUnavailable
                    : LeagueBenchSwapStatus.WriteRejected;
                return Result(status, championId, response.StatusCode, stopwatch);
            }

            foreach (var delay in VerificationDelays)
            {
                if (await VerifyChampionAsync(championId, delay, cancellationToken).ConfigureAwait(false))
                    return Result(LeagueBenchSwapStatus.Success, championId, response.StatusCode, stopwatch);
            }

            return Result(LeagueBenchSwapStatus.VerificationFailed, championId, response.StatusCode, stopwatch);
        }
        finally
        {
            stopwatch.Stop();
            _swapGate.Release();
        }
    }

    public static LeagueBenchQuickPickState ParseBenchState(
        byte[]? bytes,
        LeagueBenchSwapRoute? forceRoute = null)
    {
        if (bytes is null || bytes.Length == 0) return LeagueBenchQuickPickState.Unavailable;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return LeagueBenchQuickPickState.Unavailable;

            var root = document.RootElement;
            var route = forceRoute ?? ResolveRoute(root);
            var localCell = ReadInt(root, "localPlayerCellId");
            var localChampion = 0;
            if (TryGetArray(root, "myTeam", out var myTeam))
            {
                foreach (var member in myTeam.EnumerateArray())
                {
                    if (member.ValueKind != JsonValueKind.Object || ReadInt(member, "cellId") != localCell) continue;
                    localChampion = ReadInt(member, "championId");
                    break;
                }
            }

            var championIds = new List<int>();
            var seen = new HashSet<int>();
            if (TryGetArray(root, "benchChampions", out var benchChampions))
            {
                foreach (var item in benchChampions.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var id = ReadInt(item, "championId");
                    if (id > 0 && seen.Add(id)) championIds.Add(id);
                }
            }
            if (TryGetArray(root, "benchChampionIds", out var benchChampionIds))
            {
                foreach (var item in benchChampionIds.EnumerateArray())
                {
                    var id = item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var parsed) ? parsed : 0;
                    if (id > 0 && seen.Add(id)) championIds.Add(id);
                }
            }

            return new LeagueBenchQuickPickState(
                true,
                ReadBool(root, "benchEnabled"),
                localCell,
                localChampion,
                route,
                championIds);
        }
        catch (JsonException)
        {
            return LeagueBenchQuickPickState.Unavailable;
        }
    }

    private async Task<bool> VerifyChampionAsync(
        int championId,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        var state = await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return state.SessionAvailable && state.LocalChampionId == championId;
    }

    private static LeagueBenchSwapResult Result(
        LeagueBenchSwapStatus status,
        int championId,
        int statusCode,
        Stopwatch stopwatch) =>
        new(status, championId, statusCode, Math.Max(0L, stopwatch.ElapsedMilliseconds));

    private static LeagueBenchSwapRoute ResolveRoute(JsonElement root)
    {
        if (root.TryGetProperty("isLegacyChampSelect", out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean() ? LeagueBenchSwapRoute.Legacy : LeagueBenchSwapRoute.TeamBuilder;
        return LeagueBenchSwapRoute.Legacy;
    }

    private static bool TryGetArray(JsonElement root, string name, out JsonElement value)
    {
        value = default;
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(name, out value) &&
               value.ValueKind == JsonValueKind.Array;
    }

    private static int ReadInt(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
        return 0;
    }

    private static bool ReadBool(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed;
    }

    private void RememberRoute(LeagueBenchSwapRoute route) => Volatile.Write(ref _lastRoute, (int)route);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_iconSync) _iconCache.Clear();
        _swapGate.Dispose();
    }
}
