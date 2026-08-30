using FACM.Core.Desktop;

internal static class FacmSurfacePresentationSmoke
{
    public static void Run()
    {
        var orbBounds = new DesktopRect(80, 40, 36, 36);
        var railBounds = new DesktopRect(80, 40, 220, 36);
        var matrixBounds = new DesktopRect(80, 40, 360, 176);
        var featureBounds = new DesktopRect(80, 40, 420, 320);
        var orb = FacmSurfacePresentation.Create(FacmSurfaceMode.Orb, orbBounds, activate: false);
        var rail = FacmSurfacePresentation.Create(FacmSurfaceMode.Orb, railBounds, railVisible: true, activate: false);
        var matrix = FacmSurfacePresentation.Create(FacmSurfaceMode.ControlMatrix, matrixBounds);
        var feature = FacmSurfacePresentation.Create(FacmSurfaceMode.FeatureSurface, featureBounds);
        var hidden = FacmSurfacePresentation.Create(FacmSurfaceMode.HiddenInGame, null, activate: false);

        Assert(orb.OrbVisible && !orb.RailVisible && orb.TargetBounds?.Width == 36,
            "idle Orb owns only a 36 DIP presentation");
        Assert(rail.OrbVisible && rail.RailVisible && rail.TargetBounds?.Width == 220,
            "transient rail expands the Orb presentation without replacing it");
        Assert(matrix.ChromeVisible && matrix.ContentVisible && matrix.WindowVisible,
            "matrix presentation has chrome and visible content");
        Assert(feature.ChromeVisible && feature.ContentVisible && feature.WindowVisible,
            "feature presentation has chrome and visible content");
        Assert(!hidden.WindowVisible && !hidden.ContentVisible && hidden.TargetBounds is null,
            "HiddenInGame has no visible or interactive AppWindow contract");

        var invalidBlank = new FacmSurfacePresentation(
            FacmSurfaceMode.ControlMatrix,
            OrbVisible: false,
            RailVisible: false,
            ChromeVisible: true,
            ContentVisible: false,
            WindowVisible: true,
            TargetBounds: matrixBounds,
            Topmost: true,
            Activate: true);
        AssertThrows(invalidBlank.EnsureValid, "visible blank presentations are rejected");

        var machine = new FacmSurfaceStateMachine();
        var transitionCount = 0;
        machine.Transitioned += (_, _) => transitionCount++;
        for (var cycle = 0; cycle < 80; cycle++)
        {
            Assert(machine.TransitionTo(FacmSurfaceMode.Orb, "duplicate-orb") is null,
                "duplicate Orb requests are idempotent");
            Assert(machine.TransitionTo(FacmSurfaceMode.ControlMatrix, "cycle-matrix") is not null,
                "cycle enters matrix");
            Assert(machine.TransitionTo(FacmSurfaceMode.FeatureSurface, "cycle-feature") is not null,
                "cycle enters feature");
            Assert(machine.TransitionTo(FacmSurfaceMode.Orb, "cycle-collapse") is not null,
                "cycle returns to Orb");
            orb.EnsureValid();
            matrix.EnsureValid();
            feature.EnsureValid();
        }

        Assert(machine.Mode == FacmSurfaceMode.Orb, "80 presentation cycles end at Orb");
        Assert(transitionCount == 240, "80 presentation cycles have deterministic transition ownership");
    }

    private static void AssertThrows(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("FacmSurfacePresentationSmoke failed: " + message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FacmSurfacePresentationSmoke failed: " + message);
    }
}
