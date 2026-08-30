using System.IO.Pipes;
using System.Reflection;
using System.Text;
using FACM.Core.Runtime;
using FACM.Core.Personalization;
using FACM.Platform.Windows.Personalization;

internal static class DesktopPetIpcLifecycleSmoke
{
    public static async Task RunAsync(string? selected = null)
    {
        if (selected is null or "handshake") await VerifyActivateHandshakeOrderAsync();
        if (selected is null or "cancel") await VerifyCancellationAwareCommandWriteAsync();
        if (selected is null or "stop") await VerifyStopSendFailureIsFailSoftAsync();
        if (selected is null or "sequential") await VerifySequentialHostSessionsAsync();
        Console.WriteLine("Desktop pet IPC lifecycle smoke: SUCCESS");
    }

    private static async Task VerifyActivateHandshakeOrderAsync()
    {
        var pipeName = "FACM.WindowsSmoke.handshake." + Guid.NewGuid().ToString("N");
        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(2));
            using var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

            Equal("activate|real-bee", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)),
                "IPC first server-read command");
            await writer.WriteLineAsync("event|stage|show");
            await writer.WriteLineAsync("event|stage|loaded");
            await writer.WriteLineAsync("event|ready|flying-runtime;pet=real-bee");
        });

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        using var clientReader = new StreamReader(client, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        using var clientWriter = new StreamWriter(client, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

        await clientWriter.WriteLineAsync("activate|real-bee");
        // The first response is read only after activate. A pre-activation "connected" event would
        // therefore occupy this slot and fail the expected show-stage assertion below.
        Equal("event|stage|show", await clientReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)),
            "IPC show stage after activate");
        Equal("event|stage|loaded", await clientReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)),
            "IPC loaded stage after activate");
        Equal("event|ready|flying-runtime;pet=real-bee", await clientReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)),
            "IPC ready after loaded");
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task VerifyCancellationAwareCommandWriteAsync()
    {
        var stream = new CancellationBlockingStream();
        var writer = new StreamWriter(stream, new UTF8Encoding(false), 1, leaveOpen: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var write = SendCommandAsync(writer, "activate|timeout", cancellation.Token);
        try
        {
            await write.WaitAsync(TimeSpan.FromSeconds(1));
            throw new InvalidOperationException("Cancellation-aware IPC write unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
        }
        True(write.IsCompleted, "timed-out IPC command write left a pending task");
        GC.KeepAlive(writer);
        GC.KeepAlive(stream);
    }

    private static async Task SendCommandAsync(StreamWriter writer, string command, CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync(command.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task VerifySequentialHostSessionsAsync()
    {
        var activeHosts = 0;
        var maximumActiveHosts = 0;
        await RunSessionAsync(1);
        await RunSessionAsync(2);
        Equal(1, maximumActiveHosts, "single-host switching maximum active host count");

        async Task RunSessionAsync(int session)
        {
            var pipeName = "FACM.WindowsSmoke.single-host." + session + "." + Guid.NewGuid().ToString("N");
            var serverTask = Task.Run(async () =>
            {
                await using var server = new NamedPipeServerStream(
                    pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(2));
                activeHosts++;
                maximumActiveHosts = Math.Max(maximumActiveHosts, activeHosts);
                using var reader = new StreamReader(server, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                Equal("activate|session-" + session, await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)),
                    "single-host activate command " + session);
                await writer.WriteLineAsync("event|ready|session=" + session);
                Equal("stop", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)),
                    "single-host stop command " + session);
                activeHosts--;
            });

            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(2000);
            using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("activate|session-" + session);
            Equal("event|ready|session=" + session, await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)),
                "single-host ready event " + session);
            await writer.WriteLineAsync("stop");
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            Equal(0, activeHosts, "single-host session released " + session);
        }
    }

    private static async Task VerifyStopSendFailureIsFailSoftAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-ipc-stop-fail-soft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var stages = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var layout = new RuntimePathLayout(
                root,
                Path.Combine(root, "settings.ini"),
                Path.Combine(root, "settings.v2.json"),
                Path.Combine(root, "ui-text.ini"),
                Path.Combine(root, "logs"),
                Path.Combine(root, "runtime"),
                Path.Combine(root, "runtime", "cache"),
                Path.Combine(root, "runtime", "pethost"),
                Path.Combine(root, "runtime", "updates"));
            var store = new WindowsFlyingHostBundleStore(
                layout,
                () => new MemoryStream(new byte[] { 1 }, writable: false));
            using var runtime = new WindowsFlyingPetRuntime(
                store,
                layout.UiTextPath,
                () => { },
                () => { },
                _ => { },
                () => Task.CompletedTask,
                stages.Enqueue);

            var writer = new StreamWriter(new ThrowingWriteStream(), new UTF8Encoding(false), 4096, leaveOpen: false);
            SetPrivateField(runtime, "_writer", writer);
            SetPrivateField(runtime, "_transportPipeName", "FACM.WindowsSmoke.stop-failure");
            var stop = typeof(WindowsFlyingPetRuntime).GetMethod("StopTransportLockedAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("StopTransportLockedAsync was not found.");
            await ((Task)stop.Invoke(runtime, null)!).WaitAsync(TimeSpan.FromSeconds(2));

            True(stages.Any(stage => stage.StartsWith("flying-stop-send-failed:", StringComparison.Ordinal)),
                "closed transport stop write must be recorded as failed");
            True(stages.Any(stage => stage.StartsWith("flying-transport-dispose-finish", StringComparison.Ordinal)),
                "closed transport stop write must still dispose transport");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Private lifecycle field was not found: " + name);
        field.SetValue(target, value);
    }

    private sealed class CancellationBlockingStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
    }

    private sealed class ThrowingWriteStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => throw new IOException("simulated closed transport");
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("simulated closed transport");
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new IOException("simulated closed transport");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("simulated closed transport");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }
}
