using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
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

    internal sealed class LeagueHotkeyService : NativeWindow, IDisposable
    {
        private const int WmHotkey = 0x0312;
        private readonly LeagueHotkeyRegistrationManager _registrations;
        private bool _disposed;

        public LeagueHotkeyService(IDictionary<string, int> ids)
        {
            CreateHandle(new CreateParams { Caption = "FACM.LeagueEfficiency.Hotkeys" });
            _registrations = new LeagueHotkeyRegistrationManager(Handle, new Win32LeagueHotkeyBackend(), ids);
        }

        public event Action<string> HotkeyPressed;

        public bool TryApply(IDictionary<string, LeagueHotkeyBinding> bindings, out string error)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LeagueHotkeyService));
            return _registrations.TryApply(bindings, out error);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey)
            {
                var action = _registrations.ResolveAction(m.WParam.ToInt32());
                if (!string.IsNullOrEmpty(action))
                {
                    var handler = HotkeyPressed;
                    if (handler != null) handler(action);
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _registrations.Dispose();
            DestroyHandle();
        }
    }
}
