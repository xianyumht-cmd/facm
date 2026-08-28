using System.Diagnostics;
using FACM.Core.Maintenance;
using FACM.Core.Runtime;

namespace FACM.Platform.Windows.Runtime;

public sealed class WindowsLogFileOpener : ILogFileOpener
{
    public const string LogFileName = "facm4-events.jsonl";

    private readonly string _logPath;
    private readonly Func<string, bool> _shellOpen;

    public WindowsLogFileOpener(RuntimePathLayout layout)
        : this(layout, OpenWithWindowsShell)
    {
    }

    internal WindowsLogFileOpener(RuntimePathLayout layout, Func<string, bool> shellOpen)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _shellOpen = shellOpen ?? throw new ArgumentNullException(nameof(shellOpen));
        _logPath = Path.Combine(Path.GetFullPath(layout.LogsDirectory), LogFileName);
    }

    public Task<LogOpenResult> OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var directory = Path.GetDirectoryName(_logPath)
                ?? throw new InvalidOperationException("FACM log directory is unavailable.");
            Directory.CreateDirectory(directory);
            if (!File.Exists(_logPath))
            {
                using var stream = new FileStream(_logPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var started = _shellOpen(_logPath);
            return Task.FromResult(new LogOpenResult(started, _logPath, started ? "opened" : "shell-open-failed"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Task.FromResult(new LogOpenResult(false, _logPath, "log-open-failed"));
        }
    }

    private static bool OpenWithWindowsShell(string path)
    {
        var process = Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true
        });
        return process is not null;
    }
}
