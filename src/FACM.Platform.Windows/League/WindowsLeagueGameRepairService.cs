using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FACM.Core.League;

namespace FACM.Platform.Windows.League;

/// <summary>
/// FACM 3.5.15 native game-repair behavior on the 4.0 runtime. Window work stays in the Windows
/// adapter; LCU reads and writes are injected from the process-wide League gateway so this service
/// never creates a second discovery/auth/session owner.
/// </summary>
public sealed class WindowsLeagueGameRepairService : ILeagueGameRepairService
{
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const int ObjidWindow = 0;
    private const uint GaRoot = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private static readonly TimeSpan AutoDebounce = TimeSpan.FromMilliseconds(380);
    private static readonly TimeSpan AutoCooldown = TimeSpan.FromSeconds(2);
    private static readonly HashSet<string> GameProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "League of Legends(TM)",
        "League of Legends"
    };

    private readonly object _sync = new();
    private readonly ILeagueReadGateway _read;
    private readonly ILeagueWriteGateway _write;
    private readonly WinEventDelegate _winEventDelegate;
    private Timer? _debounceTimer;
    private IntPtr _winEventHook;
    private LeagueWindowSize? _lastSaneSize;
    private long _suppressEventsUntilUtcTicks;
    private int _repairInFlight;
    private bool _autoRepairEnabled;
    private bool _disposed;

    public WindowsLeagueGameRepairService(ILeagueReadGateway read, ILeagueWriteGateway write)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _winEventDelegate = HandleWinEvent;
    }

    public bool AutoRepairEnabled
    {
        get
        {
            lock (_sync) return _autoRepairEnabled && !_disposed;
        }
    }

    public async Task<LeagueGameRepairResult> RepairWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handles = TryFindClientWindow();
        if (handles.Main == IntPtr.Zero)
            return Result(false, false, "no-client", "未检测到英雄联盟客户端窗口", "window=missing");
        if (IsIconic(handles.Main))
            return Result(true, false, "minimized", "客户端窗口已最小化，本次不调整", "window=minimized");
        if (!TryGetBounds(handles.Main, out var current))
            return Result(false, false, "window-read-failed", "读取客户端窗口位置失败", "GetWindowRect=false");
        if (!TryGetWorkingArea(handles.Main, out var workingArea))
            return Result(false, false, "monitor-read-failed", "读取客户端所在显示器工作区失败", "GetMonitorInfo=false");

        var dpi = GetDpi(handles.Main);
        var currentSane = LeagueWindowRepairPlanner.IsSane(current, workingArea);
        if (currentSane)
        {
            lock (_sync) _lastSaneSize = current.Size;
        }

        var cefSane = true;
        if (handles.Cef != IntPtr.Zero && TryGetBounds(handles.Cef, out var cefBounds))
        {
            cefSane = Math.Abs(cefBounds.Width - current.Width) <= 6 &&
                      Math.Abs(cefBounds.Height - current.Height) <= 6;
        }

        var clampedCurrent = LeagueWindowRepairPlanner.Plan(current, workingArea, current.Size, 1.0);
        var positionNeedsRepair = clampedCurrent.TargetBounds.Left != current.Left ||
                                  clampedCurrent.TargetBounds.Top != current.Top;
        if (currentSane && cefSane && !positionNeedsRepair)
        {
            return Result(
                true,
                false,
                "healthy",
                "客户端窗口尺寸正常，无需修复",
                Diagnostic(current, current, workingArea, dpi, "already-sane", handles.Cef != IntPtr.Zero));
        }

        var zoom = currentSane ? 1.0 : await TryReadZoomAsync(cancellationToken).ConfigureAwait(false);
        LeagueWindowSize? remembered;
        lock (_sync) remembered = _lastSaneSize;
        var plan = LeagueWindowRepairPlanner.Plan(current, workingArea, remembered, zoom);
        var target = plan.TargetBounds;

        Interlocked.Exchange(ref _suppressEventsUntilUtcTicks, DateTime.UtcNow.Add(AutoCooldown).Ticks);
        if (!SetWindowPos(
                handles.Main,
                IntPtr.Zero,
                target.Left,
                target.Top,
                target.Width,
                target.Height,
                SwpNoZOrder | SwpNoActivate))
        {
            return Result(
                false,
                false,
                "window-write-failed",
                "修复客户端窗口失败",
                Diagnostic(current, target, workingArea, dpi, plan.Reason, handles.Cef != IntPtr.Zero));
        }

        if (handles.Cef != IntPtr.Zero)
        {
            _ = SetWindowPos(
                handles.Cef,
                IntPtr.Zero,
                0,
                0,
                target.Width,
                target.Height,
                SwpNoZOrder | SwpNoActivate);
        }

        lock (_sync) _lastSaneSize = target.Size;
        var changed = !current.Equals(target) || !cefSane;
        return Result(
            true,
            changed,
            changed ? "repaired" : "healthy",
            changed ? "客户端窗口已恢复到合理尺寸" : "客户端窗口尺寸正常，无需修复",
            Diagnostic(current, target, workingArea, dpi, plan.Reason, handles.Cef != IntPtr.Zero));
    }

    public LeagueGameRepairResult SetAutoRepairEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_disposed)
                return Result(false, false, "disposed", "游戏修复服务已停止", "disposed=true");
            if (_autoRepairEnabled == enabled)
            {
                return Result(
                    true,
                    false,
                    enabled ? "auto-on" : "auto-off",
                    enabled ? "自动修复窗口已开启" : "自动修复窗口已关闭",
                    "unchanged=true");
            }

            if (!enabled)
            {
                StopAutoLocked();
                return Result(true, true, "auto-off", "自动修复窗口已关闭", "hook=stopped");
            }

            _winEventHook = SetWinEventHook(
                EventObjectLocationChange,
                EventObjectLocationChange,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                WineventOutOfContext | WineventSkipOwnProcess);
            if (_winEventHook == IntPtr.Zero)
                return Result(false, false, "hook-failed", "自动修复窗口启动失败", "SetWinEventHook=0");

            _debounceTimer = new Timer(HandleDebounceTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _autoRepairEnabled = true;
        }

        QueueAutoRepair();
        return Result(true, true, "auto-on", "自动修复窗口已开启", "event-driven=true;debounceMs=380");
    }

    public async Task<LeagueGameRepairResult> SkipSettlementAsync(CancellationToken cancellationToken)
    {
        var response = await _write.ExecuteAsync(
            new LeagueWriteCommand(LeagueWriteCapability.PlayAgain, null, null),
            cancellationToken).ConfigureAwait(false);
        var success = response?.IsSuccessStatusCode == true;
        var code = response?.StatusCode ?? 0;
        return Result(
            success,
            success,
            success ? "success" : "failed",
            success ? "已向客户端发送跳过结算指令" : "跳过结算失败，请确认客户端已连接",
            "route=play-again;http=" + code.ToString(CultureInfo.InvariantCulture));
    }

    public async Task<LeagueGameRepairResult> RestartClientUxAsync(CancellationToken cancellationToken)
    {
        var response = await _write.ExecuteAsync(
            new LeagueWriteCommand(LeagueWriteCapability.RestartClientUx, null, null),
            cancellationToken).ConfigureAwait(false);
        var success = response?.IsSuccessStatusCode == true;
        var code = response?.StatusCode ?? 0;
        return Result(
            success,
            success,
            success ? "success" : "failed",
            success ? "已请求重启客户端界面" : "重启客户端界面失败，请确认客户端已连接",
            "route=kill-and-restart-ux;http=" + code.ToString(CultureInfo.InvariantCulture));
    }

    public Task<LeagueGameRepairResult> ExitGameAsync(CancellationToken cancellationToken) =>
        Task.Run(() => ExitGameCore(cancellationToken), cancellationToken);

    private static LeagueGameRepairResult ExitGameCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targets = new List<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (GameProcessNames.Contains(process.ProcessName)) targets.Add(process.Id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A process can disappear or deny access while the snapshot is being built.
            }
            finally
            {
                process.Dispose();
            }
        }

        if (targets.Count == 0)
            return Result(true, false, "no-target", "未检测到正在运行的英雄联盟游戏进程", "affected=0");

        var affected = 0;
        foreach (var processId in targets.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited) continue;
                process.Kill();
                affected++;
            }
            catch
            {
                // Keep the 3.5 behavior: report partial/failed without broadening the process target set.
            }
        }

        return affected > 0
            ? Result(true, true, "success", "已结束英雄联盟游戏进程", "affected=" + affected.ToString(CultureInfo.InvariantCulture))
            : Result(false, false, "failed", "结束英雄联盟游戏进程失败", "affected=0;targets=" + targets.Count.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<double> TryReadZoomAsync(CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _read.TryGetBytesAsync("/riotclient/zoom-scale", cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0) return 1.0;
            var text = Encoding.UTF8.GetString(bytes).Trim().Trim('"');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom) &&
                   zoom is > 0.4 and < 2.1
                ? zoom
                : 1.0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return 1.0;
        }
    }

    private void HandleWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        _ = hook;
        _ = eventType;
        _ = idChild;
        _ = eventThread;
        _ = eventTime;
        if (hwnd == IntPtr.Zero || idObject != ObjidWindow) return;
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _suppressEventsUntilUtcTicks)) return;
        if (!BelongsToLeagueClientWindow(hwnd)) return;
        QueueAutoRepair();
    }

    private void QueueAutoRepair()
    {
        lock (_sync)
        {
            if (_disposed || !_autoRepairEnabled || _debounceTimer is null) return;
            _debounceTimer.Change(AutoDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private async void HandleDebounceTimer(object? state)
    {
        _ = state;
        lock (_sync)
        {
            if (_disposed || !_autoRepairEnabled) return;
        }
        if (Interlocked.Exchange(ref _repairInFlight, 1) != 0) return;
        try
        {
            _ = await RepairWindowAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Event-driven repair is best-effort; manual repair remains available for diagnosis.
        }
        finally
        {
            Interlocked.Exchange(ref _repairInFlight, 0);
        }
    }

    private void StopAutoLocked()
    {
        _autoRepairEnabled = false;
        if (_winEventHook != IntPtr.Zero)
        {
            _ = UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            StopAutoLocked();
        }
    }

    private static WindowHandles TryFindClientWindow()
    {
        var result = new WindowHandles();
        _ = EnumWindows((hwnd, state) =>
        {
            _ = state;
            if (!IsWindowVisible(hwnd) || !ClassEquals(hwnd, "RCLIENT")) return true;
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (!IsLeagueClientUxProcess(pid)) return true;
            result.Main = hwnd;
            return false;
        }, IntPtr.Zero);

        if (result.Main != IntPtr.Zero)
        {
            _ = EnumChildWindows(result.Main, (hwnd, state) =>
            {
                _ = state;
                if (!ClassEquals(hwnd, "CefBrowserWindow")) return true;
                result.Cef = hwnd;
                return false;
            }, IntPtr.Zero);
        }
        return result;
    }

    private static bool BelongsToLeagueClientWindow(IntPtr hwnd)
    {
        var root = GetAncestor(hwnd, GaRoot);
        if (root == IntPtr.Zero) root = hwnd;
        if (!ClassEquals(root, "RCLIENT")) return false;
        _ = GetWindowThreadProcessId(root, out var pid);
        return IsLeagueClientUxProcess(pid);
    }

    private static bool TryGetBounds(IntPtr hwnd, out LeagueWindowBounds bounds)
    {
        if (!GetWindowRect(hwnd, out var native))
        {
            bounds = default;
            return false;
        }
        bounds = new LeagueWindowBounds(
            native.Left,
            native.Top,
            native.Right - native.Left,
            native.Bottom - native.Top);
        return bounds.IsValid;
    }

    private static bool TryGetWorkingArea(IntPtr hwnd, out LeagueWindowBounds workingArea)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            workingArea = default;
            return false;
        }
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            workingArea = default;
            return false;
        }
        workingArea = new LeagueWindowBounds(
            info.Work.Left,
            info.Work.Top,
            info.Work.Right - info.Work.Left,
            info.Work.Bottom - info.Work.Top);
        return workingArea.IsValid;
    }

    private static int GetDpi(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi > 0 ? (int)dpi : 96;
        }
        catch (EntryPointNotFoundException)
        {
            return 96;
        }
        catch
        {
            return 96;
        }
    }

    private static bool ClassEquals(IntPtr hwnd, string expected)
    {
        var builder = new StringBuilder(128);
        return GetClassName(hwnd, builder, builder.Capacity) > 0 &&
               string.Equals(builder.ToString(), expected, StringComparison.Ordinal);
    }

    private static bool IsLeagueClientUxProcess(uint processId)
    {
        if (processId == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "LeagueClientUx", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static LeagueGameRepairResult Result(
        bool success,
        bool changed,
        string state,
        string message,
        string diagnostic) =>
        new(success, changed, state, message, diagnostic);

    private static string Diagnostic(
        LeagueWindowBounds before,
        LeagueWindowBounds after,
        LeagueWindowBounds work,
        int dpi,
        string reason,
        bool cefFound) =>
        "before=" + before.Width + "x" + before.Height + "@" + before.Left + "," + before.Top +
        ";after=" + after.Width + "x" + after.Height + "@" + after.Left + "," + after.Top +
        ";work=" + work.Width + "x" + work.Height + "@" + work.Left + "," + work.Top +
        ";dpi=" + dpi.ToString(CultureInfo.InvariantCulture) +
        ";reason=" + reason +
        ";cef=" + (cefFound ? "found" : "missing");

    private sealed class WindowHandles
    {
        public IntPtr Main;
        public IntPtr Cef;
    }

    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr state);
    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime);

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
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowProc callback, IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr state);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHookAssembly,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
