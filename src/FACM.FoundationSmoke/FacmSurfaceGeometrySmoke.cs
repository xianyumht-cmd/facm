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
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FacmSurfaceGeometrySmoke failed: " + message);
    }
}
