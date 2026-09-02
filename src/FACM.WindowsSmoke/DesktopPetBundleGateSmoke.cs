using System.Collections.Concurrent;
using FACM.Core.Runtime;
using FACM.Platform.Windows.Personalization;

internal static class DesktopPetBundleGateSmoke
{
    private static readonly TimeSpan PrepareTimeout = TimeSpan.FromMilliseconds(150);

    public static async Task RunAsync()
    {
        await VerifyFlyingHostGateStaysOwnedUntilWorkerExitsAsync();
        await VerifyVPetHostGateStaysOwnedUntilWorkerExitsAsync();
    }

    private static async Task VerifyFlyingHostGateStaysOwnedUntilWorkerExitsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-flyinghost-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var openEntered = new ManualResetEventSlim(false);
        using var releaseOpen = new ManualResetEventSlim(false);
        var stages = new ConcurrentQueue<string>();
        var openCount = 0;
        try
        {
            var store = new WindowsFlyingHostBundleStore(
                CreateLayout(root),
                () =>
                {
                    Interlocked.Increment(ref openCount);
                    openEntered.Set();
                    releaseOpen.Wait(TimeSpan.FromSeconds(5));
                    return new MemoryStream(new byte[] { 1 }, writable: false);
                },
                expectedBundleSha256: null,
                reportStage: stages.Enqueue,
                prepareTimeout: PrepareTimeout);

            var first = store.PrepareAsync();
            True(openEntered.Wait(TimeSpan.FromSeconds(2)), "FlyingHost first blocked worker was not entered");
            await ExpectTimeoutAsync(first, "FlyingHost first blocked prepare must time out to the caller");

            await ExpectTimeoutAsync(
                store.PrepareAsync(),
                "FlyingHost second prepare must time out on the held prepare gate");

            Equal(1, openCount, "FlyingHost prepare timeout must not start a second worker while the first worker is still blocked");
            True(stages.Contains("prepare-timeout-worker-cancelling"), "FlyingHost first caller timeout stage missing");
            True(stages.Contains("prepare-gate-timeout"), "FlyingHost second caller gate-timeout stage missing");
        }
        finally
        {
            releaseOpen.Set();
            _ = SpinWait.SpinUntil(() => stages.Contains("bundle-open-finish"), TimeSpan.FromSeconds(2));
            await Task.Delay(75);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task VerifyVPetHostGateStaysOwnedUntilWorkerExitsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-pethost-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var openEntered = new ManualResetEventSlim(false);
        using var releaseOpen = new ManualResetEventSlim(false);
        var stages = new ConcurrentQueue<string>();
        var openCount = 0;
        try
        {
            var store = new WindowsPetHostBundleStore(
                CreateLayout(root),
                () =>
                {
                    Interlocked.Increment(ref openCount);
                    openEntered.Set();
                    releaseOpen.Wait(TimeSpan.FromSeconds(5));
                    return new MemoryStream(new byte[] { 1 }, writable: false);
                },
                expectedBundleSha256: null,
                reportStage: stages.Enqueue,
                prepareTimeout: PrepareTimeout);

            var first = store.PrepareAsync();
            True(openEntered.Wait(TimeSpan.FromSeconds(2)), "PetHost first blocked worker was not entered");
            await ExpectTimeoutAsync(first, "PetHost first blocked prepare must time out to the caller");

            await ExpectTimeoutAsync(
                store.PrepareAsync(),
                "PetHost second prepare must time out on the held prepare gate");

            Equal(1, openCount, "PetHost prepare timeout must not start a second worker while the first worker is still blocked");
            True(stages.Contains("prepare-timeout-worker-cancelling"), "PetHost first caller timeout stage missing");
            True(stages.Contains("prepare-gate-timeout"), "PetHost second caller gate-timeout stage missing");
        }
        finally
        {
            releaseOpen.Set();
            _ = SpinWait.SpinUntil(() => stages.Contains("bundle-open-finish"), TimeSpan.FromSeconds(2));
            await Task.Delay(75);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ExpectTimeoutAsync(Task task, string name)
    {
        try
        {
            await task;
            throw new InvalidOperationException(name + ": expected TimeoutException.");
        }
        catch (TimeoutException)
        {
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
