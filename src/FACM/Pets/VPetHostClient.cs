using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.Pets
{
    internal sealed class VPetHostClient : IDisposable
    {
        private readonly object _sync = new object();
        private Process _process;
        private NamedPipeClientStream _pipe;
        private StreamReader _reader;
        private StreamWriter _writer;
        private CancellationTokenSource _cancellation;
        private Task _readerTask;
        private Task _startupTask;
        private Action _clicked;
        private Action _rightClicked;
        private Action _ready;
        private SynchronizationContext _uiContext;
        private volatile bool _intentionalStop;
        private volatile bool _readyReceived;
        private bool _visibleRequested = true;
        private int _recoveryPosted;
        private int _startupGeneration;
        private string _activePetId = string.Empty;

        public bool IsActive
        {
            get
            {
                lock (_sync)
                {
                    try
                    {
                        return _process != null && !_process.HasExited && _pipe != null && _pipe.IsConnected;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        public bool IsVisible
        {
            get
            {
                lock (_sync) return IsActive && _visibleRequested;
            }
        }

        public string ActivePetId
        {
            get
            {
                lock (_sync) return IsActive ? _activePetId : string.Empty;
            }
        }

        public bool Activate(string petId, Action clicked, Action rightClicked, Action ready)
        {
            _clicked = clicked;
            _rightClicked = rightClicked;
            _ready = ready;
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            lock (_sync)
            {
                _visibleRequested = true;
                if (IsActive)
                {
                    _intentionalStop = false;
                    _activePetId = petId;
                    SendLocked("activate|" + petId);
                    if (_readyReceived) PostToUi(_ready);
                    return true;
                }

                // Starting PetHost used to synchronously extract a large embedded bundle and wait up to
                // seven seconds for NamedPipe.Connect on the WinForms UI thread. Keep the public launch
                // contract synchronous, but make the expensive transport startup a background operation.
                if (_startupTask != null && !_startupTask.IsCompleted)
                {
                    _activePetId = petId;
                    return true;
                }

                CleanupTransportLocked(false);
                _intentionalStop = false;
                _readyReceived = false;
                Interlocked.Exchange(ref _recoveryPosted, 0);
                _activePetId = petId;
                var generation = ++_startupGeneration;
                _startupTask = Task.Run(delegate { StartHost(generation); });
                AppLog.Info("VPet PetHost startup queued in background: " + petId);
                return true;
            }
        }

        public void SetVisible(bool visible)
        {
            lock (_sync)
            {
                _visibleRequested = visible;
                if (!IsActive) return;
                SendLocked(visible ? "show" : "hide");
            }
        }

        public void ResetToPrimaryScreen()
        {
            lock (_sync)
            {
                if (IsActive) SendLocked("reset");
            }
        }

        public void Stop()
        {
            Process process = null;
            _intentionalStop = true;
            lock (_sync)
            {
                ++_startupGeneration;
                try
                {
                    if (IsActive) SendLocked("stop");
                }
                catch { }

                process = _process;
                if (process != null)
                {
                    try { process.Exited -= HandleProcessExited; } catch { }
                }
                CleanupTransportLocked(false, false);
                _activePetId = string.Empty;
                _visibleRequested = true;
                _ready = null;
            }

            // Do not make the WinForms thread wait for WPF shutdown. The Job Object and PetHost's
            // parent watcher are the hard safety net; this is only graceful cleanup.
            if (process != null) StopProcessEventually(process);
        }

        private void StartHost(int generation)
        {
            Process process = null;
            NamedPipeClientStream pipe = null;
            StreamReader reader = null;
            StreamWriter writer = null;
            CancellationTokenSource cancellation = null;

            try
            {
                var executable = LocatePetHost();
                if (string.IsNullOrEmpty(executable))
                    throw new FileNotFoundException("VPet PetHost executable was not found.");

                RuntimePaths.Initialize();
                Directory.CreateDirectory(RuntimePaths.PetHostDataDirectory);
                var pipeName = "FACM.PetHost." + Process.GetCurrentProcess().Id + "." + Guid.NewGuid().ToString("N");
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--pipe \"" + pipeName + "\" --parent-pid " + Process.GetCurrentProcess().Id +
                                " --data-root \"" + RuntimePaths.PetHostDataDirectory + "\"" +
                                " --ui-text \"" + RuntimePaths.UiTextPath + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executable) ?? AppDomain.CurrentDomain.BaseDirectory
                };

                process = Process.Start(startInfo);
                if (process == null) throw new InvalidOperationException("PetHost process could not be started.");
                ChildProcessJob.TryAssign(process);

                pipe = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                pipe.Connect(7000);
                reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
                writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                cancellation = new CancellationTokenSource();

                lock (_sync)
                {
                    if (_intentionalStop || generation != _startupGeneration)
                    {
                        DisposeLocalTransport(process, pipe, reader, writer, cancellation, true);
                        return;
                    }

                    _process = process;
                    _pipe = pipe;
                    _reader = reader;
                    _writer = writer;
                    _cancellation = cancellation;
                    process = null;
                    pipe = null;
                    reader = null;
                    writer = null;
                    cancellation = null;

                    _process.EnableRaisingEvents = true;
                    _process.Exited += HandleProcessExited;
                    _readerTask = Task.Run((Func<Task>)ReadLoopAsync);
                    SendLocked("activate|" + _activePetId);
                    if (!_visibleRequested) SendLocked("hide");
                    AppLog.Info("VPet PetHost connected; data-root=" + RuntimePaths.PetHostDataDirectory);
                }
            }
            catch (Exception exception)
            {
                DisposeLocalTransport(process, pipe, reader, writer, cancellation, true);
                if (!_intentionalStop && generation == Volatile.Read(ref _startupGeneration))
                {
                    AppLog.Error("VPet PetHost background startup failed", exception);
                    RecoverFacm("PetHost 启动失败：" + exception.Message);
                }
            }
            finally
            {
                lock (_sync)
                {
                    if (generation == _startupGeneration && _startupTask != null)
                        _startupTask = null;
                }
            }
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (_cancellation != null && !_cancellation.IsCancellationRequested)
                {
                    var reader = _reader;
                    if (reader == null) break;
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;
                    HandleEvent(line);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException exception)
            {
                if (!_intentionalStop) RecoverFacm("PetHost IPC 已断开：" + exception.Message);
            }
            catch (Exception exception)
            {
                AppLog.Info("VPet PetHost event loop ended: " + exception.Message);
                if (!_intentionalStop) RecoverFacm("PetHost 事件通道异常：" + exception.Message);
            }
        }

        private void HandleEvent(string line)
        {
            if (!line.StartsWith("event|", StringComparison.OrdinalIgnoreCase)) return;
            var parts = line.Split(new[] { '|' }, 3);
            var eventName = parts.Length > 1 ? parts[1] : string.Empty;
            if (string.Equals(eventName, "click", StringComparison.OrdinalIgnoreCase))
            {
                var callback = _clicked;
                if (callback != null) callback();
                return;
            }
            if (string.Equals(eventName, "right-click", StringComparison.OrdinalIgnoreCase))
            {
                var callback = _rightClicked;
                if (callback != null) callback();
                return;
            }
            if (string.Equals(eventName, "ready", StringComparison.OrdinalIgnoreCase))
            {
                _readyReceived = true;
                AppLog.Info("VPet PetHost ready: " + (parts.Length > 2 ? parts[2] : string.Empty));
                PostToUi(_ready);
                return;
            }
            if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
            {
                var detail = parts.Length > 2 ? parts[2] : string.Empty;
                AppLog.Info("VPet PetHost reported error: " + detail);
                if (!_readyReceived) RecoverFacm("PetHost 启动失败：" + detail);
            }
        }

        private void PostToUi(Action callback)
        {
            if (callback == null) return;
            var context = _uiContext;
            if (context == null) return;
            context.Post(delegate
            {
                try { callback(); }
                catch (Exception exception) { AppLog.Error("VPet PetHost UI callback failed", exception); }
            }, null);
        }

        private void RecoverFacm(string reason)
        {
            if (_intentionalStop) return;
            if (Interlocked.Exchange(ref _recoveryPosted, 1) != 0) return;

            AppLog.Info(reason + "；正在恢复 FACM 默认悬浮球。");
            var context = _uiContext;
            if (context == null)
            {
                AppLog.Info("FACM UI context is unavailable; automatic PetHost recovery was skipped.");
                return;
            }

            context.Post(delegate
            {
                try
                {
                    foreach (Form form in Application.OpenForms)
                    {
                        var main = form as FACM.MainForm;
                        if (main == null || main.IsDisposed) continue;
                        main.RestoreDefaultBall();
                        return;
                    }
                    AppLog.Info("FACM MainForm was not found while recovering from PetHost failure.");
                }
                catch (Exception exception)
                {
                    AppLog.Error("Failed to restore FACM after PetHost failure", exception);
                }
            }, null);
        }

        private void SendLocked(string command)
        {
            if (_writer == null || _pipe == null || !_pipe.IsConnected) return;
            _writer.WriteLine(command);
            _writer.Flush();
        }

        private void HandleProcessExited(object sender, EventArgs e)
        {
            AppLog.Info("VPet PetHost process exited.");
            if (!_intentionalStop) RecoverFacm("PetHost 进程意外退出");
        }

        private static string LocatePetHost()
        {
            // A formal FACM build carries its exact PetHost payload. Prefer it over any sidecar left
            // by an older installation so a single-EXE online upgrade cannot accidentally run a stale host.
            var embedded = PetHostBundleLoader.TryEnsureExtracted();
            if (!string.IsNullOrWhiteSpace(embedded) && File.Exists(embedded)) return embedded;

            var packaged = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PetHost", "FACM.PetHost.exe");
            if (File.Exists(packaged)) return packaged;

            try
            {
                var development = Path.GetFullPath(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "FACM.PetHost",
                    "bin",
                    "Release",
                    "net8.0-windows",
                    "FACM.PetHost.exe"));
                if (File.Exists(development)) return development;
            }
            catch { }

            return string.Empty;
        }

        private void CleanupTransportLocked(bool terminateProcess)
        {
            CleanupTransportLocked(terminateProcess, true);
        }

        private void CleanupTransportLocked(bool terminateProcess, bool disposeProcess)
        {
            try { if (_cancellation != null) _cancellation.Cancel(); } catch { }
            try { if (_writer != null) _writer.Dispose(); } catch { }
            try { if (_reader != null) _reader.Dispose(); } catch { }
            try { if (_pipe != null) _pipe.Dispose(); } catch { }

            if (_process != null)
            {
                try { _process.Exited -= HandleProcessExited; } catch { }
                if (terminateProcess)
                {
                    try { if (!_process.HasExited) _process.Kill(); } catch { }
                }
                if (disposeProcess)
                {
                    try { _process.Dispose(); } catch { }
                }
            }

            if (_cancellation != null) _cancellation.Dispose();
            _cancellation = null;
            _readerTask = null;
            _writer = null;
            _reader = null;
            _pipe = null;
            _process = null;
            if (_intentionalStop) _activePetId = string.Empty;
        }

        private static void DisposeLocalTransport(
            Process process,
            NamedPipeClientStream pipe,
            StreamReader reader,
            StreamWriter writer,
            CancellationTokenSource cancellation,
            bool terminateProcess)
        {
            try { if (cancellation != null) cancellation.Cancel(); } catch { }
            try { if (writer != null) writer.Dispose(); } catch { }
            try { if (reader != null) reader.Dispose(); } catch { }
            try { if (pipe != null) pipe.Dispose(); } catch { }
            try { if (cancellation != null) cancellation.Dispose(); } catch { }
            if (process == null) return;
            if (terminateProcess)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
            }
            try { process.Dispose(); } catch { }
        }

        private static void StopProcessEventually(Process process)
        {
            Task.Run(delegate
            {
                try
                {
                    if (!process.HasExited && !process.WaitForExit(1200)) process.Kill();
                }
                catch { }
                finally
                {
                    try { process.Dispose(); } catch { }
                }
            });
        }

        public void Dispose()
        {
            Stop();
        }
    }
}