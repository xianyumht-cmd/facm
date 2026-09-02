using System.Text;
using FACM.Core.League;

namespace FACM.Infrastructure.League;

/// <summary>
/// FACM 3.5-style recommended setup automation rebuilt on top of the one process-wide gameflow
/// heartbeat. It owns no polling loop. A Champ Select context must remain stable across heartbeats
/// before one loadout/item-set attempt is allowed, and the same fingerprint is never retried until
/// the context changes or Champ Select is left.
/// </summary>
public sealed class LeagueRecommendedAutoApplyService : ILeagueRecommendedAutoApplyService, IDisposable
{
    internal static readonly TimeSpan StabilityWindow = TimeSpan.FromMilliseconds(1500);

    private readonly object _sync = new();
    private readonly ILeagueBuildAdvisorService _advisor;
    private readonly ILeagueBuildLoadoutService _loadout;
    private readonly ILeagueItemSetService _itemSets;
    private readonly ILeagueGameflowObservationSource _gameflow;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _evaluationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    private bool _enabled;
    private bool _disposed;
    private string _lastPhase = string.Empty;
    private string? _pendingFingerprint;
    private DateTimeOffset _pendingSinceUtc = DateTimeOffset.MinValue;
    private string? _attemptedFingerprint;
    private LeagueRecommendedAutoApplyStatus _lastStatus = LeagueRecommendedAutoApplyStatus.Disabled();

    public LeagueRecommendedAutoApplyService(
        ILeagueBuildAdvisorService advisor,
        ILeagueBuildLoadoutService loadout,
        ILeagueItemSetService itemSets,
        ILeagueGameflowObservationSource gameflow)
        : this(advisor, loadout, itemSets, gameflow, () => DateTimeOffset.UtcNow)
    {
    }

    internal LeagueRecommendedAutoApplyService(
        ILeagueBuildAdvisorService advisor,
        ILeagueBuildLoadoutService loadout,
        ILeagueItemSetService itemSets,
        ILeagueGameflowObservationSource gameflow,
        Func<DateTimeOffset> utcNow)
    {
        _advisor = advisor ?? throw new ArgumentNullException(nameof(advisor));
        _loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
        _itemSets = itemSets ?? throw new ArgumentNullException(nameof(itemSets));
        _gameflow = gameflow ?? throw new ArgumentNullException(nameof(gameflow));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _gameflow.Observed += OnGameflowObserved;
    }

    public bool Enabled
    {
        get { lock (_sync) return _enabled; }
    }

    public LeagueRecommendedAutoApplyStatus LastStatus
    {
        get { lock (_sync) return _lastStatus; }
    }

    public event EventHandler<LeagueRecommendedAutoApplyStatusChangedEventArgs>? StatusChanged;

    public void Configure(bool enabled)
    {
        LeagueGameflowSnapshot? current;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_enabled == enabled)
            {
                current = _gameflow.Current;
            }
            else
            {
                _enabled = enabled;
                ResetContextLocked(clearAttempt: true);
                current = _gameflow.Current;
            }
        }

        Publish(enabled ? "waiting" : "disabled", enabled ? "waiting-champ-select" : "disabled", string.Empty);
        if (enabled && current is not null && PrepareObservation(current))
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

            if (!string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(_lastPhase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
                    ResetContextLocked(clearAttempt: true);
                _lastPhase = phase;
                return false;
            }

            _lastPhase = phase;
            return _enabled;
        }
    }

    private async Task EvaluateObservedSafelyAsync(LeagueGameflowSnapshot observation)
    {
        try
        {
            await EvaluateObservationAsync(observation, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            Publish("failed", "unexpected-error", string.Empty);
        }
    }

    internal async Task EvaluateForSmokeTestAsync(
        LeagueGameflowSnapshot observation,
        CancellationToken cancellationToken = default)
    {
        if (!PrepareObservation(observation)) return;
        await EvaluateObservationAsync(observation, cancellationToken, waitForGate: true).ConfigureAwait(false);
    }

    private async Task EvaluateObservationAsync(
        LeagueGameflowSnapshot observation,
        CancellationToken cancellationToken,
        bool waitForGate = false)
    {
        if (waitForGate)
            await _evaluationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        else if (!await _evaluationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            if (!IsStillEnabledChampSelect(observation)) return;

            var advisor = await _advisor.RefreshAsync(false, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsStillEnabledChampSelect(observation)) return;

            if (advisor.State != LeagueBuildAdvisorState.Ready || advisor.Recommendation is null)
            {
                lock (_sync) ResetPendingLocked();
                Publish("waiting", advisor.Detail, string.Empty);
                return;
            }

            var fingerprint = BuildFingerprint(advisor);
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                lock (_sync) ResetPendingLocked();
                Publish("waiting", "recommendation-not-actionable", string.Empty);
                return;
            }

            var now = _utcNow();
            lock (_sync)
            {
                if (string.Equals(_attemptedFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    PublishLocked("already-attempted", "stable-context-already-attempted", fingerprint);
                    return;
                }

                if (!string.Equals(_pendingFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    _pendingFingerprint = fingerprint;
                    _pendingSinceUtc = now;
                    PublishLocked("stabilizing", "waiting-stable-context", fingerprint);
                    return;
                }

                if (now - _pendingSinceUtc < StabilityWindow)
                {
                    PublishLocked("stabilizing", "waiting-stable-context", fingerprint);
                    return;
                }

                // Mark before writes so overlapping heartbeats can never enqueue a second transaction.
                _attemptedFingerprint = fingerprint;
                ResetPendingLocked();
            }

            Publish("applying", "stable-context", fingerprint);

            var loadoutPlan = await _loadout.PrepareAsync(advisor, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsStillEnabledChampSelect(observation))
            {
                Publish("blocked", "champ-select-ended", fingerprint);
                return;
            }

            var itemSetPlan = await _itemSets.PrepareAsync(advisor, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsStillEnabledChampSelect(observation))
            {
                Publish("blocked", "champ-select-ended", fingerprint);
                return;
            }

            LeagueBuildLoadoutApplyResult? loadoutResult = null;
            LeagueItemSetApplyResult? itemSetResult = null;
            if (loadoutPlan is not null && (loadoutPlan.HasRunes || loadoutPlan.HasSpells))
                loadoutResult = await _loadout.ApplyAsync(loadoutPlan, cancellationToken).ConfigureAwait(false);

            // A blocked loadout means the live context drifted between preparation and write. In that
            // case do not continue to disk; the item-set service would block too, but avoiding the call
            // keeps the transaction fail-closed and easier to reason about.
            if (loadoutResult is not null && string.Equals(loadoutResult.Status, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                Publish("blocked", loadoutResult.BlockReason, fingerprint);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (itemSetPlan is not null && itemSetPlan.HasItems)
                itemSetResult = await _itemSets.ApplyAsync(itemSetPlan, cancellationToken).ConfigureAwait(false);

            var loadoutExpected = loadoutPlan is not null && (loadoutPlan.HasRunes || loadoutPlan.HasSpells);
            var itemSetExpected = itemSetPlan is not null && itemSetPlan.HasItems;
            var loadoutSucceeded = !loadoutExpected ||
                (loadoutResult is not null && string.Equals(loadoutResult.Status, "success", StringComparison.OrdinalIgnoreCase));
            var itemSetSucceeded = !itemSetExpected || itemSetResult?.Succeeded == true;
            var anySucceeded = loadoutResult?.AnyApplied == true || itemSetResult?.Succeeded == true;

            if (!loadoutExpected && !itemSetExpected)
                Publish("skipped", "recommendation-has-no-applicable-writes", fingerprint);
            else if (loadoutSucceeded && itemSetSucceeded)
                Publish("success", "recommended-setup-applied", fingerprint);
            else if (anySucceeded)
                Publish("partial", BuildDetail(loadoutResult, itemSetResult), fingerprint);
            else
                Publish("failed", BuildDetail(loadoutResult, itemSetResult), fingerprint);
        }
        finally
        {
            _evaluationGate.Release();
        }
    }

    private bool IsStillEnabledChampSelect(LeagueGameflowSnapshot observation)
    {
        lock (_sync)
        {
            if (_disposed || !_enabled || !string.Equals(_lastPhase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var current = _gameflow.Current;
        return current is not null &&
               current.ConnectionState == LeagueConnectionState.Connected &&
               string.Equals(current.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) &&
               observation.ConnectionState == LeagueConnectionState.Connected &&
               string.Equals(observation.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildFingerprint(LeagueBuildAdvisorSnapshot snapshot)
    {
        if (snapshot is null || snapshot.State != LeagueBuildAdvisorState.Ready ||
            snapshot.Recommendation is null || snapshot.ChampionId <= 0 || snapshot.QueueId < 0 ||
            string.IsNullOrWhiteSpace(snapshot.Mode) || string.IsNullOrWhiteSpace(snapshot.Version))
            return string.Empty;

        var builder = new StringBuilder();
        builder.Append(snapshot.ChampionId).Append('|')
            .Append(snapshot.QueueId).Append('|')
            .Append((snapshot.Mode ?? string.Empty).Trim().ToLowerInvariant()).Append('|')
            .Append((snapshot.Position ?? string.Empty).Trim().ToLowerInvariant()).Append('|')
            .Append((snapshot.Version ?? string.Empty).Trim());
        foreach (var row in snapshot.Recommendation.Rows
                     .Where(row => row is not null)
                     .OrderBy(row => row.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('|')
                .Append((row.Category ?? string.Empty).Trim().ToLowerInvariant())
                .Append('=')
                .Append((row.Recommendation ?? string.Empty).Trim());
        }
        return builder.ToString();
    }

    private static string BuildDetail(
        LeagueBuildLoadoutApplyResult? loadout,
        LeagueItemSetApplyResult? itemSet)
    {
        var loadoutPart = loadout is null
            ? "loadout=not-available"
            : "loadout=" + loadout.Status + "/" + loadout.RuneStatus + "/" + loadout.SpellStatus;
        var itemPart = itemSet is null
            ? "itemset=not-available"
            : "itemset=" + itemSet.Detail;
        return loadoutPart + ";" + itemPart;
    }

    private void Publish(string state, string detail, string fingerprint)
    {
        LeagueRecommendedAutoApplyStatus status;
        EventHandler<LeagueRecommendedAutoApplyStatusChangedEventArgs>? handler;
        lock (_sync)
        {
            if (_disposed) return;
            status = new LeagueRecommendedAutoApplyStatus(
                state ?? string.Empty,
                detail ?? string.Empty,
                fingerprint ?? string.Empty,
                _utcNow());
            _lastStatus = status;
            handler = StatusChanged;
        }
        handler?.Invoke(this, new LeagueRecommendedAutoApplyStatusChangedEventArgs(status));
    }

    private void PublishLocked(string state, string detail, string fingerprint)
    {
        var status = new LeagueRecommendedAutoApplyStatus(
            state ?? string.Empty,
            detail ?? string.Empty,
            fingerprint ?? string.Empty,
            _utcNow());
        _lastStatus = status;
        var handler = StatusChanged;
        if (handler is not null)
        {
            try
            {
                handler(this, new LeagueRecommendedAutoApplyStatusChangedEventArgs(status));
            }
            catch
            {
                // Status observers are a UI boundary. A faulty observer must not become an
                // unobserved Task.Run exception or interrupt the shared League evaluation gate.
            }
        }
    }

    private void ResetContextLocked(bool clearAttempt)
    {
        ResetPendingLocked();
        if (clearAttempt) _attemptedFingerprint = null;
    }

    private void ResetPendingLocked()
    {
        _pendingFingerprint = null;
        _pendingSinceUtc = DateTimeOffset.MinValue;
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
        if (_advisor is IDisposable advisorDisposable) advisorDisposable.Dispose();
        if (_loadout is IDisposable loadoutDisposable) loadoutDisposable.Dispose();
        if (_itemSets is IDisposable itemSetDisposable) itemSetDisposable.Dispose();
        // As with other process-scoped League automations, do not dispose the semaphore while an
        // in-flight heartbeat may still be unwinding and releasing it during shutdown.
    }
}
