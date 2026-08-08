using System.Text.Json;

namespace FACM.PetHost;

internal static class VPetAssetCacheValidator
{
    private static readonly string[] RequiredDirectories =
    {
        "Default", "IDEL", "MOVE", "Raise", "StartUP", "Touch_Body", "Touch_Head"
    };

    internal static void InvalidateBrokenCompletionMarkers()
    {
        ValidateRoot(PetHostPaths.RootDirectory, "portable");
        ValidateRoot(PetHostPaths.LegacyRootDirectory, "legacy");
    }

    private static void ValidateRoot(string rootDirectory, string label)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) return;

            var versionDirectory = Path.Combine(
                Path.GetFullPath(rootDirectory),
                "Assets",
                "vpet-" + PetHostPaths.UpstreamShortCommit);
            var markerPath = Path.Combine(versionDirectory, ".facm-complete.json");
            if (!File.Exists(markerPath)) return;

            string reason;
            if (IsCompleteCache(versionDirectory, markerPath, out reason)) return;

            File.Delete(markerPath);
            Console.Error.WriteLine(
                "FACM PetHost invalidated " + label + " VPet completion marker: " + reason);
        }
        catch (Exception exception)
        {
            // Validation is a repair guard. Never make PetHost unusable merely because an old cache
            // directory cannot be inspected; the existing bootstrapper will still apply its own checks.
            Console.Error.WriteLine(
                "FACM PetHost cache validation skipped for " + label + ": " + exception.Message);
        }
    }

    private static bool IsCompleteCache(string versionDirectory, string markerPath, out string reason)
    {
        reason = string.Empty;
        var petDirectory = Path.Combine(versionDirectory, "pet");
        var configPath = Path.Combine(petDirectory, "vup.lps");
        var vupDirectory = Path.Combine(petDirectory, "vup");
        if (!File.Exists(configPath))
        {
            reason = "vup.lps is missing";
            return false;
        }
        if (!Directory.Exists(vupDirectory))
        {
            reason = "vup directory is missing";
            return false;
        }

        foreach (var directory in RequiredDirectories)
        {
            if (Directory.Exists(Path.Combine(vupDirectory, directory))) continue;
            reason = "required directory is missing: vup/" + directory;
            return false;
        }

        int expectedFiles;
        long expectedBytes;
        using (var document = JsonDocument.Parse(File.ReadAllText(markerPath)))
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("commit", out var commitElement) ||
                !string.Equals(commitElement.GetString(), PetHostPaths.UpstreamCommit, StringComparison.OrdinalIgnoreCase))
            {
                reason = "pinned commit does not match";
                return false;
            }
            if (!root.TryGetProperty("files", out var filesElement) ||
                !filesElement.TryGetInt32(out expectedFiles) || expectedFiles <= 0)
            {
                reason = "file count is missing or invalid";
                return false;
            }
            if (!root.TryGetProperty("bytes", out var bytesElement) ||
                !bytesElement.TryGetInt64(out expectedBytes) || expectedBytes <= 0)
            {
                reason = "byte count is missing or invalid";
                return false;
            }
        }

        var actualFiles = 0;
        long actualBytes = 0;
        foreach (var file in Directory.EnumerateFiles(petDirectory, "*", SearchOption.AllDirectories))
        {
            actualFiles++;
            actualBytes += new FileInfo(file).Length;
        }

        if (actualFiles != expectedFiles)
        {
            reason = "file count mismatch: expected " + expectedFiles + ", actual " + actualFiles;
            return false;
        }
        if (actualBytes != expectedBytes)
        {
            reason = "byte count mismatch: expected " + expectedBytes + ", actual " + actualBytes;
            return false;
        }

        return true;
    }
}
