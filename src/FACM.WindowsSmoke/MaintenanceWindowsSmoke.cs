using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using FACM.Core.Maintenance;
using FACM.Core.Online;
using FACM.Core.Runtime;
using FACM.Platform.Windows.Runtime;

internal static class MaintenanceWindowsSmoke
{
    public static async Task RunAsync()
    {
        ValidateSingleInstanceActivation();
        ValidateMissingActivationListenerIsBounded();
        await ValidateControlledLogOpenAsync();
        await ValidateControlledUpdaterLaunchAsync();
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
        var layout = CreateLayout(root, runtime);
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

    private static async Task ValidateControlledUpdaterLaunchAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-updater-launch-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(root, "runtime");
        var layout = CreateLayout(root, runtime);
        Directory.CreateDirectory(layout.UpdatesDirectory);
        var package = Path.Combine(layout.UpdatesDirectory, "FACM-4.0.1.exe");
        var packageBytes = Enumerable.Range(0, 4096).Select(index => (byte)(index % 233)).ToArray();
        await File.WriteAllBytesAsync(package, packageBytes);
        var expectedHash = Convert.ToHexString(SHA256.HashData(packageBytes));
        var destination = Path.Combine(root, "FACM.App.exe");
        await File.WriteAllBytesAsync(destination, [0x4D, 0x5A, 0x00, 0x00]);

        var updaterBytes = new byte[2048];
        updaterBytes[0] = 0x4D;
        updaterBytes[1] = 0x5A;
        for (var index = 2; index < updaterBytes.Length; index++) updaterBytes[index] = (byte)(index % 197);
        ProcessStartInfo? observed = null;
        var starts = 0;
        try
        {
            var executablePaths = new FakeExecutablePathProvider(destination);
            var launcher = new WindowsUpdateReplacementLauncher(
                layout,
                executablePaths,
                () => updaterBytes,
                startInfo =>
                {
                    starts++;
                    observed = startInfo;
                    return new Process();
                });

            Require(await launcher.StartAsync(package, expectedHash, "4.0.1"),
                "Validated update package did not start the controlled updater helper.");
            Require(starts == 1 && observed is not null, "Updater process launch was not invoked exactly once.");
            Require(observed!.UseShellExecute && observed.Verb == "runas", "Updater helper must cross an explicit UAC boundary.");
            Require(string.Equals(observed.WorkingDirectory, layout.UpdatesDirectory, StringComparison.OrdinalIgnoreCase),
                "Updater helper escaped the controlled updates working directory.");
            Require(string.Equals(observed.FileName, Path.Combine(layout.UpdatesDirectory, WindowsUpdateReplacementLauncher.UpdaterFileName), StringComparison.OrdinalIgnoreCase),
                "Updater helper path was not controlled by RuntimePathLayout.");
            Require(observed.Arguments.Contains("--parent-pid=", StringComparison.Ordinal) &&
                    observed.Arguments.Contains("--source64=", StringComparison.Ordinal) &&
                    observed.Arguments.Contains("--dest64=", StringComparison.Ordinal) &&
                    observed.Arguments.Contains("--sha256=" + expectedHash, StringComparison.Ordinal),
                "Updater helper arguments lost parent/source/destination/hash identity.");
            Require(File.Exists(observed.FileName) &&
                    Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(observed.FileName))) == Convert.ToHexString(SHA256.HashData(updaterBytes)),
                "Embedded updater extraction changed the controlled payload bytes.");

            var outside = Path.Combine(root, "outside.exe");
            await File.WriteAllBytesAsync(outside, packageBytes);
            try
            {
                await launcher.StartAsync(outside, expectedHash, "4.0.1");
                throw new InvalidOperationException("Updater accepted a package outside RuntimePathLayout.UpdatesDirectory.");
            }
            catch (InvalidDataException)
            {
                Require(starts == 1, "Rejected outside package still reached Process.Start.");
            }

            var cancelledLauncher = new WindowsUpdateReplacementLauncher(
                layout,
                executablePaths,
                () => updaterBytes,
                _ => throw new Win32Exception(1223, "The operation was canceled by the user."));
            Require(!await cancelledLauncher.StartAsync(package, expectedHash, "4.0.1"),
                "Updater UAC cancellation must keep the current FACM instance alive.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static RuntimePathLayout CreateLayout(string root, string runtime) => new(
        root,
        Path.Combine(root, "settings.ini"),
        Path.Combine(root, "settings.v2.json"),
        Path.Combine(root, "ui-text.ini"),
        Path.Combine(root, "logs"),
        runtime,
        Path.Combine(runtime, "cache"),
        Path.Combine(runtime, "pethost"),
        Path.Combine(runtime, "updates"));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeExecutablePathProvider(string executablePath) : IExecutablePathProvider
    {
        public string ExecutablePath { get; } = Path.GetFullPath(executablePath);
        public string BaseDirectory { get; } =
            Path.GetDirectoryName(Path.GetFullPath(executablePath))
            ?? throw new InvalidOperationException("Fake executable base directory is unavailable.");
    }
}
