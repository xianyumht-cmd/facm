using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace FACM.Services
{
    internal static class PetHostBundleLoader
    {
        internal const string ResourceName = "FACM.Resources.PetHost.zip";
        private const string HostExecutableName = "FACM.PetHost.exe";
        private const string CompletionMarkerName = ".facm-pethost-complete";

        public static string TryEnsureExtracted()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var resource = assembly.GetManifestResourceStream(ResourceName))
            {
                if (resource == null) return string.Empty;

                RuntimePaths.Initialize();
                var bundleRoot = Path.Combine(RuntimePaths.RuntimeDirectory, "pethost-host");
                Directory.CreateDirectory(bundleRoot);

                // The MVID is tied to the exact FACM assembly contents. A new embedded PetHost bundle
                // therefore receives a new extraction directory without relying on mutable "latest" assets.
                var bundleId = assembly.ManifestModule.ModuleVersionId.ToString("N");
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
                    ExtractArchive(resource, stage);
                    var stagedExecutable = Path.Combine(stage, HostExecutableName);
                    if (!File.Exists(stagedExecutable) || new FileInfo(stagedExecutable).Length < 65536)
                        throw new InvalidDataException("内嵌 PetHost 包缺少有效的 FACM.PetHost.exe。");

                    long payloadBytes;
                    var payloadFiles = MeasurePayload(stage, out payloadBytes);
                    if (payloadFiles < 1 || payloadBytes < 65536)
                        throw new InvalidDataException("内嵌 PetHost 包释放后的文件统计无效。");

                    File.WriteAllText(
                        Path.Combine(stage, CompletionMarkerName),
                        "facm-mvid=" + bundleId + Environment.NewLine +
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

                    AppLog.Info("Embedded PetHost extracted: " + destination);
                    return executable;
                }
                catch
                {
                    try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
                    throw;
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
                if (!File.Exists(marker) || !File.Exists(executable) || new FileInfo(executable).Length < 65536)
                    return false;

                var expectedMvid = string.Empty;
                var expectedFiles = 0;
                long expectedBytes = 0;
                foreach (var line in File.ReadAllLines(marker))
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    var key = line.Substring(0, separator).Trim();
                    var value = line.Substring(separator + 1).Trim();
                    if (string.Equals(key, "facm-mvid", StringComparison.OrdinalIgnoreCase)) expectedMvid = value;
                    else if (string.Equals(key, "files", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out expectedFiles);
                    else if (string.Equals(key, "bytes", StringComparison.OrdinalIgnoreCase)) long.TryParse(value, out expectedBytes);
                }

                if (!string.Equals(expectedMvid, bundleId, StringComparison.OrdinalIgnoreCase) ||
                    expectedFiles < 1 || expectedBytes < 65536)
                    return false;

                long actualBytes;
                var actualFiles = MeasurePayload(directory, out actualBytes);
                return actualFiles == expectedFiles && actualBytes == expectedBytes;
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
