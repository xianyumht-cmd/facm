using System.Text;
using System.Text.RegularExpressions;

namespace FACM.App;

public partial class App
{
    // Last-resort real-machine startup evidence. This observer does not handle or suppress the
    // exception; it only writes a bounded, sanitized failure summary next to Recovery metadata.
    private readonly StartupFailureObserver _startupFailureObserver = new();
}

internal sealed partial class StartupFailureObserver
{
    private const int MaxMessageLength = 768;

    public StartupFailureObserver()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            TryWrite(exception, "launch-failure.txt", args.IsTerminating);
        }
    }

    internal static void TryWrite(Exception exception, string fileName, bool isTerminating = false)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (fileName is not ("launch-failure.txt" or "main-window-xaml-failure.txt")) return;

        try
        {
            var executable = Environment.ProcessPath;
            var distributionDirectory = string.IsNullOrWhiteSpace(executable)
                ? null
                : Path.GetDirectoryName(Path.GetFullPath(executable));
            if (string.IsNullOrWhiteSpace(distributionDirectory)) return;

            var recoveryDirectory = Path.Combine(distributionDirectory, "runtime", "recovery");
            Directory.CreateDirectory(recoveryDirectory);
            var target = Path.Combine(recoveryDirectory, fileName);

            var message = Sanitize(exception.Message);
            var inner = exception.InnerException;
            var builder = new StringBuilder();
            builder.AppendLine("FACM 4.0 startup failure evidence");
            builder.AppendLine("schema=1");
            builder.Append("timestampUtc=").AppendLine(DateTimeOffset.UtcNow.ToString("O"));
            builder.Append("exceptionType=").AppendLine(exception.GetType().FullName ?? exception.GetType().Name);
            builder.Append("hresult=0x").AppendLine(exception.HResult.ToString("X8"));
            builder.Append("message=").AppendLine(message);
            builder.Append("innerType=").AppendLine(inner?.GetType().FullName ?? string.Empty);
            builder.Append("innerHresult=").AppendLine(inner is null ? string.Empty : "0x" + inner.HResult.ToString("X8"));
            builder.Append("innerMessage=").AppendLine(inner is null ? string.Empty : Sanitize(inner.Message));
            builder.Append("terminating=").AppendLine(isTerminating ? "true" : "false");

            File.WriteAllText(target, builder.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // Failure evidence is defense-in-depth and must never interfere with the original crash.
        }
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var safe = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        safe = DrivePathRegex().Replace(safe, "<path>");
        safe = UncPathRegex().Replace(safe, "<unc-path>");
        if (!string.IsNullOrWhiteSpace(Environment.UserName))
        {
            safe = Regex.Replace(
                safe,
                Regex.Escape(Environment.UserName),
                "<user>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return safe.Length <= MaxMessageLength ? safe : safe[..MaxMessageLength];
    }

    [GeneratedRegex(@"(?i)[A-Z]:\\[^;|,\r\n\t]+", RegexOptions.CultureInvariant)]
    private static partial Regex DrivePathRegex();

    [GeneratedRegex(@"\\\\[^;|,\r\n\t]+", RegexOptions.CultureInvariant)]
    private static partial Regex UncPathRegex();
}
