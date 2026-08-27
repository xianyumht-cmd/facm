namespace FACM.Core.Desktop;

public static class DesktopDpi
{
    public const double DefaultDpi = 96d;

    public static double ScaleFromDpi(double dpi)
    {
        if (!double.IsFinite(dpi) || dpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI must be finite and positive.");
        return dpi / DefaultDpi;
    }

    public static double DipsToPixels(double dips, double scale)
    {
        if (!double.IsFinite(dips) || dips < 0)
            throw new ArgumentOutOfRangeException(nameof(dips), "DIP value must be finite and non-negative.");
        if (!double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be finite and positive.");
        return dips * scale;
    }

    public static DesktopSize DipsToPixels(DesktopSize dips, DesktopWorkArea workArea)
    {
        if (!dips.IsValid)
            throw new ArgumentException("DIP size must be finite and positive.", nameof(dips));
        ArgumentNullException.ThrowIfNull(workArea);
        return new DesktopSize(
            DipsToPixels(dips.Width, workArea.DpiScaleX),
            DipsToPixels(dips.Height, workArea.DpiScaleY));
    }

    public static double UniformDipsToPixels(double dips, DesktopWorkArea workArea)
    {
        ArgumentNullException.ThrowIfNull(workArea);
        return DipsToPixels(dips, Math.Max(workArea.DpiScaleX, workArea.DpiScaleY));
    }
}
