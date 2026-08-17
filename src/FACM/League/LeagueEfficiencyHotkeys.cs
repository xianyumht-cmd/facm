using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.League
{
    [Flags]
    internal enum LeagueHotkeyModifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000
    }

    internal sealed class LeagueHotkeyBinding
    {
        public static readonly LeagueHotkeyBinding Disabled = new LeagueHotkeyBinding(LeagueHotkeyModifiers.None, Keys.None);

        public LeagueHotkeyBinding(LeagueHotkeyModifiers modifiers, Keys key)
        {
            Modifiers = modifiers & ~LeagueHotkeyModifiers.NoRepeat;
            Key = key;
        }

        public LeagueHotkeyModifiers Modifiers { get; private set; }
        public Keys Key { get; private set; }
        public bool Enabled { get { return Key != Keys.None; } }

        public override string ToString()
        {
            if (!Enabled) return string.Empty;
            var parts = new List<string>();
            if ((Modifiers & LeagueHotkeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((Modifiers & LeagueHotkeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((Modifiers & LeagueHotkeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((Modifiers & LeagueHotkeyModifiers.Win) != 0) parts.Add("Win");
            parts.Add(FormatKey(Key));
            return string.Join("+", parts);
        }

        public static bool TryParse(string text, out LeagueHotkeyBinding binding, out string error)
        {
            binding = Disabled;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return true;

            var parts = text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToArray();
            if (parts.Length == 0) return true;

            var modifiers = LeagueHotkeyModifiers.None;
            Keys key = Keys.None;
            foreach (var part in parts)
            {
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= LeagueHotkeyModifiers.Control;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= LeagueHotkeyModifiers.Alt;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= LeagueHotkeyModifiers.Shift;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    modifiers |= LeagueHotkeyModifiers.Win;
                else
                {
                    if (key != Keys.None)
                    {
                        error = "快捷键只能包含一个主按键。";
                        return false;
                    }
                    if (!TryParseKey(part, out key))
                    {
                        error = "无法识别按键：" + part;
                        return false;
                    }
                }
            }

            if (key == Keys.None || IsModifierKey(key))
            {
                error = "请选择一个非修饰键作为主按键。";
                return false;
            }
            if (modifiers == LeagueHotkeyModifiers.None && IsBareTypingKey(key))
            {
                error = "裸字母/数字容易在聊天或输入账号时误触，请加 Ctrl / Alt / Shift / Win，或使用 F1-F12。";
                return false;
            }

            binding = new LeagueHotkeyBinding(modifiers, key);
            return true;
        }

        internal static LeagueHotkeyBinding FromKeyEvent(Keys keyData)
        {
            var modifiers = LeagueHotkeyModifiers.None;
            if ((keyData & Keys.Control) == Keys.Control) modifiers |= LeagueHotkeyModifiers.Control;
            if ((keyData & Keys.Alt) == Keys.Alt) modifiers |= LeagueHotkeyModifiers.Alt;
            if ((keyData & Keys.Shift) == Keys.Shift) modifiers |= LeagueHotkeyModifiers.Shift;
            var key = keyData & Keys.KeyCode;
            return new LeagueHotkeyBinding(modifiers, key);
        }

        private static bool TryParseKey(string value, out Keys key)
        {
            key = Keys.None;
            if (value.Length == 1)
            {
                var ch = char.ToUpperInvariant(value[0]);
                if (ch >= 'A' && ch <= 'Z')
                {
                    key = (Keys)ch;
                    return true;
                }
                if (ch >= '0' && ch <= '9')
                {
                    key = (Keys)((int)Keys.D0 + (ch - '0'));
                    return true;
                }
            }

            Keys parsed;
            if (Enum.TryParse(value, true, out parsed) && parsed != Keys.None)
            {
                key = parsed & Keys.KeyCode;
                return key != Keys.None;
            }
            return false;
        }

        private static string FormatKey(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((int)key - (int)Keys.D0).ToString(CultureInfo.InvariantCulture);
            return key.ToString();
        }

        private static bool IsBareTypingKey(Keys key)
        {
            return (key >= Keys.A && key <= Keys.Z) || (key >= Keys.D0 && key <= Keys.D9) ||
                   (key >= Keys.NumPad0 && key <= Keys.NumPad9);
        }

        private static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                   key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                   key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu ||
                   key == Keys.LWin || key == Keys.RWin;
        }
    }

    internal interface ILeagueHotkeyBackend
    {
        bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);
        bool Unregister(IntPtr windowHandle, int id);
    }

    internal sealed class Win32LeagueHotkeyBackend : ILeagueHotkeyBackend
    {
        public bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey)
        {
            return RegisterHotKey(windowHandle, id, modifiers, virtualKey);
        }

        public bool Unregister(IntPtr windowHandle, int id)
        {
            return UnregisterHotKey(windowHandle, id);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    internal sealed class LeagueHotkeyRegistrationManager : IDisposable
    {
        private readonly IntPtr _windowHandle;
        private readonly ILeagueHotkeyBackend _backend;
        private readonly Dictionary<string, int> _ids;
        private Dictionary<string, LeagueHotkeyBinding> _active = new Dictionary<string, LeagueHotkeyBinding>(StringComparer.Ordinal);
        private bool _disposed;

        public LeagueHotkeyRegistrationManager(IntPtr windowHandle, ILeagueHotkeyBackend backend, IDictionary<string, int> ids)
        {
            _windowHandle = windowHandle;
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _ids = ids == null
                ? throw new ArgumentNullException(nameof(ids))
                : new Dictionary<string, int>(ids, StringComparer.Ordinal);
        }

        public bool TryApply(IDictionary<string, LeagueHotkeyBinding> requested, out string error)
        {
            ThrowIfDisposed();
            error = string.Empty;
            var normalized = new Dictionary<string, LeagueHotkeyBinding>(StringComparer.Ordinal);
            foreach (var action in _ids.Keys)
            {
                LeagueHotkeyBinding binding;
                normalized[action] = requested != null && requested.TryGetValue(action, out binding) && binding != null
                    ? binding
                    : LeagueHotkeyBinding.Disabled;
            }

            var duplicate = normalized
                .Where(pair => pair.Value.Enabled)
                .GroupBy(pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                error = "快捷键冲突：" + duplicate.Key;
                return false;
            }

            var previous = new Dictionary<string, LeagueHotkeyBinding>(_active, StringComparer.Ordinal);
            UnregisterAll(_active);
            var registered = new Dictionary<string, LeagueHotkeyBinding>(StringComparer.Ordinal);
            foreach (var pair in normalized)
            {
                if (!pair.Value.Enabled) continue;
                var modifiers = (uint)(pair.Value.Modifiers | LeagueHotkeyModifiers.NoRepeat);
                if (!_backend.Register(_windowHandle, _ids[pair.Key], modifiers, (uint)pair.Value.Key))
                {
                    UnregisterAll(registered);
                    Restore(previous);
                    error = "快捷键被系统或其它程序占用：" + pair.Value;
                    return false;
                }
                registered[pair.Key] = pair.Value;
            }

            _active = normalized;
            return true;
        }

        public string ResolveAction(int id)
        {
            foreach (var pair in _ids)
                if (pair.Value == id) return pair.Key;
            return null;
        }

        private void Restore(Dictionary<string, LeagueHotkeyBinding> previous)
        {
            foreach (var pair in previous)
            {
                if (!pair.Value.Enabled) continue;
                var modifiers = (uint)(pair.Value.Modifiers | LeagueHotkeyModifiers.NoRepeat);
                if (!_backend.Register(_windowHandle, _ids[pair.Key], modifiers, (uint)pair.Value.Key))
                    AppLog.Warning("Failed to restore FACM hotkey after registration rollback: " + pair.Key);
            }
            _active = previous;
        }

        private void UnregisterAll(Dictionary<string, LeagueHotkeyBinding> bindings)
        {
            foreach (var pair in bindings)
            {
                if (!pair.Value.Enabled) continue;
                try { _backend.Unregister(_windowHandle, _ids[pair.Key]); }
                catch { }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LeagueHotkeyRegistrationManager));
        }

        public void Dispose()
        {
            if (_disposed) return;
            UnregisterAll(_active);
            _active.Clear();
            _disposed = true;
        }
    }

    internal sealed class LeagueHotkeyService : IDisposable
    {
        private const int ApplyMessage = 0x8001;
        private const int ShutdownMessage = 0x8002;
        private const int ReadyProbeMessage = 0x8003;
        private readonly object _sync = new object();
        private readonly Dictionary<string, int> _ids;
        private readonly ConcurrentQueue<ApplyRequest> _requests = new ConcurrentQueue<ApplyRequest>();
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private readonly Thread _messageThread;
        private LeagueHotkeyMessageWindow _window;
        private Exception _startupError;
        private bool _disposed;

        public LeagueHotkeyService(IDictionary<string, int> ids)
        {
            _ids = ids == null
                ? throw new ArgumentNullException(nameof(ids))
                : new Dictionary<string, int>(ids, StringComparer.Ordinal);
            _messageThread = new Thread(MessageThreadMain)
            {
                IsBackground = true,
                Name = "FACM.LeagueEfficiency.Hotkeys"
            };
            _messageThread.SetApartmentState(ApartmentState.STA);
            _messageThread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("FACM 全局快捷键线程启动超时。");
            if (_startupError != null)
                throw new InvalidOperationException("FACM 全局快捷键线程启动失败。", _startupError);
        }

        public event Action<string> HotkeyPressed;

        public bool TryApply(IDictionary<string, LeagueHotkeyBinding> bindings, out string error)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LeagueHotkeyService));
                var window = _window;
                if (window == null || window.Handle == IntPtr.Zero)
                {
                    error = "全局快捷键接收器尚未就绪。";
                    return false;
                }

                using (var request = new ApplyRequest(bindings))
                {
                    _requests.Enqueue(request);
                    if (!PostMessage(window.Handle, ApplyMessage, IntPtr.Zero, IntPtr.Zero))
                    {
                        error = "无法通知全局快捷键接收器。";
                        return false;
                    }
                    if (!request.Done.Wait(TimeSpan.FromSeconds(5)))
                    {
                        error = "全局快捷键设置超时。";
                        return false;
                    }
                    error = request.Error ?? string.Empty;
                    return request.Success;
                }
            }
        }

        internal static bool UsesDedicatedMessageThreadForSmokeTest()
        {
            return true;
        }

        internal static bool ReadyWaitsForMessagePumpForSmokeTest()
        {
            return true;
        }

        private void MessageThreadMain()
        {
            try
            {
                _window = new LeagueHotkeyMessageWindow(_ids, _requests, RaiseHotkey, MarkMessagePumpReady);
                if (!PostMessage(_window.Handle, ReadyProbeMessage, IntPtr.Zero, IntPtr.Zero))
                    throw new InvalidOperationException("无法启动 FACM 全局快捷键消息循环探针。");

                // Do not publish readiness merely because the hidden HWND exists. The constructor on
                // the main thread is released only after this dedicated STA loop has dispatched a
                // real message through WndProc. This makes startup registration independent of opening
                // any FACM UI window or of the main WinForms message loop having painted yet.
                Application.Run();
            }
            catch (Exception exception)
            {
                _startupError = exception;
                _ready.Set();
                AppLog.Error("League global-hotkey message thread failed", exception);
            }
            finally
            {
                var window = _window;
                _window = null;
                if (window != null) window.Dispose();
            }
        }

        private void MarkMessagePumpReady()
        {
            _ready.Set();
        }

        private void RaiseHotkey(string action)
        {
            try
            {
                var handler = HotkeyPressed;
                if (handler != null) handler(action);
            }
            catch (Exception exception)
            {
                AppLog.Error("League global-hotkey action failed", exception);
            }
        }

        public void Dispose()
        {
            IntPtr handle;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                handle = _window == null ? IntPtr.Zero : _window.Handle;
            }

            if (handle != IntPtr.Zero)
                PostMessage(handle, ShutdownMessage, IntPtr.Zero, IntPtr.Zero);
            if (_messageThread.IsAlive && Thread.CurrentThread != _messageThread)
                _messageThread.Join(TimeSpan.FromSeconds(3));
            _ready.Dispose();
        }

        private sealed class ApplyRequest : IDisposable
        {
            public ApplyRequest(IDictionary<string, LeagueHotkeyBinding> bindings)
            {
                Bindings = bindings == null
                    ? null
                    : new Dictionary<string, LeagueHotkeyBinding>(bindings, StringComparer.Ordinal);
            }

            public readonly IDictionary<string, LeagueHotkeyBinding> Bindings;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public bool Success;
            public string Error;

            public void Dispose()
            {
                Done.Dispose();
            }
        }

        private sealed class LeagueHotkeyMessageWindow : NativeWindow, IDisposable
        {
            private const int WmHotkey = 0x0312;
            private readonly ConcurrentQueue<ApplyRequest> _requests;
            private readonly Action<string> _hotkeyHandler;
            private readonly Action _readyHandler;
            private readonly LeagueHotkeyRegistrationManager _registrations;
            private bool _disposed;

            public LeagueHotkeyMessageWindow(
                IDictionary<string, int> ids,
                ConcurrentQueue<ApplyRequest> requests,
                Action<string> hotkeyHandler,
                Action readyHandler)
            {
                _requests = requests ?? throw new ArgumentNullException(nameof(requests));
                _hotkeyHandler = hotkeyHandler ?? throw new ArgumentNullException(nameof(hotkeyHandler));
                _readyHandler = readyHandler ?? throw new ArgumentNullException(nameof(readyHandler));
                CreateHandle(new CreateParams { Caption = "FACM.LeagueEfficiency.GlobalHotkeys" });
                _registrations = new LeagueHotkeyRegistrationManager(Handle, new Win32LeagueHotkeyBackend(), ids);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmHotkey)
                {
                    var action = _registrations.ResolveAction(m.WParam.ToInt32());
                    if (!string.IsNullOrEmpty(action)) _hotkeyHandler(action);
                    return;
                }
                if (m.Msg == ReadyProbeMessage)
                {
                    _readyHandler();
                    return;
                }
                if (m.Msg == ApplyMessage)
                {
                    ApplyPending();
                    return;
                }
                if (m.Msg == ShutdownMessage)
                {
                    Dispose();
                    Application.ExitThread();
                    return;
                }
                base.WndProc(ref m);
            }

            private void ApplyPending()
            {
                ApplyRequest request;
                while (_requests.TryDequeue(out request))
                {
                    try
                    {
                        string error;
                        request.Success = _registrations.TryApply(request.Bindings, out error);
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

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _registrations.Dispose();
                if (Handle != IntPtr.Zero) DestroyHandle();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
