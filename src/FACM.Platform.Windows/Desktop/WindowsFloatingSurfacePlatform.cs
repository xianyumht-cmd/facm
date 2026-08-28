using System.Runtime.InteropServices;

namespace FACM.Platform.Windows.Desktop;

public sealed class WindowsFloatingSurfacePlatform
{
    public bool TryApplyCircularRegion(IntPtr windowHandle, int widthPixels, int heightPixels)
    {
        if (windowHandle == IntPtr.Zero || widthPixels <= 0 || heightPixels <= 0) return false;

        var region = CreateEllipticRgn(0, 0, widthPixels, heightPixels);
        if (region == IntPtr.Zero) return false;

        if (SetWindowRgn(windowHandle, region, redraw: true) != 0)
        {
            // On success Windows owns the region handle and releases it when replaced/destroyed.
            return true;
        }

        _ = DeleteObject(region);
        return false;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr windowHandle, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}
