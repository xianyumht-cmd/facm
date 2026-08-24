using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace FACM.Updater
{
    internal static class Program
    {
        private const string ParentPrefix = "--parent-pid=";
        private const string SourcePrefix = "--source64=";
        private const string DestinationPrefix = "--dest64=";
        private const string HashPrefix = "--sha256=";

        [STAThread]
        private static void Main(string[] args)
        {
            string destination = null;
            try
            {
                var parentPid = ReadIntArgument(args, ParentPrefix);
                var source = DecodePath(ReadArgument(args, SourcePrefix));
                destination = DecodePath(ReadArgument(args, DestinationPrefix));
                var expectedHash = (ReadArgument(args, HashPrefix) ?? string.Empty).Trim();

                ValidateRequest(parentPid, source, destination, expectedHash);
                AppendLog(destination, "apply-start; parentPid=" + parentPid);
                WaitForParentExit(parentPid, TimeSpan.FromSeconds(120));

                var backup = ReplaceFiles(source, destination, expectedHash);
                AppendLog(destination, "replace-success; rollback=" + (File.Exists(backup) ? "ready" : "none"));

                Process restarted = null;
                try
                {
                    restarted = Process.Start(new ProcessStartInfo
                    {
                        FileName = destination,
                        WorkingDirectory = Path.GetDirectoryName(destination),
                        UseShellExecute = true
                    });
                    if (restarted == null) throw new InvalidOperationException("无法启动更新后的 FACM。");

                    AppendLog(destination, "restart-started; pid=" + restarted.Id);
                    if (restarted.WaitForExit(5000))
                    {
                        var exitCode = SafeExitCode(restarted);
                        AppendLog(destination, "restart-exited-early; exitCode=" + exitCode + "; rollback=attempt");
                        TryRollback(destination, backup);
                        TryRestartRollback(destination);
                        throw new InvalidOperationException("新版 FACM 启动后很快退出，已尝试恢复旧版本。");
                    }

                    TryDelete(source);
                    TryDelete(backup);
                    AppendLog(destination, "update-complete; cleanup=done");
                    Environment.ExitCode = 0;
                }
                finally
                {
                    if (restarted != null) restarted.Dispose();
                }
            }
            catch (Exception exception)
            {
                TryAppendFailure(destination, exception);
                try
                {
                    MessageBox.Show(
                        "FACM 自动更新失败。\r\n\r\n" + exception.Message + "\r\n\r\n旧版本已尽量保留，可重新打开 FACM 后重试。",
                        "FACM 更新失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch { }
                Environment.ExitCode = 9;
            }
        }

        private static string ReplaceFiles(string source, string destination, string expectedHash)
        {
            if (!string.Equals(ComputeSha256(source), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新文件在替换前发生变化，已停止安装。");

            var directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidDataException("更新目标目录无效。");
            Directory.CreateDirectory(directory);

            var staging = destination + ".facm-new-" + Guid.NewGuid().ToString("N");
            var backup = destination + ".facm-old";
            TryDelete(staging);

            try
            {
                File.Copy(source, staging, true);
                if (!string.Equals(ComputeSha256(staging), expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新暂存文件校验失败。");

                TryDelete(backup);
                if (File.Exists(destination))
                {
                    try
                    {
                        File.Replace(staging, destination, backup, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        FallbackReplace(staging, destination, backup);
                    }
                    catch (IOException)
                    {
                        FallbackReplace(staging, destination, backup);
                    }
                }
                else
                {
                    File.Move(staging, destination);
                }

                if (!string.Equals(ComputeSha256(destination), expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    TryRollback(destination, backup);
                    throw new InvalidDataException("更新完成后的文件校验失败，已尝试恢复旧版本。");
                }
                return backup;
            }
            finally
            {
                TryDelete(staging);
            }
        }

        private static void FallbackReplace(string staging, string destination, string backup)
        {
            if (File.Exists(destination)) File.Copy(destination, backup, true);
            try
            {
                File.Copy(staging, destination, true);
                TryDelete(staging);
            }
            catch
            {
                TryRollback(destination, backup);
                throw;
            }
        }

        private static void TryRollback(string destination, string backup)
        {
            try
            {
                if (!File.Exists(backup)) return;
                File.Copy(backup, destination, true);
            }
            catch { }
        }

        private static void TryRestartRollback(string destination)
        {
            try
            {
                if (!File.Exists(destination)) return;
                Process.Start(new ProcessStartInfo
                {
                    FileName = destination,
                    WorkingDirectory = Path.GetDirectoryName(destination),
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private static void ValidateRequest(int parentPid, string source, string destination, string expectedHash)
        {
            if (parentPid <= 0) throw new InvalidDataException("更新父进程参数无效。");
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                throw new FileNotFoundException("更新源文件不存在。", source);
            if (string.IsNullOrWhiteSpace(destination) || !destination.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新目标路径无效。");
            if (!IsSha256(expectedHash)) throw new InvalidDataException("更新 SHA-256 参数无效。");

            var updaterDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var sourceDirectory = Path.GetFullPath(Path.GetDirectoryName(source) ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(updaterDirectory, sourceDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新源文件不在 FACM 更新目录中。");
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新源和目标不能是同一个文件。");
        }

        private static void WaitForParentExit(int processId, TimeSpan timeout)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (process.HasExited) return;
                    if (!process.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)))
                        throw new TimeoutException("等待旧 FACM 退出超时。");
                }
            }
            catch (ArgumentException)
            {
                // Parent already exited between process creation and lookup.
            }
        }

        private static string ReadArgument(string[] args, string prefix)
        {
            if (args == null) return null;
            var item = args.FirstOrDefault(value => value != null && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return item == null ? null : item.Substring(prefix.Length);
        }

        private static int ReadIntArgument(string[] args, string prefix)
        {
            int value;
            return int.TryParse(ReadArgument(args, prefix), out value) ? value : 0;
        }

        private static string DecodePath(string encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded)) throw new InvalidDataException("更新路径参数缺失。");
            return Path.GetFullPath(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
        }

        private static string ComputeSha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64) return false;
            foreach (var character in value)
            {
                var valid = character >= '0' && character <= '9' ||
                            character >= 'a' && character <= 'f' ||
                            character >= 'A' && character <= 'F';
                if (!valid) return false;
            }
            return true;
        }

        private static int SafeExitCode(Process process)
        {
            try { return process.ExitCode; }
            catch { return -1; }
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void AppendLog(string destination, string message)
        {
            try
            {
                var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(destination));
                var logs = Path.Combine(baseDirectory, "logs");
                Directory.CreateDirectory(logs);
                File.AppendAllText(
                    Path.Combine(logs, "update-installer.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [INFO] " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }

        private static void TryAppendFailure(string destination, Exception exception)
        {
            if (string.IsNullOrWhiteSpace(destination)) return;
            AppendLog(destination, "apply-failed; " + exception.GetType().Name + "; " + exception.Message);
        }
    }
}
