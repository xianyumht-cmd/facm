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
