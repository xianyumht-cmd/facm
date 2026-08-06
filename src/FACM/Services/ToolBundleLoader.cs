using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace FACM.Services
{
    internal static class ToolBundleLoader
    {
        private const string BundleResourceName = "FACM.Resources.FACM.ToolBundle.dll";
        private const string StoreTypeName = "FACM.ToolBundle.EmbeddedToolStore";
        private static readonly object SyncRoot = new object();
        private static MethodInfo _extractMethod;

        public static void Prepare()
        {
            EnsureLoaded();
        }

        public static string Extract(string toolId)
        {
            EnsureLoaded();
            try
            {
                return (string)_extractMethod.Invoke(null, new object[] { toolId });
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static void EnsureLoaded()
        {
            if (_extractMethod != null) return;

            lock (SyncRoot)
            {
                if (_extractMethod != null) return;

                RuntimePaths.Initialize();
                var hostAssembly = Assembly.GetExecutingAssembly();
                byte[] bundleBytes;
                using (var resource = hostAssembly.GetManifestResourceStream(BundleResourceName))
                {
                    if (resource == null)
                    {
                        throw new InvalidOperationException("内置工具资源 DLL 缺失。");
                    }

                    using (var memory = new MemoryStream())
                    {
                        resource.CopyTo(memory);
                        bundleBytes = memory.ToArray();
                    }
                }

                var expectedHash = ComputeSha256(bundleBytes);
                var bundlePath = RuntimePaths.ToolBundlePath;
                if (!File.Exists(bundlePath) ||
                    !string.Equals(ComputeSha256(bundlePath), expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    WriteAtomically(bundlePath, bundleBytes, expectedHash);
                }

                var bundleAssembly = Assembly.LoadFile(bundlePath);
                var storeType = bundleAssembly.GetType(StoreTypeName, true, false);
                _extractMethod = storeType.GetMethod("Extract", BindingFlags.Public | BindingFlags.Static);
                if (_extractMethod == null)
                {
                    throw new MissingMethodException(StoreTypeName, "Extract");
                }

                AppLog.Info("Loaded tool bundle beside FACM.exe: " + Path.GetFileName(bundlePath));
            }
        }

        private static void WriteAtomically(string destination, byte[] bytes, string expectedHash)
        {
            var temporaryPath = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temporaryPath, bytes);
                if (!string.Equals(ComputeSha256(temporaryPath), expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("内置工具资源 DLL 写入校验失败。");
                }

                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temporaryPath, destination);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }

        private static string ComputeSha256(byte[] value)
        {
            using (var algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(value)).Replace("-", string.Empty);
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }
}
