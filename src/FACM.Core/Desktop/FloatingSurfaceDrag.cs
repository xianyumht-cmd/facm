namespace FACM.Core.Desktop;

public sealed record FloatingSurfaceDragPlacement(
    DesktopPoint TopLeft,
    DesktopWorkArea WorkArea);

public static class FloatingSurfaceDragService
{
    public static bool HasExceededThreshold(
        DesktopPoint start,
        DesktopPoint current,
        double thresholdPixels)
    {
        if (!start.IsFinite) throw new ArgumentOutOfRangeException(nameof(start));
        if (!current.IsFinite) throw new ArgumentOutOfRangeException(nameof(current));
        if (!double.IsFinite(thresholdPixels) || thresholdPixels < 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdPixels));

        var dx = current.X - start.X;
        var dy = current.Y - start.Y;
        return (dx * dx) + (dy * dy) >= thresholdPixels * thresholdPixels;
    }

    // FACM 3.5 treated drag intent as Manhattan movement greater than four physical pixels.
    // Preserve that proven interaction model while the 4.0 surface itself remains WinUI.
    public static bool HasExceededLegacyBallThreshold(
        DesktopPoint start,
        DesktopPoint current,
        double thresholdPixels = 4d)
    {
        if (!start.IsFinite) throw new ArgumentOutOfRangeException(nameof(start));
        if (!current.IsFinite) throw new ArgumentOutOfRangeException(nameof(current));
        if (!double.IsFinite(thresholdPixels) || thresholdPixels < 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdPixels));

        return Math.Abs(current.X - start.X) + Math.Abs(current.Y - start.Y) > thresholdPixels;
    }

    public static DesktopPoint DefaultLegacyBallTopLeft(
        DesktopWorkArea workArea,
        DesktopSize surfaceSize,
        double rightMarginDip = 18d)
    {
        if (!workArea.Bounds.IsValid ||
            !double.IsFinite(workArea.DpiScaleX) || workArea.DpiScaleX <= 0 ||
            !double.IsFinite(workArea.DpiScaleY) || workArea.DpiScaleY <= 0)
        {
            throw new ArgumentException("Desktop work area is invalid.", nameof(workArea));
        }
        if (!surfaceSize.IsValid)
            throw new ArgumentException("Surface size must be finite and positive.", nameof(surfaceSize));
        if (!double.IsFinite(rightMarginDip) || rightMarginDip < 0)
            throw new ArgumentOutOfRangeException(nameof(rightMarginDip));

        var marginX = rightMarginDip * workArea.DpiScaleX;
        return new DesktopPoint(
            workArea.Bounds.Right - surfaceSize.Width - marginX,
            workArea.Bounds.Top + Math.Max(0d, (workArea.Bounds.Height - surfaceSize.Height) / 2d));
    }

    // Mirrors the 3.5 floating-ball edge behavior: keep the whole ball vertically visible while
    // allowing a small horizontal overhang so the launcher can sit tightly against either edge.
    public static FloatingSurfaceDragPlacement ClampLegacyBallTopLeft(
        IReadOnlyList<DesktopWorkArea> workingAreas,
        DesktopSize surfaceSize,
        DesktopPoint proposedTopLeft,
        DesktopPoint pointerProbe)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);
        if (workingAreas.Count == 0)
            throw new ArgumentException("At least one desktop work area is required.", nameof(workingAreas));
        if (!surfaceSize.IsValid)
            throw new ArgumentException("Surface size must be finite and positive.", nameof(surfaceSize));
        if (!proposedTopLeft.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(proposedTopLeft));
        if (!pointerProbe.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(pointerProbe));

        var workArea = AnchorPlacementService.SelectWorkArea(workingAreas, pointerProbe);
        if (!workArea.Bounds.IsValid)
            throw new ArgumentException("Selected desktop work area is invalid.", nameof(workingAreas));

        var minX = workArea.Bounds.Left - (surfaceSize.Width / 3d);
        var maxX = workArea.Bounds.Right - (surfaceSize.Width * 2d / 3d);
        var minY = workArea.Bounds.Top;
        var maxY = workArea.Bounds.Bottom - surfaceSize.Height;

        if (maxX < minX)
            minX = maxX = workArea.Bounds.Left + Math.Max(0d, (workArea.Bounds.Width - surfaceSize.Width) / 2d);
        if (maxY < minY)
            minY = maxY = workArea.Bounds.Top + Math.Max(0d, (workArea.Bounds.Height - surfaceSize.Height) / 2d);

        return new FloatingSurfaceDragPlacement(
            new DesktopPoint(
                Math.Clamp(proposedTopLeft.X, minX, maxX),
                Math.Clamp(proposedTopLeft.Y, minY, maxY)),
            workArea);
    }

    public static FloatingSurfaceDragPlacement ClampTopLeft(
        IReadOnlyList<DesktopWorkArea> workingAreas,
        DesktopSize surfaceSize,
        DesktopPoint proposedTopLeft,
        DesktopPoint pointerProbe,
        double marginDip = 4d)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);
        if (workingAreas.Count == 0)
            throw new ArgumentException("At least one desktop work area is required.", nameof(workingAreas));
        if (!surfaceSize.IsValid)
            throw new ArgumentException("Surface size must be finite and positive.", nameof(surfaceSize));
        if (!proposedTopLeft.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(proposedTopLeft));
        if (!pointerProbe.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(pointerProbe));
        if (!double.IsFinite(marginDip) || marginDip < 0)
            throw new ArgumentOutOfRangeException(nameof(marginDip));

        var workArea = AnchorPlacementService.SelectWorkArea(workingAreas, pointerProbe);
        if (!workArea.Bounds.IsValid ||
            !double.IsFinite(workArea.DpiScaleX) || workArea.DpiScaleX <= 0 ||
            !double.IsFinite(workArea.DpiScaleY) || workArea.DpiScaleY <= 0)
        {
            throw new ArgumentException("Selected desktop work area is invalid.", nameof(workingAreas));
        }

        var marginX = marginDip * workArea.DpiScaleX;
        var marginY = marginDip * workArea.DpiScaleY;
        var minX = workArea.Bounds.Left + Math.Min(marginX, workArea.Bounds.Width / 2d);
        var minY = workArea.Bounds.Top + Math.Min(marginY, workArea.Bounds.Height / 2d);
        var maxX = workArea.Bounds.Right - surfaceSize.Width - Math.Min(marginX, workArea.Bounds.Width / 2d);
        var maxY = workArea.Bounds.Bottom - surfaceSize.Height - Math.Min(marginY, workArea.Bounds.Height / 2d);

        if (maxX < minX)
            minX = maxX = workArea.Bounds.Left + Math.Max(0d, (workArea.Bounds.Width - surfaceSize.Width) / 2d);
        if (maxY < minY)
            minY = maxY = workArea.Bounds.Top + Math.Max(0d, (workArea.Bounds.Height - surfaceSize.Height) / 2d);

        return new FloatingSurfaceDragPlacement(
            new DesktopPoint(
                Math.Clamp(proposedTopLeft.X, minX, maxX),
                Math.Clamp(proposedTopLeft.Y, minY, maxY)),
            workArea);
    }
}
