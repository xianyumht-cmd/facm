using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FACM.WinUI.DeploymentProbe;

public partial class App : Application
{
    private const string MarkerResourceName = "FACM.WinUI.DeploymentProbe.ProbeMarker.txt";
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();

        if (arguments.Any(value => string.Equals(value, "--gate0-probe", StringComparison.OrdinalIgnoreCase)) ||
            arguments.Any(value => string.Equals(value, "--elevated-probe", StringComparison.OrdinalIgnoreCase)))
        {
            WriteProbeReport(arguments);
            Exit();
            return;
        }

        if (arguments.Any(value => string.Equals(value, "--request-elevation-probe", StringComparison.OrdinalIgnoreCase)))
        {
            RequestElevationProbe();
            Exit();
            return;
        }

        _window = new Window
        {
            Content = new Grid()
        };
        _window.Activate();
    }

    private static void WriteProbeReport(string[] arguments)
    {
        var output = Environment.GetEnvironmentVariable("FACM_GATE0_PROBE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            output = Path.Combine(Path.GetTempPath(), "facm-gate0-winui-probe.json");

        var marker = ReadEmbeddedMarker();
        var report = new
        {
            timestamp_utc = DateTime.UtcNow.ToString("O"),
            process_path = Environment.ProcessPath ?? string.Empty,
            app_context_base_directory = AppContext.BaseDirectory,
            current_directory = Environment.CurrentDirectory,
            framework = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            process_architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            is_elevated = IsElevated(),
            embedded_marker = marker,
            command_line = arguments
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ReadEmbeddedMarker()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(MarkerResourceName)
            ?? throw new InvalidDataException("Embedded Gate 0 marker resource is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RequestElevationProbe()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("The distribution executable path is unavailable.");

        Process.Start(new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = "--elevated-probe",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Environment.CurrentDirectory
        });
    }
}
