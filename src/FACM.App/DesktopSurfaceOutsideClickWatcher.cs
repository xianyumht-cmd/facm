using System.Runtime.InteropServices;
using FACM.Core.Desktop;
using Microsoft.UI.Dispatching;

namespace FACM.App;

/// <summary>
/// Applies the legacy outside-left-click rule to any FACM-owned top-level surface.
/// The state machine deliberately waits for the opening mouse button to be released before
/// arming, so an activation click cannot close the newly opened surface.
/// </summary>
internal sealed class DesktopSurfaceOutsideClickWatcher : IDisposable
{
    private const int VirtualLeftButton = 0x01;
    private readonly DispatcherQueueTimer _timer;
    private readonly Func<DesktopRect?> _boundsProvider;
    private readonly Func<bool> _isSuppressed;
    private readonly Action _close;
    private readonly CompactLauncherOutsideClickState _state = new();
    private bool _disposed;

    public DesktopSurfaceOutsideClickWatcher(
        DispatcherQueue dispatcherQueue,
        Func<DesktopRect?> boundsProvider,
        Func<bool> isSuppressed,
        Action close)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        _boundsProvider = boundsProvider ?? throw new ArgumentNullException(nameof(boundsProvider));
        _isSuppressed = isSuppressed ?? throw new ArgumentNullException(nameof(isSuppressed));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(40);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed) return;

        var leftButtonDown = (GetAsyncKeyState(VirtualLeftButton) & 0x8000) != 0;
        var pointerInside = true;
        if (TryGetCursorPosition(out var cursor) && _boundsProvider() is { } bounds && bounds.IsValid)
            pointerInside = bounds.Contains(cursor);

        if (_state.Observe(leftButtonDown, pointerInside, _isSuppressed()) ==
            CompactLauncherOutsideClickObservation.CloseRequested)
        {
            try { _close(); } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Tick -= OnTick;
        _timer.Stop();
        _state.Dispose();
    }

    private static bool TryGetCursorPosition(out DesktopPoint point)
    {
        point = default;
        if (!GetCursorPos(out var native)) return false;
        point = new DesktopPoint(native.X, native.Y);
        return point.IsFinite;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
