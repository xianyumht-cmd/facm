using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace FACM.Services
{
    internal sealed class DiagnosticsExportReceipt
    {
        public string BundlePath { get; set; }
        public long BundleBytes { get; set; }
        public int LogFilesIncluded { get; set; }
        public int LogFilesSkipped { get; set; }
    }

    /// <summary>
    /// Lightweight FACM 3.5 adaptation of the bounded/redacted FACM 4 diagnostics exporter.
    /// It only reads two allowlisted FACM log paths (today/yesterday), never enumerates user folders,
    /// scrubs credentials and local paths before writing, and atomically publishes a bounded ZIP.
    /// </summary>
    internal static class DiagnosticsExportService
    {
        private const long MaxLogFileBytes = 4 * 1024 * 1024;
        private const long MaxTotalInputBytes = 8 * 1024 * 1024;
        private const long MaxBundleBytes = 8 * 1024 * 1024;
        private const int MaxSummaryChars = 64 * 1024;

        private static readonly Regex AuthorizationRegex = new Regex(
            @"(?i)\b(Basic|Bearer)\s+[A-Za-z0-9._~+/=-]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SensitiveAssignmentRegex = new Regex(
            @"(?i)\b(token|password|passwd|secret|authorization|api[-_]?key)\s*[:=]\s*[^\s;,\r\n]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex WindowsPathRegex = new Regex(
            @"(?i)(?<![A-Za-z0-9])[A-Z]:\\(?:[^\\/:*?<>|\r\n]+\\)*[^\\/:*?<>|\r\n]*",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex UncPathRegex = new Regex(
            @"\\\\[^\\\s]+\\[^\r\n;,|<>]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static DiagnosticsExportReceipt ExportCurrent()
        {
            RuntimePaths.Initialize();
            Directory.CreateDirectory(RuntimePaths.DiagnosticsDirectory);
            return Export(
                RuntimePaths.LogsDirectory,
                RuntimePaths.DiagnosticsDirectory,
                DateTime.Now,
                ResolveAppVersion());
        }

        internal static DiagnosticsExportReceipt Export(
            string logsDirectory,
            string outputDirectory,
            DateTime localNow,
            string appVersion)
        {
            if (string.IsNullOrWhiteSpace(logsDirectory)) throw new ArgumentException("Logs directory is required.", nameof(logsDirectory));
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("Diagnostics output directory is required.", nameof(outputDirectory));

            var logsRoot = Path.GetFullPath(logsDirectory);
            var outputRoot = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputRoot);

            var selectedLogs = new List<KeyValuePair<string, string>>();
            var skipped = 0;
            long totalInput = 0;

            // Explicit allowlist only: yesterday then today. Do not enumerate arbitrary log or user folders.
            foreach (var date in new[] { localNow.Date.AddDays(-1), localNow.Date })
            {
                var name = "facm-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log";
                var path = Path.Combine(logsRoot, name);
                if (!File.Exists(path)) continue;

                try
                {
                    var info = new FileInfo(path);
                    if (info.Length < 0 || info.Length > MaxLogFileBytes || totalInput + info.Length > MaxTotalInputBytes)
                    {
                        skipped++;
                        continue;
                    }

                    string text;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096))
                        text = reader.ReadToEnd();

                    totalInput += info.Length;
                    selectedLogs.Add(new KeyValuePair<string, string>(name, ScrubText(text)));
                }
                catch (IOException)
                {
                    skipped++;
                }
                catch (UnauthorizedAccessException)
                {
                    skipped++;
                }
            }

            var summary = BuildSummary(localNow, appVersion, selectedLogs.Count, skipped, totalInput);
            if (summary.Length > MaxSummaryChars)
                summary = summary.Substring(0, MaxSummaryChars - 24) + Environment.NewLine + "[summary-truncated]";

            var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var fileName = "facm-diagnostics-" + localNow.ToUniversalTime().ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) + "-" + suffix + ".zip";
            var finalPath = Path.Combine(outputRoot, fileName);
            var tempPath = Path.Combine(outputRoot, ".facm-diagnostics-" + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    {
                        WriteTextEntry(archive, "summary.txt", ScrubText(summary));
                        foreach (var log in selectedLogs)
                            WriteTextEntry(archive, "logs/" + log.Key, log.Value);
                    }
                    stream.Flush(true);
                }

                var bundleBytes = new FileInfo(tempPath).Length;
                if (bundleBytes > MaxBundleBytes)
                    throw new InvalidDataException("Diagnostics ZIP exceeds the output size bound.");

                File.Move(tempPath, finalPath);
                return new DiagnosticsExportReceipt
                {
                    BundlePath = finalPath,
                    BundleBytes = bundleBytes,
                    LogFilesIncluded = selectedLogs.Count,
                    LogFilesSkipped = skipped
                };
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        internal static string ScrubText(string value)
        {
            var safe = value ?? string.Empty;
            safe = AuthorizationRegex.Replace(safe, delegate(Match match)
            {
                return match.Groups[1].Value + " [redacted]";
            });
            safe = SensitiveAssignmentRegex.Replace(safe, delegate(Match match)
            {
                return match.Groups[1].Value + "=[redacted]";
            });
            safe = WindowsPathRegex.Replace(safe, "[path]");
            safe = UncPathRegex.Replace(safe, "[path]");
            return safe;
        }

        private static string BuildSummary(DateTime localNow, string appVersion, int included, int skipped, long inputBytes)
        {
            var builder = new StringBuilder();
            builder.AppendLine("FACM Diagnostics Summary");
            builder.Append("GeneratedUtc=").AppendLine(localNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            builder.Append("AppVersion=").AppendLine(appVersion ?? string.Empty);
            builder.Append("OS=").AppendLine(Environment.OSVersion.VersionString);
            builder.Append("CLR=").AppendLine(Environment.Version.ToString());
            builder.Append("Process64Bit=").AppendLine(Environment.Is64BitProcess ? "True" : "False");
            builder.Append("ProcessorCount=").AppendLine(Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("DistributionDirectory=[path]");
            builder.Append("LogsIncluded=").AppendLine(included.ToString(CultureInfo.InvariantCulture));
            builder.Append("LogsSkipped=").AppendLine(skipped.ToString(CultureInfo.InvariantCulture));
            builder.Append("InputBytes=").AppendLine(inputBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append("SettingsPresent=").AppendLine(File.Exists(RuntimePaths.SettingsPath) ? "True" : "False");
            builder.Append("SettingsRecoveryPresent=").AppendLine(File.Exists(RuntimePaths.SettingsRecoveryPath) ? "True" : "False");
            return builder.ToString();
        }

        private static void WriteTextEntry(ZipArchive archive, string name, string text)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(text ?? string.Empty);
        }

        private static string ResolveAppVersion()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? string.Empty : version.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        internal static void ValidateForSmokeTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "FACM-Diagnostics-" + Guid.NewGuid().ToString("N"));
            var logs = Path.Combine(root, "logs");
            var output = Path.Combine(root, "out");
            Directory.CreateDirectory(logs);
            Directory.CreateDirectory(output);
            try
            {
                var now = new DateTime(2026, 9, 3, 12, 34, 56, DateTimeKind.Local);
                File.WriteAllText(
                    Path.Combine(logs, "facm-20260903.log"),
                    "Authorization: Bearer abc.def.123\r\ntoken=super-secret\r\npath=C:\\Users\\Alice\\Desktop\\FACM\\settings.ini\r\n",
                    new UTF8Encoding(false));
                File.WriteAllText(
                    Path.Combine(logs, "facm-20260902.log"),
                    "previous log \\\\server\\private\\share\r\n",
                    new UTF8Encoding(false));

                var receipt = Export(logs, output, now, "3.5.19-smoke");
                if (!File.Exists(receipt.BundlePath) || receipt.LogFilesIncluded != 2 || receipt.LogFilesSkipped != 0)
                    throw new InvalidOperationException("Diagnostics export smoke receipt is invalid.");
                if (receipt.BundleBytes <= 0 || receipt.BundleBytes > MaxBundleBytes)
                    throw new InvalidOperationException("Diagnostics export smoke bundle size is invalid.");

                using (var stream = File.OpenRead(receipt.BundlePath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
                {
                    if (archive.Entries.Count != 3)
                        throw new InvalidOperationException("Diagnostics export contains unexpected entries.");

                    var allText = new StringBuilder();
                    foreach (var entry in archive.Entries.OrderBy(value => value.FullName, StringComparer.Ordinal))
                    {
                        if (entry.FullName != "summary.txt" &&
                            entry.FullName != "logs/facm-20260902.log" &&
                            entry.FullName != "logs/facm-20260903.log")
                            throw new InvalidOperationException("Diagnostics export escaped its entry allowlist.");
                        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8, true))
                            allText.AppendLine(reader.ReadToEnd());
                    }

                    var exported = allText.ToString();
                    if (exported.IndexOf("abc.def.123", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        exported.IndexOf("super-secret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        exported.IndexOf("Alice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        exported.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new InvalidOperationException("Diagnostics export leaked a smoke-test secret or local path.");
                    if (exported.IndexOf("[redacted]", StringComparison.Ordinal) < 0 ||
                        exported.IndexOf("[path]", StringComparison.Ordinal) < 0)
                        throw new InvalidOperationException("Diagnostics export did not preserve redaction markers.");
                }

                if (Directory.GetFiles(output, "*.tmp").Length != 0)
                    throw new InvalidOperationException("Diagnostics export left a temporary file behind.");
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }
    }
}
