namespace FACM.Core.Desktop;

public sealed record FacmSurfaceGeometryRequest(
    DesktopPoint Anchor,
    DesktopSize SurfaceSize,
    DesktopWorkArea WorkArea,
    double EdgeMargin = 8d,
    bool IsOrb = false);

public static class FacmSurfaceGeometryService
{
    public static DesktopRect ExpandFromAnchor(FacmSurfaceGeometryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Anchor.IsFinite) throw new ArgumentException("Anchor must be finite.", nameof(request));
        if (!request.SurfaceSize.IsValid) throw new ArgumentException("Surface size must be valid.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.WorkArea);
        if (!request.WorkArea.Bounds.IsValid) throw new ArgumentException("Work area must be valid.", nameof(request));
        if (!double.IsFinite(request.EdgeMargin) || request.EdgeMargin < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Edge margin must be finite and non-negative.");

        var area = request.WorkArea.Bounds;
        var edge = Math.Min(request.EdgeMargin, Math.Min(area.Width, area.Height) / 2d);
        var x = request.Anchor.X;
        var y = request.Anchor.Y;
        if (!request.IsOrb)
        {
            var spaceRight = area.Right - request.Anchor.X;
            var spaceLeft = request.Anchor.X - area.Left;
            var spaceDown = area.Bottom - request.Anchor.Y;
            var spaceUp = request.Anchor.Y - area.Top;
            if (spaceRight < request.SurfaceSize.Width + edge && spaceLeft >= request.SurfaceSize.Width + edge)
                x = request.Anchor.X - request.SurfaceSize.Width;
            if (spaceDown < request.SurfaceSize.Height + edge && spaceUp >= request.SurfaceSize.Height + edge)
                y = request.Anchor.Y - request.SurfaceSize.Height;
        }

        var minX = area.Left + edge;
        var minY = area.Top + edge;
        var maxX = area.Right - request.SurfaceSize.Width - edge;
        var maxY = area.Bottom - request.SurfaceSize.Height - edge;
        if (maxX < minX)
            maxX = minX = area.Left + Math.Max(0d, (area.Width - request.SurfaceSize.Width) / 2d);
        if (maxY < minY)
            maxY = minY = area.Top + Math.Max(0d, (area.Height - request.SurfaceSize.Height) / 2d);

        return new DesktopRect(
            Math.Clamp(x, minX, maxX),
            Math.Clamp(y, minY, maxY),
            request.SurfaceSize.Width,
            request.SurfaceSize.Height);
    }
}
