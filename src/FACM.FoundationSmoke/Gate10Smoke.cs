using FACM.Core.Desktop;

internal static class Gate10Smoke
{
    public static Task RunAsync()
    {
        TestDpiScaleContract();
        TestDipToPhysicalPixels();
        TestMixedDpiNegativeAndTopMonitorPlacement();
        TestInvalidDpiFailsClosed();
        return Task.CompletedTask;
    }

    private static void TestDpiScaleContract()
    {
        var cases = new (double Dpi, double Scale)[]
        {
            (96, 1.00),
            (120, 1.25),
            (144, 1.50),
            (168, 1.75),
            (192, 2.00)
        };

        foreach (var item in cases)
            Near(item.Scale, DesktopDpi.ScaleFromDpi(item.Dpi), "DPI scale " + item.Dpi);
    }

    private static void TestDipToPhysicalPixels()
    {
        var expected = new (double Dpi, double Pixels)[]
        {
            (96, 64),
            (120, 80),
            (144, 96),
            (168, 112),
            (192, 128)
        };

        foreach (var item in expected)
        {
            var scale = DesktopDpi.ScaleFromDpi(item.Dpi);
            Near(item.Pixels, DesktopDpi.DipsToPixels(64, scale), "64 DIP physical pixels " + item.Dpi);
        }

        var mixed = new DesktopWorkArea(
            "mixed",
            new DesktopRect(1920, 0, 2560, 1440),
            false,
            DesktopDpi.ScaleFromDpi(120),
            DesktopDpi.ScaleFromDpi(168));
        var size = DesktopDpi.DipsToPixels(new DesktopSize(64, 64), mixed);
        Near(80, size.Width, "mixed DPI width");
        Near(112, size.Height, "mixed DPI height");
        Near(112, DesktopDpi.UniformDipsToPixels(64, mixed), "uniform DIP uses larger axis scale");
    }

    private static void TestMixedDpiNegativeAndTopMonitorPlacement()
    {
        var left = new DesktopWorkArea(
            "left-125",
            new DesktopRect(-1920, 0, 1920, 1080),
            false,
            1.25,
            1.25);
        var primary = new DesktopWorkArea(
            "primary-100",
            new DesktopRect(0, 0, 1920, 1080),
            true,
            1.0,
            1.0);
        var right = new DesktopWorkArea(
            "right-200",
            new DesktopRect(1920, 0, 2560, 1440),
            false,
            2.0,
            2.0);
        var top = new DesktopWorkArea(
            "top-175",
            new DesktopRect(0, -1440, 2560, 1440),
            false,
            1.75,
            1.75);
        var areas = new[] { left, primary, right, top };

        var rightSelected = AnchorPlacementService.SelectWorkArea(areas, new DesktopPoint(3500, 500));
        Equal("right-200", rightSelected.Id, "right mixed-DPI monitor selection");
        var rightSize = DesktopDpi.DipsToPixels(new DesktopSize(64, 64), rightSelected);
        var rightPlacement = AnchorPlacementService.Place(new AnchorPlacementRequest(
            areas,
            rightSize,
            new DesktopPoint(4200, 1200),
            DesktopAnchor.Auto,
            DesktopDpi.UniformDipsToPixels(12, rightSelected)));
        Equal("right-200", rightPlacement.WorkArea.Id, "right placement work area");
        True(rightPlacement.TopLeft.X >= right.Bounds.Left && rightPlacement.TopLeft.X + rightSize.Width <= right.Bounds.Right,
            "right placement horizontal bounds");
        True(rightPlacement.TopLeft.Y >= right.Bounds.Top && rightPlacement.TopLeft.Y + rightSize.Height <= right.Bounds.Bottom,
            "right placement vertical bounds");

        var leftSelected = AnchorPlacementService.SelectWorkArea(areas, new DesktopPoint(-1500, 400));
        Equal("left-125", leftSelected.Id, "negative-coordinate monitor selection");
        var leftSize = DesktopDpi.DipsToPixels(new DesktopSize(64, 64), leftSelected);
        var leftPlacement = AnchorPlacementService.Place(new AnchorPlacementRequest(
            areas,
            leftSize,
            new DesktopPoint(-1800, 900),
            DesktopAnchor.BottomLeft,
            DesktopDpi.UniformDipsToPixels(12, leftSelected)));
        True(leftPlacement.TopLeft.X < 0, "negative coordinate must be preserved");

        var topSelected = AnchorPlacementService.SelectWorkArea(areas, new DesktopPoint(1200, -900));
        Equal("top-175", topSelected.Id, "top mixed-DPI monitor selection");
        var topSize = DesktopDpi.DipsToPixels(new DesktopSize(64, 64), topSelected);
        var topPlacement = AnchorPlacementService.Place(new AnchorPlacementRequest(
            areas,
            topSize,
            new DesktopPoint(2400, -100),
            DesktopAnchor.BottomRight,
            DesktopDpi.UniformDipsToPixels(12, topSelected)));
        True(topPlacement.TopLeft.Y < 0, "top monitor negative Y must be preserved");

        var recovered = AnchorPlacementService.Place(new AnchorPlacementRequest(
            areas,
            new DesktopSize(128, 128),
            new DesktopPoint(9000, 9000),
            DesktopAnchor.Auto,
            24));
        True(recovered.RecoveredOffScreen, "mixed-DPI off-screen recovery flag");
        True(recovered.WorkArea.Bounds.Contains(new DesktopPoint(
            recovered.TopLeft.X + 1,
            recovered.TopLeft.Y + 1)), "recovered point lies on selected work area");
    }

    private static void TestInvalidDpiFailsClosed()
    {
        foreach (var invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            try
            {
                _ = DesktopDpi.ScaleFromDpi(invalid);
                throw new InvalidOperationException("Invalid DPI should fail: " + invalid);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
    }

    private static void Near(double expected, double actual, string name)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
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
