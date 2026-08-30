namespace FACM.Core.Desktop;

/// <summary>
/// Immutable presentation contract for the one FACM desktop surface. The host translates this
/// contract into WinUI visibility and AppWindow mutations on its UI dispatcher.
/// </summary>
public sealed record FacmSurfacePresentation(
    FacmSurfaceMode Mode,
    bool OrbVisible,
    bool RailVisible,
    bool ChromeVisible,
    bool ContentVisible,
    bool WindowVisible,
    DesktopRect? TargetBounds,
    bool Topmost,
    bool Activate)
{
    public static FacmSurfacePresentation Create(
        FacmSurfaceMode mode,
        DesktopRect? targetBounds,
        bool railVisible = false,
        bool activate = true)
    {
        var hidden = mode == FacmSurfaceMode.HiddenInGame;
        var orb = mode == FacmSurfaceMode.Orb;
        var chrome = mode is FacmSurfaceMode.ControlMatrix or FacmSurfaceMode.FeatureSurface or FacmSurfaceMode.LeagueSurface;
        var presentation = new FacmSurfacePresentation(
            mode,
            OrbVisible: orb,
            RailVisible: orb && railVisible,
            ChromeVisible: !hidden && chrome,
            ContentVisible: !hidden,
            WindowVisible: !hidden,
            TargetBounds: hidden ? null : targetBounds,
            Topmost: true,
            Activate: !hidden && activate);
        presentation.EnsureValid();
        return presentation;
    }

    public void EnsureValid()
    {
        if (WindowVisible)
        {
            if (!ContentVisible)
                throw new InvalidOperationException("A visible FACM surface must have visible content.");
            if (TargetBounds is not { IsValid: true })
                throw new InvalidOperationException("A visible FACM surface requires valid target bounds.");
        }
        else if (ContentVisible || OrbVisible || RailVisible || ChromeVisible || TargetBounds is not null || Activate)
        {
            throw new InvalidOperationException("A hidden FACM surface cannot expose content, bounds, or activation.");
        }

        if (OrbVisible != (Mode == FacmSurfaceMode.Orb))
            throw new InvalidOperationException("Orb visibility must match Orb mode.");
        if (RailVisible && !OrbVisible)
            throw new InvalidOperationException("The transient rail can only be visible with the Orb.");
        if (Mode == FacmSurfaceMode.HiddenInGame && WindowVisible)
            throw new InvalidOperationException("HiddenInGame cannot show an AppWindow.");
    }
}

public sealed record FacmSurfacePresentationFailure(
    FacmSurfaceMode RequestedMode,
    FacmSurfaceMode PreviousMode,
    string Operation,
    string ExceptionType,
    int HResult,
    int ThreadId,
    DesktopRect Bounds,
    string CorrelationId,
    string? Phase,
    bool IsUserInitiated);
