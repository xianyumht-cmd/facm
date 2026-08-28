using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using FACM.Core.Repair;

namespace FACM.Platform.Windows.Repair;

public sealed class WindowsRepairToolService : IRepairToolService
{
    internal const string DriverCleanupResourceName = "FACM.Platform.Windows.Resources.DriverCleanup";
    internal const string DriverCleanupOutputName = "FACM-Driver-Cleanup.exe";
    internal const string DriverCleanupSha256 = "4180BAE46BED95661D63DC8D08DD458AE866CC107AB0F00AFC647B9BEB8B4ECA";

    public string DriverCleanupExpectedSha256 => DriverCleanupSha256;

    public RepairToolLaunchResult LaunchDriverCleanup()
    {
        try
        {
            var executable = EnsureDriverCleanupExtracted();
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
            if (process is null)
            {
                return new RepairToolLaunchResult(false, "launch-failed", "驱动清理工具未能启动。");
            }

            var processId = process.Id;
            process.Dispose();
            return new RepairToolLaunchResult(true, "started", "驱动清理工具已启动。", processId);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new RepairToolLaunchResult(false, "cancelled", "已取消启动驱动清理工具。");
        }
        catch (Exception exception)
        {
            return new RepairToolLaunchResult(false, "failed", "驱动清理工具启动失败：" + exception.GetType().Name);
        }
    }

    internal static string EnsureDriverCleanupExtracted(string? runtimeRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(runtimeRoot)
            ? Path.Combine(AppContext.BaseDirectory, "runtime", "tools")
            : Path.GetFullPath(runtimeRoot);
        Directory.CreateDirectory(root);

        var outputPath = Path.Combine(root, DriverCleanupOutputName);
        if (File.Exists(outputPath) && HasExpectedSha256(outputPath, DriverCleanupSha256))
        {
            return outputPath;
        }

        var temporaryPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var resource = typeof(WindowsRepairToolService).Assembly.GetManifestResourceStream(DriverCleanupResourceName))
            {
                if (resource is null)
                {
                    throw new InvalidOperationException("驱动清理工具资源缺失。");
                }

                using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                resource.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            if (!HasExpectedSha256(temporaryPath, DriverCleanupSha256))
            {
                throw new InvalidDataException("驱动清理工具完整性校验失败。");
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
            if (!HasExpectedSha256(outputPath, DriverCleanupSha256))
            {
                throw new InvalidDataException("驱动清理工具落盘后完整性校验失败。");
            }
            return outputPath;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch
            {
                // Temporary cleanup is best-effort and must not hide the launch result.
            }
        }
    }

    internal static bool HasExpectedSha256(string path, string expectedSha256)
    {
        if (!File.Exists(path) || string.IsNullOrWhiteSpace(expectedSha256)) return false;
        using var algorithm = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexString(algorithm.ComputeHash(stream));
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }
}
