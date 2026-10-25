using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FACM.Services
{
    // Companion to the existing facm daily log. Both share one correlation ID.
    // Values are restricted to fixed diagnostic tokens: never emit usernames, passwords,
    // raw URLs, IPs, champion names, game IDs or response bodies.
    internal static class CompactTraceLog
    {
        private static readonly Regex SafeToken = new Regex(@"^[A-Za-z0-9_-]{1,40}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        internal static string NewId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        internal static void Record(string id, string component, string step, string outcome, long elapsedMs)
        {
            if (!Safe(id) || !Safe(component) || !Safe(step) || !Safe(outcome)) return;
            var text = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                " trace=" + id +
                " component=" + component +
                " step=" + step +
                " outcome=" + outcome +
                " ms=" + Math.Max(0, Math.Min(300000, elapsedMs)).ToString(CultureInfo.InvariantCulture) +
                Environment.NewLine;
            try
            {
                RuntimePaths.Initialize();
                var path = Path.Combine(RuntimePaths.LogsDirectory,
                    "compact-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                AppLog.AppendBounded(path, text);
                if (outcome.StartsWith("failed_", StringComparison.Ordinal) ||
                    outcome == "incomplete" || outcome == "mode_mismatch")
                    AppLog.Warning("[COMPACT] trace=" + id + "; component=" + component +
                        "; step=" + step + "; result=" + outcome);
            }
            catch
            {
                // Diagnostic failures cannot break the game companion.
            }
        }

        private static bool Safe(string token)
        {
            return !string.IsNullOrEmpty(token) && SafeToken.IsMatch(token);
        }

        internal static void ValidateForSmokeTest()
        {
            if (!Safe(NewId()) || Safe("token=secret") || Safe("127.0.0.1") ||
                Safe("http://example.com") || Safe("x\ny") || Safe(""))
                throw new InvalidOperationException("Compact trace field validation failed.");
        }
    }
}