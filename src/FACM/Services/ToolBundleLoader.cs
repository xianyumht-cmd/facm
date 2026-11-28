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
        private static volatile bool _securityBlocked;
        internal static string SecurityState { get { return _securityBlocked ? "blocked_this_process" : "not_reported"; } }

        public static void Prepare()
        {
            lock (SyncRoot)
            {
                ThrowIfSecurityBlocked();
                try { EnsureLoaded(); }
                catch (Exception exception)
                {
                    ObserveSecurityBlock(exception, "bundle_prepare");
                    throw;
                }
            }
        }

        public static string Extract(string toolId)
        {
            lock (SyncRoot)
            {
                ThrowIfSecurityBlocked();
                try
                {
                    EnsureLoaded();
                    return (string)_extractMethod.Invoke(null, new object[] { toolId });
                }
                catch (TargetInvocationException exception)
                {
                    var cause = exception.InnerException ?? exception;
                    ObserveSecurityBlock(cause, "tool_extract");
                    throw cause;
                }
                catch (Exception exception)
                {
                    ObserveSecurityBlock(exception, "bundle_or_tool_extract");
                    throw;
                }
            }
        }

        private static void ThrowIfSecurityBlocked()
        {
            if (_securityBlocked)
                throw new InvalidOperationException("工具组件已被系统安全机制阻止，本次运行不可用。");
        }

        internal static bool IsSecurityBlock(Exception exception)
        {
            for (var cause = exception; cause != null; cause = cause.InnerException)
            {
                var io = cause as IOException;
                if (io != null && (io.HResult == unchecked((int)0x800700E1) ||
                    io.HResult == unchecked((int)0x800700E2))) return true;
                var win32 = cause as System.ComponentModel.Win32Exception;
                if (win32 != null && (win32.NativeErrorCode == 225 || win32.NativeErrorCode == 226)) return true;
            }
            return false;
        }

        internal static void ObserveSecurityBlock(Exception exception, string stage)
        {
            if (!IsSecurityBlock(exception)) return;
            lock (SyncRoot)
            {
                if (_securityBlocked) return;
                _securityBlocked = true;
                AppLog.Info("Tool bundle unavailable: system security block; stage=" + stage);
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var blocked = new IOException("fixture", unchecked((int)0x800700E1));
            var removed = new System.ComponentModel.Win32Exception(226);
            if (!IsSecurityBlock(blocked) ||
                !IsSecurityBlock(new TargetInvocationException(removed)) ||
                IsSecurityBlock(new IOException("fixture", unchecked((int)0x80070020))) ||
                IsSecurityBlock(new UnauthorizedAccessException()) ||
                IsSecurityBlock(null))
                throw new InvalidOperationException("Tool security classification regressed.");
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

        internal static string InspectIntegrity()
        {
            try
            {
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundleResourceName))
                {
                    if (resource == null) return "embedded_resource_missing";
                    using (var file = new FileStream(RuntimePaths.ToolBundlePath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (file.Length != resource.Length) return "size_mismatch";
                        using (var hash = SHA256.Create())
                        {
                            var expected = hash.ComputeHash(resource);
                            var actual = hash.ComputeHash(file);
                            return string.Equals(Convert.ToBase64String(expected), Convert.ToBase64String(actual),
                                StringComparison.Ordinal) ? "hash_match" : "hash_mismatch";
                        }
                    }
                }
            }
            catch (FileNotFoundException) { return "missing"; }
            catch (DirectoryNotFoundException) { return "missing"; }
            catch (UnauthorizedAccessException) { return "access_denied"; }
            catch (IOException exception)
            {
                if (IsSecurityBlock(exception))
                {
                    ObserveSecurityBlock(exception, "integrity_inspection");
                    return "security_block_reported";
                }
                return "io_error_" + (exception.HResult & 0xffff);
            }
            catch (Exception exception) { return "unknown_" + exception.GetType().Name; }
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
