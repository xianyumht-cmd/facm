using FACM.Core.League;
using FACM.Core.State;

namespace FACM.Infrastructure.League;

/// <summary>
/// Reuses the single process-wide Gameflow heartbeat to keep a small Bench fact current. This is
/// intentionally an observer, not a second timer or phase loop: one Gameflow observation schedules
/// at most one bounded Bench session read, and the detailed Workbench remains a separate presenter.
/// </summary>
public sealed class LeagueBenchRuntimeObserver : ILeagueBenchRuntimeState, IDisposable
{
    private readonly object _sync = new();
    private readonly ILeagueGameflowObservationSource _gameflow;
    private readonly ILeagueBenchQuickPickService _bench;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private LeagueBenchRuntimeSnapshot _current = LeagueBenchRuntimeSnapshot.Unavailable;
    private long _nextGeneration;
    private bool _disposed;

    public LeagueBenchRuntimeObserver(
        ILeagueGameflowObservationSource gameflow,
        ILeagueBenchQuickPickService bench)
    {
        _gameflow = gameflow ?? throw new ArgumentNullException(nameof(gameflow));
        _bench = bench ?? throw new ArgumentNullException(nameof(bench));
        _gameflow.Observed += OnGameflowObserved;

        if (_gameflow.Current is { } current)
            Observe(current);
    }

    public LeagueBenchRuntimeSnapshot Current
    {
        get
        {
            lock (_sync) return _current;
        }
    }

    public event EventHandler<LeagueBenchRuntimeChangedEventArgs>? Changed;

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        RefreshForObservationAsync(cancellationToken);

    private void OnGameflowObserved(object? sender, LeagueGameflowChangedEventArgs args) => Observe(args.Current);

    private void Observe(LeagueGameflowSnapshot observation)
    {
        LeagueBenchRuntimeChangedEventArgs? change = null;
        var inChampSelect = observation.ProductState == LeagueProductState.ChampSelect;
        lock (_sync)
        {
            if (_disposed) return;

            if (inChampSelect)
            {
                if (!_current.IsChampSelect)
                {
                    _nextGeneration++;
                    var next = CreateSnapshot(
                        observation,
                        _nextGeneration,
                        sessionAvailable: false,
                        benchEnabled: false,
                        route: LeagueBenchSwapRoute.Legacy,
                        championIds: Array.Empty<int>(),
                        isLatched: false,
                        freshness: "awaiting-session-read");
                    change = CreateChange(_current, next, "champ-select-context-started");
                    _current = next;
                }
                else
                {
                    _current = _current with
                    {
                        ProductState = observation.ProductState,
                        Phase = observation.Phase,
                        UpdatedAtUtc = observation.TimestampUtc
                    };
                }
            }
            else
            {
                var hadBenchContext = _current.IsChampSelect || _current.IsLatched || _current.CandidateCount > 0;
                var next = CreateSnapshot(
                    observation,
                    _current.ContextGeneration,
                    sessionAvailable: false,
                    benchEnabled: false,
                    route: _current.SwapRoute,
                    championIds: Array.Empty<int>(),
                    isLatched: false,
                    freshness: "outside-champ-select");
                if (hadBenchContext)
                    change = CreateChange(_current, next, "champ-select-context-ended");
                _current = next;
            }
        }

        Publish(change);
        if (inChampSelect)
            _ = RefreshForObservationAsync();
    }

    private async Task RefreshForObservationAsync(CancellationToken callerCancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            _lifetime.Token);
        var cancellationToken = linked.Token;
        try
        {
            if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
            try
            {
                var observed = await _bench.RefreshAsync(cancellationToken).ConfigureAwait(false);
                LeagueBenchRuntimeChangedEventArgs? change = null;
                lock (_sync)
                {
                    if (_disposed || !_current.IsChampSelect) return;

                    var candidates = observed.ChampionIds
                        .Where(id => id > 0)
                        .Distinct()
                        .ToArray();
                    var latched = _current.IsLatched ||
                                  (observed.SessionAvailable && observed.BenchEnabled && candidates.Length > 0);
                    var next = _current with
                    {
                        SessionAvailable = observed.SessionAvailable,
                        BenchEnabled = observed.BenchEnabled,
                        LocalChampionId = observed.LocalChampionId,
                        SwapRoute = observed.SwapRoute,
                        ChampionIds = candidates,
                        IsLatched = latched,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        SourceFreshness = observed.SessionAvailable ? "fresh" : "session-unavailable"
                    };
                    if (!Equivalent(_current, next))
                        change = CreateChange(_current, next, latched ? "bench-context-latched" : "bench-state-updated");
                    _current = next;
                }

                Publish(change);
            }
            finally
            {
                _refreshGate.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || callerCancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch
        {
            LeagueBenchRuntimeChangedEventArgs? change = null;
            lock (_sync)
            {
                if (_disposed || !_current.IsChampSelect) return;
                var next = _current with
                {
                    SessionAvailable = false,
                    BenchEnabled = false,
                    LocalChampionId = 0,
                    ChampionIds = Array.Empty<int>(),
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    SourceFreshness = "read-failed"
                };
                if (!Equivalent(_current, next))
                    change = CreateChange(_current, next, "bench-read-failed");
                _current = next;
            }
            Publish(change);
        }
    }

    private static LeagueBenchRuntimeSnapshot CreateSnapshot(
        LeagueGameflowSnapshot observation,
        long generation,
        bool sessionAvailable,
        bool benchEnabled,
        LeagueBenchSwapRoute route,
        IReadOnlyList<int> championIds,
        bool isLatched,
        string freshness) =>
        new(
            observation.ProductState,
            observation.Phase,
            generation,
            sessionAvailable,
            benchEnabled,
            0,
            route,
            championIds,
            isLatched,
            observation.TimestampUtc,
            "LeagueBenchRuntimeObserver",
            freshness);

    private static LeagueBenchRuntimeChangedEventArgs CreateChange(
        LeagueBenchRuntimeSnapshot previous,
        LeagueBenchRuntimeSnapshot current,
        string reason) =>
        new(previous, current, reason);

    private static bool Equivalent(LeagueBenchRuntimeSnapshot left, LeagueBenchRuntimeSnapshot right) =>
        left.ProductState == right.ProductState &&
        string.Equals(left.Phase, right.Phase, StringComparison.OrdinalIgnoreCase) &&
        left.ContextGeneration == right.ContextGeneration &&
        left.SessionAvailable == right.SessionAvailable &&
        left.BenchEnabled == right.BenchEnabled &&
        left.LocalChampionId == right.LocalChampionId &&
        left.SwapRoute == right.SwapRoute &&
        left.IsLatched == right.IsLatched &&
        string.Equals(left.SourceFreshness, right.SourceFreshness, StringComparison.Ordinal) &&
        left.ChampionIds.SequenceEqual(right.ChampionIds);

    private void Publish(LeagueBenchRuntimeChangedEventArgs? change)
    {
        if (change is null) return;
        try { Changed?.Invoke(this, change); }
        catch { }
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
        _refreshGate.Dispose();
    }
}
