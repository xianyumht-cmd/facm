using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using FACM.Core.Personalization;

namespace FACM.Platform.Windows.Personalization;

public sealed class WindowsVPetRuntime : IDesktopPetRuntime, IDisposable
{
    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly WindowsPetHostBundleStore _bundleStore;
    private readonly string _dataRoot;
    private readonly string _uiTextPath;
    private readonly Action _openRequested;
    private readonly Action _contextRequested;
    private readonly Action<bool> _setLauncherVisible;
    private readonly Func<Task> _resetLauncherPosition;

    private DesktopPetRuntimeState _current = new(false, false, string.Empty, "launcher-only");
    private Process? _process;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _transportCancellation;
    private Task? _readLoop;
    private int _generation;
    private bool _disposed;

    public WindowsVPetRuntime(
        WindowsPetHostBundleStore bundleStore,
        string dataRoot,
        string uiTextPath,
        Action openRequested,
        Action contextRequested,
        Action<bool> setLauncherVisible,
        Func<Task> resetLauncherPosition)
    {
        _bundleStore = bundleStore ?? throw new ArgumentNullException(nameof(bundleStore));
        _dataRoot = string.IsNullOrWhiteSpace(dataRoot) ? throw new ArgumentException("PetHost data root is required.", nameof(dataRoot)) : dataRoot;
        _uiTextPath = uiTextPath ?? string.Empty;
        _openRequested = openRequested ?? throw new ArgumentNullException(nameof(openRequested));
        _contextRequested = contextRequested ?? throw new ArgumentNullException(nameof(contextRequested));
        _setLauncherVisible = setLauncherVisible ?? throw new ArgumentNullException(nameof(setLauncherVisible));
        _resetLauncherPosition = resetLauncherPosition ?? throw new ArgumentNullException(nameof(resetLauncherPosition));
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!enabled)
            {
                await StopTransportLockedAsync().ConfigureAwait(false);
                SetLauncherVisible(true);
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "launcher-restored"));
                return new DesktopPetModeResult(true, false, "launcher-restored");
            }

            if (pet.Runtime == FacmPetRuntimeKind.LegacyCompatibility)
            {
                SetLauncherVisible(true);
                UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "runtime-unsupported:" + pet.Runtime));
                return new DesktopPetModeResult(false, false, "runtime-unsupported:" + pet.Runtime);
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
            UpdateState(new DesktopPetRuntimeState(true, false, pet.Id, "payload-preparing"));

            PetHostBundlePreparation preparation;
            try
            {
                preparation = await _bundleStore.PrepareAsync(cancellationToken).ConfigureAwait(false);
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

            var result = await StartPetHostLockedAsync(preparation, pet, cancellationToken).ConfigureAwait(false);
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_writer is not null) await TrySendAsync(_writer, "reset").ConfigureAwait(false);
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
        startInfo.ArgumentList.Add("--runtime");
        startInfo.ArgumentList.Add(pet.Runtime == FacmPetRuntimeKind.FlyingSprite ? "flying" : "vpet");
        startInfo.ArgumentList.Add("--pet-id");
        startInfo.ArgumentList.Add(pet.Id);
        if (!string.IsNullOrWhiteSpace(_uiTextPath))
        {
            startInfo.ArgumentList.Add("--ui-text");
            startInfo.ArgumentList.Add(_uiTextPath);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            return new DesktopPetModeResult(false, false, "process-start-rejected");
        }
        _ = WindowsChildProcessJob.TryAssign(process);

        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        var transportCancellation = new CancellationTokenSource();
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(7));
            await pipe.ConnectAsync(connectTimeout.Token).ConfigureAwait(false);

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
                preparation.CacheHit ? "host-starting-cache-hit" : "host-starting-new-payload"));

            while (!cancellationToken.IsCancellationRequested && generation == _generation)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) return new DesktopPetModeResult(false, false, "ipc-ended-before-ready");
                if (!TryParseEvent(line, out var eventName, out var detail)) continue;

                if (string.Equals(eventName, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    SetLauncherVisible(false);
                    UpdateState(new DesktopPetRuntimeState(true, true, pet.Id, "ready:" + detail));
                    _readLoop = Task.Run(() => ReadLoopAsync(generation, pet.Id, reader, transportCancellation.Token));
                    return new DesktopPetModeResult(true, true, "ready:" + detail);
                }

                if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
                    return new DesktopPetModeResult(false, false, "host-error:" + detail);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new DesktopPetModeResult(false, false, "startup-superseded");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DesktopPetModeResult(false, false, "ipc-connect-timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new DesktopPetModeResult(false, false, "host-start-failed:" + exception.GetType().Name);
        }
        finally
        {
            if (!ReferenceEquals(_process, process))
            {
                transportCancellation.Dispose();
                try { pipe.Dispose(); } catch { }
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                process.Dispose();
            }
        }
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
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || generation != _generation) return;
            await StopTransportLockedAsync().ConfigureAwait(false);
            SetLauncherVisible(true);
            UpdateState(new DesktopPetRuntimeState(false, false, string.Empty, "runtime-failed:" + detail));
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
        _readLoop = null;

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
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    process.Kill(entireProcessTree: true);
                }
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
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
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
        try { _transportCancellation?.Cancel(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        try
        {
            if (_process is not null && !_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        try { _process?.Dispose(); } catch { }
        _transportCancellation?.Dispose();
        _gate.Dispose();
        _process = null;
        _pipe = null;
        _reader = null;
        _writer = null;
        _transportCancellation = null;
        _readLoop = null;
    }
}
