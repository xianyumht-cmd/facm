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
    private readonly Action<string>? _reportStage;
    private readonly TimeSpan _prepareTimeout;
    private PetHostBundlePreparation? _cachedPreparation;

    public WindowsPetHostBundleStore(
        RuntimePathLayout layout,
        Func<Stream?> openBundle,
        string? expectedBundleSha256 = null,
        Action<string>? reportStage = null,
        TimeSpan? prepareTimeout = null)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _openBundle = openBundle ?? throw new ArgumentNullException(nameof(openBundle));
        _expectedBundleSha256 = NormalizeBundleSha256(expectedBundleSha256);
        _reportStage = reportStage;
        _prepareTimeout = prepareTimeout ?? PrepareTimeout;
        if (_prepareTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(prepareTimeout), "PetHost preparation timeout must be positive.");
    }

    public async Task<PetHostBundlePreparation> PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (!await _prepareGate.WaitAsync(_prepareTimeout, cancellationToken).ConfigureAwait(false))
        {
            ReportStage("prepare-gate-timeout");
            throw new TimeoutException($"PetHost payload preparation gate exceeded {_prepareTimeout.TotalSeconds:0.###} seconds.");
        }

        var releaseGate = true;
        CancellationTokenSource? workerCancellation = null;
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
                ReportStage("process-cache-hit");
                return cached with { CacheHit = true };
            }

            _cachedPreparation = null;
            workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var worker = Task.Run(() => PrepareCore(workerCancellation.Token), CancellationToken.None);
            try
            {
                var prepared = await worker.WaitAsync(_prepareTimeout, cancellationToken).ConfigureAwait(false);
                _cachedPreparation = prepared;
                ReportStage(prepared.CacheHit ? "prepare-finish-cache-hit" : "prepare-finish-new-payload");
                return prepared;
            }
            catch (TimeoutException)
            {
                ReportStage("prepare-timeout-worker-cancelling");
                workerCancellation.Cancel();
                var cancellationToDispose = workerCancellation;
                releaseGate = false;
                _ = worker.ContinueWith(
                    task =>
                    {
                        _ = task.Exception;
                        cancellationToDispose.Dispose();
                        _prepareGate.Release();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                workerCancellation = null;
                throw new TimeoutException($"PetHost payload preparation exceeded {_prepareTimeout.TotalSeconds:0.###} seconds.");
            }
            catch (OperationCanceledException) when (!worker.IsCompleted)
            {
                ReportStage("prepare-cancelled-worker-cancelling");
                workerCancellation.Cancel();
                var cancellationToDispose = workerCancellation;
                releaseGate = false;
                _ = worker.ContinueWith(
                    task =>
                    {
                        _ = task.Exception;
                        cancellationToDispose.Dispose();
                        _prepareGate.Release();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                workerCancellation = null;
                throw;
            }
        }
        finally
        {
            workerCancellation?.Dispose();
            if (releaseGate) _prepareGate.Release();
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
        ReportStage("cache-check-start");
        if (_expectedBundleSha256 is { } expected)
        {
            var expectedDestination = Path.Combine(bundleRoot, expected);
            var expectedExecutable = Path.Combine(expectedDestination, HostExecutableName);
            var expectedMarker = Path.Combine(expectedDestination, CompletionMarkerName);
            if (IsComplete(expectedDestination, expectedExecutable, expectedMarker, expected))
            {
                ReportStage("disk-cache-hit-build-identity");
                return new PetHostBundlePreparation(expectedExecutable, expected, expectedDestination, CacheHit: true);
            }
            ReportStage("disk-cache-miss-build-identity");
        }

        ReportStage("bundle-open-start");
        using var bundle = OpenRequiredBundle();
        ReportStage("bundle-open-finish");

        string bundleSha256;
        if (_expectedBundleSha256 is { } knownBundleSha256)
        {
            bundleSha256 = knownBundleSha256;
        }
        else
        {
            ReportStage("hash-start");
            bundleSha256 = ComputeBundleSha256(bundle, cancellationToken);
            ReportStage("hash-finish");
        }

        var destination = Path.Combine(bundleRoot, bundleSha256);
        var executable = Path.Combine(destination, HostExecutableName);
        var marker = Path.Combine(destination, CompletionMarkerName);
        ReportStage("disk-cache-check-derived");
        if (IsComplete(destination, executable, marker, bundleSha256))
        {
            ReportStage("disk-cache-hit-derived");
            return new PetHostBundlePreparation(executable, bundleSha256, destination, CacheHit: true);
        }

        if (Directory.Exists(destination))
        {
            try
            {
                ReportStage("incomplete-cache-clean-start");
                Directory.Delete(destination, recursive: true);
                ReportStage("incomplete-cache-clean-finish");
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
            ReportStage("extract-start");
            if (bundle.CanSeek)
            {
                bundle.Position = 0;
                ExtractArchive(bundle, stage, cancellationToken);
            }
            else
            {
                ReportStage("bundle-reopen-for-extract-start");
                using var extractionBundle = OpenRequiredBundle();
                ReportStage("bundle-reopen-for-extract-finish");
                ExtractArchive(extractionBundle, stage, cancellationToken);
            }
            ReportStage("extract-finish");

            var stagedExecutable = Path.Combine(stage, HostExecutableName);
            if (!File.Exists(stagedExecutable) || new FileInfo(stagedExecutable).Length < 65536)
                throw new InvalidDataException("Embedded PetHost payload does not contain a valid FACM.PetHost.exe.");

            ReportStage("measure-start");
            var (fileCount, payloadBytes) = MeasurePayload(stage, cancellationToken);
            ReportStage($"measure-finish:files={fileCount}:bytes={payloadBytes}");
            if (fileCount < 1 || payloadBytes < 65536)
                throw new InvalidDataException("Embedded PetHost payload statistics are invalid.");

            ReportStage("marker-write-start");
            File.WriteAllText(
                Path.Combine(stage, CompletionMarkerName),
                "bundle-sha256=" + bundleSha256 + Environment.NewLine +
                "files=" + fileCount + Environment.NewLine +
                "bytes=" + payloadBytes + Environment.NewLine);
            ReportStage("marker-write-finish");

            cancellationToken.ThrowIfCancellationRequested();
            if (IsComplete(destination, executable, marker, bundleSha256))
            {
                ReportStage("promote-raced-cache-hit");
                Directory.Delete(stage, recursive: true);
                return new PetHostBundlePreparation(executable, bundleSha256, destination, CacheHit: true);
            }

            try
            {
                ReportStage("promote-start");
                Directory.Move(stage, destination);
                ReportStage("promote-finish");
            }
            catch (IOException)
            {
                // Another FACM process may have completed the exact same bundle while this process staged it.
                if (!IsComplete(destination, executable, marker, bundleSha256)) throw;
                ReportStage("promote-race-cache-hit");
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

    private void ExtractArchive(Stream bundle, string stage, CancellationToken cancellationToken)
    {
        var stageRoot = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var stagePrefix = stageRoot + Path.DirectorySeparatorChar;
        using var archive = new ZipArchive(bundle, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count < 1) throw new InvalidDataException("Embedded PetHost payload is empty.");

        var buffer = new byte[128 * 1024];
        var entryIndex = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entryIndex++;
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

            if (entryIndex == 1 ||
                entryIndex % 25 == 0 ||
                entry.Length >= 8L * 1024 * 1024 ||
                CriticalPayloadFiles.Any(name => string.Equals(name, entry.FullName, StringComparison.OrdinalIgnoreCase)))
            {
                ReportStage($"extract-entry:{entryIndex}/{archive.Entries.Count}:{entry.FullName}:{entry.Length}");
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

    private void ReportStage(string stage)
    {
        try
        {
            _reportStage?.Invoke(stage ?? string.Empty);
        }
        catch
        {
            // Diagnostics are best-effort and must never affect payload preparation.
        }
    }
}
