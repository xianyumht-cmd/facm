using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FACM.Online
{
    internal static class UpdateReplacementHost
    {
        private const string ApplySwitch = "--facm-update-apply";
        private const string CleanupSwitch = "--facm-update-cleanup";
        private const string ParentPrefix = "--parent-pid=";
        private const string DestinationPrefix = "--dest64=";
        private const string HashPrefix = "--sha256=";
        private const string CleanupSourcePrefix = "--cleanup-source64=";
        private const string CleanupBackupPrefix = "--cleanup-backup64=";

        public static bool TryRunApplyMode(string[] args)
        {
            if (!HasArgument(args, ApplySwitch)) return false;

            string destination = null;
            try
            {
                var parentPid = ReadIntArgument(args, ParentPrefix);
                destination = DecodePath(ReadArgument(args, DestinationPrefix));
                var expectedHash = (ReadArgument(args, HashPrefix) ?? string.Empty).Trim();
                var source = Process.GetCurrentProcess().MainModule.FileName;

                ValidateApplyRequest(source, destination, expectedHash, parentPid);
                AppendInstallerLog(destination, "apply-start; parentPid=" + parentPid);
                WaitForParentExit(parentPid, TimeSpan.FromSeconds(120));

                var backup = ReplaceFilesCore(source, destination, expectedHash);
                AppendInstallerLog(destination, "replace-success; backup=" + (File.Exists(backup) ? "present" : "none"));

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = destination,
                        Arguments = BuildCleanupArguments(source, backup),
                        WorkingDirectory = Path.GetDirectoryName(destination),
                        UseShellExecute = true
                    });
                    AppendInstallerLog(destination, "restart-started");
                }
                catch
                {
                    TryRollback(destination, backup);
                    AppendInstallerLog(destination, "restart-failed; rollback-attempted");
                    throw;
                }

                Environment.ExitCode = 0;
            }
            catch (Exception exception)
            {
                TryAppendInstallerFailure(destination, exception);
                try
                {
                    MessageBox.Show(
                        "FACM 更新替换失败，旧版本已尽量保留。\r\n\r\n" + exception.Message,
                        "FACM 更新失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch { }
                Environment.ExitCode = 9;
            }
            return true;
        }

        public static void ScheduleCleanup(string[] args)
        {
            if (!HasArgument(args, CleanupSwitch)) return;

            var source = SafeDecodePath(ReadArgument(args, CleanupSourcePrefix));
            var backup = SafeDecodePath(ReadArgument(args, CleanupBackupPrefix));
            Task.Run(async () =>
            {
                // Give the updater process time to exit and keep the rollback copy around until
                // the replacement FACM has successfully reached normal startup.
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    TryDelete(source);
                    TryDelete(backup);
                    if ((!File.Exists(source)) && (!File.Exists(backup))) return;
                    await Task.Delay(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false);
                }
            });
        }

        public static string BuildApplyArguments(int parentPid, string destination, string expectedHash)
        {
            if (parentPid <= 0) throw new ArgumentOutOfRangeException(nameof(parentPid));
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentNullException(nameof(destination));
            if (!IsSha256(expectedHash)) throw new ArgumentException("Expected SHA-256 is invalid.", nameof(expectedHash));

            return ApplySwitch +
                   " " + ParentPrefix + parentPid +
                   " " + DestinationPrefix + Quote(EncodePath(destination)) +
                   " " + HashPrefix + expectedHash.ToUpperInvariant();
        }

        internal static void ValidateForSmokeTest()
        {
            var unicode = Path.Combine(Path.GetTempPath(), "FACM 更新 测试", "FACM.exe");
            var encoded = EncodePath(unicode);
            if (!string.Equals(DecodePath(encoded), unicode, StringComparison.Ordinal))
                throw new InvalidOperationException("Update path encoding did not round-trip.");

            var root = Path.Combine(Path.GetTempPath(), "FACM-update-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var source = Path.Combine(root, "FACM-NEW.exe");
                var destination = Path.Combine(root, "FACM.exe");
                File.WriteAllText(source, "new-binary", Encoding.UTF8);
                File.WriteAllText(destination, "old-binary", Encoding.UTF8);
                var expectedHash = ComputeSha256(source);
                var backup = ReplaceFilesCore(source, destination, expectedHash);

                if (File.ReadAllText(destination, Encoding.UTF8) != "new-binary")
                    throw new InvalidOperationException("Replacement did not install the new file.");
                if (!File.Exists(backup) || File.ReadAllText(backup, Encoding.UTF8) != "old-binary")
                    throw new InvalidOperationException("Replacement did not preserve the rollback file.");
                if (!string.Equals(ComputeSha256(destination), expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Installed file hash did not match the source hash.");

                var args = BuildApplyArguments(123, unicode, expectedHash);
                if (!args.Contains(ApplySwitch) || !args.Contains(ParentPrefix + "123") || !args.Contains(HashPrefix + expectedHash))
                    throw new InvalidOperationException("Apply-mode arguments were not composed correctly.");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static string ReplaceFilesCore(string source, string destination, string expectedHash)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("更新源文件不存在。", source);
            if (!IsSha256(expectedHash)) throw new InvalidDataException("更新替换缺少有效 SHA-256。");
            if (!string.Equals(ComputeSha256(source), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新源文件在替换前发生变化，已停止安装。");

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

        private static void ValidateApplyRequest(string source, string destination, string expectedHash, int parentPid)
        {
            if (parentPid <= 0) throw new InvalidDataException("更新父进程参数无效。");
            if (string.IsNullOrWhiteSpace(destination)) throw new InvalidDataException("更新目标路径为空。");
            destination = Path.GetFullPath(destination);
            source = Path.GetFullPath(source);
            if (!destination.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新目标必须是 EXE 文件。");
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新源和目标不能是同一个文件。");
            if (!IsSha256(expectedHash)) throw new InvalidDataException("更新 SHA-256 参数无效。");
            if (!File.Exists(source)) throw new FileNotFoundException("更新源文件不存在。", source);
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
                // Process already exited between launch and lookup.
            }
        }

        private static string BuildCleanupArguments(string source, string backup)
        {
            return CleanupSwitch +
                   " " + CleanupSourcePrefix + Quote(EncodePath(source)) +
                   " " + CleanupBackupPrefix + Quote(EncodePath(backup));
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

        private static bool HasArgument(string[] args, string value)
        {
            return args != null && args.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        }

        private static string EncodePath(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(value ?? string.Empty)));
        }

        private static string DecodePath(string encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded)) throw new InvalidDataException("更新路径参数缺失。");
            return Path.GetFullPath(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
        }

        private static string SafeDecodePath(string encoded)
        {
            try { return DecodePath(encoded); }
            catch { return null; }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        internal static string ComputeSha256(string path)
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

        private static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void AppendInstallerLog(string destination, string message)
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

        private static void TryAppendInstallerFailure(string destination, Exception exception)
        {
            if (string.IsNullOrWhiteSpace(destination)) return;
            AppendInstallerLog(destination, "apply-failed; " + exception.GetType().Name + "; " + exception.Message);
        }
    }
}
