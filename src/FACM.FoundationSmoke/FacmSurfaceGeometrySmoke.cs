using FACM.Core.Desktop;

internal static class FacmSurfaceGeometrySmoke
{
    public static void Run()
    {
        var area = new DesktopWorkArea("left", new DesktopRect(-1920, 0, 1920, 1080), false);
        var bottomRight = FacmSurfaceGeometryService.ExpandFromAnchor(new(
            new DesktopPoint(-40, 980),
            new DesktopSize(560, 320),
            area));
        Assert(bottomRight.Right <= area.Bounds.Right && bottomRight.Bottom <= area.Bounds.Bottom,
            "expanded surface stays inside a negative-coordinate monitor");
        Assert(bottomRight.Left < -40 && bottomRight.Top < 980,
            "bottom-right anchor expands inward");

        var orb = FacmSurfaceGeometryService.ExpandFromAnchor(new(
            new DesktopPoint(-1920, 0),
            new DesktopSize(36, 36),
            area,
            IsOrb: true));
        Assert(orb.Left >= area.Bounds.Left && orb.Top >= area.Bounds.Top, "orb is edge-clamped");
        Assert(orb.Width == 36 && orb.Height == 36, "orb size is preserved");

        var mainArea = new DesktopWorkArea("main", new DesktopRect(0, 0, 1920, 1080), false);
        var matrix = FacmSurfaceGeometryService.ExpandFromAnchor(new(
            new DesktopPoint(960, 540),
            new DesktopSize(360, 206),
            mainArea));
        Assert(matrix.Width == 360 && matrix.Height == 206, "control matrix compact geometry is stable");

        var transientRail = FacmSurfaceGeometryService.ExpandFromAnchor(new(
            new DesktopPoint(1880, 20),
            new DesktopSize(220, 36),
            mainArea,
            IsOrb: false));
        Assert(transientRail.Right <= mainArea.Bounds.Right - 8 && transientRail.Left < 1880,
            "transient orb rail clamps inward near the right edge");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FacmSurfaceGeometrySmoke failed: " + message);
    }
}
