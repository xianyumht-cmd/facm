using System.Security.Cryptography;
using FACM.Core.Repair;
using FACM.Platform.Windows.Repair;

internal static class RepairWindowsSmoke
{
    private const string ResourceName = "FACM.Platform.Windows.Resources.DriverCleanup";
    private const string ExpectedSha256 = "4180BAE46BED95661D63DC8D08DD458AE866CC107AB0F00AFC647B9BEB8B4ECA";

    public static void Run()
    {
        IRepairToolService service = new WindowsRepairToolService();
        Equal(ExpectedSha256, service.DriverCleanupExpectedSha256, "driver cleanup contract hash");

        var assembly = typeof(WindowsRepairToolService).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();
        True(resourceNames.Contains(ResourceName, StringComparer.Ordinal), "driver cleanup embedded resource");
        True(!resourceNames.Any(name => name.Contains("Fix-LCU", StringComparison.OrdinalIgnoreCase)), "legacy Fix-LCU must not be embedded in FACM 4.0 Windows adapter");

        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("driver cleanup resource stream missing");
        using var sha = SHA256.Create();
        var actual = Convert.ToHexString(sha.ComputeHash(stream));
        Equal(ExpectedSha256, actual, "driver cleanup embedded SHA-256");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }
}
