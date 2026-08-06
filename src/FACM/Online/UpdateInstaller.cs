using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Online
{
    internal static class UpdateInstaller
    {
        public static async Task<string> DownloadAsync(
            UpdateManifest manifest,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            ValidateManifest(manifest);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FACM",
                "Updates");
            Directory.CreateDirectory(directory);

            var version = SanitizeFileName(manifest.Version ?? "latest");
            var destination = Path.Combine(directory, "FACM-" + version + ".exe");
            var temporary = destination + ".download";

            try
            {
                using (var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                })
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) })
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM-Windows-Updater/3.1");
                    using (var response = await client.GetAsync(
                        manifest.DownloadUrl,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        var total = response.Content.Headers.ContentLength;
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
                            int read;
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                            {
                                await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                                received += read;
                                if (total.HasValue && total.Value > 0 && progress != null)
                                {
                                    progress.Report((int)Math.Min(100, received * 100L / total.Value));
                                }
                            }
                        }
                    }
                }

                var actualHash = ComputeSha256(temporary);
                if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("更新文件 SHA-256 校验失败。已停止安装。" );
                }

                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temporary, destination);
                if (progress != null) progress.Report(100);
                AppLog.Info("Update package downloaded and verified: " + Path.GetFileName(destination));
                return destination;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch
                {
                }
            }
        }

        public static void StartReplacement(string downloadedExecutable)
        {
            if (string.IsNullOrWhiteSpace(downloadedExecutable) || !File.Exists(downloadedExecutable))
            {
                throw new FileNotFoundException("已下载的更新文件不存在。", downloadedExecutable);
            }

            var currentExecutable = Process.GetCurrentProcess().MainModule.FileName;
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FACM",
                "Updates");
            Directory.CreateDirectory(directory);

            var scriptPath = Path.Combine(directory, "apply-" + Guid.NewGuid().ToString("N") + ".ps1");
            var script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine("$processId = " + Process.GetCurrentProcess().Id);
            script.AppendLine("$source = '" + EscapePowerShellLiteral(downloadedExecutable) + "'");
            script.AppendLine("$destination = '" + EscapePowerShellLiteral(currentExecutable) + "'");
            script.AppendLine("try { Wait-Process -Id $processId -Timeout 120 -ErrorAction SilentlyContinue } catch { }");
            script.AppendLine("Copy-Item -LiteralPath $source -Destination $destination -Force");
            script.AppendLine("Start-Process -FilePath $destination");
            script.AppendLine("Remove-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue");
            script.AppendLine("Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue");
            File.WriteAllText(scriptPath, script.ToString(), new UTF8Encoding(true));

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + scriptPath + "\"",
                WorkingDirectory = directory,
                UseShellExecute = true,
                Verb = "runas"
            });
            AppLog.Info("Update replacement process started");
        }

        private static void ValidateManifest(UpdateManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            Uri uri;
            if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("更新下载地址必须是有效的 HTTPS 地址。" );
            }

            if (string.IsNullOrWhiteSpace(manifest.Sha256) ||
                manifest.Sha256.Length != 64 ||
                !IsHex(manifest.Sha256))
            {
                throw new InvalidDataException("更新清单缺少有效的 SHA-256。" );
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
            {
                value = value.Replace(character, '_');
            }
            return value;
        }

        private static string EscapePowerShellLiteral(string value)
        {
            return value.Replace("'", "''");
        }
    }
}
