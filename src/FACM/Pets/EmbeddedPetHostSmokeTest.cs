using System;
using System.Diagnostics;
using System.IO;
using FACM.Services;

namespace FACM.Pets
{
    internal static class EmbeddedPetHostSmokeTest
    {
        public static int Run()
        {
            try
            {
                RuntimePaths.Initialize();
                var executable = PetHostBundleLoader.TryEnsureExtracted();
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                    throw new InvalidOperationException("Embedded PetHost was not extracted.");

                var second = PetHostBundleLoader.TryEnsureExtracted();
                if (!string.Equals(executable, second, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Embedded PetHost extraction is not idempotent.");

                var dataRoot = Path.Combine(RuntimePaths.RuntimeDirectory, "pethost-embedded-selftest");
                Directory.CreateDirectory(dataRoot);
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--self-test --data-root \"" + dataRoot + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executable) ?? RuntimePaths.RuntimeDirectory
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        throw new InvalidOperationException("Extracted PetHost process could not be started.");
                    if (!process.WaitForExit(30000))
                    {
                        try { process.Kill(); } catch { }
                        throw new TimeoutException("Extracted PetHost self-test timed out.");
                    }
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("Extracted PetHost self-test failed with exit code " + process.ExitCode + ".");
                }

                return 0;
            }
            catch (Exception exception)
            {
                AppLog.Error("Embedded PetHost smoke test failed", exception);
                return 8;
            }
        }
    }
}
