using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
        string ReadClipboardText();
        bool SendTextAndTabThenText(string first, string second);
        string LastInputFailure { get; }
    }

    internal sealed class WindowsLeagueDesktopPlatform : ILeagueDesktopPlatform
    {
        private string _lastInputFailure = string.Empty;

        public string LastInputFailure { get { return _lastInputFailure; } }

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

        public string ReadClipboardText()
        {
            // The clipboard can be held briefly by the launcher or another foreground process.
            // Retry only after the explicit credential hotkey; there is no background polling.
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                        return Clipboard.GetText(TextDataFormat.UnicodeText) ?? string.Empty;
                }
                catch (ExternalException)
                {
                    // Transient clipboard ownership race; bounded retry below.
                }
                catch
                {
                    return string.Empty;
                }

                if (attempt < 5) Thread.Sleep(25);
            }
            return string.Empty;
        }

        public bool SendTextAndTabThenText(string first, string second)
        {
            _lastInputFailure = string.Empty;
            if (!SendCtrlA("account-select")) return false;
            Thread.Sleep(25);
            if (!SendText(first ?? string.Empty, "account-text")) return false;
            Thread.Sleep(25);
            if (!SendVirtualKey(0x09, "tab-to-password")) return false;
            Thread.Sleep(60);
            if (!SendCtrlA("password-select")) return false;
            Thread.Sleep(25);
            if (!SendText(second ?? string.Empty, "password-text")) return false;
            return true;
        }

        private bool SendCtrlA(string step)
        {
            var inputs = new List<Input>();
            AppendVirtualKeyDown(inputs, 0x11);
            AppendVirtualKey(inputs, 0x41);
            AppendVirtualKeyUp(inputs, 0x11);
            return SendBatch(inputs, step);
        }

        private bool SendText(string value, string step)
        {
            if (string.IsNullOrEmpty(value))
            {
                _lastInputFailure = step + ":empty";
                return false;
            }

            var layout = GetKeyboardLayout(0);
            for (var index = 0; index < value.Length; index++)
            {
                var inputs = BuildCharacterInputs(value[index], layout);
                if (!SendBatch(inputs, step)) return false;
                Thread.Sleep(4);
            }
            return true;
        }

        private static ICollection<Input> BuildCharacterInputs(char value, IntPtr layout)
        {
            var encoded = VkKeyScanEx(value, layout);
            if (encoded != -1)
            {
                var inputs = new List<Input>();
                var virtualKey = (ushort)(encoded & 0xFF);
                var shiftState = (encoded >> 8) & 0xFF;

                if ((shiftState & 0x02) != 0) AppendVirtualKeyDown(inputs, 0x11);
                if ((shiftState & 0x04) != 0) AppendVirtualKeyDown(inputs, 0x12);
                if ((shiftState & 0x01) != 0) AppendVirtualKeyDown(inputs, 0x10);

                AppendVirtualKey(inputs, virtualKey);

                if ((shiftState & 0x01) != 0) AppendVirtualKeyUp(inputs, 0x10);
                if ((shiftState & 0x04) != 0) AppendVirtualKeyUp(inputs, 0x12);
                if ((shiftState & 0x02) != 0) AppendVirtualKeyUp(inputs, 0x11);
                return inputs;
            }

            return new[]
            {
                KeyboardInput(0, value, KeyEventUnicode),
                KeyboardInput(0, value, KeyEventUnicode | KeyEventKeyUp)
            };
        }

        private bool SendVirtualKey(ushort key, string step)
        {
            var inputs = new List<Input>();
            AppendVirtualKey(inputs, key);
            return SendBatch(inputs, step);
        }

        private bool SendBatch(ICollection<Input> inputs, string step)
        {
            if (inputs == null || inputs.Count == 0)
            {
                _lastInputFailure = step + ":no-inputs";
                return false;
            }

            var array = inputs.ToArray();
            var sent = SendInput((uint)array.Length, array, Marshal.SizeOf(typeof(Input)));
            if (sent == (uint)array.Length) return true;

            var error = Marshal.GetLastWin32Error();
            _lastInputFailure = step + ":sent=" + sent + "/" + array.Length + ",win32=" + error;
            AppLog.Warning("League credential-input SendInput failed at " + _lastInputFailure);
            return false;
        }

        internal static int InputStructureSizeForSmokeTest()
        {
            return Marshal.SizeOf(typeof(Input));
        }

        internal static int ExpectedInputStructureSizeForSmokeTest()
        {
            return IntPtr.Size == 8 ? 40 : 28;
        }

        private static void AppendVirtualKey(ICollection<Input> inputs, ushort key)
        {
            AppendVirtualKeyDown(inputs, key);
            AppendVirtualKeyUp(inputs, key);
        }

        private static void AppendVirtualKeyDown(ICollection<Input> inputs, ushort key)
        {
            inputs.Add(KeyboardInput(key, '\0', 0));
        }

        private static void AppendVirtualKeyUp(ICollection<Input> inputs, ushort key)
        {
            inputs.Add(KeyboardInput(key, '\0', KeyEventKeyUp));
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
            [FieldOffset(0)] public MouseInputData Mouse;
            [FieldOffset(0)] public KeyboardInputData Keyboard;
            [FieldOffset(0)] public HardwareInputData Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInputData
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInputData
        {
            public uint Message;
            public ushort ParamL;
            public ushort ParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern short VkKeyScanEx(char value, IntPtr keyboardLayout);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint threadId);
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

            account = text.Substring(0, separatorStart).Trim();
            password = text.Substring(separatorEnd).Trim();
            if (account.Length == 0 || password.Length == 0 || account.IndexOf('-') >= 0)
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
            "League of Legends(TM)",
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

        public Task<LeagueEfficiencyActionResult> ExitGameAsync()
        {
            return Task.FromResult(KillTargets(GameProcessNames, "game-not-running", "game-exit"));
        }

        public Task<LeagueEfficiencyActionResult> CloseLobbyAsync()
        {
            return Task.FromResult(KillTargets(LobbyProcessNames, "lobby-not-running", "lobby-exit"));
        }

        public LeagueEfficiencyActionResult InputCredentialsFromClipboard()
        {
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

                AppLog.Info("League credential-input: parsed accountLength=" + account.Length + ",passwordLength=" + password.Length);
                var sent = _platform.SendTextAndTabThenText(account, password);
                if (sent)
                {
                    AppLog.Info("League credential-input: success");
                    return Result("success", "credentials-sent", 1);
                }

                var failure = string.IsNullOrWhiteSpace(_platform.LastInputFailure)
                    ? "send-input-failed"
                    : "send-input-failed/" + _platform.LastInputFailure;
                AppLog.Warning("League credential-input: failed/" + failure);
                return Result("failed", failure, 0);
            }
            finally
            {
                clipboard = null;
                account = null;
                password = null;
            }
        }

        private LeagueEfficiencyActionResult KillTargets(HashSet<string> names, string noTargetDetail, string successDetail)
        {
            var targets = FindTargets(names);
            if (targets.Count == 0) return Result("no-target", noTargetDetail, 0);
            var affected = 0;
            foreach (var target in targets)
            {
                if (!_platform.IsProcessAlive(target.Id)) continue;
                if (_platform.Kill(target.Id)) affected++;
            }
            return Result(affected > 0 ? "success" : "failed", successDetail, affected);
        }

        private List<LeagueProcessSnapshot> FindTargets(HashSet<string> names)
        {
            return (_platform.GetProcesses() ?? new LeagueProcessSnapshot[0])
                .Where(process => process != null && process.Id > 0 && names.Contains(process.Name ?? string.Empty))
                .GroupBy(process => process.Id)
                .Select(group => group.First())
                .ToList();
        }

        private static LeagueEfficiencyActionResult Result(string status, string detail, int affected)
        {
            return new LeagueEfficiencyActionResult { Status = status, Detail = detail, AffectedProcesses = affected };
        }
    }
}
