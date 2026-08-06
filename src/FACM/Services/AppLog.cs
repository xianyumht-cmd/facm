using System;
using System.IO;
using System.Text;

namespace FACM.Services
{
    internal static class AppLog
    {
        private static readonly object Sync = new object();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FACM",
            "Logs");

        public static string CurrentLogPath
        {
            get { return Path.Combine(LogDirectory, "facm-" + DateTime.Now.ToString("yyyyMMdd") + ".log"); }
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
                    Directory.CreateDirectory(LogDirectory);
                    var builder = new StringBuilder();
                    builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    builder.Append(" [").Append(level).Append("] ").Append(message ?? string.Empty);
                    if (exception != null)
                    {
                        builder.AppendLine();
                        builder.Append(exception);
                    }
                    builder.AppendLine();
                    File.AppendAllText(CurrentLogPath, builder.ToString(), new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must never crash the cleanup application.
            }
        }
    }
}
