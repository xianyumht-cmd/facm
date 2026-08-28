using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace FACM.App;

internal static class StartupCrashDiagnostics
{
    private const string CrashFileName = "startup-crash.json";
    private const int MaxMessageChars = 2048;
    private const int MaxExceptionChars = 16384;
    private static readonly long ProcessStartTimestamp = Stopwatch.GetTimestamp();
    private static readonly TimeSpan StartupWindow = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [ThreadStatic]
    private static bool _writing;

    [ModuleInitializer]
    internal static void Initialize()
    {
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (args.Exception is not UnauthorizedAccessException exception) return;
        if (Stopwatch.GetElapsedTime(ProcessStartTimestamp) > StartupWindow) return;
        TryWrite("first-chance-access-denied", exception);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            TryWrite("unhandled", exception);
    }

    private static void TryWrite(string kind, Exception exception)
    {
        if (_writing) return;
        _writing = true;
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return;
            var distributionDirectory = Path.GetDirectoryName(Path.GetFullPath(executable));
            if (string.IsNullOrWhiteSpace(distributionDirectory)) return;

            var elapsed = Stopwatch.GetElapsedTime(ProcessStartTimestamp);
            var payload = new
            {
                schemaVersion = 1,
                observedAtUtc = DateTimeOffset.UtcNow,
                kind,
                processId = Environment.ProcessId,
                processElapsedMs = (long)elapsed.TotalMilliseconds,
                exceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                hResult = "0x" + exception.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                message = Limit(Scrub(exception.Message, distributionDirectory), MaxMessageChars),
                source = Limit(Scrub(exception.Source ?? string.Empty, distributionDirectory), 512),
                targetSite = Limit(Scrub(exception.TargetSite?.ToString() ?? string.Empty, distributionDirectory), 1024),
                exception = Limit(Scrub(exception.ToString(), distributionDirectory), MaxExceptionChars)
            };
            var json = JsonSerializer.Serialize(payload, JsonOptions);

            var recoveryPath = Path.Combine(distributionDirectory, "runtime", "recovery", CrashFileName);
            if (TryWritePath(recoveryPath, json)) return;

            // The normal recovery directory may itself be the denied resource. The distribution root
            // is the final fallback because FACM is a portable app and settings migration already uses it.
            _ = TryWritePath(Path.Combine(distributionDirectory, CrashFileName), json);
        }
        catch
        {
            // Startup diagnostics are strictly best-effort and must never replace the original failure.
        }
        finally
        {
            _writing = false;
        }
    }

    private static bool TryWritePath(string path, string json)
    {
        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
            return true;
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
            }
            return false;
        }
    }

    private static string Scrub(string value, string distributionDirectory)
    {
        var result = value.Replace(distributionDirectory, "%FACM_ROOT%", StringComparison.OrdinalIgnoreCase);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            result = result.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(temp))
            result = result.Replace(temp, "%TEMP%", StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static string Limit(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars];
}
