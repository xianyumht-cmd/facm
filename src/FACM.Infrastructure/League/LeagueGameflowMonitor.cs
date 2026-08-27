using System.Text;
using System.Text.Json;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.State;

namespace FACM.Infrastructure.League;

/// <summary>
/// The single FACM 4.0 gameflow polling owner. It reuses the shared League read gateway/session
/// accessor and publishes one mapping to Product State + Performance; UI layers never poll LCU.
/// </summary>
public sealed class LeagueGameflowMonitor : ILeagueGameflowReader, IDisposable
{
    private const string PhaseResourceKey = "/lol-gameflow/v1/gameflow-phase";

    private readonly object _sync = new();
    private readonly ILeagueReadGateway _readGateway;
    private readonly ILeagueSessionAccessor _sessions;
    private readonly IProductStateWriter _productState;
    private readonly PerformanceBudgetProvider _performance;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _lifetime = new();
    private LeagueGameflowSnapshot? _current;
    private bool _started;
    private bool _disposed;

    public LeagueGameflowMonitor(
        ILeagueReadGateway readGateway,
        ILeagueSessionAccessor sessions,
        IProductStateWriter productState,
        PerformanceBudgetProvider performance,
        Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _readGateway = readGateway ?? throw new ArgumentNullException(nameof(readGateway));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _productState = productState ?? throw new ArgumentNullException(nameof(productState));
        _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
    }

    public LeagueGameflowSnapshot? Current
    {
        get
        {
            lock (_sync) return _current;
        }
    }

    public event EventHandler<LeagueGameflowChangedEventArgs>? Changed;

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
        }
        _ = Task.Run(RunAsync);
    }

    public async Task<LeagueGameflowSnapshot> RefreshOnceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = await _readGateway.TryGetBytesAsync(PhaseResourceKey, cancellationToken).ConfigureAwait(false);
        var connection = _sessions.State;
        var readSucceeded = bytes is { Length: > 0 };
        var phase = readSucceeded ? ParsePhase(bytes!) : string.Empty;
        var mapping = LeagueGameflowPhaseMapper.Map(phase, connection, readSucceeded);
        return Publish(mapping);
    }

    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            LeagueGameflowSnapshot snapshot;
            try
            {
                snapshot = await RefreshOnceAsync(_lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
                var mapping = LeagueGameflowPhaseMapper.Map(
                    null,
                    _sessions.State == LeagueConnectionState.Connected
                        ? LeagueConnectionState.Unavailable
                        : _sessions.State,
                    phaseReadSucceeded: false);
                snapshot = Publish(mapping);
            }

            try
            {
                var mapping = new LeagueGameflowMapping(
                    snapshot.ConnectionState,
                    snapshot.Phase,
                    snapshot.ProductState,
                    snapshot.Activity);
                await _delay(LeagueGameflowCadence.Resolve(mapping), _lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private LeagueGameflowSnapshot Publish(LeagueGameflowMapping mapping)
    {
        // The same mapping drives both global facts and performance. No page-specific phase cache or
        // second poll loop is allowed to reinterpret this result.
        _productState.SetLeague(mapping.ProductState, "gameflow:" + mapping.ProductState);
        _performance.UpdateLeagueActivity(mapping.Activity);

        var next = new LeagueGameflowSnapshot(
            _utcNow(),
            mapping.ConnectionState,
            mapping.Phase,
            mapping.ProductState,
            mapping.Activity);

        LeagueGameflowSnapshot? previous;
        EventHandler<LeagueGameflowChangedEventArgs>? handler = null;
        lock (_sync)
        {
            previous = _current;
            if (Equivalent(previous, next)) return previous!;
            _current = next;
            handler = Changed;
        }

        handler?.Invoke(this, new LeagueGameflowChangedEventArgs(previous, next));
        return next;
    }

    private static bool Equivalent(LeagueGameflowSnapshot? left, LeagueGameflowSnapshot right) =>
        left is not null &&
        left.ConnectionState == right.ConnectionState &&
        string.Equals(left.Phase, right.Phase, StringComparison.OrdinalIgnoreCase) &&
        left.ProductState == right.ProductState &&
        left.Activity == right.Activity;

    private static string ParsePhase(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).Trim();
        if (text.Length == 0) return string.Empty;
        try
        {
            return (JsonSerializer.Deserialize<string>(text) ?? string.Empty).Trim();
        }
        catch (JsonException)
        {
            return text.Trim('"').Trim();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
