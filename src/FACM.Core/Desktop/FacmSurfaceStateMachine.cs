using System.Diagnostics;

namespace FACM.Core.Desktop;

/// <summary>
/// Presentation modes owned by the single FACM desktop surface. Business services remain outside
/// this state machine; they only provide inputs such as gameflow snapshots and user intents.
/// </summary>
public enum FacmSurfaceMode
{
    Orb,
    ControlMatrix,
    FeatureSurface,
    LeagueSurface,
    ChampSelectStrip,
    HiddenInGame
}

public sealed record FacmSurfaceTransition(
    FacmSurfaceMode From,
    FacmSurfaceMode To,
    string Reason,
    long DurationMs,
    string CorrelationId,
    string? Phase,
    bool IsUserInitiated);

/// <summary>
/// Deterministic coordinator for shell presentation transitions. It deliberately has no WinUI or
/// League ownership so it can be exercised by FoundationSmoke and reused by the desktop host.
/// </summary>
public sealed class FacmSurfaceStateMachine
{
    private FacmSurfaceMode _mode;
    private bool _modalScopeActive;

    public FacmSurfaceStateMachine(FacmSurfaceMode initialMode = FacmSurfaceMode.Orb)
    {
        _mode = initialMode;
    }

    public FacmSurfaceMode Mode => _mode;

    public bool IsModalScopeActive => _modalScopeActive;

    public event EventHandler<FacmSurfaceTransition>? Transitioned;

    public IDisposable EnterModalScope()
    {
        _modalScopeActive = true;
        return new ModalScope(this);
    }

    public FacmSurfaceTransition? TransitionTo(
        FacmSurfaceMode target,
        string reason,
        bool isUserInitiated = false,
        string? phase = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A transition reason is required.", nameof(reason));

        var from = _mode;
        if (from == target) return null;

        var correlationId = Guid.NewGuid().ToString("N");
        var started = Stopwatch.GetTimestamp();
        _mode = target;
        var elapsedMs = (long)Math.Max(0d, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        var transition = new FacmSurfaceTransition(
            from,
            target,
            reason,
            elapsedMs,
            correlationId,
            phase,
            isUserInitiated);
        Transitioned?.Invoke(this, transition);
        return transition;
    }

    public FacmSurfaceTransition? ObserveGameflow(
        string? phase,
        bool inGame,
        bool champSelect,
        bool lobbyRestored,
        bool manualOpenOverride = false)
    {
        if (inGame && !manualOpenOverride)
            return TransitionTo(FacmSurfaceMode.HiddenInGame, "gameflow-in-game", phase: phase);
        if (champSelect)
            return TransitionTo(FacmSurfaceMode.ChampSelectStrip, "gameflow-champ-select", phase: phase);
        if (lobbyRestored)
            return TransitionTo(FacmSurfaceMode.Orb, "gameflow-lobby-restored", phase: phase);
        return null;
    }

    public void ResetModalScope() => _modalScopeActive = false;

    private void ExitModalScope()
    {
        _modalScopeActive = false;
    }

    private sealed class ModalScope(FacmSurfaceStateMachine owner) : IDisposable
    {
        private FacmSurfaceStateMachine? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ExitModalScope();
    }
}
