using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.Pets
{
    internal sealed class PetSetupProgress
    {
        public string Message { get; set; }
        public int Percent { get; set; }
    }

    internal sealed class PetActivationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string PersonaId { get; set; }
        public string EnginePath { get; set; }
        public string ModelPath { get; set; }
    }

    internal static class DesktopHomunculusManager
    {
        private const string ReleaseApi = "https://api.github.com/repos/not-elm/desktop-homunculus/releases/latest";
        private const string ReleasesApi = "https://api.github.com/repos/not-elm/desktop-homunculus/releases?per_page=10";
        private static readonly string EngineDirectory = Path.Combine(RuntimePaths.RuntimeDirectory, "desktop-homunculus");
        private static readonly string ModelsDirectory = Path.Combine(RuntimePaths.RuntimeDirectory, "pet-models");
        private static readonly HttpClient DownloadClient = CreateDownloadClient();
        private static readonly SemaphoreSlim ActivationGate = new SemaphoreSlim(1, 1);

        public static async Task<PetActivationResult> ActivateAsync(PetDefinition pet, IProgress<PetSetupProgress> progress, CancellationToken token)
        {
            await ActivationGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                RuntimePaths.Initialize();
                Directory.CreateDirectory(EngineDirectory);
                Directory.CreateDirectory(ModelsDirectory);

                Report(progress, "正在检查桌宠组件...", 2);
                var enginePath = await EnsureEngineReadyAsync(progress, token).ConfigureAwait(false);
                Report(progress, "正在准备 " + pet.Name + "...", 58);
                var modelPath = await EnsureModelAsync(pet, progress, token).ConfigureAwait(false);

                Report(progress, "正在加载桌宠...", 88);
                using (var client = new DesktopHomunculusClient())
                {
                    await client.ImportVrmAsync(modelPath, pet, token).ConfigureAwait(false);
                    Report(progress, "正在启动桌宠...", 94);
                    await client.ActivatePersonaAsync(pet, token).ConfigureAwait(false);
                }

                Report(progress, "桌宠已启动", 100);
                AppLog.Info("Desktop pet activated: " + pet.PersonaId + "; engine=" + enginePath + "; model=" + modelPath);
                return new PetActivationResult
                {
                    Success = true,
                    PersonaId = pet.PersonaId,
                    EnginePath = enginePath,
                    ModelPath = modelPath
                };
            }
            catch (OperationCanceledException)
            {
                return Failure("操作已取消。");
            }
            catch (Exception exception)
            {
                AppLog.Error("Open-source 3D pet activation failed", exception);
                return Failure("桌宠启动失败，请稍后重试。");
            }
            finally
            {
                ActivationGate.Release();
            }
        }

        public static async Task<PetActivationResult> TryRestoreAsync(PetDefinition pet, CancellationToken token)
        {
            try
            {
                var enginePath = FindInstalledEngine();
                using (var client = new DesktopHomunculusClient())
                {
                    if (!await client.IsReadyAsync(token).ConfigureAwait(false))
                    {
                        if (string.IsNullOrWhiteSpace(enginePath)) return Failure("桌宠组件尚未安装。");
                        StartEngine(enginePath);
                        if (!await WaitForApiAsync(TimeSpan.FromSeconds(25), token).ConfigureAwait(false))
                            return Failure("桌宠组件未能正常启动。");
                    }
                }

                var modelPath = GetModelPath(pet);
                if (!File.Exists(modelPath)) return Failure("桌宠资源尚未下载。");
                using (var client = new DesktopHomunculusClient())
                {
                    await client.ImportVrmAsync(modelPath, pet, token).ConfigureAwait(false);
                    await client.ActivatePersonaAsync(pet, token).ConfigureAwait(false);
                }

                return new PetActivationResult
                {
                    Success = true,
                    PersonaId = pet.PersonaId,
                    EnginePath = enginePath ?? FindInstalledEngine(),
                    ModelPath = modelPath
                };
            }
            catch (OperationCanceledException)
            {
                return Failure("恢复已取消。");
            }
            catch (Exception exception)
            {
                AppLog.Info("Desktop pet restore skipped: " + exception.Message);
                return Failure("桌宠暂时无法恢复。");
            }
        }

        public static async Task SubscribeClicksAsync(string personaId, Action clicked, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var client = new DesktopHomunculusClient())
                        await client.SubscribeClicksAsync(personaId, clicked, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    AppLog.Info("Desktop pet event stream reconnecting: " + exception.Message);
                }

                try
                {
                    await Task.Delay(1500, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public static string FindInstalledEngine()
        {
            return DesktopHomunculusLocator.Find();
        }

        public static void OpenEngine()
        {
            var path = FindInstalledEngine();
            if (string.IsNullOrWhiteSpace(path))
                throw new FileNotFoundException("桌宠组件尚未安装，请先选择并应用一个桌宠。 ");
            StartEngine(path);
        }

        private static async Task<string> EnsureEngineReadyAsync(IProgress<PetSetupProgress> progress, CancellationToken token)
        {
            using (var client = new DesktopHomunculusClient())
            {
                if (await client.IsReadyAsync(token).ConfigureAwait(false))
                {
                    Report(progress, "桌宠组件已就绪", 18);
                    return FindInstalledEngine() ?? "running";
                }
            }

            var installed = FindInstalledEngine();
            if (!string.IsNullOrWhiteSpace(installed))
            {
                Report(progress, "正在启动已安装的桌宠组件...", 20);
                StartEngine(installed);
                if (await WaitForApiAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false)) return installed;
                AppLog.Info("Installed desktop pet executable did not expose API after launch: " + installed);
            }

            Report(progress, "首次使用：正在获取桌宠组件...", 4);
            var installer = await DownloadInstallerAsync(progress, token).ConfigureAwait(false);
            var installStartedUtc = DateTime.UtcNow;
            Report(progress, "正在安装桌宠组件...", 42);
            InstallMsi(installer, token);

            Report(progress, "正在确认安装结果...", 48);
            installed = DesktopHomunculusLocator.WaitForInstalledExecutable(installStartedUtc, TimeSpan.FromSeconds(18), token);
            if (string.IsNullOrWhiteSpace(installed))
            {
                AppLog.Info("MSI completed successfully but executable discovery returned no result. Installer=" + installer);
                throw new FileNotFoundException("桌宠组件安装后未能定位启动程序。", installer);
            }

            Report(progress, "正在首次启动桌宠组件...", 52);
            StartEngine(installed);
            if (!await WaitForApiAsync(TimeSpan.FromSeconds(45), token).ConfigureAwait(false))
            {
                var rediscovered = FindInstalledEngine();
                if (!string.IsNullOrWhiteSpace(rediscovered) && !string.Equals(rediscovered, installed, StringComparison.OrdinalIgnoreCase))
                {
                    AppLog.Info("Retrying desktop pet engine with rediscovered path: " + rediscovered);
                    StartEngine(rediscovered);
                    if (await WaitForApiAsync(TimeSpan.FromSeconds(20), token).ConfigureAwait(false)) return rediscovered;
                }
                throw new InvalidOperationException("桌宠组件已安装，但启动服务没有就绪。");
            }
            return installed;
        }

        private static async Task<string> DownloadInstallerAsync(IProgress<PetSetupProgress> progress, CancellationToken token)
        {
            Directory.CreateDirectory(EngineDirectory);
            var release = await ReadReleaseAsync(token).ConfigureAwait(false);
            if (release == null) throw new InvalidOperationException("暂时无法获取桌宠组件下载信息。");

            var asset = FindWindowsInstaller(release);
            if (asset == null) throw new InvalidOperationException("当前发布版本没有可用的 Windows 安装包。");
            var url = ReadString(asset, "browser_download_url");
            var name = ReadString(asset, "name");
            var expectedSize = ReadLong(asset, "size");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("桌宠组件下载信息不完整。");

            var target = Path.Combine(EngineDirectory, SanitizeFileName(name));
            if (File.Exists(target) && (expectedSize <= 0 || new FileInfo(target).Length == expectedSize)) return target;

            var temporary = target + ".download";
            TryDelete(temporary);
            await DownloadFileAsync(url, temporary, 6, 40, progress, "正在下载桌宠组件", token).ConfigureAwait(false);
            var length = new FileInfo(temporary).Length;
            if (length < 1024L * 1024L) throw new InvalidDataException("桌宠组件下载文件体积异常。");
            if (expectedSize > 0 && length != expectedSize)
                throw new InvalidDataException("桌宠组件下载未完成，请重试。");
            TryDelete(target);
            File.Move(temporary, target);
            return target;
        }

        private static async Task<Dictionary<string, object>> ReadReleaseAsync(CancellationToken token)
        {
            var latest = await ReadJsonAsync(ReleaseApi, token).ConfigureAwait(false) as Dictionary<string, object>;
            if (latest != null) return latest;
            var releases = await ReadJsonAsync(ReleasesApi, token).ConfigureAwait(false) as object[];
            if (releases == null) return null;
            return releases.OfType<Dictionary<string, object>>().FirstOrDefault(item => FindWindowsInstaller(item) != null);
        }

        private static Dictionary<string, object> FindWindowsInstaller(Dictionary<string, object> release)
        {
            if (release == null) return null;
            object value;
            if (!release.TryGetValue("assets", out value)) return null;
            var assets = value as object[];
            if (assets == null) return null;
            return assets
                .OfType<Dictionary<string, object>>()
                .Select(item => new { Item = item, Name = ReadString(item, "name") ?? string.Empty })
                .Where(item => item.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Name.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0 || item.Name.IndexOf("amd64", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(item => item.Name.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(item => item.Item)
                .FirstOrDefault();
        }

        private static async Task<string> EnsureModelAsync(PetDefinition pet, IProgress<PetSetupProgress> progress, CancellationToken token)
        {
            Directory.CreateDirectory(ModelsDirectory);
            var path = GetModelPath(pet);
            if (IsValidVrm(path)) return path;

            var temporary = path + ".download";
            TryDelete(temporary);
            await DownloadFileAsync(pet.ModelUrl, temporary, 60, 87, progress, "正在下载 " + pet.Name, token).ConfigureAwait(false);
            if (!IsValidVrm(temporary)) throw new InvalidDataException("下载的桌宠资源格式无效。");
            TryDelete(path);
            File.Move(temporary, path);

            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                AppLog.Info("Downloaded pet model " + pet.OriginalName + "; sha256=" + BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty));
            return path;
        }

        private static string GetModelPath(PetDefinition pet)
        {
            return Path.Combine(ModelsDirectory, pet.Id + ".vrm");
        }

        private static bool IsValidVrm(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length < 512L * 1024L) return false;
                var bytes = new byte[4];
                using (var stream = File.OpenRead(path))
                {
                    if (stream.Read(bytes, 0, bytes.Length) != bytes.Length) return false;
                }
                return bytes[0] == (byte)'g' && bytes[1] == (byte)'l' && bytes[2] == (byte)'T' && bytes[3] == (byte)'F';
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> WaitForApiAsync(TimeSpan timeout, CancellationToken token)
        {
            var end = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < end)
            {
                token.ThrowIfCancellationRequested();
                using (var client = new DesktopHomunculusClient())
                {
                    if (await client.IsReadyAsync(token).ConfigureAwait(false)) return true;
                }
                await Task.Delay(700, token).ConfigureAwait(false);
            }
            return false;
        }

        private static void StartEngine(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || string.Equals(path, "running", StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(path)) throw new FileNotFoundException("桌宠启动程序不存在。", path);

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var name = process.ProcessName ?? string.Empty;
                    if (name.IndexOf("homunculus", StringComparison.OrdinalIgnoreCase) >= 0) return;
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            var started = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
                UseShellExecute = true
            });
            if (started != null)
            {
                try { AppLog.Info("Desktop pet engine process started: pid=" + started.Id + "; path=" + path); }
                finally { started.Dispose(); }
            }
        }

        private static void InstallMsi(string installer, CancellationToken token)
        {
            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = "/i \"" + installer + "\" /passive /norestart",
                UseShellExecute = true,
                Verb = "runas"
            }))
            {
                if (process == null) throw new InvalidOperationException("无法启动桌宠安装程序。");
                while (!process.WaitForExit(500)) token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0 && process.ExitCode != 3010 && process.ExitCode != 1641)
                    throw new InvalidOperationException("桌宠组件安装失败，代码 " + process.ExitCode + "。");
                AppLog.Info("Desktop pet MSI completed with exit code " + process.ExitCode + "; installer=" + installer);
            }
        }

        private static async Task<object> ReadJsonAsync(string url, CancellationToken token)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                using (var response = await DownloadClient.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        AppLog.Info("Desktop pet release endpoint returned HTTP " + (int)response.StatusCode + ": " + url);
                        return null;
                    }
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLog.Info("Desktop pet release lookup failed: " + exception.Message);
                return null;
            }
        }

        private static async Task DownloadFileAsync(string url, string target, int startPercent, int endPercent, IProgress<PetSetupProgress> progress, string message, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var response = await DownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
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
                        var ratio = total.HasValue && total.Value > 0 ? Math.Min(1D, received / (double)total.Value) : 0D;
                        var percent = startPercent + (int)Math.Round((endPercent - startPercent) * ratio);
                        var sizeText = (received / 1024D / 1024D).ToString("0.0") + " MB";
                        if (total.HasValue) sizeText += " / " + (total.Value / 1024D / 1024D).ToString("0.0") + " MB";
                        Report(progress, message + "  " + sizeText, percent);
                    }
                }
            }
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
            long number;
            return long.TryParse(Convert.ToString(value), out number) ? number : 0;
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static HttpClient CreateDownloadClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM/3.1 (+https://github.com/xianyumht-cmd/facm)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json,application/octet-stream;q=0.9,*/*;q=0.5");
            return client;
        }

        private static PetActivationResult Failure(string message)
        {
            return new PetActivationResult { Success = false, ErrorMessage = message };
        }

        private static void Report(IProgress<PetSetupProgress> progress, string message, int percent)
        {
            if (progress != null) progress.Report(new PetSetupProgress { Message = message, Percent = Math.Max(0, Math.Min(100, percent)) });
        }
    }
}
