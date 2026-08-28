using System.Diagnostics;
using FACM.Core.Maintenance;

namespace FACM.Platform.Windows.Runtime;

public sealed class WindowsSingleInstanceGate : ISingleInstanceGate
{
    public const string DefaultMutexName = @"Local\FACM-2C429A53-6710-48BC-A57C-32BEA688B25D";
    public const string DefaultActivationEventName = @"Local\FACM-Activate-2C429A53-6710-48BC-A57C-32BEA688B25D";
    public static readonly TimeSpan DefaultSignalTimeout = TimeSpan.FromMilliseconds(1600);

    private readonly string _mutexName;
    private readonly string _eventName;
    private readonly TimeSpan _retryDelay;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _registration;
    private Action? _onActivated;
    private int _disposed;
    private bool _ownsMutex;
    private bool _entered;

    public WindowsSingleInstanceGate()
        : this(DefaultMutexName, DefaultActivationEventName, TimeSpan.FromMilliseconds(40))
    {
    }

    internal WindowsSingleInstanceGate(string mutexName, string eventName, TimeSpan retryDelay)
    {
        if (string.IsNullOrWhiteSpace(mutexName)) throw new ArgumentException("Mutex name is required.", nameof(mutexName));
        if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentException("Activation event name is required.", nameof(eventName));
        if (retryDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryDelay));
        _mutexName = mutexName;
        _eventName = eventName;
        _retryDelay = retryDelay;
    }

    public SingleInstanceDisposition EnterNormal(Action onActivated, TimeSpan signalTimeout)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_entered) throw new InvalidOperationException("Single-instance gate can only be entered once.");
        ArgumentNullException.ThrowIfNull(onActivated);
        if (signalTimeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(signalTimeout));
        _entered = true;

        bool createdNew;
        var mutex = new Mutex(initiallyOwned: true, _mutexName, out createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return TrySignalExisting(_eventName, signalTimeout, _retryDelay)
                ? SingleInstanceDisposition.ExistingSignaled
                : SingleInstanceDisposition.ExistingUnresponsive;
        }

        _mutex = mutex;
        _ownsMutex = true;
        _onActivated = onActivated;
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName, out _);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            static (state, timedOut) => ((WindowsSingleInstanceGate)state!).HandleActivation(timedOut),
            this,
            Timeout.Infinite,
            executeOnlyOnce: false);
        return SingleInstanceDisposition.Primary;
    }

    internal static bool TrySignalExisting(string eventName, TimeSpan timeout, TimeSpan retryDelay)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return false;
        if (timeout < TimeSpan.Zero) return false;
        var stopwatch = Stopwatch.StartNew();
        do
        {
            EventWaitHandle? existing = null;
            try
            {
                existing = EventWaitHandle.OpenExisting(eventName);
                existing.Set();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // Primary may own the mutex before its activation event exists. Retry only within
                // the bounded legacy-compatible window; never take over or kill an unresponsive primary.
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                existing?.Dispose();
            }

            if (stopwatch.Elapsed >= timeout) break;
            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            Thread.Sleep(remaining < retryDelay ? remaining : retryDelay);
        }
        while (stopwatch.Elapsed <= timeout);
        return false;
    }

    private void HandleActivation(bool timedOut)
    {
        if (timedOut || Volatile.Read(ref _disposed) != 0) return;
        try
        {
            _onActivated?.Invoke();
        }
        catch
        {
            // External activation is best-effort. A callback failure must not crash the primary process.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _registration?.Unregister(null); } catch { }
        _registration = null;
        _activationEvent?.Dispose();
        _activationEvent = null;
        _onActivated = null;
        if (_ownsMutex && _mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _ownsMutex = false;
        }
        _mutex?.Dispose();
        _mutex = null;
    }
}
