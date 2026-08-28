using FACM.Core.Desktop;

internal static class Gate7Smoke
{
    public static Task RunAsync()
    {
        var left = new DesktopWorkArea("left", new DesktopRect(-1920, 0, 1920, 1080), false, 1.25, 1.25);
        var primary = new DesktopWorkArea("primary", new DesktopRect(0, 0, 1920, 1040), true, 1, 1);
        var top = new DesktopWorkArea("top", new DesktopRect(0, -1200, 1920, 1200), false, 1.5, 1.5);
        DesktopWorkArea[] areas = [left, primary, top];

        Equal("primary", AnchorPlacementService.SelectWorkArea(areas, null).Id, "primary fallback");
        Equal("left", AnchorPlacementService.SelectWorkArea(areas, new DesktopPoint(-800, 300)).Id, "negative-coordinate monitor");
        Equal("top", AnchorPlacementService.SelectWorkArea(areas, new DesktopPoint(400, -900)).Id, "monitor above primary");
        Equal("left", AnchorPlacementService.SelectWorkArea(areas, new DesktopPoint(-2500, 400)).Id, "nearest off-screen monitor");

        var defaultPlacement = AnchorPlacementService.Place(new AnchorPlacementRequest(
            areas,
            new DesktopSize(64, 64),
            null,
            DesktopAnchor.Auto,
            12));
        Equal("primary", defaultPlacement.WorkArea.Id, "default placement monitor");
        Equal(DesktopAnchor.BottomRight, defaultPlacement.ResolvedAnchor, "default placement anchor");
        Equal(1844d, defaultPlacement.TopLeft.X, "default right edge");
        Equal(964d, defaultPlacement.TopLeft.Y, "default bottom edge");

        var leftBottomRight = AnchorPlacementService.Place(new AnchorPlacementRequest(
            areas,
            new DesktopSize(80, 80),
            new DesktopPoint(-1700, 700),
            DesktopAnchor.BottomRight,
            20));
        Equal("left", leftBottomRight.WorkArea.Id, "left work area selection");
        Equal(-100d, leftBottomRight.TopLeft.X, "negative work area right anchor");
        Equal(980d, leftBottomRight.TopLeft.Y, "left work area bottom anchor");
        True(!leftBottomRight.RecoveredOffScreen, "visible preferred point must not report recovery");

        var recovered = AnchorPlacementService.Place(new AnchorPlacementRequest(
            areas,
            new DesktopSize(64, 64),
            new DesktopPoint(-4000, 200),
            DesktopAnchor.Auto,
            12));
        Equal("left", recovered.WorkArea.Id, "off-screen recovery monitor");
        True(recovered.RecoveredOffScreen, "off-screen preferred location must report recovery");
        True(recovered.TopLeft.X >= -1908 && recovered.TopLeft.X <= -76, "recovered X must be clamped into negative work area");
        True(recovered.TopLeft.Y >= 12 && recovered.TopLeft.Y <= 1004, "recovered Y must be clamped into work area");

        var rightAuto = AnchorPlacementService.Place(new AnchorPlacementRequest(
            [primary],
            new DesktopSize(64, 64),
            new DesktopPoint(1820, 400),
            DesktopAnchor.Auto,
            12));
        Equal(DesktopAnchor.Right, rightAuto.ResolvedAnchor, "auto nearest right edge");
        Equal(1844d, rightAuto.TopLeft.X, "auto right edge placement");

        True(!FloatingSurfaceDragService.HasExceededThreshold(
            new DesktopPoint(100, 100),
            new DesktopPoint(103, 102),
            4), "sub-threshold pointer movement must remain a click");
        True(FloatingSurfaceDragService.HasExceededThreshold(
            new DesktopPoint(100, 100),
            new DesktopPoint(104, 101),
            4), "pointer movement past threshold must become a drag");

        var dragOnLeft = FloatingSurfaceDragService.ClampTopLeft(
            areas,
            new DesktopSize(64, 64),
            new DesktopPoint(-20, 1060),
            new DesktopPoint(-200, 700),
            4);
        Equal("left", dragOnLeft.WorkArea.Id, "drag pointer selects negative-coordinate monitor");
        Equal(-69d, dragOnLeft.TopLeft.X, "drag clamps right edge using monitor DPI margin");
        Equal(1011d, dragOnLeft.TopLeft.Y, "drag clamps bottom edge using monitor DPI margin");

        var dragOnTop = FloatingSurfaceDragService.ClampTopLeft(
            areas,
            new DesktopSize(96, 96),
            new DesktopPoint(400, -1400),
            new DesktopPoint(500, -900),
            4);
        Equal("top", dragOnTop.WorkArea.Id, "drag pointer selects monitor above primary");
        Equal(-1194d, dragOnTop.TopLeft.Y, "drag clamps negative top edge using DPI margin");

        var topLeft = AnchorPlacementService.Place(new AnchorPlacementRequest(
            [top],
            new DesktopSize(96, 96),
            new DesktopPoint(600, -700),
            DesktopAnchor.TopLeft,
            18));
        Equal(18d, topLeft.TopLeft.X, "top-left X");
        Equal(-1182d, topLeft.TopLeft.Y, "top-left negative Y");

        try
        {
            AnchorPlacementService.Place(new AnchorPlacementRequest([], new DesktopSize(64, 64)));
            throw new InvalidOperationException("empty work areas should fail");
        }
        catch (ArgumentException)
        {
        }

        try
        {
            _ = FloatingSurfaceDragService.HasExceededThreshold(
                new DesktopPoint(0, 0),
                new DesktopPoint(1, 1),
                -1);
            throw new InvalidOperationException("negative drag threshold should fail");
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return Task.CompletedTask;
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }
}
