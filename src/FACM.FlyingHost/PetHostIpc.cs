using System.IO.Pipes;
using System.Text;

namespace FACM.FlyingHost;

internal sealed class PetHostIpc : IDisposable
{
    private readonly string? _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NamedPipeServerStream? _pipe;
    private StreamWriter? _writer;
    private Task? _serverTask;
    private Action<string>? _commandHandler;

    public PetHostIpc(string? pipeName)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? null : pipeName;
    }

    public bool IsEnabled => _pipeName != null;

    public void Start(Action<string> commandHandler)
    {
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
        if (_pipeName == null || _serverTask != null) return;
        _serverTask = Task.Run(ServerLoopAsync);
    }

    public Task WaitUntilConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return Task.CompletedTask;
        return _connected.Task.WaitAsync(cancellationToken);
    }

    public async Task SendEventAsync(string name, string? value = null)
    {
        if (IsEnabled && _writer is null)
        {
            try
            {
                await WaitUntilConnectedAsync(_cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                return;
            }
        }

        var line = value == null ? "event|" + name : "event|" + name + "|" + Escape(value);
        await SendLineAsync(line).ConfigureAwait(false);
    }

    private async Task ServerLoopAsync()
    {
        try
        {
            _pipe = new NamedPipeServerStream(
                _pipeName!,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await _pipe.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);
            using var reader = new StreamReader(_pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            _writer = new StreamWriter(_pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            _connected.TrySetResult(true);
            await SendEventAsync("connected").ConfigureAwait(false);

            while (!_cancellation.IsCancellationRequested && _pipe.IsConnected)
            {
                var line = await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false);
                if (line == null) break;
                if (line.Length == 0) continue;
                try
                {
                    _commandHandler?.Invoke(line);
                }
                catch (Exception exception)
                {
                    await SendEventAsync("error", "命令处理失败：" + exception.Message).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _connected.TrySetCanceled(_cancellation.Token);
        }
        catch (IOException exception)
        {
            _connected.TrySetException(exception);
        }
        catch (Exception exception)
        {
            _connected.TrySetException(exception);
            try { await SendEventAsync("error", "IPC 失败：" + exception.Message).ConfigureAwait(false); }
            catch { }
        }
    }

    private async Task SendLineAsync(string line)
    {
        var writer = _writer;
        if (writer == null) return;
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_pipe == null || !_pipe.IsConnected) return;
            await writer.WriteLineAsync(line).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _writeGate.Release();
        }
    }

    internal static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("|", "\\p");
    }

    public void Dispose()
    {
        try { _cancellation.Cancel(); } catch { }
        _connected.TrySetCanceled();
        try { _writer?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _writeGate.Dispose();
        _cancellation.Dispose();
    }
}
