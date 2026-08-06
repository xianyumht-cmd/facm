using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace FACM.Services
{
    internal static class ToolRunner
    {
        private const string ResourceName = "FACM.Resources.FixLcu.GzipBase64";
        private const string ExpectedSha256 = "A30E8ABD86AF01746EC63E2B51F80B83703965D5F1001768236F8BE3B5A3B935";

        public static void RunFixLcu(int mode)
        {
            if (mode < 1 || mode > 4) throw new ArgumentOutOfRangeException(nameof(mode));
            var executable = EnsureFixLcuExtracted();
            AppLog.Info("Run built-in Fix-LCU-Window mode " + mode);
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--mode " + mode,
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = true
            });
        }

        private static string EnsureFixLcuExtracted()
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FACM", "Tools");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "Fix-LCU-Window.exe");
            if (File.Exists(path) && string.Equals(ComputeSha256(path), ExpectedSha256, StringComparison.OrdinalIgnoreCase)) return path;

            byte[] gzipBytes;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidOperationException("内置修复工具资源缺失。");
                using (var reader = new StreamReader(stream)) gzipBytes = Convert.FromBase64String(reader.ReadToEnd());
            }

            using (var compressed = new MemoryStream(gzipBytes, false))
            using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
            using (var output = File.Create(path))
            {
                gzip.CopyTo(output);
            }

            var actual = ComputeSha256(path);
            if (!string.Equals(actual, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(path); } catch { }
                throw new InvalidDataException("内置修复工具校验失败，已停止运行。");
            }
            return path;
        }

        private static string ComputeSha256(string path)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }
}
