using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using FACM.Core.Personalization;
using FACM.Core.Runtime;
using FACM.Platform.Windows.Personalization;

internal static class PetHostBundleSmoke
{
    private const int CacheStressIterations = 24;

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
        await RejectsFlyingSpriteWithoutOpeningPetHostBundleAsync();
        await ExtractsExactBundleAndReusesCacheAsync();
        await ReusesDiskCacheAcrossProcessBoundaryWithoutOpeningBundleAsync();
        await BoundsNonCooperativePreparationWallTimeAsync();
        await BoundsNonCooperativeProcessStartAndReleasesRuntimeAsync();
        await RejectsPathTraversalAsync();
    }

    private static async Task RejectsFlyingSpriteWithoutOpeningPetHostBundleAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-pethost-route-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var openCount = 0;
            var layout = CreateLayout(root);
            var store = new WindowsPetHostBundleStore(
                layout,
                () =>
                {
                    Interlocked.Increment(ref openCount);
                    return new MemoryStream(CreateBundle(includeTraversal: false), writable: false);
                });
            using var runtime = new WindowsVPetRuntime(
                store,
                layout.PetHostDataDirectory,
                layout.UiTextPath,
                () => { },
                () => { },
                _ => { },
                () => Task.CompletedTask);

            var rejected = await runtime.ApplyAsync(true, FacmPetCatalog.Get("bee"));
            True(!rejected.Success, "VPet runtime must reject FlyingSprite before payload preparation");
            Equal("runtime-unsupported:FlyingSprite", rejected.Detail, "VPet FlyingSprite route guard detail");
            Equal(0, openCount, "FlyingSprite route must not open the VPet PetHost bundle");
            True(!runtime.Current.StartRequested && !runtime.Current.PetVisible, "VPet FlyingSprite rejection must stay fail-soft");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ExtractsExactBundleAndReusesCacheAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-pethost-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bundle = CreateBundle(includeTraversal: false);
            var layout = CreateLayout(root);
            var openCount = 0;
            var store = new WindowsPetHostBundleStore(
                layout,
                () =>
                {
                    Interlocked.Increment(ref openCount);
                    return new MemoryStream(bundle, writable: false);
                });

            var first = await store.PrepareAsync();
            True(!first.CacheHit, "first controlled PetHost extraction must create a new SHA-owned directory");
            True(File.Exists(first.ExecutablePath), "controlled PetHost executable must exist after extraction");
            True(first.BundleSha256.Length == 64, "controlled PetHost bundle must use SHA-256 identity");
            True(first.PayloadDirectory.StartsWith(Path.Combine(layout.RuntimeDirectory, "pethost-host"), StringComparison.OrdinalIgnoreCase),
                "controlled PetHost payload must stay under runtime/pethost-host");
            Equal(1, openCount, "first prepare must hash and extract from one seekable embedded bundle stream");

            var second = await store.PrepareAsync();
            True(second.CacheHit, "second controlled PetHost extraction must reuse the exact bundle cache");
            Equal(first.BundleSha256, second.BundleSha256, "controlled PetHost cache identity");
            Equal(first.ExecutablePath, second.ExecutablePath, "controlled PetHost cache executable path");
            Equal(1, openCount, "second prepare must not reopen the embedded bundle; process cache avoids rehashing the large payload");

            for (var cycle = 0; cycle < CacheStressIterations; cycle++)
            {
                var repeated = await store.PrepareAsync();
                True(repeated.CacheHit, "repeated PetHost prepare lost process cache in stress cycle " + cycle);
                Equal(first.BundleSha256, repeated.BundleSha256, "repeated PetHost bundle identity " + cycle);
                Equal(first.ExecutablePath, repeated.ExecutablePath, "repeated PetHost executable path " + cycle);
            }
            Equal(1, openCount, "repeated PetHost prepare reopened or rehashed the embedded bundle");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ReusesDiskCacheAcrossProcessBoundaryWithoutOpeningBundleAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-pethost-cross-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bundle = CreateBundle(includeTraversal: false);
            var layout = CreateLayout(root);
            var firstStore = new WindowsPetHostBundleStore(
                layout,
                () => new MemoryStream(bundle, writable: false));
            var prepared = await firstStore.PrepareAsync();

            var nextProcessOpenCount = 0;
            var nextProcessStore = new WindowsPetHostBundleStore(
                layout,
                () =>
                {
                    Interlocked.Increment(ref nextProcessOpenCount);
                    return new MemoryStream(bundle, writable: false);
                },
                prepared.BundleSha256);

            var reused = await nextProcessStore.PrepareAsync();
            True(reused.CacheHit, "build-time PetHost identity did not reuse the completed disk cache in a new store/process");
            Equal(prepared.BundleSha256, reused.BundleSha256, "cross-process PetHost bundle identity");
            Equal(prepared.ExecutablePath, reused.ExecutablePath, "cross-process PetHost executable path");
            Equal(0, nextProcessOpenCount, "cross-process cache hit must not reopen the embedded bundle");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task BoundsNonCooperativePreparationWallTimeAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-pethost-hard-timeout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var openEntered = new ManualResetEventSlim(false);
        using var releaseOpen = new ManualResetEventSlim(false);
        var stages = new ConcurrentQueue<string>();
        try
        {
            var bundle = CreateBundle(includeTraversal: false);
            var store = new WindowsPetHostBundleStore(
                CreateLayout(root),
                () =>
                {
                    openEntered.Set();
                    releaseOpen.Wait(TimeSpan.FromSeconds(5));
                    return new MemoryStream(bundle, writable: false);
                },
                expectedBundleSha256: null,
                reportStage: stages.Enqueue,
                prepareTimeout: TimeSpan.FromMilliseconds(150));

            var watch = Stopwatch.StartNew();
            var prepare = store.PrepareAsync();
            True(openEntered.Wait(TimeSpan.FromSeconds(2)), "blocking PetHost open delegate was not entered");
            try
            {
                _ = await prepare;
                throw new InvalidOperationException("non-cooperative PetHost preparation: expected TimeoutException");
            }
            catch (TimeoutException)
            {
            }
            watch.Stop();

            True(watch.Elapsed < TimeSpan.FromSeconds(2), "non-cooperative PetHost prepare did not release caller on hard timeout");
            True(stages.Contains("cache-check-start"), "PetHost stage diagnostics missed cache-check-start");
            True(stages.Contains("bundle-open-start"), "PetHost stage diagnostics missed bundle-open-start");
            True(stages.Contains("prepare-timeout-worker-cancelling"), "PetHost hard timeout stage was not reported");
        }
        finally
        {
            releaseOpen.Set();
            _ = SpinWait.SpinUntil(() => stages.Contains("bundle-open-finish"), TimeSpan.FromSeconds(2));
            await Task.Delay(50);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task BoundsNonCooperativeProcessStartAndReleasesRuntimeAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-pethost-process-start-timeout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var launch = new TaskCompletionSource<Process?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stages = new ConcurrentQueue<string>();
        try
        {
            var bundle = CreateBundle(includeTraversal: false);
            var layout = CreateLayout(root);
            var store = new WindowsPetHostBundleStore(
                layout,
                () => new MemoryStream(bundle, writable: false));
            using var runtime = new WindowsVPetRuntime(
                store,
                layout.PetHostDataDirectory,
                layout.UiTextPath,
                () => { },
                () => { },
                _ => { },
                () => Task.CompletedTask,
                stages.Enqueue,
                TimeSpan.FromMilliseconds(150),
                _ => launch.Task);

            var watch = Stopwatch.StartNew();
            var result = await runtime.ApplyAsync(true, FacmPetCatalog.Get("vpet"));
            watch.Stop();

            True(!result.Success, "non-cooperative PetHost process start unexpectedly succeeded");
            Equal("process-start-timeout", result.Detail, "non-cooperative PetHost process-start timeout detail");
            True(watch.Elapsed < TimeSpan.FromSeconds(2), "non-cooperative PetHost process start kept caller busy past the hard timeout");
            True(stages.Contains("process-start-start"), "PetHost process startup diagnostics missed process-start-start");
            True(stages.Contains("process-start-timeout"), "PetHost process startup diagnostics missed process-start-timeout");
            True(!runtime.Current.StartRequested && !runtime.Current.PetVisible, "PetHost timeout did not restore a non-busy launcher runtime state");

            var restored = await runtime.ApplyAsync(false, FacmPetCatalog.Get("vpet")).WaitAsync(TimeSpan.FromSeconds(1));
            True(restored.Success, "PetHost runtime gate remained blocked after process-start timeout");
            Equal("launcher-restored", restored.Detail, "PetHost process-start timeout launcher recovery detail");
        }
        finally
        {
            launch.TrySetResult(null);
            await Task.Delay(50);
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
