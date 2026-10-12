using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace FACM.Services
{
    internal static class PetHostBundleLoader
    {
        internal const string ResourceName = "FACM.Resources.PetHost.zip";
        private const string HostExecutableName = "FACM.PetHost.exe";
        private const string CompletionMarkerName = ".facm-pethost-complete";
        private static readonly object WarmupSync = new object();
        private static Task<string> _warmupTask;

        // A cache hit only needs to prove that the exact embedded bundle owns this directory and that
        // the files required to boot WPF/VPet are still present. The old implementation recursively
        // enumerated the whole self-contained runtime (hundreds of files) on every activation; on some
        // Windows Defender / slow-disk systems that alone can delay the PetHost window for many seconds.
        private static readonly string[] CriticalPayloadFiles =
        {
            HostExecutableName,
            "FACM.PetHost.dll",
            "FACM.PetHost.deps.json",
            "hostfxr.dll",
            "hostpolicy.dll",
            "VPet-Simulator.Core.dll",
            "PresentationFramework.dll",
            "WindowsBase.dll",
            "wpfgfx_cor3.dll"
        };

        public static Task<string> BeginWarmup()
        {
            lock (WarmupSync)
            {
                if (_warmupTask == null || _warmupTask.IsCanceled || _warmupTask.IsFaulted)
                    _warmupTask = Task.Run((Func<string>)EnsureExtractedCore);
                return _warmupTask;
            }
        }

        public static string TryEnsureExtracted()
        {
            // VPetHostClient already calls this from its background startup worker. Sharing the same
            // task with startup warmup prevents two concurrent extractions of the large embedded host.
            return BeginWarmup().GetAwaiter().GetResult();
        }

        private static string EnsureExtractedCore()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var bundleId = ComputeBundleSha256(assembly);
            if (string.IsNullOrWhiteSpace(bundleId)) return string.Empty;

            RuntimePaths.Initialize();
            var bundleRoot = Path.Combine(RuntimePaths.RuntimeDirectory, "pethost-host");
            Directory.CreateDirectory(bundleRoot);

            // Cache identity follows the PetHost payload itself, not FACM's MVID. A FACM-only update can
            // therefore reuse the exact same host, while any PetHost code/resource change gets a distinct
            // directory even if compiler metadata behavior changes.
            var destination = Path.Combine(bundleRoot, bundleId);
            var executable = Path.Combine(destination, HostExecutableName);
            var marker = Path.Combine(destination, CompletionMarkerName);
            if (IsComplete(destination, executable, marker, bundleId)) return executable;

            if (Directory.Exists(destination))
            {
                try { Directory.Delete(destination, true); }
                catch (Exception exception)
                {
                    throw new IOException("无法清理残缺的 PetHost 运行目录。", exception);
                }
            }

            var stage = Path.Combine(bundleRoot, "." + bundleId + ".partial-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                using (var resource = assembly.GetManifestResourceStream(ResourceName))
                {
                    if (resource == null) return string.Empty;
                    ExtractArchive(resource, stage);
                }

                var stagedExecutable = Path.Combine(stage, HostExecutableName);
                if (!File.Exists(stagedExecutable) || new FileInfo(stagedExecutable).Length < 65536)
                    throw new InvalidDataException("内嵌 PetHost 包缺少有效的 FACM.PetHost.exe。");

                long payloadBytes;
                var payloadFiles = MeasurePayload(stage, out payloadBytes);
                if (payloadFiles < 1 || payloadBytes < 65536)
                    throw new InvalidDataException("内嵌 PetHost 包释放后的文件统计无效。");

                File.WriteAllText(
                    Path.Combine(stage, CompletionMarkerName),
                    "bundle-sha256=" + bundleId + Environment.NewLine +
                    "facm-version=" + assembly.GetName().Version + Environment.NewLine +
                    "files=" + payloadFiles + Environment.NewLine +
                    "bytes=" + payloadBytes + Environment.NewLine);

                // Another FACM process may have completed the same extraction while this process was
                // preparing its private stage. Never replace a complete directory in that case.
                if (IsComplete(destination, executable, marker, bundleId))
                {
                    Directory.Delete(stage, true);
                    return executable;
                }

                try
                {
                    Directory.Move(stage, destination);
                }
                catch (IOException)
                {
                    // Close the tiny race between the complete check above and Directory.Move.
                    if (IsComplete(destination, executable, marker, bundleId))
                    {
                        Directory.Delete(stage, true);
                        return executable;
                    }
                    throw;
                }

                AppLog.Info("Embedded PetHost extracted: bundle=" + bundleId + "; path=" + destination);
                return executable;
            }
            catch
            {
                try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
                throw;
            }
        }

        private static string ComputeBundleSha256(Assembly assembly)
        {
            using (var resource = assembly.GetManifestResourceStream(ResourceName))
            {
                if (resource == null) return string.Empty;
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(resource);
                    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
        }

        private static void ExtractArchive(Stream resource, string stage)
        {
            var stageRoot = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var stagePrefix = stageRoot + Path.DirectorySeparatorChar;

            using (var archive = new ZipArchive(resource, ZipArchiveMode.Read, false))
            {
                if (archive.Entries.Count < 1)
                    throw new InvalidDataException("内嵌 PetHost 包为空。");

                foreach (var entry in archive.Entries)
                {
                    var relative = (entry.FullName ?? string.Empty)
                        .Replace('/', Path.DirectorySeparatorChar)
                        .Replace('\\', Path.DirectorySeparatorChar);
                    if (string.IsNullOrWhiteSpace(relative)) continue;
                    if (Path.IsPathRooted(relative))
                        throw new InvalidDataException("PetHost 包包含绝对路径：" + entry.FullName);

                    var output = Path.GetFullPath(Path.Combine(stageRoot, relative));
                    if (!output.StartsWith(stagePrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("PetHost 包包含越界路径：" + entry.FullName);

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(output);
                        continue;
                    }

                    var parent = Path.GetDirectoryName(output);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    using (var input = entry.Open())
                    using (var file = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
                        input.CopyTo(file);
                }
            }
        }

        private static bool IsComplete(string directory, string executable, string marker, string bundleId)
        {
            try
            {
                if (!Directory.Exists(directory) || !File.Exists(marker) || !File.Exists(executable) ||
                    new FileInfo(executable).Length < 65536)
                    return false;

                var expectedBundle = string.Empty;
                var expectedFiles = 0;
                long expectedBytes = 0;
                foreach (var line in File.ReadAllLines(marker))
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    var key = line.Substring(0, separator).Trim();
                    var value = line.Substring(separator + 1).Trim();
                    if (string.Equals(key, "bundle-sha256", StringComparison.OrdinalIgnoreCase)) expectedBundle = value;
                    else if (string.Equals(key, "files", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out expectedFiles);
                    else if (string.Equals(key, "bytes", StringComparison.OrdinalIgnoreCase)) long.TryParse(value, out expectedBytes);
                }

                if (!string.Equals(expectedBundle, bundleId, StringComparison.OrdinalIgnoreCase) ||
                    expectedFiles < 1 || expectedBytes < 65536)
                    return false;

                foreach (var relative in CriticalPayloadFiles)
                {
                    var path = Path.Combine(directory, relative);
                    if (!File.Exists(path) || new FileInfo(path).Length < 1) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int MeasurePayload(string directory, out long bytes)
        {
            bytes = 0;
            var files = 0;
            if (!Directory.Exists(directory)) return 0;

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(path), CompletionMarkerName, StringComparison.OrdinalIgnoreCase))
                    continue;
                files++;
                bytes += new FileInfo(path).Length;
            }
            return files;
        }
    }
}
