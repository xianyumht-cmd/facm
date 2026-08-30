using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using FACM.Core.Personalization;

namespace FACM.Platform.Windows.Personalization;

public sealed class WindowsFlyingPetRuntime : IDesktopPetRuntime, IDisposable
{
    private static readonly TimeSpan HostReadyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HostProcessStartTimeout = TimeSpan.FromSeconds(10);

    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WindowsFlyingHostBundleStore _bundleStore;
    private readonly string _uiTextPath;
    private readonly Action _openRequested;
    private readonly Action _contextRequested;
    private readonly Action<bool> _setLauncherVisible;
    private readonly Func<Task> _resetLauncherPosition;
    private readonly Action<string>? _reportStage;
    private readonly TimeSpan _hostProcessStartTimeout;
    private readonly Func<ProcessStartInfo, Task<Process?>> _launchProcess;

    private DesktopPetRuntimeState _current = new(false, false, string.Empty, "launcher-only");
    private Process? _process;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _transportCancellation;
    private int _generation;
    private bool _disposed;

    public WindowsFlyingPetRuntime(
        WindowsFlyingHostBundleStore bundleStore,
        string uiTextPath,
        Action openRequested,
        Action contextRequested,
        Action<bool> setLauncherVisible,
        Func<Task> resetLauncherPosition,
        Action<string>? reportStage = null,
        TimeSpan? hostProcessStartTimeout = null,
        Func<ProcessStartInfo, Task<Process?>>? launchProcess = null)
    {
        _bundleStore = bundleStore ?? throw new ArgumentNullException(nameof(bundleStore));
        _uiTextPath = uiTextPath ?? string.Empty;
        _openRequested = openRequested ?? throw new ArgumentNullException(nameof(openRequested));
        _contextRequested = contextRequested ?? throw new ArgumentNullException(nameof(contextRequested));
        _setLauncherVisible = setLauncherVisible ?? throw new ArgumentNullException(nameof(setLauncherVisible));
        _resetLauncherPosition = resetLauncherPosition ?? throw new ArgumentNullException(nameof(resetLauncherPosition));
        _reportStage = reportStage;
        _hostProcessStartTimeout = hostProcessStartTimeout ?? HostProcessStartTimeout;
        if (_hostProcessStartTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(hostProcessStartTimeout), "FlyingHost process-start timeout must be positive.");
        _launchProcess = launchProcess ?? LaunchProcessAsync;
    }

    public DesktopPetRuntimeState Current
    {
        get
        {
            lock (_stateSync) return _current;
        }
    }

    public event EventHandler<DesktopPetRuntimeState>? StateChanged;

    public async Task<DesktopPetModeResult> ApplyAsync(
        bool enabled,
        FacmPetDefinition pet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pet);
        ThrowIfDisposed();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            if (!enabled)
            {
                await StopTransportLockedAsync().ConfigureAwait(false);
                SetLauncherVisible(true);
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "launcher-restored"));
                return new DesktopPetModeResult(true, false, "launcher-restored");
            }

            if (pet.Runtime != FacmPetRuntimeKind.FlyingSprite)
            {
                await StopTransportLockedAsync().ConfigureAwait(false);
                SetLauncherVisible(true);
                var unsupported = "runtime-unsupported:" + pet.Runtime;
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, unsupported));
                return new DesktopPetModeResult(false, false, unsupported);
            }

            var existing = Current;
            if (existing.PetVisible &&
                string.Equals(existing.ActivePetId, pet.Id, StringComparison.OrdinalIgnoreCase) &&
                IsProcessAlive(_process))
            {
                return new DesktopPetModeResult(true, true, "already-active");
            }

            await StopTransportLockedAsync().ConfigureAwait(false);
            SetLauncherVisible(true);
            UpdateState(new DesktopPetRuntimeState(true, false, pet.Id, "flying-payload-preparing"));

            FlyingHostBundlePreparation preparation;
            try
            {
                preparation = await _bundleStore.PrepareAsync(operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "runtime-disposing"));
                return new DesktopPetModeResult(false, false, "runtime-disposing");
            }
            catch (OperationCanceledException)
            {
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "flying-payload-cancelled"));
                throw;
            }
            catch (Exception exception)
            {
                var detail = "flying-payload-failed:" + exception.GetType().Name;
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, detail));
                return new DesktopPetModeResult(false, false, detail);
            }

            var result = await StartFlyingHostLockedAsync(preparation, pet, operation.Token).ConfigureAwait(false);
            if (!result.Success)
            {
                await StopTransportLockedAsync().ConfigureAwait(false);
                SetLauncherVisible(true);
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, result.Detail));
            }
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetPositionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            if (_writer is not null) await TrySendAsync(_writer, "reset").ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            await _resetLauncherPosition().ConfigureAwait(false);
            UpdateState(Current with { Detail = "desktop-position-reset" });
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DesktopPetModeResult> StartFlyingHostLockedAsync(
        FlyingHostBundlePreparation preparation,
        FacmPetDefinition pet,
        CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        var pipeName = "FACM.FlyingHost." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
        var startInfo = new ProcessStartInfo
        {
            FileName = preparation.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = preparation.PayloadDirectory
        };
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--pet-id");
        startInfo.ArgumentList.Add(pet.Id);
        if (!string.IsNullOrWhiteSpace(_uiTextPath))
        {
            startInfo.ArgumentList.Add("--ui-text");
            startInfo.ArgumentList.Add(_uiTextPath);
        }

        Process? process;
        Task<Process?>? launchTask = null;
        try
        {
            ReportStage("flying-process-start-start");
            launchTask = _launchProcess(startInfo);
            process = await launchTask.WaitAsync(_hostProcessStartTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ReportStage("flying-process-start-timeout");
            if (launchTask is not null) ScheduleLateProcessCleanup(launchTask);
            return new DesktopPetModeResult(false, false, "flying-process-start-timeout");
        }
        catch (OperationCanceledException)
        {
            ReportStage("flying-process-start-cancelled");
            if (launchTask is not null) ScheduleLateProcessCleanup(launchTask);
            throw;
        }
        catch (Exception exception)
        {
            var detail = "flying-process-start-failed:" + exception.GetType().Name;
            ReportStage(detail);
            return new DesktopPetModeResult(false, false, detail);
        }

        if (process is null)
        {
            ReportStage("flying-process-start-rejected");
            return new DesktopPetModeResult(false, false, "flying-process-start-rejected");
        }
        ReportStage("flying-process-start-finish");

        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var transportCancellation = new CancellationTokenSource();
        var connected = false;
        try
        {
            using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupTimeout.CancelAfter(HostReadyTimeout);
            ReportStage("flying-pipe-connect-start");
            await pipe.ConnectAsync(startupTimeout.Token).ConfigureAwait(false);
            connected = true;
            ReportStage("flying-pipe-connect-finish");

            var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

            _process = process;
            _pipe = pipe;
            _reader = reader;
            _writer = writer;
            _transportCancellation = transportCancellation;
            process.Exited += OnProcessExited;

            await TrySendAsync(writer, "activate|" + pet.Id).ConfigureAwait(false);
            UpdateState(new DesktopPetRuntimeState(true, false, pet.Id,
                preparation.CacheHit ? "flying-host-starting-cache-hit" : "flying-host-starting-new-payload"));

            while (!startupTimeout.Token.IsCancellationRequested && generation == _generation)
            {
                var line = await reader.ReadLineAsync(startupTimeout.Token).ConfigureAwait(false);
                if (line is null) return new DesktopPetModeResult(false, false, "flying-ipc-ended-before-ready");
                if (!TryParseEvent(line, out var eventName, out var detail)) continue;
                if (string.Equals(eventName, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    ReportStage("flying-host-ready");
                    SetLauncherVisible(false);
                    UpdateState(new DesktopPetRuntimeState(true, true, pet.Id, "ready:" + detail));
                    _ = Task.Run(() => ReadLoopAsync(generation, pet.Id, reader, transportCancellation.Token));
                    return new DesktopPetModeResult(true, true, "ready:" + detail);
                }
                if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
                {
                    ReportStage("flying-host-error:" + detail);
                    return new DesktopPetModeResult(false, false, "flying-host-error:" + detail);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new DesktopPetModeResult(false, false, "flying-startup-superseded");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ReportStage(connected ? "flying-host-ready-timeout" : "flying-pipe-connect-timeout");
            return new DesktopPetModeResult(false, false, connected ? "flying-host-ready-timeout" : "flying-ipc-connect-timeout");
        }
        catch (OperationCanceledException)
        {
            ReportStage("flying-host-start-cancelled");
            throw;
        }
        catch (Exception exception)
        {
            var detail = "flying-host-start-failed:" + exception.GetType().Name;
            ReportStage(detail);
            return new DesktopPetModeResult(false, false, detail);
        }
        finally
        {
            if (!ReferenceEquals(_process, process))
            {
                transportCancellation.Dispose();
                try { pipe.Dispose(); } catch { }
                KillAndDisposeProcess(process);
            }
        }
    }

    private static Task<Process?> LaunchProcessAsync(ProcessStartInfo startInfo) =>
        Task.Run(() =>
        {
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            try
            {
                if (!process.Start())
                {
                    process.Dispose();
                    return null;
                }
                _ = WindowsChildProcessJob.TryAssign(process);
                return process;
            }
            catch
            {
                process.Dispose();
                throw;
            }
        });

    private static void ScheduleLateProcessCleanup(Task<Process?> launchTask)
    {
        _ = launchTask.ContinueWith(
            task =>
            {
                try
                {
                    if (task.Status == TaskStatus.RanToCompletion && task.Result is { } process)
                        KillAndDisposeProcess(process);
                    else
                        _ = task.Exception;
                }
                catch { }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void KillAndDisposeProcess(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        try { process.Dispose(); } catch { }
    }

    private async Task ReadLoopAsync(int generation, string petId, StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && generation == Volatile.Read(ref _generation))
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    QueueRecovery(generation, "ipc-ended");
                    return;
                }
                if (!TryParseEvent(line, out var eventName, out var detail)) continue;
                if (string.Equals(eventName, "click", StringComparison.OrdinalIgnoreCase))
                {
                    TryInvoke(_openRequested);
                    continue;
                }
                if (string.Equals(eventName, "right-click", StringComparison.OrdinalIgnoreCase))
                {
                    TryInvoke(_contextRequested);
                    continue;
                }
                if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
                {
                    QueueRecovery(generation, "host-error:" + detail);
                    return;
                }
                if (string.Equals(eventName, "ready", StringComparison.OrdinalIgnoreCase))
                    UpdateState(new DesktopPetRuntimeState(true, true, petId, "ready:" + detail));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException exception) { QueueRecovery(generation, "ipc-io:" + exception.GetType().Name); }
        catch (Exception exception) { QueueRecovery(generation, "ipc-failed:" + exception.GetType().Name); }
    }

    private void OnProcessExited(object? sender, EventArgs args) =>
        QueueRecovery(Volatile.Read(ref _generation), "process-exited");

    private void QueueRecovery(int generation, string detail) =>
        _ = Task.Run(() => RecoverAsync(generation, detail));

    private async Task RecoverAsync(int generation, string detail)
    {
        if (_disposed) return;
        try { await _gate.WaitAsync(_lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        try
        {
            if (_disposed || generation != _generation) return;
            await StopTransportLockedAsync().ConfigureAwait(false);
            SetLauncherVisible(true);
            UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "runtime-failed:flying-" + detail));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopTransportLockedAsync()
    {
        ++_generation;
        var process = _process;
        var pipe = _pipe;
        var reader = _reader;
        var writer = _writer;
        var cancellation = _transportCancellation;
        _process = null;
        _pipe = null;
        _reader = null;
        _writer = null;
        _transportCancellation = null;

        try { cancellation?.Cancel(); } catch { }
        if (writer is not null) await TrySendAsync(writer, "stop").ConfigureAwait(false);
        try { writer?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
        try { pipe?.Dispose(); } catch { }
        cancellation?.Dispose();

        if (process is null) return;
        try { process.Exited -= OnProcessExited; } catch { }
        try
        {
            if (!process.HasExited)
            {
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false); }
                catch (TimeoutException) { process.Kill(entireProcessTree: true); }
            }
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        }
        finally
        {
            process.Dispose();
        }
    }

    private static async Task TrySendAsync(StreamWriter writer, string command)
    {
        try
        {
            await writer.WriteLineAsync(command).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private static bool TryParseEvent(string line, out string eventName, out string detail)
    {
        eventName = string.Empty;
        detail = string.Empty;
        if (!line.StartsWith("event|", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = line.Split('|', 3);
        if (parts.Length < 2) return false;
        eventName = parts[1];
        detail = parts.Length > 2 ? parts[2] : string.Empty;
        return true;
    }

    private void SetLauncherVisible(bool visible)
    {
        try { _setLauncherVisible(visible); } catch { }
    }

    private static void TryInvoke(Action action)
    {
        try { action(); } catch { }
    }

    private void UpdateState(DesktopPetRuntimeState state)
    {
        lock (_stateSync) _current = state;
        try { StateChanged?.Invoke(this, state); } catch { }
    }

    private void ReportStage(string stage)
    {
        try { _reportStage?.Invoke(stage); } catch { }
    }

    private static bool IsProcessAlive(Process? process)
    {
        if (process is null) return false;
        try { return !process.HasExited; } catch { return false; }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_generation;
        try { _lifetime.Cancel(); } catch { }
        try { _transportCancellation?.Cancel(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        try { if (_process is not null && !_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
        try { _process?.Dispose(); } catch { }
        _process = null;
        _pipe = null;
        _reader = null;
        _writer = null;
        _transportCancellation = null;
        StateChanged = null;
    }
}
