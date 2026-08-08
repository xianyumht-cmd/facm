using System;
using System.IO;
using System.Threading;

namespace FACM.Services
{
    internal static class GameLocatorSmokeTest
    {
        public static int Run()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "FACM-GameLocator-Test-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(root);

                var driveRoot = Path.GetPathRoot(Path.GetFullPath(root));
                var normalizedDriveRoot = GameLocator.NormalizeDirectoryForTest(driveRoot);
                if (!string.Equals(
                    Path.GetFullPath(driveRoot),
                    normalizedDriveRoot,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("GameLocator changed a drive root into drive-relative syntax.");
                }

                var expectedRoot = Path.Combine(root, "Install");
                Directory.CreateDirectory(Path.Combine(expectedRoot, "Game"));
                var resolved = GameLocator.ResolveGameRootForTest(
                    root,
                    100,
                    TimeSpan.FromSeconds(3),
                    CancellationToken.None);
                if (!string.Equals(
                    Path.GetFullPath(expectedRoot).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(resolved ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("GameLocator did not resolve the marker-backed install root.");
                }

                var wide = Path.Combine(root, "Wide");
                Directory.CreateDirectory(wide);
                for (var index = 0; index < 12; index++)
                    Directory.CreateDirectory(Path.Combine(wide, "Folder-" + index.ToString("00")));

                var limited = false;
                try
                {
                    GameLocator.ResolveGameRootForTest(
                        wide,
                        3,
                        TimeSpan.FromSeconds(3),
                        CancellationToken.None);
                }
                catch (GameLocationSearchLimitException)
                {
                    limited = true;
                }
                if (!limited)
                    throw new InvalidOperationException("GameLocator directory-count budget was not enforced.");

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    var cancelled = false;
                    try
                    {
                        GameLocator.ResolveGameRootForTest(
                            wide,
                            100,
                            TimeSpan.FromSeconds(3),
                            cancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                    if (!cancelled)
                        throw new InvalidOperationException("GameLocator cancellation was not enforced.");
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 9;
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }
    }
}
