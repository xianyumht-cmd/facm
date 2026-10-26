using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal interface ILeagueGameMessageInput
    {
        bool IsGameForeground();
        bool AreModifiersReleased();
        bool TryPressEnter();
        bool TrySendText(string text, CancellationToken token);
    }

    internal sealed class LeagueNativeGameMessageInput : ILeagueGameMessageInput
    {
        private const uint InputKeyboard = 1;
        private const uint KeyUp = 0x0002;
        private const uint Unicode = 0x0004;
        private IntPtr _validatedWindow;

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            internal uint Type;
            internal InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] internal KeyboardInput Keyboard;
            [FieldOffset(0)] internal MouseInput Mouse;
            [FieldOffset(0)] internal HardwareInput Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            internal int X;
            internal int Y;
            internal uint MouseData;
            internal uint Flags;
            internal uint Time;
            internal UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInput
        {
            internal uint Message;
            internal ushort Low;
            internal ushort High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            internal ushort VirtualKey;
            internal ushort Scan;
            internal uint Flags;
            internal uint Time;
            internal UIntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public bool IsGameForeground()
        {
            var window = GetForegroundWindow();
            if (window != IntPtr.Zero && window == _validatedWindow) return true;
            _validatedWindow = IntPtr.Zero;
            if (window == IntPtr.Zero || !LeagueRespawnOverlayVisibility.IsGameForeground()) return false;
            _validatedWindow = window;
            return true;
        }

        public bool AreModifiersReleased()
        {
            return !Down(0x10) && !Down(0x11) && !Down(0x12) && !Down(0x5B) && !Down(0x5C);
        }

        private static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

        internal static bool HasNativeInputLayoutForSmokeTest()
        {
            return Marshal.SizeOf(typeof(Input)) == (IntPtr.Size == 8 ? 40 : 28) &&
                Marshal.OffsetOf(typeof(Input), "Data").ToInt64() == (IntPtr.Size == 8 ? 8 : 4);
        }

        public bool TryPressEnter()
        {
            return IsGameForeground() && AreModifiersReleased() && Emit(0x0D, 0, 0);
        }

        public bool TrySendText(string text, CancellationToken token)
        {
            foreach (var ch in text)
            {
                if (token.IsCancellationRequested || !IsGameForeground() || !AreModifiersReleased() ||
                    !Emit(0, ch, Unicode)) return false;
            }
            return true;
        }

        private static bool Emit(ushort virtualKey, ushort scan, uint flags)
        {
            var inputs = new[]
            {
                new Input { Type = InputKeyboard, Data = new InputUnion { Keyboard =
                    new KeyboardInput { VirtualKey = virtualKey, Scan = scan, Flags = flags } } },
                new Input { Type = InputKeyboard, Data = new InputUnion { Keyboard =
                    new KeyboardInput { VirtualKey = virtualKey, Scan = scan, Flags = flags | KeyUp } } }
            };
            return SendInput(2, inputs, Marshal.SizeOf(typeof(Input))) == 2;
        }
    }

    internal sealed class LeagueInGameMessageSender : IDisposable
    {
        private const int InternalDelayMs = 20;
        private readonly object _sync = new object();
        private readonly ILeagueGameMessageInput _input;
        private readonly Func<LeagueDashboardPhaseState> _phase;
        private CancellationTokenSource _active;
        private bool _disposed;

        internal LeagueInGameMessageSender(ILeagueGameMessageInput input, Func<LeagueDashboardPhaseState> phase)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _phase = phase ?? throw new ArgumentNullException(nameof(phase));
        }

        internal async Task<bool> SendAsync(string text, int intervalMilliseconds, CancellationToken cancellationToken = default(CancellationToken))
        {
            var normalized = LeagueQuickMessagePresets.NormalizeMessage(text);
            if (normalized.Length == 0 || intervalMilliseconds < 0 || intervalMilliseconds > 5000) return false;
            CancellationTokenSource current;
            lock (_sync)
            {
                if (_disposed) return false;
                _active?.Cancel();
                current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _active = current;
            }
            try
            {
                var token = current.Token;
                // Hotkey modifier keys must be up before Enter or Unicode injection.
                for (var attempt = 0; attempt < 25 && !_input.AreModifiersReleased(); attempt++)
                    await Task.Delay(20, token).ConfigureAwait(false);
                if (!_input.AreModifiersReleased()) return false;
                var lines = normalized.Split('\n');
                for (var index = 0; index < lines.Length; index++)
                {
                    if (!Eligible(token) || !_input.TryPressEnter()) return false;
                    await Task.Delay(InternalDelayMs, token).ConfigureAwait(false);
                    if (!Eligible(token) || !_input.TrySendText(lines[index], token)) return false;
                    await Task.Delay(InternalDelayMs, token).ConfigureAwait(false);
                    if (!Eligible(token) || !_input.TryPressEnter()) return false;
                    if (index + 1 < lines.Length) await Task.Delay(intervalMilliseconds, token).ConfigureAwait(false);
                }
                return true;
            }
            catch (OperationCanceledException) { return false; }
            finally
            {
                lock (_sync)
                    if (ReferenceEquals(_active, current)) _active = null;
                current.Dispose();
            }
        }

        private bool Eligible(CancellationToken token)
        {
            if (token.IsCancellationRequested || !_input.IsGameForeground() || !_input.AreModifiersReleased()) return false;
            var state = _phase();
            return state != null && state.Connected && state.Phase == "InProgress";
        }

        internal void Cancel()
        {
            lock (_sync)
                if (!_disposed) _active?.Cancel();
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _active?.Cancel();
            }
        }
    }
}
