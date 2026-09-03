using System;
using System.IO;
using System.Text;

namespace FACM.Services
{
    internal static class AppLog
    {
        internal const long MaxLogBytes = 4 * 1024 * 1024;
        private static readonly object Sync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string CurrentLogPath
        {
            get { return Path.Combine(RuntimePaths.LogsDirectory, "facm-" + DateTime.Now.ToString("yyyyMMdd") + ".log"); }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warning(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                lock (Sync)
                {
                    RuntimePaths.Initialize();
                    var builder = new StringBuilder();
                    builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    builder.Append(" [").Append(level).Append("] ").Append(message ?? string.Empty);
                    if (exception != null)
                    {
                        builder.AppendLine();
                        builder.Append(exception);
                    }
                    builder.AppendLine();
                    AppendBounded(CurrentLogPath, builder.ToString());
                }
            }
            catch
            {
                // Logging must never become a product failure path.
            }
        }

        internal static void AppendBounded(string path, string text)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(text)) return;

            var bytes = Utf8NoBom.GetByteCount(text);
            // One individual diagnostic entry should never make a bounded log unbounded.
            if (bytes <= 0 || bytes > MaxLogBytes) return;

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);

            long existingBytes = 0;
            if (File.Exists(fullPath))
            {
                try { existingBytes = new FileInfo(fullPath).Length; }
                catch { return; }
            }

            if (existingBytes > MaxLogBytes || existingBytes + bytes > MaxLogBytes)
            {
                if (!TryRotate(fullPath)) return;
            }

            File.AppendAllText(fullPath, text, Utf8NoBom);
        }

        internal static string RotatedPath(string logPath)
        {
            if (string.IsNullOrWhiteSpace(logPath)) return string.Empty;
            var extension = Path.GetExtension(logPath);
            return extension.Length == 0
                ? logPath + ".1"
                : logPath.Substring(0, logPath.Length - extension.Length) + ".1" + extension;
        }

        private static bool TryRotate(string path)
        {
            try
            {
                if (!File.Exists(path)) return true;
                var rotated = RotatedPath(path);
                if (File.Exists(rotated)) File.Delete(rotated);
                File.Move(path, rotated);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "FACM-AppLog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, "facm-20260903.log");
                var rotated = RotatedPath(path);
                var nearLimit = new string('a', (int)MaxLogBytes - 64);
                File.WriteAllText(path, nearLimit, Utf8NoBom);

                AppendBounded(path, "rotation-trigger\r\n");
                if (!File.Exists(path) || !File.Exists(rotated))
                    throw new InvalidOperationException("Bounded AppLog did not create a single rotated backup.");
                if (new FileInfo(path).Length > MaxLogBytes || new FileInfo(rotated).Length > MaxLogBytes)
                    throw new InvalidOperationException("Bounded AppLog exceeded its file-size contract.");
                if (File.ReadAllText(path).IndexOf("rotation-trigger", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Bounded AppLog lost the entry that triggered rotation.");

                File.WriteAllText(path, new string('b', (int)MaxLogBytes - 32), Utf8NoBom);
                AppendBounded(path, "second-rotation\r\n");
                if (Directory.GetFiles(root, "facm-20260903.1.log").Length != 1)
                    throw new InvalidOperationException("Bounded AppLog retained more than one rotated backup.");
                if (File.ReadAllText(path).IndexOf("second-rotation", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Bounded AppLog failed after replacing the previous backup.");

                var before = new FileInfo(path).Length;
                AppendBounded(path, new string('z', (int)MaxLogBytes + 1));
                if (new FileInfo(path).Length != before)
                    throw new InvalidOperationException("Bounded AppLog accepted an oversized single entry.");
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }
    }
}
