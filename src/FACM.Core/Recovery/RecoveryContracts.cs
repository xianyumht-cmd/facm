using FACM.Core.Runtime;

namespace FACM.Core.Recovery;

public enum RecoveryPhase
{
    Clean,
    Starting,
    Running,
    Failed,
    Recovering
}

public enum RecoveryLoadOrigin
{
    Missing,
    Existing,
    Malformed
}

public sealed record RecoveryStateSnapshot(
    int SchemaVersion,
    RecoveryPhase Phase,
    string CurrentAppVersion,
    string LastKnownGoodAppVersion,
    int ConsecutiveFailures,
    string Reason,
    DateTimeOffset UpdatedAtUtc)
{
    public const int CurrentSchemaVersion = 1;

    public static RecoveryStateSnapshot CreateInitial(DateTimeOffset nowUtc) => new(
        CurrentSchemaVersion,
        RecoveryPhase.Clean,
        string.Empty,
        string.Empty,
        0,
        "initial",
        nowUtc.ToUniversalTime());
}

public sealed record RecoveryStateLoadResult(
    RecoveryStateSnapshot State,
    RecoveryLoadOrigin Origin);

public interface IRecoveryStateStore
{
    Task<RecoveryStateLoadResult> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(RecoveryStateSnapshot state, CancellationToken cancellationToken = default);
}

public static class RecoveryStateValidator
{
    public static void ThrowIfInvalid(RecoveryStateSnapshot? state)
    {
        if (state is null) throw new InvalidDataException("Recovery state is null.");
        if (state.SchemaVersion != RecoveryStateSnapshot.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported recovery schema version.");
        if (state.ConsecutiveFailures is < 0 or > 1000)
            throw new InvalidDataException("Recovery failure count is invalid.");
        ValidateSingleLine(state.CurrentAppVersion, 64, "currentAppVersion");
        ValidateSingleLine(state.LastKnownGoodAppVersion, 64, "lastKnownGoodAppVersion");
        ValidateSingleLine(state.Reason, 256, "reason");
        if (state.UpdatedAtUtc == default)
            throw new InvalidDataException("Recovery timestamp is missing.");
    }

    private static void ValidateSingleLine(string? value, int maxLength, string name)
    {
        if (value is null) throw new InvalidDataException($"Recovery {name} is null.");
        if (value.Length > maxLength) throw new InvalidDataException($"Recovery {name} is too long.");
        if (value.Contains('\r') || value.Contains('\n'))
            throw new InvalidDataException($"Recovery {name} must be single-line.");
    }
}

public static class RecoveryStateMachine
{
    public static RecoveryStateSnapshot BeginStart(
        RecoveryStateSnapshot previous,
        string appVersion,
        DateTimeOffset nowUtc)
    {
        RecoveryStateValidator.ThrowIfInvalid(previous);
        ValidateVersion(appVersion);
        var interrupted = previous.Phase == RecoveryPhase.Starting;
        var failures = interrupted
            ? checked(previous.ConsecutiveFailures + 1)
            : previous.ConsecutiveFailures;
        return previous with
        {
            Phase = RecoveryPhase.Starting,
            CurrentAppVersion = appVersion,
            ConsecutiveFailures = failures,
            Reason = interrupted ? "previous-start-incomplete" : "start-begin",
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public static RecoveryStateSnapshot MarkRunning(
        RecoveryStateSnapshot current,
        DateTimeOffset nowUtc)
    {
        RecoveryStateValidator.ThrowIfInvalid(current);
        if (current.Phase is not (RecoveryPhase.Starting or RecoveryPhase.Recovering))
            throw new InvalidOperationException("Only starting/recovering state can become running.");
        return current with
        {
            Phase = RecoveryPhase.Running,
            LastKnownGoodAppVersion = current.CurrentAppVersion,
            ConsecutiveFailures = 0,
            Reason = "running",
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public static RecoveryStateSnapshot MarkFailed(
        RecoveryStateSnapshot current,
        string reason,
        DateTimeOffset nowUtc)
    {
        RecoveryStateValidator.ThrowIfInvalid(current);
        ValidateReason(reason);
        return current with
        {
            Phase = RecoveryPhase.Failed,
            ConsecutiveFailures = checked(current.ConsecutiveFailures + 1),
            Reason = NormalizeReason(reason),
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public static RecoveryStateSnapshot BeginRecovery(
        RecoveryStateSnapshot current,
        string reason,
        DateTimeOffset nowUtc)
    {
        RecoveryStateValidator.ThrowIfInvalid(current);
        if (current.Phase is not (RecoveryPhase.Failed or RecoveryPhase.Starting))
            throw new InvalidOperationException("Recovery can only begin from failed/starting state.");
        ValidateReason(reason);
        return current with
        {
            Phase = RecoveryPhase.Recovering,
            Reason = NormalizeReason(reason),
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public static RecoveryStateSnapshot ResetClean(
        RecoveryStateSnapshot current,
        DateTimeOffset nowUtc)
    {
        RecoveryStateValidator.ThrowIfInvalid(current);
        return current with
        {
            Phase = RecoveryPhase.Clean,
            Reason = "recovery-clean",
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    private static void ValidateVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("App version must be a non-empty single line up to 64 characters.", nameof(value));
    }

    private static void ValidateReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Recovery reason is required.", nameof(value));
    }

    private static string NormalizeReason(string value)
    {
        var normalized = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }
}

public sealed class RecoveryCoordinator(
    IRecoveryStateStore store,
    IClock clock)
{
    private RecoveryStateSnapshot? _current;

    public RecoveryStateSnapshot? Current => _current;

    public async Task<RecoveryStateSnapshot> BeginStartAsync(
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        var loaded = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var state = RecoveryStateMachine.BeginStart(loaded.State, appVersion, clock.UtcNow);
        await store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        _current = state;
        return state;
    }

    public async Task<RecoveryStateSnapshot> MarkRunningAsync(CancellationToken cancellationToken = default)
    {
        var current = _current ?? throw new InvalidOperationException("Recovery start has not begun.");
        var state = RecoveryStateMachine.MarkRunning(current, clock.UtcNow);
        await store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        _current = state;
        return state;
    }

    public async Task<RecoveryStateSnapshot> MarkFailedAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        var current = _current ?? throw new InvalidOperationException("Recovery start has not begun.");
        var state = RecoveryStateMachine.MarkFailed(current, reason, clock.UtcNow);
        await store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        _current = state;
        return state;
    }
}

public enum UpdateReplacementOutcome
{
    NotAttempted,
    ValidatedReady,
    Replaced,
    ReplacementFailed,
    RolledBack
}

public sealed record UpdateRecoveryEvidence(
    string PreviousVersion,
    string CandidateVersion,
    bool ValidatedReceipt,
    bool OldVersionPreserved,
    bool RollbackAvailable,
    UpdateReplacementOutcome Outcome,
    string Reason);

public sealed record UpdateRecoveryDecision(
    bool PermitReplacement,
    bool KeepCurrentVersion,
    bool RequireRollback,
    string Reason);

public static class UpdateRecoveryPolicy
{
    public static UpdateRecoveryDecision Evaluate(UpdateRecoveryEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!evidence.ValidatedReceipt)
            return new(false, true, false, "validated-receipt-required");
        if (!evidence.OldVersionPreserved && evidence.Outcome is not UpdateReplacementOutcome.Replaced)
            return new(false, true, true, "old-version-not-preserved");

        return evidence.Outcome switch
        {
            UpdateReplacementOutcome.ValidatedReady => new(true, true, false, "validated-ready"),
            UpdateReplacementOutcome.Replaced => new(false, false, false, "replacement-complete"),
            UpdateReplacementOutcome.ReplacementFailed => new(false, true, evidence.RollbackAvailable, "replacement-failed-keep-old"),
            UpdateReplacementOutcome.RolledBack => new(false, true, false, "rollback-complete"),
            _ => new(false, true, false, "replacement-not-attempted")
        };
    }
}
