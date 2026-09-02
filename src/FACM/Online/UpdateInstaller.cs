using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Online
{
    internal static class UpdateInstaller
    {
        private const long MaximumUpdateBytes = 512L * 1024L * 1024L;
        private const string UpdaterResourceName = "FACM.Resources.FACM.Updater.exe";
        private const string UpdaterFileName = "FACM.Updater.exe";
        private static readonly object ReceiptSync = new object();
        private static readonly Dictionary<string, ValidatedUpdateReceipt> ValidatedPackages =
            new Dictionary<string, ValidatedUpdateReceipt>(StringComparer.OrdinalIgnoreCase);

        private sealed class ValidatedUpdateReceipt
        {
            public string Version;
            public string Sha256;
        }

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
                        await DownloadCandidateAsync(candidate.Url, temporary, progress, cancellationToken).ConfigureAwait(false);

                        var actualHash = ComputeSha256(temporary);
                        if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("更新文件 SHA-256 校验失败。");

                        SignatureInspector.ValidateUpdatePackage(temporary, manifest.Version);
                        stopwatch.Stop();
                        UpdateMirrorRouter.RecordSuccess(candidate.SourceName, stopwatch.ElapsedMilliseconds);
                        AppLog.Info("Update source succeeded: " + candidate.SourceName);

                        if (File.Exists(destination)) File.Delete(destination);
                        File.Move(temporary, destination);
                        RememberValidatedPackage(destination, manifest.Version, manifest.Sha256);
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
                        AppLog.Info("Update source failed: " + candidate.SourceName + "; " + exception.GetType().Name);
                        TryDelete(temporary);
                    }
                }

                throw new IOException("所有更新线路均不可用或文件校验未通过，请稍后重试。", lastFailure);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        public static Task<string> DownloadMigrationBootstrapperAsync(
            Facm4MigrationTarget target,
            UpdateMirrorSource[] sources,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return DownloadAsync(
                new UpdateManifest
                {
                    Enabled = true,
                    Version = target.Version,
                    DownloadUrl = target.BootstrapperUrl,
                    Sha256 = target.BootstrapperSha256,
                    ReleaseNotes = target.ReleaseNotes,
                    ResolvedSources = sources
                },
                progress,
                cancellationToken);
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
                    response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    var total = response.Content.Headers.ContentLength;
                    if (total.HasValue && total.Value > MaximumUpdateBytes)
                        throw new InvalidDataException("更新文件大小异常。");

                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        long received = 0;
                        while (true)
                        {
                            var read = await ReadWithInactivityTimeoutAsync(input, buffer, cancellationToken).ConfigureAwait(false);
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
                    return await input.ReadAsync(buffer, 0, buffer.Length, readTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("更新线路连续 20 秒未收到数据。");
                }
            }
        }

        public static void StartReplacement(string downloadedExecutable)
        {
            if (string.IsNullOrWhiteSpace(downloadedExecutable) || !File.Exists(downloadedExecutable))
                throw new FileNotFoundException("已下载的更新文件不存在。", downloadedExecutable);

            var receipt = TakeValidatedPackage(downloadedExecutable);
            if (receipt == null)
                throw new InvalidDataException("更新包缺少本次下载校验凭据，已停止安装。");

            var actualHash = ComputeSha256(downloadedExecutable);
            if (!string.Equals(actualHash, receipt.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新文件在安装前发生变化，已停止替换。");
            SignatureInspector.ValidateUpdatePackage(downloadedExecutable, receipt.Version);

            RuntimePaths.Initialize();
            var updaterPath = ExtractEmbeddedUpdater();
            var currentProcess = Process.GetCurrentProcess();
            var currentExecutable = currentProcess.MainModule.FileName;
            var arguments = BuildUpdaterArguments(currentProcess.Id, downloadedExecutable, currentExecutable, receipt.Sha256);

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = arguments,
                WorkingDirectory = RuntimePaths.UpdatesDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            AppLog.Info("Update replacement process started; mode=embedded-winexe-updater; console=false");
        }

        public static void StartMigrationReplacement(
            string downloadedBootstrapper,
            string migrationVersion,
            string migrationStatePath)
        {
            if (string.IsNullOrWhiteSpace(downloadedBootstrapper) || !File.Exists(downloadedBootstrapper))
                throw new FileNotFoundException("FACM 4.0 bootstrapper 更新文件不存在。", downloadedBootstrapper);
            if (string.IsNullOrWhiteSpace(migrationVersion) || string.IsNullOrWhiteSpace(migrationStatePath))
                throw new InvalidDataException("FACM 4.0 迁移参数缺失。");

            var receipt = TakeValidatedPackage(downloadedBootstrapper);
            if (receipt == null || !string.Equals(receipt.Version, migrationVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("FACM 4.0 bootstrapper 缺少本次下载校验凭据，已停止安装。");

            var actualHash = ComputeSha256(downloadedBootstrapper);
            if (!string.Equals(actualHash, receipt.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("FACM 4.0 bootstrapper 在安装前发生变化，已停止替换。");
            SignatureInspector.ValidateUpdatePackage(downloadedBootstrapper, migrationVersion);

            RuntimePaths.Initialize();
            var updaterPath = ExtractEmbeddedUpdater();
            var currentProcess = Process.GetCurrentProcess();
            var currentExecutable = currentProcess.MainModule.FileName;
            var arguments = BuildMigrationUpdaterArguments(
                currentProcess.Id,
                downloadedBootstrapper,
                currentExecutable,
                receipt.Sha256,
                migrationVersion,
                migrationStatePath);

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = arguments,
                WorkingDirectory = RuntimePaths.UpdatesDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            AppLog.Info("FACM 4.0 migration replacement process started; mode=embedded-winexe-updater; console=false");
        }

        internal static void ValidateEmbeddedUpdaterForSmokeTest()
        {
            var bytes = ReadEmbeddedUpdaterBytes();
            if (bytes.Length < 1024) throw new InvalidDataException("Embedded updater is unexpectedly small.");
            if (bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
                throw new InvalidDataException("Embedded updater is not a PE executable.");

            var root = Path.Combine(Path.GetTempPath(), "FACM-updater-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, UpdaterFileName);
                File.WriteAllBytes(path, bytes);
                if (!string.Equals(ComputeSha256(path), ComputeSha256(bytes), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Embedded updater extraction changed its bytes.");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static void RememberValidatedPackage(string path, string version, string sha256)
        {
            var fullPath = Path.GetFullPath(path);
            lock (ReceiptSync)
            {
                ValidatedPackages[fullPath] = new ValidatedUpdateReceipt
                {
                    Version = version,
                    Sha256 = sha256
                };
            }
        }

        private static ValidatedUpdateReceipt TakeValidatedPackage(string path)
        {
            var fullPath = Path.GetFullPath(path);
            lock (ReceiptSync)
            {
                ValidatedUpdateReceipt receipt;
                if (!ValidatedPackages.TryGetValue(fullPath, out receipt)) return null;
                ValidatedPackages.Remove(fullPath);
                return receipt;
            }
        }

        private static string ExtractEmbeddedUpdater()
        {
            var bytes = ReadEmbeddedUpdaterBytes();
            var destination = Path.Combine(RuntimePaths.UpdatesDirectory, UpdaterFileName);
            var expectedHash = ComputeSha256(bytes);

            if (File.Exists(destination))
            {
                try
                {
                    if (string.Equals(ComputeSha256(destination), expectedHash, StringComparison.OrdinalIgnoreCase))
                        return destination;
                }
                catch { }
            }

            var temporary = destination + ".new";
            TryDelete(temporary);
            File.WriteAllBytes(temporary, bytes);
            if (!string.Equals(ComputeSha256(temporary), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(temporary);
                throw new InvalidDataException("内置更新器提取校验失败。");
            }

            TryDelete(destination);
            File.Move(temporary, destination);
            return destination;
        }

        private static byte[] ReadEmbeddedUpdaterBytes()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(UpdaterResourceName))
            {
                if (stream == null) throw new InvalidDataException("FACM 内置更新器资源缺失。");
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }

        private static string BuildUpdaterArguments(int parentPid, string source, string destination, string expectedHash)
        {
            return "--parent-pid=" + parentPid +
                   " --source64=\"" + EncodePath(source) + "\"" +
                   " --dest64=\"" + EncodePath(destination) + "\"" +
                   " --sha256=" + expectedHash.ToUpperInvariant();
        }

        private static string BuildMigrationUpdaterArguments(
            int parentPid,
            string source,
            string destination,
            string expectedHash,
            string migrationVersion,
            string migrationStatePath)
        {
            return BuildUpdaterArguments(parentPid, source, destination, expectedHash) +
                   " --mode=migration" +
                   " --migration-version=\"" + EncodeText(migrationVersion) + "\"" +
                   " --migration-state64=\"" + EncodePath(migrationStatePath) + "\"";
        }

        private static string EncodePath(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(value)));
        }

        private static string EncodeText(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static void ValidateManifest(UpdateManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            Uri uri;
            if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !IsApprovedReleaseUrl(uri, manifest.Version))
                throw new InvalidDataException("更新源地址必须指向受信任的 HTTPS 发布文件。");

            if (string.IsNullOrWhiteSpace(manifest.Sha256) || manifest.Sha256.Length != 64 || !IsHex(manifest.Sha256))
                throw new InvalidDataException("更新清单缺少有效的 SHA-256。");
        }

        private static bool IsApprovedReleaseUrl(Uri uri, string version)
        {
            if (uri == null || string.IsNullOrWhiteSpace(version) ||
                !string.IsNullOrWhiteSpace(uri.Query) || !string.IsNullOrWhiteSpace(uri.Fragment))
                return false;

            var normalizedVersion = version.Trim().TrimStart('v', 'V');
            var githubPrefix = "/xianyumht-cmd/facm/releases/download/v" + normalizedVersion + "/";
            if (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                return uri.AbsolutePath.StartsWith(githubPrefix, StringComparison.OrdinalIgnoreCase) &&
                       uri.AbsolutePath.Length > githubPrefix.Length &&
                       !uri.AbsolutePath.Substring(githubPrefix.Length).Contains("/");

            var giteePrefix = "/xymhtcmd/facm/releases/download/v" + normalizedVersion + "/";
            return string.Equals(uri.Host, "gitee.com", StringComparison.OrdinalIgnoreCase) &&
                   uri.AbsolutePath.StartsWith(giteePrefix, StringComparison.OrdinalIgnoreCase) &&
                   uri.AbsolutePath.Length > giteePrefix.Length &&
                   !uri.AbsolutePath.Substring(giteePrefix.Length).Contains("/");
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

        private static string ComputeSha256(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty);
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
