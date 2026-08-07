using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

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

                if (DesktopPetLaunchGate.ExplicitUseAllowed)
                    throw new InvalidOperationException("Desktop pet launch gate must be closed by default.");
                if (DesktopHomunculusLocator.Find() != null)
                    throw new InvalidOperationException("Desktop pet executable discovery must stay disabled without explicit user action.");

                using (DesktopPetLaunchGate.BeginExplicitUse())
                {
                    var found = DesktopHomunculusLocator.Find();
                    if (string.IsNullOrWhiteSpace(found))
                        throw new InvalidOperationException("Desktop pet executable discovery returned no result during explicit use.");
                    if (Path.GetFileName(found).IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new InvalidOperationException("Desktop pet executable discovery selected an uninstaller.");

                    using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                    {
                        var waited = DesktopHomunculusLocator.WaitForInstalledExecutable(DateTime.UtcNow.AddMinutes(-1), TimeSpan.FromSeconds(2), cancellation.Token);
                        if (string.IsNullOrWhiteSpace(waited))
                            throw new InvalidOperationException("Post-install executable discovery did not stabilize.");
                    }
                }

                ValidateNvidiaCompatibilityProfile();
                ValidateNvidiaInspectorDownload();
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

        private static void ValidateNvidiaCompatibilityProfile()
        {
            var preferNative = NvidiaDesktopPetCompatibility.BuildProfileXmlForSmokeTest(0);
            if (string.IsNullOrWhiteSpace(preferNative))
                throw new InvalidOperationException("NVIDIA compatibility profile XML is empty.");
            if (preferNative.IndexOf("<SettingID>550932728</SettingID>", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("NVIDIA Vulkan/OpenGL present-method setting ID is incorrect.");
            if (preferNative.IndexOf("<SettingValue>0</SettingValue>", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("NVIDIA Prefer Native setting value is incorrect.");
            if (preferNative.IndexOf("desktop_homunculus.exe", StringComparison.OrdinalIgnoreCase) < 0 ||
                preferNative.IndexOf("desktop-homunculus.exe", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Desktop pet executable aliases are missing from NVIDIA profile.");

            var restore = NvidiaDesktopPetCompatibility.BuildProfileXmlForSmokeTest(2);
            if (restore.IndexOf("<SettingValue>2</SettingValue>", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("NVIDIA restore-to-Auto profile value is incorrect.");
        }

        private static void ValidateNvidiaInspectorDownload()
        {
            var method = typeof(NvidiaDesktopPetCompatibility).GetMethod(
                "EnsureInspectorAsync",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) throw new InvalidOperationException("NVIDIA compatibility download method is missing.");

            using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                var task = method.Invoke(null, new object[] { null, cancellation.Token }) as Task<string>;
                if (task == null) throw new InvalidOperationException("NVIDIA compatibility download method returned an invalid task.");
                var inspector = task.GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(inspector) || !File.Exists(inspector))
                    throw new InvalidOperationException("Current NVIDIA Profile Inspector release could not be downloaded and extracted.");
                if (!string.Equals(Path.GetFileName(inspector), "nvidiaProfileInspector.exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unexpected NVIDIA Profile Inspector executable name.");
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
