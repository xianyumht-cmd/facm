using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using FACM.Core.Desktop;

namespace FACM.Platform.Windows.Desktop;

public sealed class WindowsDesktopWorkAreaProvider : IDesktopWorkAreaProvider
{
    private const uint MonitorInfoPrimary = 0x00000001;
    private const uint MonitorDpiTypeEffective = 0;

    public IReadOnlyList<DesktopWorkArea> GetWorkingAreas()
    {
        var result = new List<DesktopWorkArea>();
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetMonitorInfo failed.");

            var dpi = TryGetDpi(monitor);
            var work = info.Work;
            result.Add(new DesktopWorkArea(
                monitor.ToInt64().ToString("X", CultureInfo.InvariantCulture),
                new DesktopRect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top),
                (info.Flags & MonitorInfoPrimary) != 0,
                DesktopDpi.ScaleFromDpi(dpi.X),
                DesktopDpi.ScaleFromDpi(dpi.Y)));
            return true;
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors failed.");
        GC.KeepAlive(callback);

        if (result.Count == 0)
            throw new InvalidOperationException("Windows reported no desktop monitors.");
        return result;
    }

    private static (double X, double Y) TryGetDpi(IntPtr monitor)
    {
        try
        {
            var hr = GetDpiForMonitor(monitor, MonitorDpiTypeEffective, out var x, out var y);
            if (hr == 0 && x > 0 && y > 0) return (x, y);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return (DesktopDpi.DefaultDpi, DesktopDpi.DefaultDpi);
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr monitorRect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        uint dpiType,
        out uint dpiX,
        out uint dpiY);
}
