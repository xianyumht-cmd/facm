using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.Pets
{
    internal static class NvidiaDesktopPetCompatibility
    {
        private const string ReleaseApi = "https://api.github.com/repos/Orbmu2k/nvidiaProfileInspector/releases/latest";
        private const int PresentMethodSettingId = 550932728; // 0x20D690F8 OGL_CPL_PREFER_DXPRESENT_ID
        private const int PreferNativeValue = 0;
        private const int AutoValue = 2;
        private static readonly string ToolDirectory = Path.Combine(RuntimePaths.RuntimeDirectory, "nvidia-profile-inspector");
        private static readonly string MarkerPath = Path.Combine(ToolDirectory, "facm-native-present-applied.txt");
        private static readonly HttpClient Client = CreateClient();
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        public static bool IsNvidiaDriverPresent()
        {
            try
            {
                var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                return File.Exists(Path.Combine(system, "nvapi64.dll")) ||
                       File.Exists(Path.Combine(system, "nvapi.dll")) ||
                       File.Exists(Path.Combine(windows, "System32", "DriverStore", "FileRepository", "nvapi64.dll"));
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> EnsurePreferNativeAsync(string enginePath, IProgress<PetSetupProgress> progress, CancellationToken token)
        {
            if (!IsNvidiaDriverPresent()) return true;
            await Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                RuntimePaths.Initialize();
                Directory.CreateDirectory(ToolDirectory);
                if (MarkerIsFresh(enginePath))
                {
                    AppLog.Info("NVIDIA desktop pet native-present compatibility is already marked as applied.");
                    return true;
                }

                Report(progress, "正在准备 NVIDIA 透明显示兼容修复...", 22);
                var inspector = await EnsureInspectorAsync(progress, token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(inspector) || !File.Exists(inspector))
                {
                    AppLog.Info("NVIDIA compatibility skipped because Profile Inspector could not be prepared.");
                    return false;
                }

                var applyProfile = Path.Combine(ToolDirectory, "FACM-Desktop-Homunculus-Prefer-Native.nip");
                var restoreProfile = Path.Combine(ToolDirectory, "FACM-Desktop-Homunculus-Restore-Auto.nip");
                File.WriteAllText(applyProfile, BuildProfileXml(PreferNativeValue), Encoding.Unicode);
                File.WriteAllText(restoreProfile, BuildProfileXml(AutoValue), Encoding.Unicode);

                Report(progress, "正在应用 NVIDIA 透明显示兼容设置...", 28);
                var applied = RunSilentImport(inspector, applyProfile, token);
                if (!applied)
                {
                    AppLog.Info("NVIDIA Profile Inspector import did not complete successfully.");
                    return false;
                }

                File.WriteAllText(
                    MarkerPath,
                    "engine=" + (enginePath ?? string.Empty) + Environment.NewLine +
                    "setting=0x20D690F8" + Environment.NewLine +
                    "value=PreferNative(0)" + Environment.NewLine +
                    "appliedUtc=" + DateTime.UtcNow.ToString("O"),
                    Encoding.UTF8);
                AppLog.Info("Applied NVIDIA app profile for Desktop Homunculus: Vulkan/OpenGL present method = Prefer Native.");
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLog.Info("NVIDIA desktop pet compatibility could not be applied: " + exception.Message);
                return false;
            }
            finally
            {
                Gate.Release();
            }
        }

        internal static string BuildProfileXmlForSmokeTest(int value)
        {
            return BuildProfileXml(value);
        }

        private static bool MarkerIsFresh(string enginePath)
        {
            try
            {
                if (!File.Exists(MarkerPath)) return false;
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(MarkerPath);
                if (age > TimeSpan.FromDays(30)) return false;
                var text = File.ReadAllText(MarkerPath);
                return string.IsNullOrWhiteSpace(enginePath) || text.IndexOf("engine=" + enginePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<string> EnsureInspectorAsync(IProgress<PetSetupProgress> progress, CancellationToken token)
        {
            var existing = FindInspectorExecutable();
            if (!string.IsNullOrWhiteSpace(existing)) return existing;

            using (var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi))
            using (var response = await Client.SendAsync(request, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var release = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>;
                var asset = FindZipAsset(release);
                if (asset == null) throw new InvalidOperationException("未找到 NVIDIA 兼容工具下载资源。");
                var url = ReadString(asset, "browser_download_url");
                var name = ReadString(asset, "name") ?? "nvidiaProfileInspector.zip";
                var expectedSize = ReadLong(asset, "size");
                if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("NVIDIA 兼容工具下载地址无效。");

                Directory.CreateDirectory(ToolDirectory);
                var zipPath = Path.Combine(ToolDirectory, SanitizeFileName(name));
                if (!File.Exists(zipPath) || (expectedSize > 0 && new FileInfo(zipPath).Length != expectedSize))
                {
                    var temporary = zipPath + ".download";
                    TryDelete(temporary);
                    await DownloadAsync(url, temporary, expectedSize, progress, token).ConfigureAwait(false);
                    TryDelete(zipPath);
                    File.Move(temporary, zipPath);
                }

                ExtractZipSafely(zipPath, ToolDirectory);
            }

            existing = FindInspectorExecutable();
            if (string.IsNullOrWhiteSpace(existing)) throw new FileNotFoundException("NVIDIA 兼容工具解压后未找到启动程序。");
            return existing;
        }

        private static Dictionary<string, object> FindZipAsset(Dictionary<string, object> release)
        {
            if (release == null) return null;
            object raw;
            if (!release.TryGetValue("assets", out raw)) return null;
            var assets = raw as object[];
            if (assets == null) return null;
            return assets
                .OfType<Dictionary<string, object>>()
                .Select(item => new { Item = item, Name = ReadString(item, "name") ?? string.Empty })
                .Where(item => item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Name.IndexOf("nvidiaProfileInspector", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(item => item.Item)
                .FirstOrDefault();
        }

        private static async Task DownloadAsync(string url, string target, long expectedSize, IProgress<PetSetupProgress> progress, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? (expectedSize > 0 ? expectedSize : (long?)null);
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                        received += read;
                        if (progress != null && total.HasValue && total.Value > 0)
                        {
                            var ratio = Math.Min(1D, received / (double)total.Value);
                            progress.Report(new PetSetupProgress
                            {
                                Message = "正在下载 NVIDIA 兼容组件...",
                                Percent = 22 + (int)Math.Round(5D * ratio)
                            });
                        }
                    }
                }
            }

            var length = new FileInfo(target).Length;
            if (length < 128 * 1024) throw new InvalidDataException("NVIDIA 兼容组件下载文件体积异常。");
            if (expectedSize > 0 && length != expectedSize) throw new InvalidDataException("NVIDIA 兼容组件下载不完整。");
        }

        private static void ExtractZipSafely(string zipPath, string destination)
        {
            var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var stream = File.OpenRead(zipPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                foreach (var entry in archive.Entries)
                {
                    var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("NVIDIA 兼容组件压缩包包含异常路径。");
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                }
            }
        }

        private static string FindInspectorExecutable()
        {
            try
            {
                if (!Directory.Exists(ToolDirectory)) return null;
                return Directory.EnumerateFiles(ToolDirectory, "nvidiaProfileInspector.exe", SearchOption.AllDirectories).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static bool RunSilentImport(string inspector, string profile, CancellationToken token)
        {
            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = inspector,
                Arguments = "-silentImport \"" + profile + "\"",
                WorkingDirectory = Path.GetDirectoryName(inspector) ?? ToolDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                if (process == null) return false;
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(25);
                while (!process.WaitForExit(250))
                {
                    token.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow < deadline) continue;
                    try { process.Kill(); } catch { }
                    return false;
                }
                return process.ExitCode == 0;
            }
        }

        private static string BuildProfileXml(int value)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n" +
                   "<ArrayOfProfile>\r\n" +
                   "  <Profile>\r\n" +
                   "    <ProfileName>FACM Desktop Homunculus Compatibility</ProfileName>\r\n" +
                   "    <Executeables>\r\n" +
                   "      <string>desktop_homunculus.exe</string>\r\n" +
                   "      <string>desktop-homunculus.exe</string>\r\n" +
                   "    </Executeables>\r\n" +
                   "    <Settings>\r\n" +
                   "      <ProfileSetting>\r\n" +
                   "        <SettingNameInfo />\r\n" +
                   "        <SettingID>" + PresentMethodSettingId + "</SettingID>\r\n" +
                   "        <SettingValue>" + value + "</SettingValue>\r\n" +
                   "        <ValueType>Dword</ValueType>\r\n" +
                   "      </ProfileSetting>\r\n" +
                   "    </Settings>\r\n" +
                   "  </Profile>\r\n" +
                   "</ArrayOfProfile>\r\n";
        }

        private static void Report(IProgress<PetSetupProgress> progress, string message, int percent)
        {
            if (progress != null) progress.Report(new PetSetupProgress { Message = message, Percent = percent });
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
        }

        private static long ReadLong(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return 0;
            long parsed;
            return long.TryParse(Convert.ToString(value), out parsed) ? parsed : 0;
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM/3.1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json,application/octet-stream;q=0.9,*/*;q=0.5");
            return client;
        }
    }
}
