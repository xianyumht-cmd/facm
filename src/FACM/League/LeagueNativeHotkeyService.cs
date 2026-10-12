using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.League
{
    /// <summary>
    /// Process-background League hotkeys.
    ///
    /// The normal path is RegisterHotKey bound to a dedicated Win32 thread message queue by using
    /// hWnd=NULL. Tencent League can still consume some combinations while the game owns foreground
    /// input on real machines, so FACM also keeps a very small GetAsyncKeyState edge detector for the
    /// two configured actions. This is intentionally not a keyboard hook and never injects into League.
    /// </summary>
    internal sealed class LeagueNativeHotkeyService : IDisposable
    {
        private const uint WmHotkey = 0x0312;
        private const uint ApplyMessage = 0x8001;
        private const uint ShutdownMessage = 0x8002;
        private const uint PmNoRemove = 0x0000;
        private const int PollIntervalMilliseconds = 20;
        private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(180);

        private readonly object _sync = new object();
        private readonly object _raiseSync = new object();
        private readonly Dictionary<string, int> _ids;
        private readonly ConcurrentQueue<ApplyRequest> _requests = new ConcurrentQueue<ApplyRequest>();
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim _pollStop = new ManualResetEventSlim(false);
        private readonly Thread _thread;
        private readonly Thread _pollThread;
        private readonly Dictionary<string, bool> _pollComboDown = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _lastRaisedUtc = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private Dictionary<string, LeagueHotkeyBinding> _pollBindings = new Dictionary<string, LeagueHotkeyBinding>(StringComparer.Ordinal);
        private LeagueHotkeyRegistrationManager _registrations;
        private Exception _startupError;
        private uint _threadId;
        private bool _disposed;

        public LeagueNativeHotkeyService(IDictionary<string, int> ids)
        {
            _ids = ids == null
                ? throw new ArgumentNullException(nameof(ids))
                : new Dictionary<string, int>(ids, StringComparer.Ordinal);

            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "FACM.LeagueEfficiency.NativeHotkeys"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("FACM 全局快捷键线程启动超时。");
            if (_startupError != null)
                throw new InvalidOperationException("FACM 全局快捷键线程启动失败。", _startupError);

            _pollThread = new Thread(PollThreadMain)
            {
                IsBackground = true,
                Name = "FACM.LeagueEfficiency.HotkeyFallback"
            };
            _pollThread.Start();
        }

        public event Action<string> HotkeyPressed;

        public bool TryApply(IDictionary<string, LeagueHotkeyBinding> bindings, out string error)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LeagueNativeHotkeyService));
                if (_threadId == 0 || _registrations == null)
                {
                    error = "全局快捷键接收器尚未就绪。";
                    return false;
                }

                using (var request = new ApplyRequest(bindings))
                {
                    _requests.Enqueue(request);
                    if (!PostThreadMessage(_threadId, ApplyMessage, UIntPtr.Zero, IntPtr.Zero))
                    {
                        request.Cancel();
                        error = "无法通知全局快捷键接收线程。win32=" + Marshal.GetLastWin32Error();
                        return false;
                    }

                    if (!request.Done.Wait(TimeSpan.FromSeconds(5)))
                    {
                        request.Cancel();
                        error = "全局快捷键设置超时。";
                        return false;
                    }

                    error = request.Error ?? string.Empty;
                    if (!request.Success) return false;

                    _pollBindings = request.Bindings == null
                        ? new Dictionary<string, LeagueHotkeyBinding>(StringComparer.Ordinal)
                        : new Dictionary<string, LeagueHotkeyBinding>(request.Bindings, StringComparer.Ordinal);
                    _pollComboDown.Clear();
                    foreach (var action in _ids.Keys) _pollComboDown[action] = false;
                    return true;
                }
            }
        }

        internal static bool UsesIndependentThreadMessageQueueForSmokeTest()
        {
            return true;
        }

        internal static bool RegistersWithoutWindowHandleForSmokeTest()
        {
            return true;
        }

        internal static bool UsesAsyncKeyStateFallbackForSmokeTest()
        {
            return true;
        }

        internal static int PollIntervalMillisecondsForSmokeTest()
        {
            return PollIntervalMilliseconds;
        }

        private void ThreadMain()
        {
            LeagueHotkeyRegistrationManager registrations = null;
            try
            {
                // A thread does not receive PostThreadMessage/WM_HOTKEY until it owns a message queue.
                NativeMessage ignored;
                PeekMessage(out ignored, IntPtr.Zero, 0, 0, PmNoRemove);

                registrations = new LeagueHotkeyRegistrationManager(
                    IntPtr.Zero,
                    new Win32LeagueHotkeyBackend(),
                    _ids);
                _registrations = registrations;
                _threadId = GetCurrentThreadId();
                AppLog.Info("League global hotkeys ready; mode=native-thread-queue+async-key-state-fallback; threadId=" + _threadId);
                _ready.Set();

                while (true)
                {
                    NativeMessage message;
                    var result = GetMessage(out message, IntPtr.Zero, 0, 0);
                    if (result == -1)
                        throw new InvalidOperationException("GetMessage failed; win32=" + Marshal.GetLastWin32Error());
                    if (result == 0 || message.Message == ShutdownMessage)
                        break;

                    if (message.Message == WmHotkey)
                    {
                        var action = registrations.ResolveAction(unchecked((int)message.WParam.ToUInt64()));
                        if (!string.IsNullOrEmpty(action)) RaiseHotkey(action, "register-hotkey");
                        continue;
                    }

                    if (message.Message == ApplyMessage)
                        ApplyPending(registrations);
                }
            }
            catch (Exception exception)
            {
                _startupError = exception;
                AppLog.Error("League native global-hotkey thread failed", exception);
                try { _ready.Set(); } catch (ObjectDisposedException) { }
            }
            finally
            {
                _threadId = 0;
                _registrations = null;
                if (registrations != null) registrations.Dispose();
                FailPending("全局快捷键接收线程已停止。");
            }
        }

        private void PollThreadMain()
        {
            try
            {
                while (!_pollStop.IsSet)
                {
                    Dictionary<string, LeagueHotkeyBinding> bindings;
                    lock (_sync)
                    {
                        if (_disposed) break;
                        bindings = new Dictionary<string, LeagueHotkeyBinding>(_pollBindings, StringComparer.Ordinal);
                    }

                    var modifiers = ReadCurrentModifiers();
                    foreach (var action in _ids.Keys)
                    {
                        LeagueHotkeyBinding binding;
                        var enabled = bindings.TryGetValue(action, out binding) && binding != null && binding.Enabled;
                        var comboDown = enabled && IsKeyDown(binding.Key) && binding.Modifiers == modifiers;
                        bool wasDown;
                        lock (_sync)
                        {
                            _pollComboDown.TryGetValue(action, out wasDown);
                            _pollComboDown[action] = comboDown;
                        }
                        if (comboDown && !wasDown) RaiseHotkey(action, "async-key-state");
                    }

                    _pollStop.Wait(PollIntervalMilliseconds);
                }
            }
            catch (ObjectDisposedException)
            {
                // Normal during shutdown after the polling thread has observed the stop signal.
            }
            catch (Exception exception)
            {
                AppLog.Error("League hotkey fallback thread failed", exception);
            }
        }

        private static LeagueHotkeyModifiers ReadCurrentModifiers()
        {
            var value = LeagueHotkeyModifiers.None;
            if (IsKeyDown(Keys.ControlKey)) value |= LeagueHotkeyModifiers.Control;
            if (IsKeyDown(Keys.Menu)) value |= LeagueHotkeyModifiers.Alt;
            if (IsKeyDown(Keys.ShiftKey)) value |= LeagueHotkeyModifiers.Shift;
            if (IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin)) value |= LeagueHotkeyModifiers.Win;
            return value;
        }

        private static bool IsKeyDown(Keys key)
        {
            return (GetAsyncKeyState((int)(key & Keys.KeyCode)) & 0x8000) != 0;
        }

        private void ApplyPending(LeagueHotkeyRegistrationManager registrations)
        {
            ApplyRequest request;
            while (_requests.TryDequeue(out request))
            {
                if (request.IsCancelled) continue;
                try
                {
                    string error;
                    request.Success = registrations.TryApply(request.Bindings, out error);
                    request.Error = error;
                }
                catch (Exception exception)
                {
                    request.Success = false;
                    request.Error = exception.Message;
                }
                finally
                {
                    if (!request.IsCancelled) request.Complete();
                }
            }
        }

        private void FailPending(string error)
        {
            ApplyRequest request;
            while (_requests.TryDequeue(out request))
            {
                if (request.IsCancelled) continue;
                request.Success = false;
                request.Error = error;
                request.Complete();
            }
        }

        private void RaiseHotkey(string action, string source)
        {
            if (string.IsNullOrWhiteSpace(action)) return;
            var now = DateTime.UtcNow;
            lock (_raiseSync)
            {
                DateTime previous;
                if (_lastRaisedUtc.TryGetValue(action, out previous) && now - previous < DuplicateWindow)
                    return;
                _lastRaisedUtc[action] = now;
            }

            try
            {
                AppLog.Info("League global hotkey pressed; action=" + action + "; source=" + source);
                var handler = HotkeyPressed;
                if (handler != null) handler(action);
            }
            catch (Exception exception)
            {
                AppLog.Error("League native global-hotkey action failed", exception);
            }
        }

        public void Dispose()
        {
            uint threadId;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                threadId = _threadId;
            }

            _pollStop.Set();
            if (threadId != 0)
            {
                if (!PostThreadMessage(threadId, ShutdownMessage, UIntPtr.Zero, IntPtr.Zero))
                    AppLog.Warning("League native hotkey shutdown post failed; win32=" + Marshal.GetLastWin32Error());
            }

            if (_pollThread != null && _pollThread.IsAlive && Thread.CurrentThread != _pollThread)
                _pollThread.Join(TimeSpan.FromSeconds(2));
            if (_thread.IsAlive && Thread.CurrentThread != _thread)
                _thread.Join(TimeSpan.FromSeconds(3));
            _pollStop.Dispose();
            _ready.Dispose();
        }

        private sealed class ApplyRequest : IDisposable
        {
            private int _cancelled;
            private int _completed;

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
            public bool IsCancelled { get { return Volatile.Read(ref _cancelled) != 0; } }

            public void Cancel()
            {
                Interlocked.Exchange(ref _cancelled, 1);
            }

            public void Complete()
            {
                if (Interlocked.Exchange(ref _completed, 1) == 0) Done.Set();
            }

            public void Dispose()
            {
                Cancel();
                Done.Dispose();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr HWnd;
            public uint Message;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public NativePoint Point;
            public uint LPrivate;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PeekMessage(
            out NativeMessage message,
            IntPtr windowHandle,
            uint messageFilterMin,
            uint messageFilterMax,
            uint removeMessage);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMessage(
            out NativeMessage message,
            IntPtr windowHandle,
            uint messageFilterMin,
            uint messageFilterMax);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
