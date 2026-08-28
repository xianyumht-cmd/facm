using System.Runtime.InteropServices;

namespace FACM.Platform.Windows.Desktop;

public sealed class WindowsFloatingSurfacePlatform
{
    public bool TryApplyCircularRegion(IntPtr windowHandle, int fallbackWidthPixels, int fallbackHeightPixels)
    {
        if (windowHandle == IntPtr.Zero || fallbackWidthPixels <= 0 || fallbackHeightPixels <= 0) return false;

        var left = 0;
        var top = 0;
        var right = fallbackWidthPixels;
        var bottom = fallbackHeightPixels;

        if (TryGetClientBoundsInWindow(windowHandle, out var clientBounds))
        {
            left = clientBounds.Left;
            top = clientBounds.Top;
            right = clientBounds.Right;
            bottom = clientBounds.Bottom;
        }

        var region = CreateEllipticRgn(left, top, right, bottom);
        if (region == IntPtr.Zero) return false;

        if (SetWindowRgn(windowHandle, region, redraw: true) != 0)
        {
            // On success Windows owns the region handle and releases it when replaced/destroyed.
            return true;
        }

        _ = DeleteObject(region);
        return false;
    }

    private static bool TryGetClientBoundsInWindow(IntPtr windowHandle, out NativeRect bounds)
    {
        bounds = default;
        if (!GetWindowRect(windowHandle, out var windowRect)) return false;
        if (!GetClientRect(windowHandle, out var clientRect)) return false;

        var clientOrigin = new NativePoint { X = clientRect.Left, Y = clientRect.Top };
        if (!ClientToScreen(windowHandle, ref clientOrigin)) return false;

        var width = clientRect.Right - clientRect.Left;
        var height = clientRect.Bottom - clientRect.Top;
        if (width <= 0 || height <= 0) return false;

        var left = clientOrigin.X - windowRect.Left;
        var top = clientOrigin.Y - windowRect.Top;
        bounds = new NativeRect
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height
        };
        return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr windowHandle, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr windowHandle, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr windowHandle, ref NativePoint point);
}
