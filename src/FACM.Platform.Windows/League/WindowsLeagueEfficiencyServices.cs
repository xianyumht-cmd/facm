using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FACM.Core.League;

namespace FACM.Platform.Windows.League;

internal sealed record WindowsLeagueProcessSnapshot(int Id, string Name);

internal interface IWindowsLeagueProcessController
{
    IReadOnlyList<WindowsLeagueProcessSnapshot> GetProcesses();
    bool TryKillIfStillMatches(int processId, IReadOnlySet<string> allowedNames);
}

internal sealed class WindowsLeagueProcessController : IWindowsLeagueProcessController
{
    public IReadOnlyList<WindowsLeagueProcessSnapshot> GetProcesses()
    {
        var result = new List<WindowsLeagueProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                result.Add(new WindowsLeagueProcessSnapshot(process.Id, process.ProcessName ?? string.Empty));
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        return result;
    }

    public bool TryKillIfStillMatches(int processId, IReadOnlySet<string> allowedNames)
    {
        if (processId <= 0 || allowedNames is null || allowedNames.Count == 0) return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited || !allowedNames.Contains(process.ProcessName ?? string.Empty)) return false;
            process.Kill();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// FACM 3.5.15 efficiency process actions, kept behind a Windows-only narrow adapter. The process
/// name is checked again immediately before termination to avoid a PID-reuse escape.
/// </summary>
public sealed class WindowsLeagueEfficiencyActionService : ILeagueEfficiencyActionService
{
    internal static readonly IReadOnlySet<string> GameProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "League of Legends(TM)",
        "League of Legends"
    };

    internal static readonly IReadOnlySet<string> LobbyProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "LeagueClient",
        "LeagueClientUx",
        "LeagueClientUxRender"
    };

    private readonly IWindowsLeagueProcessController _processes;

    public WindowsLeagueEfficiencyActionService()
        : this(new WindowsLeagueProcessController())
    {
    }

    internal WindowsLeagueEfficiencyActionService(IWindowsLeagueProcessController processes)
    {
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
    }

    public Task<LeagueEfficiencyActionResult> ExitGameAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(KillTargets(GameProcessNames, "game-not-running", "game-exit", cancellationToken));

    public Task<LeagueEfficiencyActionResult> CloseLobbyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(KillTargets(LobbyProcessNames, "lobby-not-running", "lobby-exit", cancellationToken));

    private LeagueEfficiencyActionResult KillTargets(
        IReadOnlySet<string> allowedNames,
        string noTargetDetail,
        string successDetail,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targets = (_processes.GetProcesses() ?? Array.Empty<WindowsLeagueProcessSnapshot>())
            .Where(process => process is not null && process.Id > 0 && allowedNames.Contains(process.Name ?? string.Empty))
            .GroupBy(process => process.Id)
            .Select(group => group.First())
            .ToArray();

        if (targets.Length == 0)
            return new LeagueEfficiencyActionResult("no-target", noTargetDetail, 0);

        var affected = 0;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_processes.TryKillIfStillMatches(target.Id, allowedNames)) affected++;
        }

        return new LeagueEfficiencyActionResult(
            affected > 0 ? "success" : "failed",
            successDetail,
            affected);
    }
}

/// <summary>
/// Win32 RegisterHotKey owner on one dedicated message thread. It mirrors the FACM 3.5 lifecycle
/// without bringing WinForms into FACM 4.0. Registration changes are transactional: duplicate,
/// unsupported or occupied bindings restore the previous set.
/// </summary>
public sealed class WindowsLeagueGlobalHotkeyService : ILeagueGlobalHotkeyService
{
    private const uint WmHotkey = 0x0312;
    private const uint WmApply = 0x8001;
    private const uint WmShutdown = 0x8002;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndMessage = new(-3);

    private static readonly IReadOnlyDictionary<LeagueEfficiencyAction, int> ActionIds =
        new Dictionary<LeagueEfficiencyAction, int>
        {
            [LeagueEfficiencyAction.ExitGame] = 0x5A11,
            [LeagueEfficiencyAction.CloseLobby] = 0x5A12
        };

    private readonly object _sync = new();
    private readonly ConcurrentQueue<ApplyRequest> _requests = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private readonly string _className = "FACM.LeagueEfficiency.GlobalHotkeys." + Guid.NewGuid().ToString("N");
    private readonly WndProcDelegate _wndProc;
    private Dictionary<LeagueEfficiencyAction, LeagueHotkeyBinding> _active = new();
    private IntPtr _window;
    private Exception? _startupError;
    private bool _disposed;

    public WindowsLeagueGlobalHotkeyService()
    {
        _wndProc = WindowProc;
        _thread = new Thread(MessageThreadMain)
        {
            IsBackground = true,
            Name = "FACM.LeagueEfficiency.Hotkeys"
        };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("FACM 全局快捷键线程启动超时。");
        if (_startupError is not null)
            throw new InvalidOperationException("FACM 全局快捷键线程启动失败。", _startupError);
    }

    public event EventHandler<LeagueGlobalHotkeyPressedEventArgs>? HotkeyPressed;

    public bool TryApply(
        IReadOnlyDictionary<LeagueEfficiencyAction, LeagueHotkeyBinding> bindings,
        out string error)
    {
        ApplyRequest request;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_window == IntPtr.Zero)
            {
                error = "全局快捷键接收器尚未就绪。";
                return false;
            }

            request = new ApplyRequest(bindings);
            _requests.Enqueue(request);
            if (!PostMessageW(_window, WmApply, IntPtr.Zero, IntPtr.Zero))
            {
                error = "无法通知全局快捷键接收器。";
                return false;
            }
        }

        if (!request.Done.Wait(TimeSpan.FromSeconds(5)))
        {
            error = "全局快捷键设置超时。";
            return false;
        }

        error = request.Error;
        return request.Success;
    }

    internal static bool UsesDedicatedMessageThreadForSmokeTest() => true;

    internal static bool TryResolveVirtualKeyForSmokeTest(string key, out uint virtualKey) =>
        TryResolveVirtualKey(key, out virtualKey);

    private void MessageThreadMain()
    {
        ushort atom = 0;
        try
        {
            var module = GetModuleHandleW(null);
            var windowClass = new WNDCLASSW
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = module,
                lpszClassName = _className
            };
            atom = RegisterClassW(ref windowClass);
            if (atom == 0) throw new InvalidOperationException("RegisterClassW failed: " + Marshal.GetLastWin32Error());

            _window = CreateWindowExW(
                0,
                _className,
                "FACM.LeagueEfficiency.GlobalHotkeys",
                0,
                0,
                0,
                0,
                0,
                HwndMessage,
                IntPtr.Zero,
                module,
                IntPtr.Zero);
            if (_window == IntPtr.Zero)
                throw new InvalidOperationException("CreateWindowExW failed: " + Marshal.GetLastWin32Error());

            _ready.Set();
            while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch (Exception exception)
        {
            _startupError = exception;
            _ready.Set();
        }
        finally
        {
            try { UnregisterAll(_active); } catch { }
            _active.Clear();
            if (_window != IntPtr.Zero)
            {
                try { DestroyWindow(_window); } catch { }
                _window = IntPtr.Zero;
            }
            if (atom != 0)
            {
                try { UnregisterClassW(_className, GetModuleHandleW(null)); } catch { }
            }
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey)
        {
            var id = wParam.ToInt32();
            foreach (var pair in ActionIds)
            {
                if (pair.Value != id) continue;
                try { HotkeyPressed?.Invoke(this, new LeagueGlobalHotkeyPressedEventArgs(pair.Key)); }
                catch { }
                break;
            }
            return IntPtr.Zero;
        }

        if (message == WmApply)
        {
            ApplyPending(hwnd);
            return IntPtr.Zero;
        }

        if (message == WmShutdown)
        {
            UnregisterAll(_active);
            _active.Clear();
            DestroyWindow(hwnd);
            _window = IntPtr.Zero;
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void ApplyPending(IntPtr hwnd)
    {
        while (_requests.TryDequeue(out var request))
        {
            try
            {
                request.Success = TryApplyOnMessageThread(hwnd, request.Bindings, out var error);
                request.Error = error;
            }
            catch (Exception exception)
            {
                request.Success = false;
                request.Error = exception.Message;
            }
            finally
            {
                request.Done.Set();
            }
        }
    }

    private bool TryApplyOnMessageThread(
        IntPtr hwnd,
        IReadOnlyDictionary<LeagueEfficiencyAction, LeagueHotkeyBinding> requested,
        out string error)
    {
        error = string.Empty;
        var normalized = new Dictionary<LeagueEfficiencyAction, LeagueHotkeyBinding>();
        foreach (var action in ActionIds.Keys)
        {
            normalized[action] = requested is not null && requested.TryGetValue(action, out var binding) && binding is not null
                ? binding
                : LeagueHotkeyBinding.Disabled;
        }

        var duplicate = normalized
            .Where(pair => pair.Value.Enabled)
            .GroupBy(pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            error = "快捷键冲突：" + duplicate.Key;
            return false;
        }

        foreach (var pair in normalized.Where(pair => pair.Value.Enabled))
        {
            if (!TryResolveVirtualKey(pair.Value.Key, out _))
            {
                error = "无法识别按键：" + pair.Value.Key;
                return false;
            }
        }

        var previous = new Dictionary<LeagueEfficiencyAction, LeagueHotkeyBinding>(_active);
        UnregisterAll(_active);
        var registered = new Dictionary<LeagueEfficiencyAction, LeagueHotkeyBinding>();
        foreach (var pair in normalized)
        {
            if (!pair.Value.Enabled) continue;
            TryResolveVirtualKey(pair.Value.Key, out var key);
            var modifiers = (uint)pair.Value.Modifiers | ModNoRepeat;
            if (!RegisterHotKey(hwnd, ActionIds[pair.Key], modifiers, key))
            {
                UnregisterAll(registered);
                Restore(hwnd, previous);
                error = "快捷键被系统或其它程序占用：" + pair.Value;
                return false;
            }
            registered[pair.Key] = pair.Value;
        }

        _active = normalized;
        return true;
    }

    private void Restore(IntPtr hwnd, IReadOnlyDictionary<LeagueEfficiencyAction, LeagueHotkeyBinding> previous)
    {
        var restored = new Dictionary<LeagueEfficiencyAction, LeagueHotkeyBinding>();
        foreach (var pair in previous)
        {
            if (!pair.Value.Enabled || !TryResolveVirtualKey(pair.Value.Key, out var key)) continue;
            if (RegisterHotKey(hwnd, ActionIds[pair.Key], (uint)pair.Value.Modifiers | ModNoRepeat, key))
                restored[pair.Key] = pair.Value;
        }
        _active = restored;
    }

    private void UnregisterAll(IReadOnlyDictionary<LeagueEfficiencyAction, LeagueHotkeyBinding> bindings)
    {
        if (_window == IntPtr.Zero) return;
        foreach (var pair in bindings)
        {
            if (!pair.Value.Enabled) continue;
            try { UnregisterHotKey(_window, ActionIds[pair.Key]); } catch { }
        }
    }

    private static bool TryResolveVirtualKey(string key, out uint virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (key.Length == 1)
        {
            var ch = char.ToUpperInvariant(key[0]);
            if (ch is >= 'A' and <= 'Z' || ch is >= '0' and <= '9')
            {
                virtualKey = ch;
                return true;
            }
        }
        if (key.StartsWith('F') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + f - 1);
            return true;
        }
        if (key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && key.Length == 7 && char.IsDigit(key[6]))
        {
            virtualKey = (uint)(0x60 + key[6] - '0');
            return true;
        }

        var known = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backspace"] = 0x08,
            ["Tab"] = 0x09,
            ["Enter"] = 0x0D,
            ["Pause"] = 0x13,
            ["CapsLock"] = 0x14,
            ["Escape"] = 0x1B,
            ["Space"] = 0x20,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["End"] = 0x23,
            ["Home"] = 0x24,
            ["Left"] = 0x25,
            ["Up"] = 0x26,
            ["Right"] = 0x27,
            ["Down"] = 0x28,
            ["PrintScreen"] = 0x2C,
            ["Insert"] = 0x2D,
            ["Delete"] = 0x2E,
            ["Multiply"] = 0x6A,
            ["Add"] = 0x6B,
            ["Subtract"] = 0x6D,
            ["Decimal"] = 0x6E,
            ["Divide"] = 0x6F,
            ["NumLock"] = 0x90,
            ["ScrollLock"] = 0x91
        };
        return known.TryGetValue(key, out virtualKey);
    }

    public void Dispose()
    {
        IntPtr handle;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            handle = _window;
        }

        if (handle != IntPtr.Zero) PostMessageW(handle, WmShutdown, IntPtr.Zero, IntPtr.Zero);
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(3));
        _ready.Dispose();
        HotkeyPressed = null;
    }

    private sealed class ApplyRequest
    {
        public ApplyRequest(IReadOnlyDictionary<LeagueEfficiencyAction, LeagueHotkeyBinding> bindings)
        {
            Bindings = bindings is null
                ? new Dictionary<LeagueEfficiencyAction, LeagueHotkeyBinding>()
                : new Dictionary<LeagueEfficiencyAction, LeagueHotkeyBinding>(bindings);
        }

        public IReadOnlyDictionary<LeagueEfficiencyAction, LeagueHotkeyBinding> Bindings { get; }
        public ManualResetEventSlim Done { get; } = new(false);
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int width,
        int height,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessageW(out MSG message, IntPtr hwnd, uint minFilter, uint maxFilter);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}
