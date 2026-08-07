using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;
using Microsoft.Win32;

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

                Report(progress, "正在检查开源桌宠引擎...", 2);
                var enginePath = await EnsureEngineReadyAsync(progress, token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(enginePath))
                    return Failure("无法启动 Desktop Homunculus 开源桌宠引擎。");

                Report(progress, "正在准备 " + pet.Name + " 的 VRM 模型...", 58);
                var modelPath = await EnsureModelAsync(pet, progress, token).ConfigureAwait(false);

                Report(progress, "正在导入 VRM 模型...", 88);
                using (var client = new DesktopHomunculusClient())
                {
                    await client.ImportVrmAsync(modelPath, pet, token).ConfigureAwait(false);
                    Report(progress, "正在切换并启动桌宠...", 94);
                    await client.ActivatePersonaAsync(pet, token).ConfigureAwait(false);
                }

                Report(progress, "桌宠已启动；点击角色可打开 FACM 控制面板", 100);
                AppLog.Info("Desktop Homunculus persona activated: " + pet.PersonaId + "; model=" + modelPath);
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
                return Failure(exception.Message);
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
                using (var client = new DesktopHomunculusClient())
                {
                    if (!await client.IsReadyAsync(token).ConfigureAwait(false))
                    {
                        var enginePath = FindInstalledEngine();
                        if (string.IsNullOrWhiteSpace(enginePath)) return Failure("引擎尚未安装。 ");
                        StartEngine(enginePath);
                        if (!await WaitForApiAsync(TimeSpan.FromSeconds(25), token).ConfigureAwait(false))
                            return Failure("引擎启动后 API 未就绪。");
                    }
                }

                var modelPath = GetModelPath(pet);
                if (!File.Exists(modelPath)) return Failure("模型尚未下载。");
                using (var client = new DesktopHomunculusClient())
                {
                    await client.ImportVrmAsync(modelPath, pet, token).ConfigureAwait(false);
                    await client.ActivatePersonaAsync(pet, token).ConfigureAwait(false);
                }
                return new PetActivationResult
                {
                    Success = true,
                    PersonaId = pet.PersonaId,
                    EnginePath = FindInstalledEngine(),
                    ModelPath = modelPath
                };
            }
            catch (Exception exception)
            {
                AppLog.Info("Open-source pet restore skipped: " + exception.Message);
                return Failure(exception.Message);
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
            var candidates = new List<string>();
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            AddCandidate(candidates, Path.Combine(local, "Programs", "desktop-homunculus", "desktop-homunculus.exe"));
            AddCandidate(candidates, Path.Combine(local, "desktop-homunculus", "desktop-homunculus.exe"));
            AddCandidate(candidates, Path.Combine(programFiles, "desktop-homunculus", "desktop-homunculus.exe"));
            AddCandidate(candidates, Path.Combine(programFiles, "Desktop Homunculus", "desktop-homunculus.exe"));
            AddCandidate(candidates, Path.Combine(programFilesX86, "desktop-homunculus", "desktop-homunculus.exe"));

            foreach (var path in ReadInstallLocations())
            {
                AddCandidate(candidates, Path.Combine(path, "desktop-homunculus.exe"));
                AddCandidate(candidates, Path.Combine(path, "desktop_homunculus.exe"));
                AddCandidate(candidates, Path.Combine(path, "homunculus.exe"));
            }

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }

            var roots = new[]
            {
                Path.Combine(local, "Programs"),
                Path.Combine(programFiles, "desktop-homunculus"),
                Path.Combine(programFiles, "Desktop Homunculus")
            };
            foreach (var root in roots)
            {
                var found = FindExecutableUnder(root);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
            return null;
        }

        public static void OpenEngine()
        {
            var path = FindInstalledEngine();
            if (string.IsNullOrWhiteSpace(path))
                throw new FileNotFoundException("Desktop Homunculus 尚未安装，请先选择并应用一个桌宠模型。");
            StartEngine(path);
        }

        private static async Task<string> EnsureEngineReadyAsync(IProgress<PetSetupProgress> progress, CancellationToken token)
        {
            using (var client = new DesktopHomunculusClient())
            {
                if (await client.IsReadyAsync(token).ConfigureAwait(false))
                {
                    Report(progress, "已连接正在运行的 Desktop Homunculus", 18);
                    return FindInstalledEngine() ?? "running";
                }
            }

            var installed = FindInstalledEngine();
            if (!string.IsNullOrWhiteSpace(installed))
            {
                Report(progress, "正在启动已安装的开源桌宠引擎...", 20);
                StartEngine(installed);
                if (await WaitForApiAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false)) return installed;
                throw new InvalidOperationException("Desktop Homunculus 已启动，但本地 API 在 30 秒内没有就绪。请检查显卡设置或引擎窗口提示。");
            }

            Report(progress, "首次使用：正在获取 Desktop Homunculus 官方安装包信息...", 4);
            var installer = await DownloadInstallerAsync(progress, token).ConfigureAwait(false);
            Report(progress, "正在安装开源桌宠引擎（约 200 MB）...", 42);
            InstallMsi(installer, token);

            installed = FindInstalledEngine();
            if (string.IsNullOrWhiteSpace(installed))
                throw new FileNotFoundException("安装完成，但没有找到 desktop-homunculus.exe。请查看安装程序是否被安全软件拦截。", installer);

            Report(progress, "正在首次启动开源桌宠引擎...", 52);
            StartEngine(installed);
            if (!await WaitForApiAsync(TimeSpan.FromSeconds(45), token).ConfigureAwait(false))
                throw new InvalidOperationException("Desktop Homunculus 安装成功，但本地 API 没有就绪。NVIDIA 显卡需将 Vulkan/OpenGL present method 设为 Prefer native。 ");
            return installed;
        }

        private static async Task<string> DownloadInstallerAsync(IProgress<PetSetupProgress> progress, CancellationToken token)
        {
            Directory.CreateDirectory(EngineDirectory);
            var release = await ReadJsonAsync(ReleaseApi, token).ConfigureAwait(false);
            if (release == null) release = FirstRelease(await ReadJsonAsync(ReleasesApi, token).ConfigureAwait(false));
            if (release == null) throw new InvalidOperationException("GitHub 没有返回 Desktop Homunculus 发布信息。 ");

            var asset = FindWindowsInstaller(release);
            if (asset == null) throw new InvalidOperationException("最新发布中没有找到 Windows x64 MSI 安装包。 ");

            var url = ReadString(asset, "browser_download_url");
            var name = ReadString(asset, "name");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("发布信息中的安装包地址无效。 ");

            var target = Path.Combine(EngineDirectory, SanitizeFileName(name));
            var expectedSize = ReadLong(asset, "size");
            if (File.Exists(target) && (expectedSize <= 0 || new FileInfo(target).Length == expectedSize)) return target;

            var temporary = target + ".download";
            if (File.Exists(temporary)) File.Delete(temporary);
            await DownloadFileAsync(url, temporary, 6, 40, progress, "正在下载开源桌宠引擎", token).ConfigureAwait(false);
            if (new FileInfo(temporary).Length < 50L * 1024L * 1024L)
                throw new InvalidDataException("下载到的引擎安装包体积异常。 ");
            if (File.Exists(target)) File.Delete(target);
            File.Move(temporary, target);
            return target;
        }

        private static async Task<string> EnsureModelAsync(PetDefinition pet, IProgress<PetSetupProgress> progress, CancellationToken token)
        {
            Directory.CreateDirectory(ModelsDirectory);
            var path = GetModelPath(pet);
            if (IsValidVrm(path)) return path;

            var temporary = path + ".download";
            if (File.Exists(temporary)) File.Delete(temporary);
            await DownloadFileAsync(pet.ModelUrl, temporary, 60, 87, progress, "正在下载 " + pet.Name + " VRM 模型", token).ConfigureAwait(false);
            if (!IsValidVrm(temporary))
                throw new InvalidDataException("下载的 VRM 模型格式无效：" + pet.Name);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);

            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                AppLog.Info("Downloaded CC0 VRM " + pet.OriginalName + "; sha256=" + BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty));
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
                if (!File.Exists(path) || new FileInfo(path).Length < 1024 * 512) return false;
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
                await Task.Delay(750, token).ConfigureAwait(false);
            }
            return false;
        }

        private static void StartEngine(string path)
        {
            if (Process.GetProcesses().Any(process =>
            {
                try
                {
                    return process.ProcessName.IndexOf("homunculus", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch
                {
                    return false;
                }
            })) return;

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
                UseShellExecute = true
            });
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
                if (process == null) throw new InvalidOperationException("无法启动 MSI 安装程序。 ");
                while (!process.WaitForExit(500)) token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0 && process.ExitCode != 3010)
                    throw new InvalidOperationException("Desktop Homunculus 安装失败，MSI 返回 " + process.ExitCode + "。 ");
            }
        }

        private static async Task<object> ReadJsonAsync(string url, CancellationToken token)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                using (var response = await DownloadClient.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return null;
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text);
                }
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, object> FirstRelease(object value)
        {
            var array = value as object[];
            if (array == null || array.Length == 0) return null;
            return array[0] as Dictionary<string, object>;
        }

        private static Dictionary<string, object> FindWindowsInstaller(object releaseObject)
        {
            var release = releaseObject as Dictionary<string, object>;
            if (release == null) return null;
            object value;
            if (!release.TryGetValue("assets", out value)) return null;
            var assets = value as object[];
            if (assets == null) return null;

            return assets
                .OfType<Dictionary<string, object>>()
                .Select(item => new { Item = item, Name = ReadString(item, "name") ?? string.Empty })
                .Where(item => item.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Name.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(item => item.Name.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(item => item.Item)
                .FirstOrDefault();
        }

        private static async Task DownloadFileAsync(
            string url,
            string target,
            int startPercent,
            int endPercent,
            IProgress<PetSetupProgress> progress,
            string message,
            CancellationToken token)
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

        private static IEnumerable<string> ReadInstallLocations()
        {
            var output = new List<string>();
            var roots = new[]
            {
                Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
            };
            foreach (var root in roots)
            {
                using (root)
                {
                    if (root == null) continue;
                    foreach (var subName in root.GetSubKeyNames())
                    {
                        using (var sub = root.OpenSubKey(subName))
                        {
                            var display = Convert.ToString(sub == null ? null : sub.GetValue("DisplayName"));
                            if (display.IndexOf("desktop", StringComparison.OrdinalIgnoreCase) < 0 ||
                                display.IndexOf("homunculus", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            var location = Convert.ToString(sub.GetValue("InstallLocation"));
                            if (!string.IsNullOrWhiteSpace(location)) output.Add(location.Trim(' ', '"'));
                        }
                    }
                }
            }
            return output;
        }

        private static string FindExecutableUnder(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return null;
                return Directory.GetFiles(root, "*homunculus*.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(path => Path.GetFileName(path).IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) < 0);
            }
            catch
            {
                return null;
            }
        }

        private static void AddCandidate(ICollection<string> list, string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && !list.Contains(path)) list.Add(path);
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
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

        private static HttpClient CreateDownloadClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
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
