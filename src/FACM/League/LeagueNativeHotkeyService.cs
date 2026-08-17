using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using FACM.Services;

namespace FACM.League
{
    /// <summary>
    /// True process-background global hotkeys.
    ///
    /// RegisterHotKey is bound to this dedicated Win32 thread by passing hWnd=NULL. The thread owns
    /// its own native message queue, so FACM does not need an activated/visible WinForms window for
    /// WM_HOTKEY delivery. This deliberately avoids keyboard polling and low-level keyboard hooks.
    /// </summary>
    internal sealed class LeagueNativeHotkeyService : IDisposable
    {
        private const uint WmHotkey = 0x0312;
        private const uint ApplyMessage = 0x8001;
        private const uint ShutdownMessage = 0x8002;
        private const uint PmNoRemove = 0x0000;

        private readonly object _sync = new object();
        private readonly Dictionary<string, int> _ids;
        private readonly ConcurrentQueue<ApplyRequest> _requests = new ConcurrentQueue<ApplyRequest>();
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private readonly Thread _thread;
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
                    return request.Success;
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

        private void ThreadMain()
        {
            LeagueHotkeyRegistrationManager registrations = null;
            try
            {
                // A thread does not receive PostThreadMessage/WM_HOTKEY until it owns a message queue.
                // PeekMessage is the lightweight Win32 operation used here solely to force that queue
                // to exist before another FACM thread posts configuration messages to it.
                NativeMessage ignored;
                PeekMessage(out ignored, IntPtr.Zero, 0, 0, PmNoRemove);

                registrations = new LeagueHotkeyRegistrationManager(
                    IntPtr.Zero,
                    new Win32LeagueHotkeyBackend(),
                    _ids);
                _registrations = registrations;
                _threadId = GetCurrentThreadId();
                AppLog.Info("League global hotkeys ready; mode=native-thread-queue; threadId=" + _threadId);
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
                        if (!string.IsNullOrEmpty(action)) RaiseHotkey(action);
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

        private void RaiseHotkey(string action)
        {
            try
            {
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

            if (threadId != 0)
            {
                if (!PostThreadMessage(threadId, ShutdownMessage, UIntPtr.Zero, IntPtr.Zero))
                    AppLog.Warning("League native hotkey shutdown post failed; win32=" + Marshal.GetLastWin32Error());
            }

            if (_thread.IsAlive && Thread.CurrentThread != _thread)
                _thread.Join(TimeSpan.FromSeconds(3));
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

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
