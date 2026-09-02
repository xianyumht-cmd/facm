using System.Diagnostics;
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
public sealed class LeagueGameflowMonitor : ILeagueGameflowObservationSource, IDisposable
{
    private const string PhaseResourceKey = "/lol-gameflow/v1/gameflow-phase";

    private readonly object _sync = new();
    private readonly ILeagueReadGateway _readGateway;
    private readonly ILeagueSessionAccessor _sessions;
    private readonly IProductStateWriter _productState;
    private readonly PerformanceBudgetProvider _performance;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<LeagueGameflowDiagnostic>? _diagnosticReporter;
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
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action<LeagueGameflowDiagnostic>? diagnosticReporter = null)
    {
        _readGateway = readGateway ?? throw new ArgumentNullException(nameof(readGateway));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _productState = productState ?? throw new ArgumentNullException(nameof(productState));
        _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
        _diagnosticReporter = diagnosticReporter;
    }

    public LeagueGameflowSnapshot? Current
    {
        get
        {
            lock (_sync) return _current;
        }
    }

    public event EventHandler<LeagueGameflowChangedEventArgs>? Changed;
    public event EventHandler<LeagueGameflowChangedEventArgs>? Observed;

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
        var context = LeagueDiagnosticContext.Current;
        var pollId = Guid.NewGuid().ToString("N");
        var correlationId = context?.CorrelationId ?? LeagueDiagnosticContext.CreateCorrelationId();
        var traceStartedUtc = DateTimeOffset.UtcNow;
        var startTimestamp = Stopwatch.GetTimestamp();
        var phase = string.Empty;
        var connection = LeagueConnectionState.NotRunning;
        var productState = LeagueProductState.NotRunning;
        var changed = (bool?)null;
        DateTimeOffset? observationTimestampUtc = null;
        var outcome = "unhandled-exception";
        var reason = "unhandled-exception";
        using var diagnosticScope = LeagueDiagnosticContext.Begin(correlationId, "gameflow", "poll");
        ReportDiagnostic(
            pollId,
            correlationId,
            "started",
            "started",
            "started",
            phase,
            connection,
            productState,
            changed,
            traceStartedUtc,
            traceStartedUtc,
            0);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytes = await _readGateway.TryGetBytesAsync(PhaseResourceKey, cancellationToken).ConfigureAwait(false);
            connection = _sessions.State;
            var readSucceeded = bytes is { Length: > 0 };
            phase = readSucceeded ? ParsePhase(bytes!) : string.Empty;
            var mapping = LeagueGameflowPhaseMapper.Map(phase, connection, readSucceeded);
            var previous = Current;
            var snapshot = Publish(mapping);
            productState = snapshot.ProductState;
            changed = previous is null || !Equivalent(previous, snapshot);
            observationTimestampUtc = snapshot.TimestampUtc;
            outcome = "success";
            reason = readSucceeded ? "phase-read" : "phase-unavailable";
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "caller-cancelled";
            reason = "caller-cancelled";
            throw;
        }
        catch (Exception exception)
        {
            outcome = "failure";
            reason = exception.GetType().Name;
            throw;
        }
        finally
        {
            var finishedUtc = DateTimeOffset.UtcNow;
            ReportDiagnostic(
                pollId,
                correlationId,
                "completed",
                outcome,
                reason,
                phase,
                connection,
                productState,
                changed,
                traceStartedUtc,
                finishedUtc,
                Math.Max(0L, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds),
                observationTimestampUtc: observationTimestampUtc);
        }
    }

    private void ReportDiagnostic(
        string pollId,
        string correlationId,
        string eventName,
        string outcome,
        string reason,
        string phase,
        LeagueConnectionState connection,
        LeagueProductState productState,
        bool? changed,
        DateTimeOffset startedUtc,
        DateTimeOffset finishedUtc,
        long durationMs,
        Exception? exception = null,
        DateTimeOffset? observationTimestampUtc = null)
    {
        try
        {
            _diagnosticReporter?.Invoke(new LeagueGameflowDiagnostic(
                pollId,
                correlationId,
                eventName,
                outcome,
                reason,
                phase,
                connection,
                productState,
                changed,
                startedUtc,
                finishedUtc,
                durationMs,
                exception?.GetType().FullName ?? string.Empty,
                exception is null
                    ? string.Empty
                    : "0x" + exception.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                Environment.CurrentManagedThreadId,
                observationTimestampUtc));
        }
        catch
        {
            // Diagnostics must never change the gameflow loop behavior.
        }
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

        var observed = new LeagueGameflowSnapshot(
            _utcNow(),
            mapping.ConnectionState,
            mapping.Phase,
            mapping.ProductState,
            mapping.Activity);

        LeagueGameflowSnapshot? previous;
        LeagueGameflowSnapshot published;
        EventHandler<LeagueGameflowChangedEventArgs>? changedHandler;
        EventHandler<LeagueGameflowChangedEventArgs>? observedHandler;
        var changed = false;
        lock (_sync)
        {
            previous = _current;
            changed = !Equivalent(previous, observed);
            if (changed)
            {
                _current = observed;
                published = observed;
                changedHandler = Changed;
            }
            else
            {
                // Preserve the stable snapshot identity contract for state consumers. Heartbeat
                // consumers receive the fresh observation below without mutating Current.
                published = previous!;
                changedHandler = null;
            }
            observedHandler = Observed;
        }

        var eventArgs = new LeagueGameflowChangedEventArgs(previous, observed);
        if (changed && changedHandler is not null)
        {
            try
            {
                changedHandler(this, eventArgs);
            }
            catch (Exception exception)
            {
                ReportDiagnostic(
                    Guid.NewGuid().ToString("N"),
                    Guid.NewGuid().ToString("N"),
                    "changed-handler-failed",
                    "failure",
                    exception.GetType().Name,
                    observed.Phase,
                    observed.ConnectionState,
                    observed.ProductState,
                    changed,
                    observed.TimestampUtc,
                    DateTimeOffset.UtcNow,
                    0,
                    exception);
            }
        }

        if (observedHandler is not null)
        {
            try
            {
                observedHandler(this, eventArgs);
            }
            catch (Exception exception)
            {
                ReportDiagnostic(
                    Guid.NewGuid().ToString("N"),
                    Guid.NewGuid().ToString("N"),
                    "observed-handler-failed",
                    "failure",
                    exception.GetType().Name,
                    observed.Phase,
                    observed.ConnectionState,
                    observed.ProductState,
                    changed,
                    observed.TimestampUtc,
                    DateTimeOffset.UtcNow,
                    0,
                    exception);
            }
        }
        return published;
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
