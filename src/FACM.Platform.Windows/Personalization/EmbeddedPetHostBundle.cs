using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using FACM.Core.Runtime;

namespace FACM.Platform.Windows.Personalization;

public sealed class EmbeddedPetHostBundle
{
    public const string ResourceName = "FACM.Platform.Windows.Resources.PetHost.zip";
    private const string HostExecutableName = "FACM.PetHost.exe";
    private const int MaxEntries = 4096;
    private const long MaxExpandedBytes = 900L * 1024 * 1024;

    private readonly RuntimePathLayout _layout;
    private readonly Assembly _assembly;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EmbeddedPetHostBundle(RuntimePathLayout layout, Assembly? assembly = null)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _assembly = assembly ?? typeof(EmbeddedPetHostBundle).Assembly;
    }

    public bool IsEmbedded => _assembly.GetManifestResourceNames().Contains(ResourceName, StringComparer.Ordinal);

    public async Task<string?> TryEnsureExtractedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var resource = _assembly.GetManifestResourceStream(ResourceName);
            if (resource is null) return null;

            byte[] bundleBytes;
            using (var memory = new MemoryStream())
            {
                await resource.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
                bundleBytes = memory.ToArray();
            }

            var hash = Convert.ToHexString(SHA256.HashData(bundleBytes)).ToLowerInvariant();
            var root = Path.Combine(_layout.RuntimeDirectory, "pethost-host");
            var destination = Path.Combine(root, hash[..20]);
            var host = Path.Combine(destination, HostExecutableName);
            var marker = Path.Combine(destination, ".complete.sha256");
            if (File.Exists(host) && File.Exists(marker))
            {
                var existing = (await File.ReadAllTextAsync(marker, cancellationToken).ConfigureAwait(false)).Trim();
                if (string.Equals(existing, hash, StringComparison.OrdinalIgnoreCase)) return host;
            }

            Directory.CreateDirectory(root);
            var staging = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(staging);
                ExtractValidated(bundleBytes, staging, cancellationToken);
                var stagedHost = Path.Combine(staging, HostExecutableName);
                if (!File.Exists(stagedHost)) throw new InvalidDataException("Embedded PetHost bundle does not contain FACM.PetHost.exe.");
                await File.WriteAllTextAsync(Path.Combine(staging, ".complete.sha256"), hash, cancellationToken).ConfigureAwait(false);

                if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
                Directory.Move(staging, destination);
                return host;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                }
                catch
                {
                    // Stale staging data is harmless and will never be selected without the hash marker.
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ExtractValidated(byte[] zipBytes, string staging, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntries)
            throw new InvalidDataException("Embedded PetHost bundle entry count is outside the allowed range.");

        var stagingFull = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.IndexOf('\0') >= 0) throw new InvalidDataException("Embedded PetHost bundle contains an invalid path.");

            expanded = checked(expanded + Math.Max(0, entry.Length));
            if (expanded > MaxExpandedBytes) throw new InvalidDataException("Embedded PetHost bundle exceeds the expansion budget.");

            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var output = Path.GetFullPath(Path.Combine(staging, relative));
            if (!output.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded PetHost bundle attempted path traversal.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(output);
                continue;
            }

            var parent = Path.GetDirectoryName(output);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            using var input = entry.Open();
            using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(target);
        }
    }
}
