using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Online
{
    internal static class OnlineService
    {
        internal const string UpdateManifestUrl =
            "https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/version.json";

        internal const string AnnouncementManifestUrl =
            "https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/announcement.json";

        private const string PreviewUpdateManifestUrl =
            "https://raw.githubusercontent.com/xianyumht-cmd/facm/codex/facm-online-toolbundle-20260807/online/version.json";

        private const string PreviewAnnouncementManifestUrl =
            "https://raw.githubusercontent.com/xianyumht-cmd/facm/codex/facm-online-toolbundle-20260807/online/announcement.json";

        public static async Task<OnlineSnapshot> FetchSnapshotAsync(CancellationToken cancellationToken)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var snapshot = new OnlineSnapshot
            {
                CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0)
            };

            try
            {
                using (var client = CreateClient())
                {
                    var updateTask = DownloadJsonWithFallbackAsync<UpdateManifest>(
                        client,
                        UpdateManifestUrl,
                        PreviewUpdateManifestUrl,
                        cancellationToken);
                    var announcementTask = DownloadJsonWithFallbackAsync<AnnouncementManifest>(
                        client,
                        AnnouncementManifestUrl,
                        PreviewAnnouncementManifestUrl,
                        cancellationToken);
                    await Task.WhenAll(updateTask, announcementTask).ConfigureAwait(false);
                    snapshot.Update = updateTask.Result;
                    snapshot.Announcement = announcementTask.Result;
                }

                Version latest;
                if (snapshot.Update != null &&
                    snapshot.Update.Enabled &&
                    TryParseVersion(snapshot.Update.Version, out latest))
                {
                    snapshot.LatestVersion = latest;
                    snapshot.UpdateAvailable = latest.CompareTo(snapshot.CurrentVersion) > 0;

                    Version minimum;
                    var belowMinimum = TryParseVersion(snapshot.Update.MinimumVersion, out minimum) &&
                                       snapshot.CurrentVersion.CompareTo(minimum) < 0;
                    snapshot.ForceUpdateRequired = snapshot.UpdateAvailable &&
                                                   (snapshot.Update.ForceUpdate || belowMinimum);
                }
            }
            catch (OperationCanceledException)
            {
                snapshot.ErrorMessage = "联网请求已取消。";
            }
            catch (Exception exception)
            {
                snapshot.ErrorMessage = exception.Message;
                AppLog.Error("Online metadata request failed", exception);
            }

            return snapshot;
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(12)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM-Windows/3.1");
            client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            return client;
        }

        private static async Task<T> DownloadJsonWithFallbackAsync<T>(
            HttpClient client,
            string primaryUrl,
            string previewUrl,
            CancellationToken cancellationToken)
        {
            try
            {
                return await DownloadJsonAsync<T>(client, primaryUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException primaryException)
            {
                AppLog.Info("Primary online feed unavailable, trying preview feed: " + primaryException.Message);
                try
                {
                    return await DownloadJsonAsync<T>(client, previewUrl, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception previewException)
                {
                    throw new InvalidOperationException(
                        "在线配置读取失败。请确认仓库 main 分支已包含 online/version.json 与 online/announcement.json。",
                        previewException);
                }
            }
        }

        private static async Task<T> DownloadJsonAsync<T>(
            HttpClient client,
            string url,
            CancellationToken cancellationToken)
        {
            var separator = url.IndexOf('?') >= 0 ? "&" : "?";
            var requestUrl = url + separator + "ts=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (var response = await client.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                using (var stream = new MemoryStream(bytes, false))
                {
                    var serializer = new DataContractJsonSerializer(typeof(T));
                    return (T)serializer.ReadObject(stream);
                }
            }
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var normalized = value.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(1);
            }

            return Version.TryParse(normalized, out version);
        }
    }
}
