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
    public const string HashResourceName = "FACM.Resources.PetHost.sha256";

    private const string HostExecutableName = "FACM.PetHost.exe";
    private const string CompletionMarkerName = ".facm-pethost-complete";
    private static readonly TimeSpan PrepareTimeout = TimeSpan.FromSeconds(60);

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
    private readonly SemaphoreSlim _prepareGate = new(1, 1);
    private readonly string? _expectedBundleSha256;
    private PetHostBundlePreparation? _cachedPreparation;

    public WindowsPetHostBundleStore(
        RuntimePathLayout layout,
        Func<Stream?> openBundle,
        string? expectedBundleSha256 = null)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _openBundle = openBundle ?? throw new ArgumentNullException(nameof(openBundle));
        _expectedBundleSha256 = NormalizeBundleSha256(expectedBundleSha256);
    }

    public async Task<PetHostBundlePreparation> PrepareAsync(CancellationToken cancellationToken = default)
    {
        await _prepareGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cached = _cachedPreparation;
            if (cached is not null &&
                IsComplete(
                    cached.PayloadDirectory,
                    cached.ExecutablePath,
                    Path.Combine(cached.PayloadDirectory, CompletionMarkerName),
                    cached.BundleSha256))
            {
                return cached with { CacheHit = true };
            }

            _cachedPreparation = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PrepareTimeout);
            try
            {
                var prepared = await Task.Run(() => PrepareCore(timeout.Token), CancellationToken.None).ConfigureAwait(false);
                _cachedPreparation = prepared;
                return prepared;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"PetHost payload preparation exceeded {PrepareTimeout.TotalSeconds:0} seconds.");
            }
        }
        finally
        {
            _prepareGate.Release();
        }
    }

    private PetHostBundlePreparation PrepareCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bundleRoot = Path.Combine(_layout.RuntimeDirectory, "pethost-host");
        Directory.CreateDirectory(bundleRoot);

        // Foundation emits the SHA-256 next to the exact embedded bundle and FACM embeds that tiny
        // identity resource as well. On a later process launch we can therefore test the immutable
        // disk cache before opening/rehashing the 70+ MiB embedded ZIP. If the identity resource is
        // absent (for example a lightweight local developer build), retain the older hash-on-demand
        // fallback so the store remains safe and functional.
        if (_expectedBundleSha256 is { } expected)
        {
            var expectedDestination = Path.Combine(bundleRoot, expected);
            var expectedExecutable = Path.Combine(expectedDestination, HostExecutableName);
            var expectedMarker = Path.Combine(expectedDestination, CompletionMarkerName);
            if (IsComplete(expectedDestination, expectedExecutable, expectedMarker, expected))
                return new PetHostBundlePreparation(expectedExecutable, expected, expectedDestination, CacheHit: true);
        }

        using var bundle = OpenRequiredBundle();
        var bundleSha256 = _expectedBundleSha256 ?? ComputeBundleSha256(bundle, cancellationToken);
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
            if (bundle.CanSeek)
            {
                bundle.Position = 0;
                ExtractArchive(bundle, stage, cancellationToken);
            }
            else
            {
                using var extractionBundle = OpenRequiredBundle();
                ExtractArchive(extractionBundle, stage, cancellationToken);
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

    private static string? NormalizeBundleSha256(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length != 64) return null;
        return normalized.All(Uri.IsHexDigit) ? normalized : null;
    }

    private static string ComputeBundleSha256(Stream bundle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hash = SHA256.HashData(bundle);
        cancellationToken.ThrowIfCancellationRequested();
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
        using var archive = new ZipArchive(bundle, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count < 1) throw new InvalidDataException("Embedded PetHost payload is empty.");

        var buffer = new byte[128 * 1024];
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
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = input.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                file.Write(buffer, 0, read);
            }
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
