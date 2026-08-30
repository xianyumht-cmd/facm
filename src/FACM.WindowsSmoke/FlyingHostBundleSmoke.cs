using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using FACM.Core.Personalization;
using FACM.Core.Runtime;
using FACM.Platform.Windows.Personalization;

internal static class FlyingHostBundleSmoke
{
    private const int CacheStressIterations = 24;

    private static readonly string[] CriticalPayloadFiles =
    [
        "FACM.FlyingHost.exe",
        "FACM.FlyingHost.dll",
        "FACM.FlyingHost.deps.json",
        "hostfxr.dll",
        "hostpolicy.dll",
        "PresentationFramework.dll",
        "WindowsBase.dll",
        "wpfgfx_cor3.dll"
    ];

    public static async Task RunAsync()
    {
        await ExtractsExactBundleAndReusesCacheAsync();
        await ReusesDiskCacheAcrossProcessBoundaryWithoutOpeningBundleAsync();
        await BoundsNonCooperativePreparationWallTimeAsync();
        await BoundsNonCooperativeProcessStartAndReleasesRuntimeAsync();
        await RejectsPathTraversalAsync();
    }

    private static async Task ExtractsExactBundleAndReusesCacheAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-flyinghost-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bundle = CreateBundle(includeTraversal: false);
            var layout = CreateLayout(root);
            var openCount = 0;
            var store = new WindowsFlyingHostBundleStore(
                layout,
                () =>
                {
                    Interlocked.Increment(ref openCount);
                    return new MemoryStream(bundle, writable: false);
                });

            var first = await store.PrepareAsync();
            True(!first.CacheHit, "first FlyingHost extraction must create a new SHA-owned directory");
            True(File.Exists(first.ExecutablePath), "FlyingHost executable must exist after extraction");
            True(first.BundleSha256.Length == 64, "FlyingHost bundle must use SHA-256 identity");
            True(first.PayloadDirectory.StartsWith(Path.Combine(layout.RuntimeDirectory, "flying-host"), StringComparison.OrdinalIgnoreCase),
                "FlyingHost payload must stay under runtime/flying-host");
            Equal(1, openCount, "first FlyingHost prepare must hash and extract from one seekable stream");

            var second = await store.PrepareAsync();
            True(second.CacheHit, "second FlyingHost prepare must reuse process cache");
            Equal(first.BundleSha256, second.BundleSha256, "FlyingHost cache identity");
            Equal(first.ExecutablePath, second.ExecutablePath, "FlyingHost cache executable path");
            Equal(1, openCount, "second FlyingHost prepare must not reopen the embedded bundle");

            for (var cycle = 0; cycle < CacheStressIterations; cycle++)
            {
                var repeated = await store.PrepareAsync();
                True(repeated.CacheHit, "repeated FlyingHost prepare lost process cache in stress cycle " + cycle);
                Equal(first.BundleSha256, repeated.BundleSha256, "repeated FlyingHost bundle identity " + cycle);
                Equal(first.ExecutablePath, repeated.ExecutablePath, "repeated FlyingHost executable path " + cycle);
            }
            Equal(1, openCount, "repeated FlyingHost prepare reopened or rehashed the embedded bundle");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ReusesDiskCacheAcrossProcessBoundaryWithoutOpeningBundleAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-flyinghost-cross-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bundle = CreateBundle(includeTraversal: false);
            var layout = CreateLayout(root);
            var firstStore = new WindowsFlyingHostBundleStore(
                layout,
                () => new MemoryStream(bundle, writable: false));
            var prepared = await firstStore.PrepareAsync();

            var nextProcessOpenCount = 0;
            var nextProcessStore = new WindowsFlyingHostBundleStore(
                layout,
                () =>
                {
                    Interlocked.Increment(ref nextProcessOpenCount);
                    return new MemoryStream(bundle, writable: false);
                },
                prepared.BundleSha256);

            var reused = await nextProcessStore.PrepareAsync();
            True(reused.CacheHit, "build-time FlyingHost identity did not reuse completed disk cache");
            Equal(prepared.BundleSha256, reused.BundleSha256, "cross-process FlyingHost bundle identity");
            Equal(prepared.ExecutablePath, reused.ExecutablePath, "cross-process FlyingHost executable path");
            Equal(0, nextProcessOpenCount, "cross-process FlyingHost cache hit must not reopen the embedded bundle");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task BoundsNonCooperativePreparationWallTimeAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-flyinghost-hard-timeout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var openEntered = new ManualResetEventSlim(false);
        using var releaseOpen = new ManualResetEventSlim(false);
        var stages = new ConcurrentQueue<string>();
        try
        {
            var bundle = CreateBundle(includeTraversal: false);
            var store = new WindowsFlyingHostBundleStore(
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
            True(openEntered.Wait(TimeSpan.FromSeconds(2)), "blocking FlyingHost open delegate was not entered");
            try
            {
                _ = await prepare;
                throw new InvalidOperationException("non-cooperative FlyingHost preparation: expected TimeoutException");
            }
            catch (TimeoutException)
            {
            }
            watch.Stop();

            True(watch.Elapsed < TimeSpan.FromSeconds(2), "non-cooperative FlyingHost prepare did not release caller on hard timeout");
            True(stages.Contains("cache-check-start"), "FlyingHost diagnostics missed cache-check-start");
            True(stages.Contains("bundle-open-start"), "FlyingHost diagnostics missed bundle-open-start");
            True(stages.Contains("prepare-timeout-worker-cancelling"), "FlyingHost hard timeout stage was not reported");
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
        var root = Path.Combine(Path.GetTempPath(), "facm4-flyinghost-process-start-timeout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var launch = new TaskCompletionSource<Process?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stages = new ConcurrentQueue<string>();
        try
        {
            var bundle = CreateBundle(includeTraversal: false);
            var layout = CreateLayout(root);
            var store = new WindowsFlyingHostBundleStore(
                layout,
                () => new MemoryStream(bundle, writable: false));
            using var runtime = new WindowsFlyingPetRuntime(
                store,
                layout.UiTextPath,
                () => { },
                () => { },
                _ => { },
                () => Task.CompletedTask,
                stages.Enqueue,
                TimeSpan.FromMilliseconds(150),
                _ => launch.Task);

            var watch = Stopwatch.StartNew();
            var result = await runtime.ApplyAsync(true, FacmPetCatalog.Get("bee"));
            watch.Stop();

            True(!result.Success, "non-cooperative FlyingHost process start unexpectedly succeeded");
            Equal("flying-process-start-timeout", result.Detail, "FlyingHost process-start timeout detail");
            True(watch.Elapsed < TimeSpan.FromSeconds(2), "FlyingHost process start kept caller busy past hard timeout");
            True(stages.Contains("flying-process-start-start"), "FlyingHost startup diagnostics missed start");
            True(stages.Contains("flying-process-start-timeout"), "FlyingHost startup diagnostics missed timeout");
            True(!runtime.Current.StartRequested && !runtime.Current.PetVisible, "FlyingHost timeout did not restore non-busy runtime state");

            var restored = await runtime.ApplyAsync(false, FacmPetCatalog.Get("bee")).WaitAsync(TimeSpan.FromSeconds(1));
            True(restored.Success, "FlyingHost runtime gate remained blocked after process-start timeout");
            Equal("launcher-restored", restored.Detail, "FlyingHost timeout launcher recovery detail");
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
        var root = Path.Combine(Path.GetTempPath(), "facm4-flyinghost-traversal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bundle = CreateBundle(includeTraversal: true);
            var store = new WindowsFlyingHostBundleStore(
                CreateLayout(root),
                () => new MemoryStream(bundle, writable: false));
            try
            {
                _ = await store.PrepareAsync();
                throw new InvalidOperationException("FlyingHost bundle traversal: expected InvalidDataException");
            }
            catch (InvalidDataException)
            {
            }
            True(!File.Exists(Path.Combine(root, "escaped.txt")), "FlyingHost traversal must not escape runtime staging");
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
