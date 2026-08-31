using System.Runtime.InteropServices;
using FACM.Core.Desktop;

namespace FACM.Platform.Windows.Desktop;

public sealed class WindowsFloatingSurfacePlatform : IDesktopCursorPositionProvider
{
    private readonly object _smallWindowGate = new();
    private readonly Dictionary<IntPtr, SmallWindowSubclass> _smallWindowSubclasses = new();

    public bool TryGetCursorPosition(out DesktopPoint position)
    {
        position = default;
        if (!GetCursorPos(out var point)) return false;
        position = new DesktopPoint(point.X, point.Y);
        return position.IsFinite;
    }

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

    public bool TryEnableSmallSurfaceWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;
        lock (_smallWindowGate)
        {
            if (_smallWindowSubclasses.ContainsKey(windowHandle)) return true;
            var subclass = new SmallWindowSubclass(windowHandle, 1, 1);
            if (!subclass.TryInstall()) return false;
            _smallWindowSubclasses.Add(windowHandle, subclass);
            return true;
        }
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

    private sealed class SmallWindowSubclass
    {
        private const uint WmGetMinMaxInfo = 0x0024;
        private const int GwlWndProc = -4;

        private readonly IntPtr _windowHandle;
        private readonly int _minimumWidth;
        private readonly int _minimumHeight;
        private readonly NativeWindowProc _windowProc;
        private IntPtr _previousWindowProc;

        public SmallWindowSubclass(IntPtr windowHandle, int minimumWidth, int minimumHeight)
        {
            _windowHandle = windowHandle;
            _minimumWidth = minimumWidth;
            _minimumHeight = minimumHeight;
            _windowProc = WindowProc;
        }

        public bool TryInstall()
        {
            _previousWindowProc = SetWindowLongPtr(
                _windowHandle,
                GwlWndProc,
                Marshal.GetFunctionPointerForDelegate(_windowProc));
            return _previousWindowProc != IntPtr.Zero;
        }

        private IntPtr WindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
        {
            var result = CallWindowProc(_previousWindowProc, windowHandle, message, wParam, lParam);
            if (message == WmGetMinMaxInfo && lParam != IntPtr.Zero)
            {
                var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                info.MinTrackSize.X = _minimumWidth;
                info.MinTrackSize.Y = _minimumHeight;
                Marshal.StructureToPtr(info, lParam, false);
            }

            return result;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr NativeWindowProc(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(
            IntPtr windowHandle,
            int index,
            IntPtr value);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW", SetLastError = true)]
        private static extern IntPtr CallWindowProc(
            IntPtr previousWindowProc,
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam);
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
