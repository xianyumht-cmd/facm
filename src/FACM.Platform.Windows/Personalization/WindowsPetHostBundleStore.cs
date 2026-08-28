using System.IO.Compression;
using System.Security.Cryptography;
using FACM.Core.Runtime;

namespace FACM.Platform.Windows.Personalization;

public sealed record PetHostBundlePreparation(
    string ExecutablePath,
    string BundleSha256,
    string PayloadDirectory,
    bool CacheHit);

public sealed class WindowsPetHostBundleStore
{
    public const string ResourceName = "FACM.Resources.PetHost.zip";

    private const string HostExecutableName = "FACM.PetHost.exe";
    private const string CompletionMarkerName = ".facm-pethost-complete";

    private static readonly string[] CriticalPayloadFiles =
    [
        HostExecutableName,
        "FACM.PetHost.dll",
        "FACM.PetHost.deps.json",
        "hostfxr.dll",
        "hostpolicy.dll",
        "VPet-Simulator.Core.dll",
        "PresentationFramework.dll",
        "WindowsBase.dll",
        "wpfgfx_cor3.dll"
    ];

    private readonly RuntimePathLayout _layout;
    private readonly Func<Stream?> _openBundle;

    public WindowsPetHostBundleStore(RuntimePathLayout layout, Func<Stream?> openBundle)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _openBundle = openBundle ?? throw new ArgumentNullException(nameof(openBundle));
    }

    public Task<PetHostBundlePreparation> PrepareAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => PrepareCore(cancellationToken), cancellationToken);

    private PetHostBundlePreparation PrepareCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bundleSha256 = ComputeBundleSha256(cancellationToken);
        var bundleRoot = Path.Combine(_layout.RuntimeDirectory, "pethost-host");
        Directory.CreateDirectory(bundleRoot);

        var destination = Path.Combine(bundleRoot, bundleSha256);
        var executable = Path.Combine(destination, HostExecutableName);
        var marker = Path.Combine(destination, CompletionMarkerName);
        if (IsComplete(destination, executable, marker, bundleSha256))
            return new PetHostBundlePreparation(executable, bundleSha256, destination, CacheHit: true);

        if (Directory.Exists(destination))
        {
            try
            {
                Directory.Delete(destination, recursive: true);
            }
            catch (Exception exception)
            {
                throw new IOException("Unable to clean incomplete PetHost payload directory.", exception);
            }
        }

        var stage = Path.Combine(bundleRoot, "." + bundleSha256 + ".partial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var bundle = OpenRequiredBundle())
            {
                ExtractArchive(bundle, stage, cancellationToken);
            }

            var stagedExecutable = Path.Combine(stage, HostExecutableName);
            if (!File.Exists(stagedExecutable) || new FileInfo(stagedExecutable).Length < 65536)
                throw new InvalidDataException("Embedded PetHost payload does not contain a valid FACM.PetHost.exe.");

            var (fileCount, payloadBytes) = MeasurePayload(stage, cancellationToken);
            if (fileCount < 1 || payloadBytes < 65536)
                throw new InvalidDataException("Embedded PetHost payload statistics are invalid.");

            File.WriteAllText(
                Path.Combine(stage, CompletionMarkerName),
                "bundle-sha256=" + bundleSha256 + Environment.NewLine +
                "files=" + fileCount + Environment.NewLine +
                "bytes=" + payloadBytes + Environment.NewLine);

            cancellationToken.ThrowIfCancellationRequested();
            if (IsComplete(destination, executable, marker, bundleSha256))
            {
                Directory.Delete(stage, recursive: true);
                return new PetHostBundlePreparation(executable, bundleSha256, destination, CacheHit: true);
            }

            try
            {
                Directory.Move(stage, destination);
            }
            catch (IOException)
            {
                // Another FACM process may have completed the exact same bundle while this process staged it.
                if (!IsComplete(destination, executable, marker, bundleSha256)) throw;
                Directory.Delete(stage, recursive: true);
                return new PetHostBundlePreparation(executable, bundleSha256, destination, CacheHit: true);
            }

            return new PetHostBundlePreparation(executable, bundleSha256, destination, CacheHit: false);
        }
        catch
        {
            try
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            }
            catch
            {
            }
            throw;
        }
    }

    private string ComputeBundleSha256(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bundle = OpenRequiredBundle();
        var hash = SHA256.HashData(bundle);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private Stream OpenRequiredBundle() =>
        _openBundle() ?? throw new FileNotFoundException(
            "The FACM build does not contain the controlled PetHost payload resource.",
            ResourceName);

    private static void ExtractArchive(Stream bundle, string stage, CancellationToken cancellationToken)
    {
        var stageRoot = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var stagePrefix = stageRoot + Path.DirectorySeparatorChar;
        using var archive = new ZipArchive(bundle, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count < 1) throw new InvalidDataException("Embedded PetHost payload is empty.");

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = (entry.FullName ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative)) continue;
            if (Path.IsPathRooted(relative))
                throw new InvalidDataException("PetHost payload contains an absolute path: " + entry.FullName);

            var output = Path.GetFullPath(Path.Combine(stageRoot, relative));
            if (!output.StartsWith(stagePrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PetHost payload contains a path traversal entry: " + entry.FullName);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(output);
                continue;
            }

            var parent = Path.GetDirectoryName(output);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            using var input = entry.Open();
            using var file = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(file);
        }
    }

    private static bool IsComplete(
        string directory,
        string executable,
        string marker,
        string bundleSha256)
    {
        try
        {
            if (!Directory.Exists(directory) || !File.Exists(marker) || !File.Exists(executable) ||
                new FileInfo(executable).Length < 65536)
            {
                return false;
            }

            var markerBundle = File.ReadLines(marker)
                .FirstOrDefault(line => line.StartsWith("bundle-sha256=", StringComparison.OrdinalIgnoreCase))?
                .Substring("bundle-sha256=".Length)
                .Trim();
            if (!string.Equals(markerBundle, bundleSha256, StringComparison.OrdinalIgnoreCase)) return false;

            foreach (var relative in CriticalPayloadFiles)
            {
                var path = Path.Combine(directory, relative);
                if (!File.Exists(path) || new FileInfo(path).Length < 1) return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (int Files, long Bytes) MeasurePayload(string directory, CancellationToken cancellationToken)
    {
        var files = 0;
        long bytes = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFileName(path), CompletionMarkerName, StringComparison.OrdinalIgnoreCase)) continue;
            files++;
            bytes += new FileInfo(path).Length;
        }
        return (files, bytes);
    }
}
