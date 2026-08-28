using System.IO.Compression;
using FACM.Core.Runtime;
using FACM.Platform.Windows.Personalization;

internal static class PetHostBundleSmoke
{
    private static readonly string[] CriticalPayloadFiles =
    [
        "FACM.PetHost.exe",
        "FACM.PetHost.dll",
        "FACM.PetHost.deps.json",
        "hostfxr.dll",
        "hostpolicy.dll",
        "VPet-Simulator.Core.dll",
        "PresentationFramework.dll",
        "WindowsBase.dll",
        "wpfgfx_cor3.dll"
    ];

    public static async Task RunAsync()
    {
        await ExtractsExactBundleAndReusesCacheAsync();
        await RejectsPathTraversalAsync();
    }

    private static async Task ExtractsExactBundleAndReusesCacheAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-pethost-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bundle = CreateBundle(includeTraversal: false);
            var layout = CreateLayout(root);
            var store = new WindowsPetHostBundleStore(
                layout,
                () => new MemoryStream(bundle, writable: false));

            var first = await store.PrepareAsync();
            True(!first.CacheHit, "first controlled PetHost extraction must create a new SHA-owned directory");
            True(File.Exists(first.ExecutablePath), "controlled PetHost executable must exist after extraction");
            True(first.BundleSha256.Length == 64, "controlled PetHost bundle must use SHA-256 identity");
            True(first.PayloadDirectory.StartsWith(Path.Combine(layout.RuntimeDirectory, "pethost-host"), StringComparison.OrdinalIgnoreCase),
                "controlled PetHost payload must stay under runtime/pethost-host");

            var second = await store.PrepareAsync();
            True(second.CacheHit, "second controlled PetHost extraction must reuse the exact bundle cache");
            Equal(first.BundleSha256, second.BundleSha256, "controlled PetHost cache identity");
            Equal(first.ExecutablePath, second.ExecutablePath, "controlled PetHost cache executable path");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RejectsPathTraversalAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-pethost-traversal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bundle = CreateBundle(includeTraversal: true);
            var store = new WindowsPetHostBundleStore(
                CreateLayout(root),
                () => new MemoryStream(bundle, writable: false));
            try
            {
                _ = await store.PrepareAsync();
                throw new InvalidOperationException("controlled PetHost bundle traversal: expected InvalidDataException");
            }
            catch (InvalidDataException)
            {
            }
            True(!File.Exists(Path.Combine(root, "escaped.txt")), "controlled PetHost traversal must not escape runtime staging");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static RuntimePathLayout CreateLayout(string root)
    {
        var runtime = Path.Combine(root, "runtime");
        return new RuntimePathLayout(
            root,
            Path.Combine(root, "settings.ini"),
            Path.Combine(root, "settings.v2.json"),
            Path.Combine(root, "ui-text.ini"),
            Path.Combine(root, "logs"),
            runtime,
            Path.Combine(runtime, "cache"),
            Path.Combine(runtime, "pethost"),
            Path.Combine(runtime, "updates"));
    }

    private static byte[] CreateBundle(bool includeTraversal)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in CriticalPayloadFiles)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var stream = entry.Open();
                var bytes = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? new byte[65536]
                    : new byte[] { 1, 2, 3, 4 };
                stream.Write(bytes);
            }

            if (includeTraversal)
            {
                var entry = archive.CreateEntry("../escaped.txt", CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.WriteByte(7);
            }
        }
        return output.ToArray();
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
