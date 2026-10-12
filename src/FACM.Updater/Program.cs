using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        private const string SelfTestArgument = "--self-test";
        private const int MoveFileReplaceExisting = 0x1;
        private const int MoveFileWriteThrough = 0x8;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Any(value => string.Equals(value, SelfTestArgument, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    RunSelfTest();
                    Environment.ExitCode = 0;
                }
                catch
                {
                    Environment.ExitCode = 8;
                }
                return;
            }

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
                    AtomicReplaceFromStaging(staging, destination);
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
                AtomicReplaceFromStaging(staging, destination);
            }
            catch
            {
                TryRollback(destination, backup);
                throw;
            }
        }

        private static void AtomicReplaceFromStaging(string staging, string destination)
        {
            if (string.IsNullOrWhiteSpace(staging) || string.IsNullOrWhiteSpace(destination))
                throw new InvalidDataException("更新原子替换路径无效。");

            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(staging));
            var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (!string.Equals(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新原子替换必须在同一目录执行。");

            if (!MoveFileEx(staging, destination, MoveFileReplaceExisting | MoveFileWriteThrough))
                throw new IOException("Windows 原子替换 FACM 文件失败。", Marshal.GetHRForLastWin32Error());
        }

        private static void TryRollback(string destination, string backup)
        {
            try
            {
                if (!File.Exists(backup)) return;
                AtomicReplaceFromStaging(backup, destination);
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

        private static void RunSelfTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "facm-updater-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var destination = Path.Combine(root, "FACM.exe");
                var staging = Path.Combine(root, "FACM.exe.facm-new-selftest");
                var backup = destination + ".facm-old";
                var oldBytes = Encoding.UTF8.GetBytes(new string('O', 8192) + "-old");
                var newBytes = Encoding.UTF8.GetBytes(new string('N', 12288) + "-new");
                File.WriteAllBytes(destination, oldBytes);
                File.WriteAllBytes(staging, newBytes);
                var oldHash = ComputeSha256(destination);
                var newHash = ComputeSha256(staging);

                File.Copy(destination, backup, true);
                RequireSelfTest(string.Equals(ComputeSha256(destination), oldHash, StringComparison.OrdinalIgnoreCase),
                    "backup preparation changed the live executable");
                RequireSelfTest(string.Equals(ComputeSha256(backup), oldHash, StringComparison.OrdinalIgnoreCase),
                    "backup preparation did not preserve the old executable");

                AtomicReplaceFromStaging(staging, destination);
                RequireSelfTest(string.Equals(ComputeSha256(destination), newHash, StringComparison.OrdinalIgnoreCase),
                    "atomic replacement did not produce the complete candidate");
                RequireSelfTest(!File.Exists(staging), "atomic replacement left the staging file behind");

                TryRollback(destination, backup);
                RequireSelfTest(string.Equals(ComputeSha256(destination), oldHash, StringComparison.OrdinalIgnoreCase),
                    "atomic rollback did not restore the complete old executable");
                RequireSelfTest(!File.Exists(backup), "atomic rollback did not consume the backup staging file");

                File.WriteAllBytes(staging, newBytes);
                FallbackReplace(staging, destination, backup);
                RequireSelfTest(string.Equals(ComputeSha256(destination), newHash, StringComparison.OrdinalIgnoreCase),
                    "fallback replacement did not produce the complete candidate");
                RequireSelfTest(string.Equals(ComputeSha256(backup), oldHash, StringComparison.OrdinalIgnoreCase),
                    "fallback replacement did not preserve a complete rollback image");
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }

        private static void RequireSelfTest(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FACM updater self-test failed: " + message);
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
