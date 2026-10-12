using System;
using System.Diagnostics;
using System.Threading;

namespace FACM.Services
{
    internal sealed class SingleInstanceActivation : IDisposable
    {
        internal const string DefaultEventName = @"Local\FACM-Activate-2C429A53-6710-48BC-A57C-32BEA688B25D";

        private readonly EventWaitHandle _event;
        private readonly RegisteredWaitHandle _registration;
        private readonly Action _onActivated;
        private int _disposed;

        private SingleInstanceActivation(string eventName, Action onActivated)
        {
            if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentException("Activation event name is required.", "eventName");
            if (onActivated == null) throw new ArgumentNullException("onActivated");

            _onActivated = onActivated;
            bool createdNew;
            _event = new EventWaitHandle(false, EventResetMode.AutoReset, eventName, out createdNew);
            _registration = ThreadPool.RegisterWaitForSingleObject(
                _event,
                HandleActivation,
                null,
                Timeout.Infinite,
                false);
        }

        public static SingleInstanceActivation Listen(Action onActivated)
        {
            return new SingleInstanceActivation(DefaultEventName, onActivated);
        }

        public static bool TrySignalExisting(TimeSpan timeout)
        {
            return TrySignalExisting(DefaultEventName, timeout);
        }

        internal static SingleInstanceActivation Listen(string eventName, Action onActivated)
        {
            return new SingleInstanceActivation(eventName, onActivated);
        }

        internal static bool TrySignalExisting(string eventName, TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(eventName)) return false;
            var timeoutMilliseconds = Math.Max(0, (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds));
            var stopwatch = Stopwatch.StartNew();

            do
            {
                EventWaitHandle existing = null;
                try
                {
                    existing = EventWaitHandle.OpenExisting(eventName);
                    existing.Set();
                    return true;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // The primary instance may have acquired its mutex but not created the activation
                    // event yet. Keep this retry bounded so a damaged primary instance never blocks launch.
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
                finally
                {
                    if (existing != null) existing.Dispose();
                }

                if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds) break;
                Thread.Sleep(40);
            }
            while (stopwatch.ElapsedMilliseconds <= timeoutMilliseconds);

            return false;
        }

        internal static int RunSmokeTest()
        {
            var eventName = @"Local\FACM-ActivationSmoke-" + Guid.NewGuid().ToString("N");
            try
            {
                if (TrySignalExisting(eventName, TimeSpan.FromMilliseconds(70)))
                    throw new InvalidOperationException("Activation unexpectedly succeeded before a listener existed.");

                var callbackObserved = new AutoResetEvent(false);
                try
                {
                    var callbackCount = 0;
                    using (Listen(eventName, delegate
                    {
                        Interlocked.Increment(ref callbackCount);
                        callbackObserved.Set();
                    }))
                    {
                        if (!TrySignalExisting(eventName, TimeSpan.FromMilliseconds(500)))
                            throw new InvalidOperationException("Activation listener could not be signaled.");
                        if (!callbackObserved.WaitOne(1500) || Volatile.Read(ref callbackCount) != 1)
                            throw new InvalidOperationException("First activation callback was not observed exactly once.");

                        if (!TrySignalExisting(eventName, TimeSpan.FromMilliseconds(500)))
                            throw new InvalidOperationException("Activation listener could not be signaled a second time.");
                        if (!callbackObserved.WaitOne(1500) || Volatile.Read(ref callbackCount) != 2)
                            throw new InvalidOperationException("Second activation callback was not observed exactly once.");
                    }
                }
                finally
                {
                    callbackObserved.Dispose();
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 9;
            }
        }

        private void HandleActivation(object state, bool timedOut)
        {
            if (timedOut || Volatile.Read(ref _disposed) != 0) return;
            try
            {
                _onActivated();
            }
            catch (Exception exception)
            {
                AppLog.Error("Single-instance activation callback failed", exception);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { if (_registration != null) _registration.Unregister(null); } catch { }
            if (_event != null) _event.Dispose();
        }
    }
}
