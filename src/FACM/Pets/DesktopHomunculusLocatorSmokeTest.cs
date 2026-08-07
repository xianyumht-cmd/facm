using System;
using System.IO;
using System.Threading;

namespace FACM.Pets
{
    internal static class DesktopHomunculusLocatorSmokeTest
    {
        public static int Run()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "FACM-desktop-homunculus-smoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var expected = Path.Combine(root, "desktop-homunculus.exe");
                var unwanted = Path.Combine(root, "uninstall.exe");
                WriteFakeExecutable(expected);
                WriteFakeExecutable(unwanted);

                var found = DesktopHomunculusLocator.Find();
                if (string.IsNullOrWhiteSpace(found))
                    throw new InvalidOperationException("Desktop pet executable discovery returned no result.");
                if (Path.GetFileName(found).IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("Desktop pet executable discovery selected an uninstaller.");

                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    var waited = DesktopHomunculusLocator.WaitForInstalledExecutable(DateTime.UtcNow.AddMinutes(-1), TimeSpan.FromSeconds(2), cancellation.Token);
                    if (string.IsNullOrWhiteSpace(waited))
                        throw new InvalidOperationException("Post-install executable discovery did not stabilize.");
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 7;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
                catch
                {
                }
            }
        }

        private static void WriteFakeExecutable(string path)
        {
            var bytes = new byte[1024];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            File.WriteAllBytes(path, bytes);
        }
    }
}
