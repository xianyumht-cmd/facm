using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using FACM.Core.Personalization;
using FACM.Core.Runtime;

namespace FACM.Platform.Windows.Personalization;

public sealed class WindowsFlyingPetRuntime : IDesktopPetRuntime, IDisposable
{
    private static readonly TimeSpan HostReadyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HostProcessStartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CommandWriteTimeout = TimeSpan.FromMilliseconds(750);

    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WindowsFlyingHostBundleStore _bundleStore;
    private readonly IComponentAvailability _componentAvailability;
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
    private string? _transportPipeName;
    private bool _transportPoisoned;
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
        Func<ProcessStartInfo, Task<Process?>>? launchProcess = null,
        IComponentAvailability? componentAvailability = null)
    {
        _bundleStore = bundleStore ?? throw new ArgumentNullException(nameof(bundleStore));
        _componentAvailability = componentAvailability ?? AlwaysAvailableComponentAvailability.Instance;
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
                RestoreLauncher("flying-launcher-restore");
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "launcher-restored"));
                return new DesktopPetModeResult(true, false, "launcher-restored");
            }

            if (pet.Runtime != FacmPetRuntimeKind.FlyingSprite)
            {
                await StopTransportLockedAsync().ConfigureAwait(false);
                RestoreLauncher("flying-launcher-restore-unsupported");
                var unsupported = "runtime-unsupported:" + pet.Runtime;
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, unsupported));
                return new DesktopPetModeResult(false, false, unsupported);
            }

            if (!_componentAvailability.IsAvailable(FacmComponentIds.FlyingHostWinX64))
            {
                await StopTransportLockedAsync().ConfigureAwait(false);
                RestoreLauncher("component-unavailable");
                var detail = $"component-unavailable;requestedStyleId={pet.Id};requiredComponent={FacmComponentIds.FlyingHostWinX64}";
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, detail));
                return new DesktopPetModeResult(false, false, detail);
            }

            var existing = Current;
            if (existing.PetVisible &&
                string.Equals(existing.ActivePetId, pet.Id, StringComparison.OrdinalIgnoreCase) &&
                IsProcessAlive(_process))
            {
                return new DesktopPetModeResult(true, true, "already-active");
            }

            await StopTransportLockedAsync().ConfigureAwait(false);
            RestoreLauncher("flying-launcher-restore-before-start");
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

            DesktopPetModeResult result;
            try
            {
                result = await StartFlyingHostLockedAsync(preparation, pet, operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await StopTransportLockedAsync().ConfigureAwait(false);
                RestoreLauncher("flying-launcher-restore-cancelled");
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "flying-start-cancelled"));
                throw;
            }
            if (!result.Success)
            {
                await StopTransportLockedAsync().ConfigureAwait(false);
                RestoreLauncher("flying-launcher-restore-failure");
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
                    PoisonTransport("flying-reset-send-timeout", Volatile.Read(ref _generation), _transportPipeName, _process, "reset");
                }
                catch (Exception exception)
                {
                    PoisonTransport("flying-reset-send-failed:" + exception.GetType().Name,
                        Volatile.Read(ref _generation), _transportPipeName, _process, "reset");
                }
            }
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
        _transportPipeName = pipeName;
        _transportPoisoned = false;
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
        ReportStage("flying-process-start-finish", generation, pipeName, process);

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
            _transportPoisoned = false;
            process.Exited += OnProcessExited;

            const string command = "activate|";
            var activateCommand = command + pet.Id;
            var activateClock = Stopwatch.StartNew();
            ReportStage("flying-activate-send-start", generation, pipeName, process, activateCommand);
            try
            {
                using var activateTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                activateTimeout.CancelAfter(CommandWriteTimeout);
                await SendCommandAsync(writer, activateCommand, activateTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                PoisonTransport("flying-activate-send-timeout", generation, pipeName, process, activateCommand, activateClock.ElapsedMilliseconds);
                return new DesktopPetModeResult(false, false, "flying-activate-send-timeout");
            }
            catch (Exception exception)
            {
                PoisonTransport("flying-activate-send-failed:" + exception.GetType().Name,
                    generation, pipeName, process, activateCommand, activateClock.ElapsedMilliseconds);
                return new DesktopPetModeResult(false, false, "flying-activate-send-failed:" + exception.GetType().Name);
            }
            ReportStage("flying-activate-send-finish", generation, pipeName, process, activateCommand, activateClock.ElapsedMilliseconds);
            UpdateState(new DesktopPetRuntimeState(true, false, pet.Id,
                preparation.CacheHit ? "flying-host-starting-cache-hit" : "flying-host-starting-new-payload"));

            while (!startupTimeout.Token.IsCancellationRequested && generation == _generation)
            {
                var line = await reader.ReadLineAsync(startupTimeout.Token).ConfigureAwait(false);
                if (line is null) return new DesktopPetModeResult(false, false, "flying-ipc-ended-before-ready");
                if (!TryParseEvent(line, out var eventName, out var detail)) continue;
                if (string.Equals(eventName, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    ReportStage("flying-host-ready", generation, pipeName, process);
                    ReportStage("flying-launcher-hide", generation, pipeName, process);
                    SetLauncherVisible(false);
                    UpdateState(new DesktopPetRuntimeState(true, true, pet.Id, "ready:" + detail));
                    _ = Task.Run(() => ReadLoopAsync(generation, pet.Id, reader, transportCancellation.Token));
                    return new DesktopPetModeResult(true, true, "ready:" + detail);
                }
                if (string.Equals(eventName, "stage", StringComparison.OrdinalIgnoreCase))
                {
                    ReportStage("flying-host-" + detail, generation, pipeName, process);
                    continue;
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
            RestoreLauncher("flying-launcher-restore-recovery");
            UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "runtime-failed:flying-" + detail));
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
        _transportPipeName = null;
        _transportPoisoned = false;

        ReportStage("flying-transport-detach", generation, pipeName, process);
        try { cancellation?.Cancel(); } catch { }
        if (writer is not null && !poisoned)
        {
            const string command = "stop";
            var stopClock = Stopwatch.StartNew();
            ReportStage("flying-stop-send-start", generation, pipeName, process, command);
            try
            {
                using var stopTimeout = new CancellationTokenSource(CommandWriteTimeout);
                await SendCommandAsync(writer, command, stopTimeout.Token).ConfigureAwait(false);
                ReportStage("flying-stop-send-finish", generation, pipeName, process, command, stopClock.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                ReportStage("flying-stop-send-timeout", generation, pipeName, process, command, stopClock.ElapsedMilliseconds);
            }
            catch (Exception exception)
            {
                ReportStage("flying-stop-send-failed:" + exception.GetType().Name,
                    generation, pipeName, process, command, stopClock.ElapsedMilliseconds);
            }
        }
        else if (writer is not null)
        {
            ReportStage("flying-stop-send-skipped-poisoned", generation, pipeName, process, "stop");
        }

        ReportStage("flying-transport-dispose-start", generation, pipeName, process);
        try { writer?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
        try { pipe?.Dispose(); } catch { }
        try { cancellation?.Dispose(); } catch { }
        ReportStage("flying-transport-dispose-finish", generation, pipeName, process);

        if (process is null) return;
        try { process.Exited -= OnProcessExited; } catch { }
        ReportStage("flying-process-wait-start", generation, pipeName, process);
        try
        {
            if (!process.HasExited)
            {
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);
                    ReportStage("flying-process-wait-finish", generation, pipeName, process);
                }
                catch (TimeoutException)
                {
                    ReportStage("flying-process-wait-timeout", generation, pipeName, process);
                    KillProcess(process, generation, pipeName);
                    await WaitForKilledProcessAsync(process, generation, pipeName).ConfigureAwait(false);
                }
            }
            else ReportStage("flying-process-wait-finish", generation, pipeName, process);
        }
        catch (Exception exception)
        {
            ReportStage("flying-process-wait-failed:" + exception.GetType().Name, generation, pipeName, process);
            KillProcess(process, generation, pipeName);
            await WaitForKilledProcessAsync(process, generation, pipeName).ConfigureAwait(false);
        }
        finally
        {
            ReportStage("flying-process-dispose-start", generation, pipeName, process);
            try { process.Dispose(); }
            catch (Exception exception) { ReportStage("flying-process-dispose-failed:" + exception.GetType().Name, generation, pipeName, null); }
            ReportStage("flying-process-dispose-finish", generation, pipeName, null);
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
        ReportStage("flying-transport-poisoned", generation, pipeName, process, command, elapsedMs);
    }

    private void KillProcess(Process process, int generation, string? pipeName)
    {
        ReportStage("flying-process-kill-start", generation, pipeName, process);
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                ReportStage("flying-process-kill-finish", generation, pipeName, process);
            }
        }
        catch (Exception exception)
        {
            ReportStage("flying-process-kill-failed:" + exception.GetType().Name, generation, pipeName, process);
        }
    }

    private async Task WaitForKilledProcessAsync(Process process, int generation, string? pipeName)
    {
        ReportStage("flying-process-kill-wait-start", generation, pipeName, process);
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);
            ReportStage("flying-process-kill-wait-finish", generation, pipeName, process);
        }
        catch (TimeoutException)
        {
            ReportStage("flying-process-kill-wait-timeout", generation, pipeName, process);
        }
        catch (Exception exception)
        {
            ReportStage("flying-process-kill-wait-failed:" + exception.GetType().Name, generation, pipeName, process);
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
        try { _transportCancellation?.Dispose(); } catch { }
        try
        {
            if (_process is not null && !_process.HasExited)
            {
                ReportStage("flying-runtime-dispose-kill", _generation, _transportPipeName, _process);
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { }
        try { _process?.Dispose(); } catch { }
        _process = null;
        _pipe = null;
        _reader = null;
        _writer = null;
        _transportCancellation = null;
        _transportPipeName = null;
        _transportPoisoned = false;
        StateChanged = null;
    }
}
