using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Online
{
    internal static class UpdateInstaller
    {
        private const long MaximumUpdateBytes = 512L * 1024L * 1024L;

        public static async Task<string> DownloadAsync(
            UpdateManifest manifest,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            ValidateManifest(manifest);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            RuntimePaths.Initialize();

            var directory = RuntimePaths.UpdatesDirectory;
            Directory.CreateDirectory(directory);

            var version = SanitizeFileName(manifest.Version ?? "latest");
            var destination = Path.Combine(directory, "FACM-" + version + ".exe");
            var temporary = destination + ".download";
            var candidates = UpdateMirrorRouter.BuildCandidates(manifest.DownloadUrl, manifest.ResolvedSources);
            if (candidates.Length == 0)
                throw new InvalidDataException("没有可用的更新下载线路。");

            Exception lastFailure = null;
            try
            {
                foreach (var candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TryDelete(temporary);
                    if (progress != null) progress.Report(0);

                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        await DownloadCandidateAsync(
                            candidate.Url,
                            temporary,
                            progress,
                            cancellationToken).ConfigureAwait(false);

                        var actualHash = ComputeSha256(temporary);
                        if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("更新文件 SHA-256 校验失败。");
                        }

                        SignatureInspector.ValidateUpdatePackage(temporary, manifest.Version);
                        stopwatch.Stop();
                        UpdateMirrorRouter.RecordSuccess(candidate.SourceName, stopwatch.ElapsedMilliseconds);
                        AppLog.Info("Update source succeeded: " + candidate.SourceName);

                        if (File.Exists(destination)) File.Delete(destination);
                        File.Move(temporary, destination);
                        if (progress != null) progress.Report(100);
                        AppLog.Info("Update package downloaded and verified: " + Path.GetFileName(destination));
                        return destination;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        stopwatch.Stop();
                        lastFailure = exception;
                        UpdateMirrorRouter.RecordFailure(candidate.SourceName, stopwatch.ElapsedMilliseconds);
                        AppLog.Info(
                            "Update source failed: " + candidate.SourceName + "; " +
                            exception.GetType().Name);
                        TryDelete(temporary);
                    }
                }

                throw new IOException(
                    "所有更新线路均不可用或文件校验未通过，请稍后重试。",
                    lastFailure);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        private static async Task DownloadCandidateAsync(
            string url,
            string temporary,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            using (var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            using (var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
            using (var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM-Windows-Updater/3.5");
                headerTimeout.CancelAfter(TimeSpan.FromSeconds(10));

                HttpResponseMessage response = null;
                try
                {
                    response = await client.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead,
                        headerTimeout.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    var total = response.Content.Headers.ContentLength;
                    if (total.HasValue && total.Value > MaximumUpdateBytes)
                        throw new InvalidDataException("更新文件大小异常。");

                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(
                        temporary,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        true))
                    {
                        var buffer = new byte[81920];
                        long received = 0;
                        while (true)
                        {
                            var read = await ReadWithInactivityTimeoutAsync(
                                input,
                                buffer,
                                cancellationToken).ConfigureAwait(false);
                            if (read <= 0) break;

                            received += read;
                            if (received > MaximumUpdateBytes)
                                throw new InvalidDataException("更新文件大小异常。");

                            await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            if (total.HasValue && total.Value > 0 && progress != null)
                                progress.Report((int)Math.Min(99, received * 100L / total.Value));
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("更新线路连接或传输超时。");
                }
                finally
                {
                    if (response != null) response.Dispose();
                }
            }
        }

        private static async Task<int> ReadWithInactivityTimeoutAsync(
            Stream input,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                readTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    return await input.ReadAsync(
                        buffer,
                        0,
                        buffer.Length,
                        readTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("更新线路连续 20 秒未收到数据。");
                }
            }
        }

        public static void StartReplacement(string downloadedExecutable, UpdateManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(downloadedExecutable) || !File.Exists(downloadedExecutable))
                throw new FileNotFoundException("已下载的更新文件不存在。", downloadedExecutable);

            ValidateManifest(manifest);
            var actualHash = ComputeSha256(downloadedExecutable);
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新文件在安装前发生变化，已停止替换。");
            SignatureInspector.ValidateUpdatePackage(downloadedExecutable, manifest.Version);

            RuntimePaths.Initialize();
            var currentProcess = Process.GetCurrentProcess();
            var currentExecutable = currentProcess.MainModule.FileName;
            var arguments = UpdateReplacementHost.BuildApplyArguments(
                currentProcess.Id,
                currentExecutable,
                manifest.Sha256);

            Process.Start(new ProcessStartInfo
            {
                FileName = downloadedExecutable,
                Arguments = arguments,
                WorkingDirectory = RuntimePaths.UpdatesDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            AppLog.Info("Update replacement process started; mode=self-updater; console=false");
        }

        private static void ValidateManifest(UpdateManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            Uri uri;
            if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新源地址必须指向 GitHub 的有效 HTTPS 发布文件。");
            }

            if (string.IsNullOrWhiteSpace(manifest.Sha256) ||
                manifest.Sha256.Length != 64 ||
                !IsHex(manifest.Sha256))
            {
                throw new InvalidDataException("更新清单缺少有效的 SHA-256。");
            }
        }

        private static bool IsHex(string value)
        {
            foreach (var character in value)
            {
                var valid = character >= '0' && character <= '9' ||
                            character >= 'a' && character <= 'f' ||
                            character >= 'A' && character <= 'F';
                if (!valid) return false;
            }
            return true;
        }

        private static string ComputeSha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var character in Path.GetInvalidFileNameChars())
                value = value.Replace(character, '_');
            return value;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }
}
