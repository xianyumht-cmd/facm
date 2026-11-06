using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FACM.Presentation.Glass
{
    // The release-candidate EXE carries managed WebView2 bindings and the x64 native loader.
    // The Evergreen WebView2 Runtime remains an OS prerequisite; its absence falls back to WinForms.
    internal static class UnifiedWebView2Bundle
    {
        private const string CoreResource = "FACM.Resources.WebView2.Core.dll";
        private const string FormsResource = "FACM.Resources.WebView2.WinForms.dll";
        private const string LoaderResource = "FACM.Resources.WebView2.Loader.x64.dll";
        private static readonly object Sync = new object();
        private static bool _registered;
        private static IntPtr _nativeHandle;

        internal static void Register()
        {
            lock (Sync)
            {
                if (_registered) return;
                AppDomain.CurrentDomain.AssemblyResolve += ResolveWebView2;
                _registered = true;
            }
        }

        private static Assembly ResolveWebView2(object sender, ResolveEventArgs args)
        {
            AssemblyName request;
            try { request = new AssemblyName(args.Name); }
            catch (ArgumentException) { return null; }
            var resource = string.Equals(request.Name, "Microsoft.Web.WebView2.Core", StringComparison.Ordinal)
                ? CoreResource : string.Equals(request.Name, "Microsoft.Web.WebView2.WinForms", StringComparison.Ordinal)
                ? FormsResource : null;
            if (resource == null) return null;
            var bytes = ReadResource(resource);
            var assembly = Assembly.Load(bytes);
            if (!string.Equals(assembly.GetName().Name, request.Name, StringComparison.Ordinal))
                throw new InvalidOperationException("Embedded WebView2 assembly identity mismatch.");
            return assembly;
        }

        internal static void PrepareNativeLoader()
        {
            if (!Environment.Is64BitProcess)
                throw new PlatformNotSupportedException("Unified WebView2 bundle requires Windows x64.");
            lock (Sync)
            {
                if (_nativeHandle != IntPtr.Zero) return;
                var bytes = ReadResource(LoaderResource);
                var hash = Sha256(bytes);
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FACM", "WebView2Native", hash);
                Directory.CreateDirectory(root);
                var target = Path.Combine(root, "WebView2Loader.dll");
                if (!File.Exists(target))
                {
                    var staged = Path.Combine(root, Guid.NewGuid().ToString("N") + ".partial");
                    try
                    {
                        File.WriteAllBytes(staged, bytes);
                        try { File.Move(staged, target); }
                        catch (IOException)
                        {
                            if (!File.Exists(target)) throw;
                        }
                    }
                    finally
                    {
                        try { if (File.Exists(staged)) File.Delete(staged); }
                        catch { }
                    }
                }
                var onDisk = Sha256(File.ReadAllBytes(target));
                if (!string.Equals(hash, onDisk, StringComparison.Ordinal))
                    throw new InvalidOperationException("Cached native WebView2 loader integrity check failed.");
                var module = LoadLibrary(target);
                if (module == IntPtr.Zero)
                    throw new InvalidOperationException("Cannot load embedded x64 WebView2Loader.dll; win32=" +
                        Marshal.GetLastWin32Error());
                _nativeHandle = module;
            }
        }

        internal static bool IsRuntimeAvailable(string folder)
        {
            // The native loader is already registered for this candidate. An
            // explicit missing folder selects the same native-menu fallback path.
            if (folder != null && !Directory.Exists(folder)) return false;
            try
            {
                return !string.IsNullOrWhiteSpace(
                    Microsoft.Web.WebView2.Core.CoreWebView2Environment
                        .GetAvailableBrowserVersionString(folder));
            }
            catch (Exception) { return false; }
        }

        internal static void ValidateRuntimeFallbackForSmokeTest()
        {
            Register();
            PrepareNativeLoader();
            var missing = Path.Combine(Path.GetTempPath(), "GGman-No-WebView2-" +
                Guid.NewGuid().ToString("N"));
            if (Directory.Exists(missing) || IsRuntimeAvailable(missing))
                throw new InvalidOperationException("An unavailable WebView2 runtime must select native fallback.");
        }

        internal static void ValidateForSmokeTest()
        {
            Register();
            foreach (var name in new[] { "Microsoft.Web.WebView2.Core", "Microsoft.Web.WebView2.WinForms" })
            {
                var resolved = ResolveWebView2(null, new ResolveEventArgs(name));
                if (resolved == null || resolved.GetName().Name != name)
                    throw new InvalidOperationException("Embedded managed WebView2 dependency missing: " + name);
            }
            var loader = ReadResource(LoaderResource);
            if (loader.Length < 1024 || loader.Length < 0x40)
                throw new InvalidOperationException("Embedded native WebView2 loader is missing.");
            var offset = BitConverter.ToInt32(loader, 0x3c);
            if (offset < 64 || offset > loader.Length - 6 ||
                BitConverter.ToUInt32(loader, offset) != 0x00004550 ||
                BitConverter.ToUInt16(loader, offset + 4) != 0x8664)
                throw new InvalidOperationException("Embedded native WebView2 loader is not x64.");
            PrepareNativeLoader();
        }

        private static byte[] ReadResource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null || stream.Length <= 0 || stream.Length > 5 * 1024 * 1024)
                    throw new InvalidOperationException("WebView2 bundle resource is unavailable: " + name);
                var bytes = new byte[(int)stream.Length];
                var read = 0;
                while (read < bytes.Length)
                {
                    var count = stream.Read(bytes, read, bytes.Length - read);
                    if (count <= 0) throw new EndOfStreamException("WebView2 bundle resource truncated.");
                    read += count;
                }
                return bytes;
            }
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fullPath);
    }
}
