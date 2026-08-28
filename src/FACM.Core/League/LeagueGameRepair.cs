namespace FACM.Core.League;

public readonly record struct LeagueWindowSize(int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

public readonly record struct LeagueWindowBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public LeagueWindowSize Size => new(Width, Height);
    public bool IsValid => Width > 0 && Height > 0;

    public bool Intersects(LeagueWindowBounds other) =>
        IsValid && other.IsValid &&
        Left < other.Right && Right > other.Left &&
        Top < other.Bottom && Bottom > other.Top;
}

public sealed record LeagueWindowRepairPlan(
    bool CurrentIsSane,
    LeagueWindowBounds TargetBounds,
    string Reason);

public static class LeagueWindowRepairPlanner
{
    private const double TargetAspect = 16.0 / 9.0;
    private const double AspectTolerance = 0.045;

    public static bool IsSane(LeagueWindowBounds bounds, LeagueWindowBounds workingArea)
    {
        if (!bounds.IsValid || !workingArea.IsValid) return false;
        if (bounds.Width < 640 || bounds.Height < 360) return false;
        if (bounds.Width > workingArea.Width * 1.08 || bounds.Height > workingArea.Height * 1.08) return false;
        var aspect = bounds.Width / (double)Math.Max(1, bounds.Height);
        if (Math.Abs(aspect - TargetAspect) > AspectTolerance) return false;
        var visible = Intersect(bounds, workingArea);
        if (!visible.IsValid) return false;
        var visibleArea = (long)visible.Width * visible.Height;
        var totalArea = (long)bounds.Width * bounds.Height;
        return totalArea > 0 && visibleArea >= totalArea / 4;
    }

    public static LeagueWindowRepairPlan Plan(
        LeagueWindowBounds current,
        LeagueWindowBounds workingArea,
        LeagueWindowSize? rememberedSaneSize,
        double zoom)
    {
        if (!workingArea.IsValid) throw new ArgumentOutOfRangeException(nameof(workingArea));

        if (IsSane(current, workingArea))
        {
            var clamped = ClampPosition(current, workingArea);
            return new LeagueWindowRepairPlan(
                true,
                clamped,
                current.Equals(clamped) ? "already-sane" : "offscreen");
        }

        LeagueWindowSize target;
        string reason;
        if (rememberedSaneSize is { } remembered && IsSaneSize(remembered, workingArea))
        {
            target = Fit(remembered, workingArea);
            reason = "remembered-sane-size";
        }
        else if (CanUseWidth(current.Width, workingArea))
        {
            target = Fit(new LeagueWindowSize(current.Width, (int)Math.Round(current.Width / TargetAspect)), workingArea);
            reason = "preserve-current-width";
        }
        else if (CanUseHeight(current.Height, workingArea))
        {
            target = Fit(new LeagueWindowSize((int)Math.Round(current.Height * TargetAspect), current.Height), workingArea);
            reason = "preserve-current-height";
        }
        else
        {
            var safeZoom = zoom is > 0.4 and < 2.1 ? zoom : 1.0;
            var zoomWidth = (int)Math.Round(1280 * safeZoom);
            var monitorWidth = (int)Math.Round(workingArea.Width * 0.78);
            var width = Math.Max(zoomWidth, monitorWidth);
            target = Fit(new LeagueWindowSize(width, (int)Math.Round(width / TargetAspect)), workingArea);
            reason = "monitor-fallback";
        }

        var intersects = current.Intersects(workingArea);
        var x = current.IsValid && intersects
            ? current.Left
            : workingArea.Left + (workingArea.Width - target.Width) / 2;
        var y = current.IsValid && intersects
            ? current.Top
            : workingArea.Top + (workingArea.Height - target.Height) / 2;
        var planned = ClampPosition(
            new LeagueWindowBounds(x, y, target.Width, target.Height),
            workingArea);
        return new LeagueWindowRepairPlan(false, planned, reason);
    }

    private static bool IsSaneSize(LeagueWindowSize size, LeagueWindowBounds workingArea)
    {
        if (!size.IsValid || size.Width < 640 || size.Height < 360) return false;
        if (size.Width > workingArea.Width || size.Height > workingArea.Height) return false;
        return Math.Abs(size.Width / (double)Math.Max(1, size.Height) - TargetAspect) <= AspectTolerance;
    }

    private static bool CanUseWidth(int width, LeagueWindowBounds workingArea)
    {
        if (width < 640 || width > workingArea.Width) return false;
        var height = (int)Math.Round(width / TargetAspect);
        return height >= 360 && height <= workingArea.Height;
    }

    private static bool CanUseHeight(int height, LeagueWindowBounds workingArea)
    {
        if (height < 360 || height > workingArea.Height) return false;
        var width = (int)Math.Round(height * TargetAspect);
        return width >= 640 && width <= workingArea.Width;
    }

    private static LeagueWindowSize Fit(LeagueWindowSize requested, LeagueWindowBounds workingArea)
    {
        var maxWidth = Math.Max(320, (int)Math.Floor(workingArea.Width * 0.96));
        var maxHeight = Math.Max(180, (int)Math.Floor(workingArea.Height * 0.96));
        var width = Math.Max(320, requested.Width);
        var height = Math.Max(180, requested.Height);
        var scale = Math.Min(1.0, Math.Min(maxWidth / (double)width, maxHeight / (double)height));
        width = Math.Max(320, (int)Math.Round(width * scale));
        height = Math.Max(180, (int)Math.Round(height * scale));
        var correctedHeight = (int)Math.Round(width / TargetAspect);
        if (correctedHeight <= maxHeight) height = correctedHeight;
        else width = (int)Math.Round(height * TargetAspect);
        return new LeagueWindowSize(width, height);
    }

    private static LeagueWindowBounds ClampPosition(LeagueWindowBounds bounds, LeagueWindowBounds workingArea)
    {
        var width = Math.Min(bounds.Width, workingArea.Width);
        var height = Math.Min(bounds.Height, workingArea.Height);
        var maxX = workingArea.Right - width;
        var maxY = workingArea.Bottom - height;
        var x = Math.Max(workingArea.Left, Math.Min(bounds.Left, maxX));
        var y = Math.Max(workingArea.Top, Math.Min(bounds.Top, maxY));
        return new LeagueWindowBounds(x, y, width, height);
    }

    private static LeagueWindowBounds Intersect(LeagueWindowBounds first, LeagueWindowBounds second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        return right <= left || bottom <= top
            ? default
            : new LeagueWindowBounds(left, top, right - left, bottom - top);
    }
}

public sealed record LeagueGameRepairResult(
    bool Success,
    bool Changed,
    string State,
    string Message,
    string Diagnostic = "");

public interface ILeagueGameRepairService : IDisposable
{
    bool AutoRepairEnabled { get; }

    Task<LeagueGameRepairResult> RepairWindowAsync(CancellationToken cancellationToken);
    LeagueGameRepairResult SetAutoRepairEnabled(bool enabled);
    Task<LeagueGameRepairResult> SkipSettlementAsync(CancellationToken cancellationToken);
    Task<LeagueGameRepairResult> RestartClientUxAsync(CancellationToken cancellationToken);
    Task<LeagueGameRepairResult> ExitGameAsync(CancellationToken cancellationToken);
}
