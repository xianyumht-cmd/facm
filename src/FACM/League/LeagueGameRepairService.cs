using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using ThreadingTimer = System.Threading.Timer;

namespace FACM.League
{
    internal sealed class LeagueGameRepairResult
    {
        public bool Success { get; set; }
        public bool Changed { get; set; }
        public string State { get; set; }
        public string Message { get; set; }
        public string Diagnostic { get; set; }
    }

    internal sealed class LeagueWindowRepairPlan
    {
        public bool CurrentIsSane { get; set; }
        public Rectangle TargetBounds { get; set; }
        public string Reason { get; set; }
    }

    internal static class LeagueWindowRepairPlanner
    {
        private const double TargetAspect = 16.0 / 9.0;
        private const double AspectTolerance = 0.045;

        internal static bool IsSane(Rectangle bounds, Rectangle workingArea)
        {
            if (bounds.Width < 640 || bounds.Height < 360) return false;
            if (bounds.Width > workingArea.Width * 1.08 || bounds.Height > workingArea.Height * 1.08) return false;
            var aspect = bounds.Width / (double)Math.Max(1, bounds.Height);
            if (Math.Abs(aspect - TargetAspect) > AspectTolerance) return false;
            var visible = Rectangle.Intersect(bounds, workingArea);
            if (visible.Width <= 0 || visible.Height <= 0) return false;
            var visibleArea = (long)visible.Width * visible.Height;
            var totalArea = (long)bounds.Width * bounds.Height;
            return totalArea > 0 && visibleArea >= totalArea / 4;
        }

        internal static LeagueWindowRepairPlan Plan(
            Rectangle current,
            Rectangle workingArea,
            Size? rememberedSaneSize,
            double zoom)
        {
            if (workingArea.Width <= 0 || workingArea.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(workingArea));

            if (IsSane(current, workingArea))
            {
                return new LeagueWindowRepairPlan
                {
                    CurrentIsSane = true,
                    TargetBounds = ClampPosition(current, workingArea),
                    Reason = current == ClampPosition(current, workingArea) ? "already-sane" : "offscreen"
                };
            }

            Size target;
            string reason;
            if (rememberedSaneSize.HasValue && IsSaneSize(rememberedSaneSize.Value, workingArea))
            {
                target = Fit(rememberedSaneSize.Value, workingArea);
                reason = "remembered-sane-size";
            }
            else if (CanUseWidth(current.Width, workingArea))
            {
                target = Fit(new Size(current.Width, (int)Math.Round(current.Width / TargetAspect)), workingArea);
                reason = "preserve-current-width";
            }
            else if (CanUseHeight(current.Height, workingArea))
            {
                target = Fit(new Size((int)Math.Round(current.Height * TargetAspect), current.Height), workingArea);
                reason = "preserve-current-height";
            }
            else
            {
                var safeZoom = zoom > 0.4 && zoom < 2.1 ? zoom : 1.0;
                var zoomWidth = (int)Math.Round(1280 * safeZoom);
                var monitorWidth = (int)Math.Round(workingArea.Width * 0.78);
                var width = Math.Max(zoomWidth, monitorWidth);
                target = Fit(new Size(width, (int)Math.Round(width / TargetAspect)), workingArea);
                reason = "monitor-fallback";
            }

            var x = current.Width > 0 && current.Height > 0 && current.IntersectsWith(workingArea)
                ? current.Left
                : workingArea.Left + (workingArea.Width - target.Width) / 2;
            var y = current.Width > 0 && current.Height > 0 && current.IntersectsWith(workingArea)
                ? current.Top
                : workingArea.Top + (workingArea.Height - target.Height) / 2;

            var planned = ClampPosition(new Rectangle(x, y, target.Width, target.Height), workingArea);
            return new LeagueWindowRepairPlan
            {
                CurrentIsSane = false,
                TargetBounds = planned,
                Reason = reason
            };
        }

        private static bool IsSaneSize(Size size, Rectangle workingArea)
        {
            if (size.Width < 640 || size.Height < 360) return false;
            if (size.Width > workingArea.Width || size.Height > workingArea.Height) return false;
            return Math.Abs(size.Width / (double)Math.Max(1, size.Height) - TargetAspect) <= AspectTolerance;
        }

        private static bool CanUseWidth(int width, Rectangle workingArea)
        {
            if (width < 640 || width > workingArea.Width) return false;
            var height = (int)Math.Round(width / TargetAspect);
            return height >= 360 && height <= workingArea.Height;
        }

        private static bool CanUseHeight(int height, Rectangle workingArea)
        {
            if (height < 360 || height > workingArea.Height) return false;
            var width = (int)Math.Round(height * TargetAspect);
            return width >= 640 && width <= workingArea.Width;
        }

        private static Size Fit(Size requested, Rectangle workingArea)
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
            return new Size(width, height);
        }

        private static Rectangle ClampPosition(Rectangle bounds, Rectangle workingArea)
        {
            var width = Math.Min(bounds.Width, workingArea.Width);
            var height = Math.Min(bounds.Height, workingArea.Height);
            var maxX = workingArea.Right - width;
            var maxY = workingArea.Bottom - height;
            var x = Math.Max(workingArea.Left, Math.Min(bounds.Left, maxX));
            var y = Math.Max(workingArea.Top, Math.Min(bounds.Top, maxY));
            return new Rectangle(x, y, width, height);
        }
    }

    /// <summary>
    /// FACM-native replacement for the legacy Fix-LCU-Window executable. Window work is local Win32;
    /// all LCU reads/writes are injected from LeagueClientModule so no second discovery/auth stack exists.
    /// Auto repair uses WinEvent location notifications with debounce/cooldown instead of a permanent
    /// 1500ms polling process.
    /// </summary>
    internal sealed class LeagueGameRepairService : IDisposable
    {
        private const uint EventObjectLocationChange = 0x800B;
        private const uint WineventOutOfContext = 0x0000;
        private const uint WineventSkipOwnProcess = 0x0002;
        private const int ObjidWindow = 0;
        private const uint GaRoot = 2;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private static readonly TimeSpan AutoDebounce = TimeSpan.FromMilliseconds(380);
        private static readonly TimeSpan AutoCooldown = TimeSpan.FromSeconds(2);

        private readonly object _sync = new object();
        private readonly ILeagueClientApi _read;
        private readonly ILeaguePostGameWriteApi _postGameWrite;
        private readonly ILeagueClientUxRepairWriteApi _uxRepairWrite;
        private readonly WinEventDelegate _winEventDelegate;
        private ThreadingTimer _debounceTimer;
        private IntPtr _winEventHook;
        private Size? _lastSaneSize;
        private long _suppressEventsUntilUtcTicks;
        private int _repairInFlight;
        private bool _autoRepairEnabled;
        private bool _disposed;

        public LeagueGameRepairService(
            ILeagueClientApi read,
            ILeaguePostGameWriteApi postGameWrite,
            ILeagueClientUxRepairWriteApi uxRepairWrite)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _postGameWrite = postGameWrite ?? throw new ArgumentNullException(nameof(postGameWrite));
            _uxRepairWrite = uxRepairWrite ?? throw new ArgumentNullException(nameof(uxRepairWrite));
            _winEventDelegate = HandleWinEvent;
        }

        public bool AutoRepairEnabled
        {
            get { lock (_sync) return _autoRepairEnabled && !_disposed; }
        }

        public async Task<LeagueGameRepairResult> RepairWindowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handles = LeagueWindowNative.TryFindClientWindow();
            if (handles.Main == IntPtr.Zero)
                return Result(false, false, "no-client", "未检测到英雄联盟客户端窗口", "window=missing");
            if (LeagueWindowNative.IsMinimized(handles.Main))
                return Result(true, false, "minimized", "客户端窗口已最小化，本次不调整", "window=minimized");

            Rectangle current;
            if (!LeagueWindowNative.TryGetBounds(handles.Main, out current))
                return Result(false, false, "window-read-failed", "读取客户端窗口位置失败", "GetWindowRect=false");

            var screen = Screen.FromHandle(handles.Main);
            var workingArea = screen == null ? Screen.PrimaryScreen.WorkingArea : screen.WorkingArea;
            var dpi = LeagueWindowNative.GetDpi(handles.Main);
            var currentSane = LeagueWindowRepairPlanner.IsSane(current, workingArea);
            if (currentSane)
            {
                lock (_sync) _lastSaneSize = current.Size;
            }

            var cefSane = true;
            Rectangle cefBounds;
            if (handles.Cef != IntPtr.Zero && LeagueWindowNative.TryGetBounds(handles.Cef, out cefBounds))
            {
                cefSane = Math.Abs(cefBounds.Width - current.Width) <= 6 && Math.Abs(cefBounds.Height - current.Height) <= 6;
            }

            var clampedCurrent = LeagueWindowRepairPlanner.Plan(current, workingArea, current.Size, 1.0);
            var positionNeedsRepair = clampedCurrent.TargetBounds.Location != current.Location;
            if (currentSane && cefSane && !positionNeedsRepair)
            {
                return Result(true, false, "healthy", "客户端窗口尺寸正常，无需修复",
                    Diagnostic(current, current, workingArea, dpi, "already-sane", handles.Cef != IntPtr.Zero));
            }

            var zoom = 1.0;
            if (!currentSane)
                zoom = await TryReadZoomAsync(cancellationToken).ConfigureAwait(false);

            Size? remembered;
            lock (_sync) remembered = _lastSaneSize;
            var plan = LeagueWindowRepairPlanner.Plan(current, workingArea, remembered, zoom);
            var target = plan.TargetBounds;

            Interlocked.Exchange(ref _suppressEventsUntilUtcTicks, DateTime.UtcNow.Add(AutoCooldown).Ticks);
            if (!LeagueWindowNative.SetBounds(handles.Main, target.Left, target.Top, target.Width, target.Height, SwpNoZOrder | SwpNoActivate))
                return Result(false, false, "window-write-failed", "修复客户端窗口失败，请查看日志",
                    Diagnostic(current, target, workingArea, dpi, plan.Reason, handles.Cef != IntPtr.Zero));

            if (handles.Cef != IntPtr.Zero)
                LeagueWindowNative.SetBounds(handles.Cef, 0, 0, target.Width, target.Height, SwpNoZOrder | SwpNoActivate);

            lock (_sync) _lastSaneSize = target.Size;
            var changed = current != target || !cefSane;
            var diagnostic = Diagnostic(current, target, workingArea, dpi, plan.Reason, handles.Cef != IntPtr.Zero);
            AppLog.Info("League native window repair: changed=" + changed + "; " + diagnostic);
            return Result(true, changed, changed ? "repaired" : "healthy",
                changed ? "客户端窗口已恢复到合理尺寸" : "客户端窗口尺寸正常，无需修复",
                diagnostic);
        }

        public async Task<LeagueGameRepairResult> SkipSettlementAsync(CancellationToken cancellationToken)
        {
            var response = await _postGameWrite.TrySendAsync(
                "POST",
                LeaguePostGameWriteApiClient.PlayAgainPath,
                null,
                cancellationToken).ConfigureAwait(false);
            var success = response != null && response.IsSuccessStatusCode;
            var code = response == null ? 0 : response.StatusCode;
            AppLog.Info("League manual skip settlement: success=" + success + "; http=" + code);
            return Result(success, success, success ? "success" : "failed",
                success ? "已向客户端发送跳过结算指令" : "跳过结算失败，请确认客户端已连接",
                "route=play-again;http=" + code);
        }

        public async Task<LeagueGameRepairResult> RestartClientUxAsync(CancellationToken cancellationToken)
        {
            var response = await _uxRepairWrite.TryRestartUxAsync(cancellationToken).ConfigureAwait(false);
            var success = response != null && response.IsSuccessStatusCode;
            var code = response == null ? 0 : response.StatusCode;
            AppLog.Info("League manual restart client UX: success=" + success + "; http=" + code);
            return Result(success, success, success ? "success" : "failed",
                success ? "已请求重启客户端界面" : "重启客户端界面失败，请确认客户端已连接",
                "route=kill-and-restart-ux;http=" + code);
        }

        public LeagueGameRepairResult SetAutoRepairEnabled(bool enabled)
        {
            lock (_sync)
            {
                if (_disposed) return Result(false, false, "disposed", "游戏修复服务已停止", "disposed=true");
                if (_autoRepairEnabled == enabled)
                    return Result(true, false, enabled ? "auto-on" : "auto-off",
                        enabled ? "自动修复窗口已开启" : "自动修复窗口已关闭", "unchanged=true");

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
                    return Result(false, false, "hook-failed", "自动修复窗口启动失败，请查看日志", "SetWinEventHook=0");

                _debounceTimer = new ThreadingTimer(HandleDebounceTimer, null, Timeout.Infinite, Timeout.Infinite);
                _autoRepairEnabled = true;
            }

            QueueAutoRepair();
            AppLog.Info("League native auto window repair enabled; event-driven=true; debounceMs=" + (int)AutoDebounce.TotalMilliseconds);
            return Result(true, true, "auto-on", "自动修复窗口已开启", "event-driven=true");
        }

        private async Task<double> TryReadZoomAsync(CancellationToken cancellationToken)
        {
            try
            {
                var bytes = await _read.TryGetBytesAsync("/riotclient/zoom-scale", cancellationToken).ConfigureAwait(false);
                if (bytes == null || bytes.Length == 0) return 1.0;
                var text = Encoding.UTF8.GetString(bytes).Trim().Trim('"');
                double zoom;
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out zoom) && zoom > 0.4 && zoom < 2.1
                    ? zoom
                    : 1.0;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                AppLog.Info("League repair zoom read skipped: " + exception.Message);
                return 1.0;
            }
        }

        private void HandleWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
        {
            if (hwnd == IntPtr.Zero || idObject != ObjidWindow) return;
            if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _suppressEventsUntilUtcTicks)) return;
            if (!LeagueWindowNative.BelongsToLeagueClientWindow(hwnd)) return;
            QueueAutoRepair();
        }

        private void QueueAutoRepair()
        {
            lock (_sync)
            {
                if (_disposed || !_autoRepairEnabled || _debounceTimer == null) return;
                _debounceTimer.Change(AutoDebounce, Timeout.InfiniteTimeSpan);
            }
        }

        private async void HandleDebounceTimer(object state)
        {
            lock (_sync)
            {
                if (_disposed || !_autoRepairEnabled) return;
            }
            if (Interlocked.Exchange(ref _repairInFlight, 1) != 0) return;
            try
            {
                var result = await RepairWindowAsync(CancellationToken.None).ConfigureAwait(false);
                if (result != null && result.Changed)
                    AppLog.Info("League native auto repair applied: " + result.Diagnostic);
            }
            catch (Exception exception)
            {
                AppLog.Info("League native auto repair skipped: " + exception.Message);
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
                UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
            if (_debounceTimer != null)
            {
                _debounceTimer.Dispose();
                _debounceTimer = null;
            }
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

        private static LeagueGameRepairResult Result(bool success, bool changed, string state, string message, string diagnostic)
        {
            return new LeagueGameRepairResult
            {
                Success = success,
                Changed = changed,
                State = state,
                Message = message,
                Diagnostic = diagnostic
            };
        }

        private static string Diagnostic(Rectangle before, Rectangle after, Rectangle work, int dpi, string reason, bool cefFound)
        {
            return "before=" + before.Width + "x" + before.Height + "@" + before.Left + "," + before.Top +
                   ";after=" + after.Width + "x" + after.Height + "@" + after.Left + "," + after.Top +
                   ";work=" + work.Width + "x" + work.Height + "@" + work.Left + "," + work.Top +
                   ";dpi=" + dpi + ";reason=" + reason + ";cef=" + (cefFound ? "found" : "missing");
        }

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr eventHookAssembly, WinEventDelegate callback, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hook);

        private static class LeagueWindowNative
        {
            internal sealed class WindowHandles
            {
                public IntPtr Main;
                public IntPtr Cef;
            }

            internal static WindowHandles TryFindClientWindow()
            {
                var result = new WindowHandles();
                EnumWindows(delegate(IntPtr hwnd, IntPtr state)
                {
                    if (!IsWindowVisible(hwnd) || !ClassEquals(hwnd, "RCLIENT")) return true;
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (!IsLeagueClientUxProcess(pid)) return true;
                    result.Main = hwnd;
                    return false;
                }, IntPtr.Zero);

                if (result.Main != IntPtr.Zero)
                {
                    EnumChildWindows(result.Main, delegate(IntPtr hwnd, IntPtr state)
                    {
                        if (!ClassEquals(hwnd, "CefBrowserWindow")) return true;
                        result.Cef = hwnd;
                        return false;
                    }, IntPtr.Zero);
                }
                return result;
            }

            internal static bool BelongsToLeagueClientWindow(IntPtr hwnd)
            {
                var root = GetAncestor(hwnd, GaRoot);
                if (root == IntPtr.Zero) root = hwnd;
                if (!ClassEquals(root, "RCLIENT")) return false;
                uint pid;
                GetWindowThreadProcessId(root, out pid);
                return IsLeagueClientUxProcess(pid);
            }

            internal static bool IsMinimized(IntPtr hwnd) { return IsIconic(hwnd); }

            internal static bool TryGetBounds(IntPtr hwnd, out Rectangle rectangle)
            {
                NativeRect rect;
                if (!GetWindowRect(hwnd, out rect))
                {
                    rectangle = Rectangle.Empty;
                    return false;
                }
                rectangle = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                return rectangle.Width > 0 && rectangle.Height > 0;
            }

            internal static bool SetBounds(IntPtr hwnd, int x, int y, int width, int height, uint flags)
            {
                return SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, flags);
            }

            internal static int GetDpi(IntPtr hwnd)
            {
                try
                {
                    var dpi = GetDpiForWindow(hwnd);
                    return dpi > 0 ? (int)dpi : 96;
                }
                catch (EntryPointNotFoundException) { return 96; }
                catch { return 96; }
            }

            private static bool ClassEquals(IntPtr hwnd, string expected)
            {
                var builder = new StringBuilder(128);
                return GetClassName(hwnd, builder, builder.Capacity) > 0 &&
                       string.Equals(builder.ToString(), expected, StringComparison.Ordinal);
            }

            private static bool IsLeagueClientUxProcess(uint pid)
            {
                if (pid == 0) return false;
                try
                {
                    using (var process = Process.GetProcessById((int)pid))
                        return string.Equals(process.ProcessName, "LeagueClientUx", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }

            private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr state);

            [StructLayout(LayoutKind.Sequential)]
            private struct NativeRect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
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
            private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

            [DllImport("user32.dll")]
            private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

            [DllImport("user32.dll")]
            private static extern uint GetDpiForWindow(IntPtr hwnd);
        }
    }
}
