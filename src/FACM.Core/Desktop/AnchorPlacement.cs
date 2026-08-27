namespace FACM.Core.Desktop;

public readonly record struct DesktopPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public readonly record struct DesktopSize(double Width, double Height)
{
    public bool IsValid => double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
}

public readonly record struct DesktopRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool IsValid => double.IsFinite(Left) && double.IsFinite(Top) &&
                           double.IsFinite(Width) && double.IsFinite(Height) &&
                           Width > 0 && Height > 0;

    public bool Contains(DesktopPoint point) =>
        point.IsFinite && point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public DesktopPoint Clamp(DesktopPoint point) => new(
        Math.Clamp(point.X, Left, Right),
        Math.Clamp(point.Y, Top, Bottom));
}

public sealed record DesktopWorkArea(
    string Id,
    DesktopRect Bounds,
    bool IsPrimary,
    double DpiScaleX = 1d,
    double DpiScaleY = 1d);

public enum DesktopAnchor
{
    Auto,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public sealed record AnchorPlacementRequest(
    IReadOnlyList<DesktopWorkArea> WorkingAreas,
    DesktopSize SurfaceSize,
    DesktopPoint? PreferredTopLeft = null,
    DesktopAnchor Anchor = DesktopAnchor.Auto,
    double Margin = 12d);

public sealed record AnchorPlacementResult(
    DesktopPoint TopLeft,
    DesktopWorkArea WorkArea,
    DesktopAnchor ResolvedAnchor,
    bool RecoveredOffScreen);

public static class AnchorPlacementService
{
    public static AnchorPlacementResult Place(AnchorPlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkingAreas is null || request.WorkingAreas.Count == 0)
            throw new ArgumentException("At least one desktop work area is required.", nameof(request));
        if (!request.SurfaceSize.IsValid)
            throw new ArgumentException("Surface size must be finite and positive.", nameof(request));
        if (!double.IsFinite(request.Margin) || request.Margin < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Margin must be finite and non-negative.");

        foreach (var area in request.WorkingAreas)
        {
            if (area is null || !area.Bounds.IsValid)
                throw new ArgumentException("Every desktop work area must have valid bounds.", nameof(request));
            if (!double.IsFinite(area.DpiScaleX) || !double.IsFinite(area.DpiScaleY) ||
                area.DpiScaleX <= 0 || area.DpiScaleY <= 0)
                throw new ArgumentException("Every desktop work area must have positive finite DPI scales.", nameof(request));
        }

        var preferred = request.PreferredTopLeft is { IsFinite: true } value ? value : (DesktopPoint?)null;
        var probe = preferred is null
            ? null
            : new DesktopPoint(
                preferred.Value.X + (request.SurfaceSize.Width / 2d),
                preferred.Value.Y + (request.SurfaceSize.Height / 2d));

        var area = SelectWorkArea(request.WorkingAreas, probe);
        var preferredWasVisible = preferred is not null && Intersects(area.Bounds, preferred.Value, request.SurfaceSize);
        var anchor = ResolveAnchor(request.Anchor, area.Bounds, preferred, request.SurfaceSize);
        var topLeft = Calculate(area.Bounds, request.SurfaceSize, preferred, anchor, request.Margin);

        return new AnchorPlacementResult(topLeft, area, anchor, preferred is not null && !preferredWasVisible);
    }

    public static DesktopWorkArea SelectWorkArea(IReadOnlyList<DesktopWorkArea> areas, DesktopPoint? probe)
    {
        ArgumentNullException.ThrowIfNull(areas);
        if (areas.Count == 0) throw new ArgumentException("At least one desktop work area is required.", nameof(areas));

        if (probe is { IsFinite: true } point)
        {
            foreach (var area in areas)
            {
                if (area.Bounds.Contains(point)) return area;
            }

            return areas
                .OrderBy(area => SquaredDistanceToRect(point, area.Bounds))
                .ThenByDescending(area => area.IsPrimary)
                .First();
        }

        return areas.FirstOrDefault(area => area.IsPrimary) ?? areas[0];
    }

    private static DesktopAnchor ResolveAnchor(
        DesktopAnchor requested,
        DesktopRect area,
        DesktopPoint? preferred,
        DesktopSize size)
    {
        if (requested != DesktopAnchor.Auto) return requested;
        if (preferred is null) return DesktopAnchor.BottomRight;

        var centerX = preferred.Value.X + (size.Width / 2d);
        var centerY = preferred.Value.Y + (size.Height / 2d);
        var left = Math.Abs(centerX - area.Left);
        var right = Math.Abs(area.Right - centerX);
        var top = Math.Abs(centerY - area.Top);
        var bottom = Math.Abs(area.Bottom - centerY);
        var nearest = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (nearest == left) return DesktopAnchor.Left;
        if (nearest == right) return DesktopAnchor.Right;
        if (nearest == top) return DesktopAnchor.Top;
        return DesktopAnchor.Bottom;
    }

    private static DesktopPoint Calculate(
        DesktopRect area,
        DesktopSize size,
        DesktopPoint? preferred,
        DesktopAnchor anchor,
        double margin)
    {
        var minX = area.Left + Math.Min(margin, area.Width / 2d);
        var minY = area.Top + Math.Min(margin, area.Height / 2d);
        var maxX = area.Right - size.Width - Math.Min(margin, area.Width / 2d);
        var maxY = area.Bottom - size.Height - Math.Min(margin, area.Height / 2d);
        if (maxX < minX) maxX = area.Left + Math.Max(0d, (area.Width - size.Width) / 2d);
        if (maxY < minY) maxY = area.Top + Math.Max(0d, (area.Height - size.Height) / 2d);

        var preferredX = preferred?.X ?? maxX;
        var preferredY = preferred?.Y ?? maxY;
        var x = Math.Clamp(preferredX, Math.Min(minX, maxX), Math.Max(minX, maxX));
        var y = Math.Clamp(preferredY, Math.Min(minY, maxY), Math.Max(minY, maxY));

        switch (anchor)
        {
            case DesktopAnchor.Left:
                x = minX;
                break;
            case DesktopAnchor.Right:
                x = maxX;
                break;
            case DesktopAnchor.Top:
                y = minY;
                break;
            case DesktopAnchor.Bottom:
                y = maxY;
                break;
            case DesktopAnchor.TopLeft:
                x = minX;
                y = minY;
                break;
            case DesktopAnchor.TopRight:
                x = maxX;
                y = minY;
                break;
            case DesktopAnchor.BottomLeft:
                x = minX;
                y = maxY;
                break;
            case DesktopAnchor.BottomRight:
                x = maxX;
                y = maxY;
                break;
        }

        return new DesktopPoint(x, y);
    }

    private static bool Intersects(DesktopRect area, DesktopPoint topLeft, DesktopSize size)
    {
        var right = topLeft.X + size.Width;
        var bottom = topLeft.Y + size.Height;
        return right > area.Left && topLeft.X < area.Right && bottom > area.Top && topLeft.Y < area.Bottom;
    }

    private static double SquaredDistanceToRect(DesktopPoint point, DesktopRect rect)
    {
        var dx = point.X < rect.Left ? rect.Left - point.X : point.X > rect.Right ? point.X - rect.Right : 0d;
        var dy = point.Y < rect.Top ? rect.Top - point.Y : point.Y > rect.Bottom ? point.Y - rect.Bottom : 0d;
        return (dx * dx) + (dy * dy);
    }
}

public interface IDesktopWorkAreaProvider
{
    IReadOnlyList<DesktopWorkArea> GetWorkingAreas();
}
