using System.Globalization;
using System.Management;

namespace FACM.App;

/// <summary>
/// App-hosted WMI command-line fallback for League process discovery. The WinUI self-contained
/// host can initialize the managed WMI bridge reliably even when the platform-only COM fallback
/// cannot. Command-line contents are consumed locally and are never sent to diagnostics.
/// </summary>
internal static class WindowsAppLeagueCommandLineReader
{
    public static string? TryRead(int processId)
    {
        if (processId <= 0) return null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " +
                processId.ToString(CultureInfo.InvariantCulture));
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var commandLine = item["CommandLine"] as string;
                    if (!string.IsNullOrWhiteSpace(commandLine)) return commandLine;
                }
            }
        }
        catch
        {
            // Discovery remains fail-closed if WMI is unavailable or the process exits mid-query.
        }

        return null;
    }
}
