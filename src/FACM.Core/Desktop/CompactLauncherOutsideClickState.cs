namespace FACM.Core.Desktop;

public enum CompactLauncherOutsideClickObservation
{
    Ignored,
    Armed,
    CloseRequested
}

/// <summary>
/// Deterministic state machine for the legacy CompactMenu outside-left-click behavior.
/// </summary>
public sealed class CompactLauncherOutsideClickState : IDisposable
{
    private bool _opening = true;
    private bool _previousLeftButtonDown;
    private bool _closeRequested;
    private bool _disposed;

    public bool IsArmed => !_opening && !_disposed;
    public bool IsDisposed => _disposed;

    public void Reset()
    {
        if (_disposed) return;
        _opening = true;
        _previousLeftButtonDown = false;
        _closeRequested = false;
    }

    public CompactLauncherOutsideClickObservation Observe(
        bool leftButtonDown,
        bool pointerInside,
        bool suppressOutsideClose)
    {
        if (_disposed || _closeRequested) return CompactLauncherOutsideClickObservation.Ignored;

        if (!leftButtonDown)
        {
            var armed = _opening;
            _opening = false;
            _previousLeftButtonDown = false;
            return armed
                ? CompactLauncherOutsideClickObservation.Armed
                : CompactLauncherOutsideClickObservation.Ignored;
        }

        var downTransition = !_previousLeftButtonDown;
        _previousLeftButtonDown = true;
        if (_opening || !downTransition || pointerInside || suppressOutsideClose)
            return CompactLauncherOutsideClickObservation.Ignored;

        _closeRequested = true;
        return CompactLauncherOutsideClickObservation.CloseRequested;
    }

    public void Dispose()
    {
        _disposed = true;
        _previousLeftButtonDown = false;
    }
}
