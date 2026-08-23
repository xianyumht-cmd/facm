using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        private const int MetadataRaceWidth = 3;
        private const int MaxMetadataBytes = 128 * 1024;

        public static async Task<OnlineSnapshot> FetchSnapshotAsync(CancellationToken cancellationToken)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var snapshot = new OnlineSnapshot
            {
                CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0)
            };

            try
            {
                var sources = UpdateMirrorRouter.LoadCachedAndBuiltInSources();

                // The mirror catalog is transport configuration only. It may add or reprioritize
                // HTTPS proxy prefixes, but UpdateMirrorRouter always keeps the built-in GitHub
                // fallback. A bad catalog therefore cannot remove the official origin path.
                var catalogResult = await TryDownloadFromMirrorsAsync<UpdateMirrorCatalog>(
                    UpdateMirrorRouter.CatalogOriginUrl,
                    sources,
                    MetadataRaceWidth,
                    cancellationToken,
                    UpdateMirrorRouter.IsValidCatalog).ConfigureAwait(false);
                if (catalogResult.Value != null)
                {
                    UpdateMirrorRouter.SaveCatalog(catalogResult.Value);
                    sources = UpdateMirrorRouter.MergeWithBuiltIns(catalogResult.Value.Sources);
                }

                var updateResult = await TryDownloadFromMirrorsAsync<UpdateManifest>(
                    UpdateManifestUrl,
                    sources,
                    MetadataRaceWidth,
                    cancellationToken,
                    IsValidUpdateManifest).ConfigureAwait(false);
                if (updateResult.Value == null)
                {
                    throw new HttpRequestException("No update metadata source returned a valid FACM manifest.");
                }

                snapshot.Update = updateResult.Value;
                snapshot.Update.ResolvedSources = sources;
                snapshot.MetadataSourceName = updateResult.SourceName;

                // Announcements can contain human-readable text and links, so do not accept them
                // from an unauthenticated third-party proxy. A temporary GitHub announcement
                // failure must never make update discovery fail.
                try
                {
                    using (var client = CreateClient(TimeSpan.FromSeconds(8)))
                    {
                        snapshot.Announcement = await DownloadJsonAsync<AnnouncementManifest>(
                            client,
                            AnnouncementManifestUrl,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    AppLog.Error("Announcement metadata request failed", exception);
                }

                Version latest;
                if (snapshot.Update.Enabled && TryParseVersion(snapshot.Update.Version, out latest))
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
                snapshot.ErrorMessage = "暂时无法读取更新信息，请稍后重试。";
                AppLog.Error("Online metadata request failed", exception);
            }

            return snapshot;
        }

        private static async Task<MirrorFetchResult<T>> TryDownloadFromMirrorsAsync<T>(
            string originUrl,
            UpdateMirrorSource[] sources,
            int raceWidth,
            CancellationToken cancellationToken,
            Func<T, bool> validator) where T : class
        {
            var candidates = UpdateMirrorRouter.BuildCandidates(originUrl, sources);
            if (candidates.Length == 0) return new MirrorFetchResult<T>();

            raceWidth = Math.Max(1, Math.Min(3, raceWidth));
            for (var offset = 0; offset < candidates.Length; offset += raceWidth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = candidates.Skip(offset).Take(raceWidth).ToArray();
                using (var batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var pending = new List<Task<MirrorFetchResult<T>>>();
                    foreach (var candidate in batch)
                    {
                        pending.Add(DownloadCandidateAsync<T>(candidate, batchCancellation.Token, validator));
                    }

                    while (pending.Count > 0)
                    {
                        var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                        pending.Remove(completed);
                        MirrorFetchResult<T> result;
                        try
                        {
                            result = await completed.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            continue;
                        }

                        if (result.Value != null)
                        {
                            batchCancellation.Cancel();
                            return result;
                        }
                    }
                }
            }

            return new MirrorFetchResult<T>();
        }

        private static async Task<MirrorFetchResult<T>> DownloadCandidateAsync<T>(
            UpdateDownloadCandidate candidate,
            CancellationToken cancellationToken,
            Func<T, bool> validator) where T : class
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var client = CreateClient(TimeSpan.FromSeconds(7)))
                {
                    var value = await DownloadJsonAsync<T>(client, candidate.Url, cancellationToken).ConfigureAwait(false);
                    if (value == null || validator != null && !validator(value))
                    {
                        throw new InvalidDataException("Mirror returned invalid FACM metadata.");
                    }

                    stopwatch.Stop();
                    UpdateMirrorRouter.RecordSuccess(candidate.SourceName, stopwatch.ElapsedMilliseconds);
                    return new MirrorFetchResult<T>
                    {
                        Value = value,
                        SourceName = candidate.SourceName
                    };
                }
            }
            catch (OperationCanceledException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    stopwatch.Stop();
                    UpdateMirrorRouter.RecordFailure(candidate.SourceName, stopwatch.ElapsedMilliseconds);
                }
                throw;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                UpdateMirrorRouter.RecordFailure(candidate.SourceName, stopwatch.ElapsedMilliseconds);
                AppLog.Info("Update metadata source failed: " + candidate.SourceName + "; " + exception.GetType().Name);
                return new MirrorFetchResult<T>();
            }
        }

        private static HttpClient CreateClient(TimeSpan timeout)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            var client = new HttpClient(handler)
            {
                Timeout = timeout
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FACM-Windows/3.5");
            client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            return client;
        }

        private static async Task<T> DownloadJsonAsync<T>(
            HttpClient client,
            string url,
            CancellationToken cancellationToken)
        {
            var separator = url.IndexOf('?') >= 0 ? "&" : "?";
            var requestUrl = url + separator + "ts=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (var response = await client.GetAsync(
                requestUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var length = response.Content.Headers.ContentLength;
                if (length.HasValue && length.Value > MaxMetadataBytes)
                {
                    throw new InvalidDataException("FACM metadata response is too large.");
                }

                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var buffer = new MemoryStream())
                {
                    var chunk = new byte[8192];
                    int read;
                    while ((read = await input.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        if (buffer.Length + read > MaxMetadataBytes)
                            throw new InvalidDataException("FACM metadata response is too large.");
                        buffer.Write(chunk, 0, read);
                    }
                    buffer.Position = 0;
                    var serializer = new DataContractJsonSerializer(typeof(T));
                    return (T)serializer.ReadObject(buffer);
                }
            }
        }

        private static bool IsValidUpdateManifest(UpdateManifest manifest)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version)) return false;

            Version parsedVersion;
            if (!TryParseVersion(manifest.Version, out parsedVersion)) return false;

            Version minimum;
            if (!string.IsNullOrWhiteSpace(manifest.MinimumVersion) &&
                !TryParseVersion(manifest.MinimumVersion, out minimum)) return false;

            Uri download;
            if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out download) ||
                download.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(download.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                return false;

            var expectedPrefix = "/xianyumht-cmd/facm/releases/download/v" + manifest.Version.Trim().TrimStart('v', 'V') + "/";
            if (!download.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) return false;

            if (string.IsNullOrWhiteSpace(manifest.Sha256) || manifest.Sha256.Length != 64) return false;
            for (var index = 0; index < manifest.Sha256.Length; index++)
            {
                var character = manifest.Sha256[index];
                var valid = character >= '0' && character <= '9' ||
                            character >= 'a' && character <= 'f' ||
                            character >= 'A' && character <= 'F';
                if (!valid) return false;
            }

            return true;
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

        private sealed class MirrorFetchResult<T> where T : class
        {
            public T Value { get; set; }
            public string SourceName { get; set; }
        }
    }
}
