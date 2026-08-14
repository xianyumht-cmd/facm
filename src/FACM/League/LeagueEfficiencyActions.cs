using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueProcessSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    internal interface ILeagueDesktopPlatform
    {
        IReadOnlyList<LeagueProcessSnapshot> GetProcesses();
        bool IsProcessAlive(int processId);
        bool RequestClose(int processId);
        bool Kill(int processId);
        string GetForegroundProcessName();
        string GetForegroundWindowTitle();
        string ReadClipboardText();
        bool SendTextAndTabThenText(string first, string second);
    }

    internal sealed class WindowsLeagueDesktopPlatform : ILeagueDesktopPlatform
    {
        public IReadOnlyList<LeagueProcessSnapshot> GetProcesses()
        {
            var result = new List<LeagueProcessSnapshot>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    result.Add(new LeagueProcessSnapshot { Id = process.Id, Name = process.ProcessName ?? string.Empty });
                }
                catch { }
                finally { process.Dispose(); }
            }
            return result;
        }

        public bool IsProcessAlive(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId)) return !process.HasExited;
            }
            catch { return false; }
        }

        public bool RequestClose(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (process.HasExited) return true;
                    if (process.MainWindowHandle == IntPtr.Zero) return false;
                    return process.CloseMainWindow();
                }
            }
            catch { return false; }
        }

        public bool Kill(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (process.HasExited) return true;
                    process.Kill();
                    return true;
                }
            }
            catch { return false; }
        }

        public string GetForegroundProcessName()
        {
            int pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            if (pid <= 0) return string.Empty;
            try
            {
                using (var process = Process.GetProcessById(pid)) return process.ProcessName ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        public string GetForegroundWindowTitle()
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return string.Empty;
            var length = GetWindowTextLength(handle);
            if (length <= 0) return string.Empty;
            var buffer = new System.Text.StringBuilder(length + 1);
            GetWindowText(handle, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        public string ReadClipboardText()
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : string.Empty; }
            catch { return string.Empty; }
        }

        public bool SendTextAndTabThenText(string first, string second)
        {
            var inputs = new List<Input>();
            AppendUnicode(inputs, first ?? string.Empty);
            AppendVirtualKey(inputs, 0x09);
            AppendUnicode(inputs, second ?? string.Empty);
            if (inputs.Count == 0) return false;
            var array = inputs.ToArray();
            return SendInput((uint)array.Length, array, Marshal.SizeOf(typeof(Input))) == array.Length;
        }

        private static void AppendUnicode(ICollection<Input> inputs, string value)
        {
            foreach (var ch in value)
            {
                inputs.Add(KeyboardInput(0, ch, KeyEventUnicode));
                inputs.Add(KeyboardInput(0, ch, KeyEventUnicode | KeyEventKeyUp));
            }
        }

        private static void AppendVirtualKey(ICollection<Input> inputs, ushort key)
        {
            inputs.Add(KeyboardInput(key, 0, 0));
            inputs.Add(KeyboardInput(key, 0, KeyEventKeyUp));
        }

        private static Input KeyboardInput(ushort virtualKey, char scan, uint flags)
        {
            return new Input
            {
                Type = 1,
                Union = new InputUnion
                {
                    Keyboard = new KeyboardInputData
                    {
                        VirtualKey = virtualKey,
                        ScanCode = scan,
                        Flags = flags,
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero
                    }
                }
            };
        }

        private const uint KeyEventKeyUp = 0x0002;
        private const uint KeyEventUnicode = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public InputUnion Union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KeyboardInputData Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInputData
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);
    }

    internal static class LeagueCredentialParser
    {
        public static bool TryParse(string value, out string account, out string password)
        {
            account = string.Empty;
            password = string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var text = value.Trim();
            if (ContainsUnsafeControl(text)) return false;

            var separatorStart = text.IndexOf('-');
            if (separatorStart <= 0) return false;
            var separatorEnd = separatorStart;
            while (separatorEnd < text.Length && text[separatorEnd] == '-') separatorEnd++;
            if (separatorEnd >= text.Length) return false;
            if (text.IndexOf('-', separatorEnd) >= 0) return false;

            account = text.Substring(0, separatorStart).Trim();
            password = text.Substring(separatorEnd).Trim();
            if (account.Length == 0 || password.Length == 0 || account.IndexOf('-') >= 0 || password.IndexOf('-') >= 0)
            {
                account = string.Empty;
                password = string.Empty;
                return false;
            }
            if (ContainsUnsafeControl(account) || ContainsUnsafeControl(password))
            {
                account = string.Empty;
                password = string.Empty;
                return false;
            }
            return true;
        }

        private static bool ContainsUnsafeControl(string value)
        {
            return value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\t') >= 0 || value.IndexOf('\0') >= 0;
        }
    }

    internal sealed class LeagueEfficiencyActionResult
    {
        public string Status { get; set; }
        public string Detail { get; set; }
        public int AffectedProcesses { get; set; }
    }

    internal sealed class LeagueEfficiencyActionService
    {
        private static readonly HashSet<string> GameProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "League of Legends"
        };

        private static readonly HashSet<string> LobbyProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LeagueClient",
            "LeagueClientUx",
            "LeagueClientUxRender"
        };

        private readonly ILeagueDesktopPlatform _platform;

        public LeagueEfficiencyActionService()
            : this(new WindowsLeagueDesktopPlatform())
        {
        }

        internal LeagueEfficiencyActionService(ILeagueDesktopPlatform platform)
        {
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        }

        public async Task<LeagueEfficiencyActionResult> ExitGameAsync()
        {
            var targets = FindTargets(GameProcessNames);
            if (targets.Count == 0) return Result("no-target", "game-not-running", 0);
            var affected = await CloseTargetsAsync(targets, 900).ConfigureAwait(false);
            return Result(affected > 0 ? "success" : "failed", "game-exit", affected);
        }

        public async Task<LeagueEfficiencyActionResult> CloseLobbyAsync()
        {
            if (FindTargets(GameProcessNames).Count > 0)
                return Result("blocked", "game-still-running", 0);
            var targets = FindTargets(LobbyProcessNames);
            if (targets.Count == 0) return Result("no-target", "lobby-not-running", 0);
            var affected = await CloseTargetsAsync(targets, 700).ConfigureAwait(false);
            return Result(affected > 0 ? "success" : "failed", "lobby-exit", affected);
        }

        public LeagueEfficiencyActionResult InputCredentialsFromClipboard()
        {
            if (!IsAllowedLoginForeground(_platform.GetForegroundProcessName(), _platform.GetForegroundWindowTitle()))
            {
                AppLog.Info("League credential-input: blocked");
                return Result("blocked", "login-window-required", 0);
            }

            var clipboard = _platform.ReadClipboardText();
            string account = null;
            string password = null;
            try
            {
                if (!LeagueCredentialParser.TryParse(clipboard, out account, out password))
                {
                    AppLog.Info("League credential-input: invalid-format");
                    return Result("invalid", "clipboard-format", 0);
                }
                var sent = _platform.SendTextAndTabThenText(account, password);
                AppLog.Info("League credential-input: " + (sent ? "success" : "failed"));
                return Result(sent ? "success" : "failed", sent ? "credentials-sent" : "send-input-failed", sent ? 1 : 0);
            }
            finally
            {
                clipboard = null;
                account = null;
                password = null;
            }
        }

        internal static bool IsAllowedLoginForeground(string processName, string title)
        {
            processName = processName ?? string.Empty;
            title = title ?? string.Empty;
            if (processName.Equals("RiotClientServices", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("RiotClientUx", StringComparison.OrdinalIgnoreCase))
                return true;

            if (processName.Equals("WeGame", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("wegame", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("wegamehelper", StringComparison.OrdinalIgnoreCase))
            {
                return ContainsAny(title, "WeGame", "登录", "英雄联盟", "账号");
            }

            if (processName.Equals("LeagueClientUx", StringComparison.OrdinalIgnoreCase))
                return ContainsAny(title, "登录", "login", "sign in", "账号");
            return false;
        }

        private List<LeagueProcessSnapshot> FindTargets(HashSet<string> names)
        {
            return (_platform.GetProcesses() ?? new LeagueProcessSnapshot[0])
                .Where(process => process != null && process.Id > 0 && names.Contains(process.Name ?? string.Empty))
                .GroupBy(process => process.Id)
                .Select(group => group.First())
                .ToList();
        }

        private async Task<int> CloseTargetsAsync(IReadOnlyList<LeagueProcessSnapshot> targets, int graceMilliseconds)
        {
            var affected = 0;
            foreach (var target in targets)
            {
                if (!_platform.IsProcessAlive(target.Id)) continue;
                if (_platform.RequestClose(target.Id)) affected++;
            }

            await Task.Delay(graceMilliseconds).ConfigureAwait(false);
            foreach (var target in targets)
            {
                if (!_platform.IsProcessAlive(target.Id)) continue;
                if (_platform.Kill(target.Id)) affected++;
            }
            return affected;
        }

        private static LeagueEfficiencyActionResult Result(string status, string detail, int affected)
        {
            return new LeagueEfficiencyActionResult { Status = status, Detail = detail, AffectedProcesses = affected };
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (var needle in needles)
                if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
