using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using FACM.Core.Personalization;

namespace FACM.Platform.Windows.Personalization;

public sealed class WindowsVPetRuntime : IDesktopPetRuntime, IDisposable
{
    private static readonly TimeSpan HostReadyTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HostProcessStartTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CommandWriteTimeout = TimeSpan.FromMilliseconds(750);

    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WindowsPetHostBundleStore _bundleStore;
    private readonly string _dataRoot;
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
    private Task? _readLoop;
    private string? _transportPipeName;
    private bool _transportPoisoned;
    private int _generation;
    private bool _disposed;

    public WindowsVPetRuntime(
        WindowsPetHostBundleStore bundleStore,
        string dataRoot,
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
        _dataRoot = string.IsNullOrWhiteSpace(dataRoot) ? throw new ArgumentException("PetHost data root is required.", nameof(dataRoot)) : dataRoot;
        _uiTextPath = uiTextPath ?? string.Empty;
        _openRequested = openRequested ?? throw new ArgumentNullException(nameof(openRequested));
        _contextRequested = contextRequested ?? throw new ArgumentNullException(nameof(contextRequested));
        _setLauncherVisible = setLauncherVisible ?? throw new ArgumentNullException(nameof(setLauncherVisible));
        _resetLauncherPosition = resetLauncherPosition ?? throw new ArgumentNullException(nameof(resetLauncherPosition));
        _reportStage = reportStage;
        _hostProcessStartTimeout = hostProcessStartTimeout ?? HostProcessStartTimeout;
        if (_hostProcessStartTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(hostProcessStartTimeout), "PetHost process-start timeout must be positive.");
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
                RestoreLauncher("vpet-launcher-restore");
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "launcher-restored"));
                return new DesktopPetModeResult(true, false, "launcher-restored");
            }

            if (pet.Runtime != FacmPetRuntimeKind.VPetCore)
            {
                await StopTransportLockedAsync().ConfigureAwait(false);
                RestoreLauncher("vpet-launcher-restore-unsupported");
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
            RestoreLauncher("vpet-launcher-restore-before-start");
            UpdateState(new DesktopPetRuntimeState(true, false, pet.Id, "payload-preparing"));

            PetHostBundlePreparation preparation;
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
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "payload-cancelled"));
                throw;
            }
            catch (Exception exception)
            {
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "payload-failed:" + exception.GetType().Name));
                return new DesktopPetModeResult(false, false, "payload-failed:" + exception.GetType().Name);
            }

            DesktopPetModeResult result;
            try
            {
                result = await StartPetHostLockedAsync(preparation, pet, operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await StopTransportLockedAsync().ConfigureAwait(false);
                RestoreLauncher("vpet-launcher-restore-cancelled");
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "vpet-start-cancelled"));
                throw;
            }
            if (!result.Success)
            {
                await StopTransportLockedAsync().ConfigureAwait(false);
                RestoreLauncher("vpet-launcher-restore-failure");
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
            if (_writer is not null)
            {
                var writer = _writer;
                using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
                writeTimeout.CancelAfter(CommandWriteTimeout);
                try
                {
                    await SendCommandAsync(writer, "reset", writeTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!operation.Token.IsCancellationRequested)
                {
                    PoisonTransport("reset-send-timeout", Volatile.Read(ref _generation), _transportPipeName, _process, "reset");
                }
                catch (Exception exception)
                {
                    PoisonTransport("reset-send-failed:" + exception.GetType().Name,
                        Volatile.Read(ref _generation), _transportPipeName, _process, "reset");
                }
            }
            operation.Token.ThrowIfCancellationRequested();
            await _resetLauncherPosition().ConfigureAwait(false);
            var current = Current;
            UpdateState(current with { Detail = "desktop-position-reset" });
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DesktopPetModeResult> StartPetHostLockedAsync(
        PetHostBundlePreparation preparation,
        FacmPetDefinition pet,
        CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        var pipeName = "FACM.PetHost." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
        _transportPipeName = pipeName;
        _transportPoisoned = false;
        Directory.CreateDirectory(_dataRoot);

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
        startInfo.ArgumentList.Add("--data-root");
        startInfo.ArgumentList.Add(_dataRoot);
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
            ReportStage("process-start-start");
            launchTask = _launchProcess(startInfo);
            process = await launchTask.WaitAsync(_hostProcessStartTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ReportStage("process-start-timeout");
            if (launchTask is not null) ScheduleLateProcessCleanup(launchTask);
            return new DesktopPetModeResult(false, false, "process-start-timeout");
        }
        catch (OperationCanceledException)
        {
            ReportStage("process-start-cancelled");
            if (launchTask is not null) ScheduleLateProcessCleanup(launchTask);
            throw;
        }
        catch (Exception exception)
        {
            ReportStage("process-start-failed:" + exception.GetType().Name);
            return new DesktopPetModeResult(false, false, "process-start-failed:" + exception.GetType().Name);
        }

        if (process is null)
        {
            ReportStage("process-start-rejected");
            return new DesktopPetModeResult(false, false, "process-start-rejected");
        }
        ReportStage("process-start-finish", generation, pipeName, process);

        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        var transportCancellation = new CancellationTokenSource();
        var connected = false;
        try
        {
            // A process that connects to the pipe but never reports ready is just as unusable as one
            // that never connects. Bound the complete host handshake so Personalization cannot remain
            // IsBusy forever after payload preparation has completed.
            using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupTimeout.CancelAfter(HostReadyTimeout);
            ReportStage("pipe-connect-start");
            await pipe.ConnectAsync(startupTimeout.Token).ConfigureAwait(false);
            connected = true;
            ReportStage("pipe-connect-finish");

            var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

            _process = process;
            _pipe = pipe;
            _reader = reader;
            _writer = writer;
            _transportCancellation = transportCancellation;
            _transportPoisoned = false;
            process.Exited += OnProcessExited;

            const string commandPrefix = "activate|";
            var activateCommand = commandPrefix + pet.Id;
            var activateClock = Stopwatch.StartNew();
            ReportStage("activate-send-start", generation, pipeName, process, activateCommand);
            try
            {
                using var activateTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                activateTimeout.CancelAfter(CommandWriteTimeout);
                await SendCommandAsync(writer, activateCommand, activateTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                PoisonTransport("activate-send-timeout", generation, pipeName, process, activateCommand, activateClock.ElapsedMilliseconds);
                return new DesktopPetModeResult(false, false, "activate-send-timeout");
            }
            catch (Exception exception)
            {
                PoisonTransport("activate-send-failed:" + exception.GetType().Name,
                    generation, pipeName, process, activateCommand, activateClock.ElapsedMilliseconds);
                return new DesktopPetModeResult(false, false, "activate-send-failed:" + exception.GetType().Name);
            }
            ReportStage("activate-send-finish", generation, pipeName, process, activateCommand, activateClock.ElapsedMilliseconds);
            UpdateState(new DesktopPetRuntimeState(true, false, pet.Id,
                preparation.CacheHit ? "host-starting-cache-hit" : "host-starting-new-payload"));

            while (!startupTimeout.Token.IsCancellationRequested && generation == _generation)
            {
                var line = await reader.ReadLineAsync(startupTimeout.Token).ConfigureAwait(false);
                if (line is null) return new DesktopPetModeResult(false, false, "ipc-ended-before-ready");
                if (!TryParseEvent(line, out var eventName, out var detail)) continue;

                if (string.Equals(eventName, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    ReportStage("host-ready", generation, pipeName, process);
                    ReportStage("launcher-hide", generation, pipeName, process);
                    SetLauncherVisible(false);
                    UpdateState(new DesktopPetRuntimeState(true, true, pet.Id, "ready:" + detail));
                    _readLoop = Task.Run(() => ReadLoopAsync(generation, pet.Id, reader, transportCancellation.Token));
                    return new DesktopPetModeResult(true, true, "ready:" + detail);
                }

                if (string.Equals(eventName, "stage", StringComparison.OrdinalIgnoreCase))
                {
                    ReportStage("host-" + detail, generation, pipeName, process);
                    continue;
                }

                if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
                {
                    ReportStage("host-error:" + detail);
                    return new DesktopPetModeResult(false, false, "host-error:" + detail);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new DesktopPetModeResult(false, false, "startup-superseded");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ReportStage(connected ? "host-ready-timeout" : "pipe-connect-timeout");
            return new DesktopPetModeResult(false, false, connected ? "host-ready-timeout" : "ipc-connect-timeout");
        }
        catch (OperationCanceledException)
        {
            ReportStage("host-start-cancelled");
            throw;
        }
        catch (Exception exception)
        {
            ReportStage("host-start-failed:" + exception.GetType().Name);
            return new DesktopPetModeResult(false, false, "host-start-failed:" + exception.GetType().Name);
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
                catch
                {
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void KillAndDisposeProcess(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        try { process.Dispose(); } catch { }
    }

    private async Task ReadLoopAsync(
        int generation,
        string petId,
        StreamReader reader,
        CancellationToken cancellationToken)
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
                {
                    UpdateState(new DesktopPetRuntimeState(true, true, petId, "ready:" + detail));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException exception)
        {
            QueueRecovery(generation, "ipc-io:" + exception.GetType().Name);
        }
        catch (Exception exception)
        {
            QueueRecovery(generation, "ipc-failed:" + exception.GetType().Name);
        }
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        var generation = Volatile.Read(ref _generation);
        QueueRecovery(generation, "process-exited");
    }

    private void QueueRecovery(int generation, string detail) =>
        _ = Task.Run(() => RecoverAsync(generation, detail));

    private async Task RecoverAsync(int generation, string detail)
    {
        if (_disposed) return;
        try
        {
            await _gate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            if (_disposed || generation != _generation) return;
            await StopTransportLockedAsync().ConfigureAwait(false);
            RestoreLauncher("vpet-launcher-restore-recovery");
            UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "runtime-failed:" + detail));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopTransportLockedAsync()
    {
        var generation = ++_generation;
        var process = _process;
        var pipe = _pipe;
        var reader = _reader;
        var writer = _writer;
        var cancellation = _transportCancellation;
        var pipeName = _transportPipeName;
        var poisoned = _transportPoisoned;

        _process = null;
        _pipe = null;
        _reader = null;
        _writer = null;
        _transportCancellation = null;
        _readLoop = null;
        _transportPipeName = null;
        _transportPoisoned = false;

        ReportStage("transport-detach", generation, pipeName, process);
        try { cancellation?.Cancel(); } catch { }
        if (writer is not null && !poisoned)
        {
            const string command = "stop";
            var stopClock = Stopwatch.StartNew();
            ReportStage("stop-send-start", generation, pipeName, process, command);
            try
            {
                using var stopTimeout = new CancellationTokenSource(CommandWriteTimeout);
                await SendCommandAsync(writer, command, stopTimeout.Token).ConfigureAwait(false);
                ReportStage("stop-send-finish", generation, pipeName, process, command, stopClock.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                ReportStage("stop-send-timeout", generation, pipeName, process, command, stopClock.ElapsedMilliseconds);
            }
            catch (Exception exception)
            {
                ReportStage("stop-send-failed:" + exception.GetType().Name,
                    generation, pipeName, process, command, stopClock.ElapsedMilliseconds);
            }
        }
        else if (writer is not null)
        {
            ReportStage("stop-send-skipped-poisoned", generation, pipeName, process, "stop");
        }

        ReportStage("transport-dispose-start", generation, pipeName, process);
        try { writer?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
        try { pipe?.Dispose(); } catch { }
        try { cancellation?.Dispose(); } catch { }
        ReportStage("transport-dispose-finish", generation, pipeName, process);

        if (process is null) return;
        try { process.Exited -= OnProcessExited; } catch { }
        ReportStage("process-wait-start", generation, pipeName, process);
        try
        {
            if (!process.HasExited)
            {
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);
                    ReportStage("process-wait-finish", generation, pipeName, process);
                }
                catch (TimeoutException)
                {
                    ReportStage("process-wait-timeout", generation, pipeName, process);
                    KillProcess(process, generation, pipeName);
                    await WaitForKilledProcessAsync(process, generation, pipeName).ConfigureAwait(false);
                }
            }
            else ReportStage("process-wait-finish", generation, pipeName, process);
        }
        catch (Exception exception)
        {
            ReportStage("process-wait-failed:" + exception.GetType().Name, generation, pipeName, process);
            KillProcess(process, generation, pipeName);
            await WaitForKilledProcessAsync(process, generation, pipeName).ConfigureAwait(false);
        }
        finally
        {
            ReportStage("process-dispose-start", generation, pipeName, process);
            try { process.Dispose(); }
            catch (Exception exception) { ReportStage("process-dispose-failed:" + exception.GetType().Name, generation, pipeName, null); }
            ReportStage("process-dispose-finish", generation, pipeName, null);
        }
    }

    private static async Task SendCommandAsync(StreamWriter writer, string command, CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
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

    private void RestoreLauncher(string stage)
    {
        ReportStage(stage);
        SetLauncherVisible(true);
    }

    private void PoisonTransport(string stage, int generation, string? pipeName, Process? process, string command, long? elapsedMs = null)
    {
        _transportPoisoned = true;
        ReportStage(stage, generation, pipeName, process, command, elapsedMs);
        ReportStage("transport-poisoned", generation, pipeName, process, command, elapsedMs);
    }

    private void KillProcess(Process process, int generation, string? pipeName)
    {
        ReportStage("process-kill-start", generation, pipeName, process);
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                ReportStage("process-kill-finish", generation, pipeName, process);
            }
        }
        catch (Exception exception)
        {
            ReportStage("process-kill-failed:" + exception.GetType().Name, generation, pipeName, process);
        }
    }

    private async Task WaitForKilledProcessAsync(Process process, int generation, string? pipeName)
    {
        ReportStage("process-kill-wait-start", generation, pipeName, process);
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);
            ReportStage("process-kill-wait-finish", generation, pipeName, process);
        }
        catch (TimeoutException)
        {
            ReportStage("process-kill-wait-timeout", generation, pipeName, process);
        }
        catch (Exception exception)
        {
            ReportStage("process-kill-wait-failed:" + exception.GetType().Name, generation, pipeName, process);
        }
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

    private void ReportStage(
        string stage,
        int? generation = null,
        string? pipeName = null,
        Process? process = null,
        string? command = null,
        long? elapsedMs = null)
    {
        try
        {
            var fields = new List<string> { stage };
            if (generation is not null) fields.Add("generation=" + generation.Value);
            if (pipeName is not null) fields.Add("pipeName=" + pipeName);
            if (process is not null)
            {
                try { fields.Add("pid=" + process.Id); } catch { }
            }
            if (command is not null) fields.Add("command=" + command);
            if (elapsedMs is not null) fields.Add("elapsedMs=" + elapsedMs.Value);
            _reportStage?.Invoke(string.Join(';', fields));
        }
        catch { }
    }

    private static bool IsProcessAlive(Process? process)
    {
        if (process is null) return false;
        try { return !process.HasExited; } catch { return false; }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

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
        try { _transportCancellation?.Dispose(); } catch { }
        try
        {
            if (_process is not null && !_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        try { _process?.Dispose(); } catch { }
        _process = null;
        _pipe = null;
        _reader = null;
        _writer = null;
        _transportCancellation = null;
        _transportPipeName = null;
        _transportPoisoned = false;
        _readLoop = null;
        StateChanged = null;
        // Do not dispose _gate or _lifetime here: an in-flight ApplyAsync/ResetPositionAsync may be
        // unwinding through finally and must still be able to release the gate safely.
    }
}
