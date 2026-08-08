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
        private Action _clicked;
        private Action _rightClicked;
        private SynchronizationContext _uiContext;
        private volatile bool _intentionalStop;
        private volatile bool _readyReceived;
        private int _recoveryPosted;
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

        public string ActivePetId
        {
            get
            {
                lock (_sync) return IsActive ? _activePetId : string.Empty;
            }
        }

        public bool Activate(string petId, Action clicked, Action rightClicked)
        {
            _clicked = clicked;
            _rightClicked = rightClicked;
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            lock (_sync)
            {
                if (IsActive)
                {
                    _intentionalStop = false;
                    _activePetId = petId;
                    SendLocked("activate|" + petId);
                    return true;
                }

                CleanupTransportLocked(false);
                _intentionalStop = false;
                _readyReceived = false;
                Interlocked.Exchange(ref _recoveryPosted, 0);

                var executable = LocatePetHost();
                if (string.IsNullOrEmpty(executable))
                {
                    AppLog.Info("VPet PetHost executable was not found.");
                    return false;
                }

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

                _process = Process.Start(startInfo);
                if (_process == null) return false;
                _process.EnableRaisingEvents = true;
                _process.Exited += HandleProcessExited;

                try
                {
                    _pipe = new NamedPipeClientStream(
                        ".",
                        pipeName,
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous);
                    _pipe.Connect(7000);
                    _reader = new StreamReader(_pipe, new UTF8Encoding(false), false, 4096, true);
                    _writer = new StreamWriter(_pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                    _cancellation = new CancellationTokenSource();
                    _activePetId = petId;
                    _readerTask = Task.Run((Func<Task>)ReadLoopAsync);
                    SendLocked("activate|" + petId);
                    AppLog.Info("VPet PetHost connected: " + executable + "; data-root=" + RuntimePaths.PetHostDataDirectory);
                    return true;
                }
                catch (Exception exception)
                {
                    AppLog.Error("VPet PetHost connection failed", exception);
                    _intentionalStop = true;
                    CleanupTransportLocked(true);
                    return false;
                }
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
            _intentionalStop = true;
            lock (_sync)
            {
                if (_process == null)
                {
                    CleanupTransportLocked(false);
                    return;
                }

                try
                {
                    if (IsActive) SendLocked("stop");
                }
                catch { }

                try
                {
                    if (!_process.HasExited && !_process.WaitForExit(1200)) _process.Kill();
                }
                catch { }
                CleanupTransportLocked(false);
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
                return;
            }
            if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
            {
                var detail = parts.Length > 2 ? parts[2] : string.Empty;
                AppLog.Info("VPet PetHost reported error: " + detail);
                if (!_readyReceived) RecoverFacm("PetHost 启动失败：" + detail);
            }
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
                try { _process.Dispose(); } catch { }
            }

            if (_cancellation != null) _cancellation.Dispose();
            _cancellation = null;
            _readerTask = null;
            _writer = null;
            _reader = null;
            _pipe = null;
            _process = null;
            _activePetId = string.Empty;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
