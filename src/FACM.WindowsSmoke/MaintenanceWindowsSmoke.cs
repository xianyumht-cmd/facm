using System.Diagnostics;
using FACM.Core.Maintenance;
using FACM.Core.Runtime;
using FACM.Platform.Windows.Runtime;

internal static class MaintenanceWindowsSmoke
{
    public static async Task RunAsync()
    {
        ValidateSingleInstanceActivation();
        ValidateMissingActivationListenerIsBounded();
        await ValidateControlledLogOpenAsync();
    }

    private static void ValidateSingleInstanceActivation()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = @"Local\FACM4-Smoke-Mutex-" + suffix;
        var eventName = @"Local\FACM4-Smoke-Activate-" + suffix;
        using var observed = new AutoResetEvent(false);
        var callbackCount = 0;

        using (var primary = new WindowsSingleInstanceGate(mutexName, eventName, TimeSpan.FromMilliseconds(10)))
        {
            var disposition = primary.EnterNormal(() =>
            {
                Interlocked.Increment(ref callbackCount);
                observed.Set();
            }, TimeSpan.FromMilliseconds(300));
            Require(disposition == SingleInstanceDisposition.Primary, "First normal instance did not become primary.");

            using var secondary = new WindowsSingleInstanceGate(mutexName, eventName, TimeSpan.FromMilliseconds(10));
            var secondaryDisposition = secondary.EnterNormal(() => { }, TimeSpan.FromMilliseconds(300));
            Require(secondaryDisposition == SingleInstanceDisposition.ExistingSignaled,
                "Second normal instance did not signal the existing primary.");
            Require(observed.WaitOne(1500), "Primary activation callback was not observed.");
            Thread.Sleep(80);
            Require(Volatile.Read(ref callbackCount) == 1, "One activation signal must produce exactly one callback.");
        }

        using var replacement = new WindowsSingleInstanceGate(mutexName, eventName, TimeSpan.FromMilliseconds(10));
        Require(replacement.EnterNormal(() => { }, TimeSpan.FromMilliseconds(200)) == SingleInstanceDisposition.Primary,
            "Disposed primary did not release the named mutex for a later normal launch.");
    }

    private static void ValidateMissingActivationListenerIsBounded()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = @"Local\FACM4-Smoke-Orphan-" + suffix;
        var eventName = @"Local\FACM4-Smoke-Missing-" + suffix;
        using var orphan = new Mutex(true, mutexName, out var created);
        Require(created, "Smoke orphan mutex could not be created.");
        var started = Stopwatch.StartNew();
        using var secondary = new WindowsSingleInstanceGate(mutexName, eventName, TimeSpan.FromMilliseconds(10));
        var disposition = secondary.EnterNormal(() => { }, TimeSpan.FromMilliseconds(90));
        started.Stop();
        Require(disposition == SingleInstanceDisposition.ExistingUnresponsive,
            "Missing activation listener must fail closed instead of taking over the primary.");
        Require(started.Elapsed < TimeSpan.FromMilliseconds(700), "Missing activation listener retry was not bounded.");
        orphan.ReleaseMutex();
    }

    private static async Task ValidateControlledLogOpenAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-log-open-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(root, "runtime");
        var layout = new RuntimePathLayout(
            root,
            Path.Combine(root, "settings.ini"),
            Path.Combine(root, "settings.v2.json"),
            Path.Combine(root, "ui-text.ini"),
            Path.Combine(root, "logs"),
            runtime,
            Path.Combine(runtime, "cache"),
            Path.Combine(runtime, "pethost"),
            Path.Combine(runtime, "updates"));
        var shellCalls = 0;
        string? openedPath = null;
        try
        {
            var opener = new WindowsLogFileOpener(layout, path =>
            {
                shellCalls++;
                openedPath = path;
                return true;
            });
            var result = await opener.OpenAsync();
            var expected = Path.Combine(layout.LogsDirectory, WindowsLogFileOpener.LogFileName);
            Require(result.Started && result.Reason == "opened", "Controlled log opener did not report shell launch success.");
            Require(string.Equals(result.Path, expected, StringComparison.OrdinalIgnoreCase), "Log opener returned an uncontrolled path.");
            Require(string.Equals(openedPath, expected, StringComparison.OrdinalIgnoreCase) && shellCalls == 1,
                "Windows Shell launcher was not invoked exactly once for the controlled log path.");
            Require(File.Exists(expected), "Log opener did not prepare the missing diagnostic log file.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
