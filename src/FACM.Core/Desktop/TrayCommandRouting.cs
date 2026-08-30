namespace FACM.Core.Desktop;

public enum TrayCommand
{
    OpenCompactLauncher,
    OpenCleanup,
    OpenLeague,
    OpenPersonalization,
    OpenDesktopPetSettings,
    RestoreDefaultLauncher,
    ResetDesktopPosition,
    CheckForUpdates,
    OpenLog,
    Exit
}

/// <summary>
/// Keeps tray command dispatch deterministic and independently testable from the native tray UI.
/// The host owns the native menu; the application supplies the existing product actions.
/// </summary>
public sealed class TrayCommandRouter : IDisposable
{
    private readonly IReadOnlyDictionary<TrayCommand, Action> _handlers;
    private int _disposed;

    public TrayCommandRouter(IReadOnlyDictionary<TrayCommand, Action> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        if (handlers.Count == 0) throw new ArgumentException("At least one tray command is required.", nameof(handlers));
        _handlers = new Dictionary<TrayCommand, Action>(handlers);
        if (_handlers.Any(pair => pair.Value is null))
            throw new ArgumentException("Tray command handlers may not be null.", nameof(handlers));
    }

    public bool TryDispatch(TrayCommand command)
    {
        if (Volatile.Read(ref _disposed) != 0 || !_handlers.TryGetValue(command, out var handler)) return false;
        try
        {
            handler();
            return true;
        }
        catch
        {
            // Native tray callbacks are a shell convenience and must not crash FACM when an optional
            // destination is unavailable during shutdown or startup recovery.
            return false;
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
