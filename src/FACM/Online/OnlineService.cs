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
        private const int UpdateManifestProbeWidth = 6;
        private static readonly TimeSpan UpdateManifestProbeTimeout = TimeSpan.FromSeconds(4);
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

                // Refresh transport configuration and fetch the required version manifest in
                // parallel. A slow mirrors.json or a blocked GitHub announcement must never hold
                // up a version check that has already succeeded through another mirror.
                var catalogTask = TryDownloadFromMirrorsAsync<UpdateMirrorCatalog>(
                    new[]
                    {
                        UpdateMirrorRouter.GiteeCatalogOriginUrl,
                        UpdateMirrorRouter.CatalogOriginUrl
                    },
                    sources,
                    MetadataRaceWidth,
                    cancellationToken,
                    UpdateMirrorRouter.IsValidCatalog);

                // GitHub main is the canonical 3.5 release manifest. Do not race a separately
                // maintained Gitee manifest against it: a stale first-party mirror can be valid JSON
                // while advertising an older release. Probe several transports for the same canonical
                // GitHub object and choose the highest valid version seen in the bounded probe.
                var updateTask = TryDownloadNewestUpdateManifestAsync(
                    sources,
                    snapshot.CurrentVersion,
                    cancellationToken);
                var announcementTask = TryDownloadAnnouncementAsync(cancellationToken);

                var updateResult = await updateTask.ConfigureAwait(false);
                if (updateResult.Value == null)
                {
                    throw new HttpRequestException("No update metadata source returned a valid FACM manifest.");
                }

                // If the dynamic catalog finished while the required version request was running,
                // use it immediately and cache it. Otherwise keep the already cached/bootstrap pool
                // for this check; the catalog request is deliberately not on the critical path.
                if (catalogTask.IsCompleted)
                {
                    try
                    {
                        var catalogResult = await catalogTask.ConfigureAwait(false);
                        if (catalogResult.Value != null)
                        {
                            UpdateMirrorRouter.SaveCatalog(catalogResult.Value);
                            sources = UpdateMirrorRouter.MergeWithBuiltIns(catalogResult.Value.Sources);
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                    }
                }

                snapshot.Update = updateResult.Value;
                snapshot.Update.ResolvedSources = sources;
                snapshot.MetadataSourceName = updateResult.SourceName;

                if (announcementTask.IsCompleted)
                {
                    snapshot.Announcement = await announcementTask.ConfigureAwait(false);
                }

                Version latest;
                if (snapshot.Update.Enabled && TryParseVersion(snapshot.Update.Version, out latest))
                {
                    var advertisedLatest = latest;
                    latest = PreventLatestVersionRegression(snapshot.CurrentVersion, advertisedLatest);
                    if (CompareProductVersions(advertisedLatest, snapshot.CurrentVersion) < 0)
                    {
                        AppLog.Info(
                            "Ignoring stale update metadata; source=" + (snapshot.MetadataSourceName ?? "unknown") +
                            "; advertised=" + advertisedLatest +
                            "; current=" + snapshot.CurrentVersion);
                    }

                    snapshot.LatestVersion = latest;
                    snapshot.UpdateAvailable = CompareProductVersions(latest, snapshot.CurrentVersion) > 0;

                    Version minimum;
                    var belowMinimum = TryParseVersion(snapshot.Update.MinimumVersion, out minimum) &&
                                       CompareProductVersions(snapshot.CurrentVersion, minimum) < 0;
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

        private static async Task<AnnouncementManifest> TryDownloadAnnouncementAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                // Announcements can contain human-readable text and links, so do not accept them
                // from an unauthenticated third-party proxy. Failure is intentionally best-effort.
                using (var client = CreateClient(TimeSpan.FromSeconds(8)))
                {
                    return await DownloadJsonAsync<AnnouncementManifest>(
                        client,
                        AnnouncementManifestUrl,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (TaskCanceledException)
            {
                AppLog.Info("Announcement metadata request skipped: timeout");
                return null;
            }
            catch (Exception exception)
            {
                AppLog.Info("Announcement metadata request skipped: " + exception.GetType().Name);
                return null;
            }
        }

        private static async Task<MirrorFetchResult<UpdateManifest>> TryDownloadNewestUpdateManifestAsync(
            UpdateMirrorSource[] sources,
            Version currentVersion,
            CancellationToken cancellationToken)
        {
            var candidates = UpdateMirrorRouter.BuildCandidates(UpdateManifestUrl, sources)
                .GroupBy(candidate => candidate.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            if (candidates.Length == 0) return new MirrorFetchResult<UpdateManifest>();

            var direct = candidates
                .Where(candidate => string.Equals(candidate.Url, UpdateManifestUrl, StringComparison.OrdinalIgnoreCase))
                .Take(1)
                .ToArray();
            var selected = candidates
                .Where(candidate => !string.Equals(candidate.Url, UpdateManifestUrl, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Max(1, UpdateManifestProbeWidth - direct.Length))
                .Concat(direct)
                .Take(UpdateManifestProbeWidth)
                .ToArray();

            var results = new List<MirrorFetchResult<UpdateManifest>>();
            using (var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                probeCancellation.CancelAfter(UpdateManifestProbeTimeout);
                var tasks = selected
                    .Select(candidate => DownloadCandidateAsync<UpdateManifest>(
                        candidate,
                        probeCancellation.Token,
                        IsValidUpdateManifest))
                    .ToArray();

                foreach (var task in tasks)
                {
                    try
                    {
                        var result = await task.ConfigureAwait(false);
                        if (result.Value != null) results.Add(result);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            var selectedManifest = SelectNewestManifest(results.Select(result => result.Value));
            if (selectedManifest != null)
            {
                var selectedResult = results.First(result => ReferenceEquals(result.Value, selectedManifest));
                Version selectedVersion;
                if (TryParseVersion(selectedManifest.Version, out selectedVersion) &&
                    CompareProductVersions(selectedVersion, currentVersion) < 0)
                {
                    // A client must never stop at metadata that is provably older than itself. Scan
                    // the full canonical GitHub transport pool for an equal/newer manifest before
                    // falling back to the stale probe result.
                    var recovered = await TryDownloadFromMirrorsAsync<UpdateManifest>(
                        new[] { UpdateManifestUrl },
                        sources,
                        MetadataRaceWidth,
                        cancellationToken,
                        manifest => IsValidUpdateManifest(manifest) &&
                                    IsManifestAtLeastVersion(manifest, currentVersion)).ConfigureAwait(false);
                    if (recovered.Value != null) return recovered;
                }

                return selectedResult;
            }

            return await TryDownloadFromMirrorsAsync<UpdateManifest>(
                new[] { UpdateManifestUrl },
                sources,
                MetadataRaceWidth,
                cancellationToken,
                IsValidUpdateManifest).ConfigureAwait(false);
        }

        private static async Task<MirrorFetchResult<T>> TryDownloadFromMirrorsAsync<T>(
            IEnumerable<string> originUrls,
            UpdateMirrorSource[] sources,
            int raceWidth,
            CancellationToken cancellationToken,
            Func<T, bool> validator) where T : class
        {
            var candidates = (originUrls ?? Enumerable.Empty<string>())
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .SelectMany(origin => UpdateMirrorRouter.BuildCandidates(origin, sources))
                .GroupBy(candidate => candidate.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
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
                !IsApprovedReleaseUrl(download, parsedVersion, "FACM.exe"))
                return false;

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

        private static bool IsManifestAtLeastVersion(UpdateManifest manifest, Version minimumVersion)
        {
            Version parsed;
            return manifest != null &&
                   TryParseVersion(manifest.Version, out parsed) &&
                   CompareProductVersions(parsed, minimumVersion) >= 0;
        }

        internal static UpdateManifest SelectNewestManifest(IEnumerable<UpdateManifest> manifests)
        {
            UpdateManifest best = null;
            Version bestVersion = null;
            foreach (var manifest in manifests ?? Enumerable.Empty<UpdateManifest>())
            {
                Version candidateVersion;
                if (manifest == null || !TryParseVersion(manifest.Version, out candidateVersion)) continue;

                if (best == null)
                {
                    best = manifest;
                    bestVersion = candidateVersion;
                    continue;
                }

                var comparison = CompareProductVersions(candidateVersion, bestVersion);
                if (comparison > 0 || comparison == 0 && manifest.Enabled && !best.Enabled)
                {
                    best = manifest;
                    bestVersion = candidateVersion;
                }
            }

            return best;
        }

        internal static Version PreventLatestVersionRegression(Version currentVersion, Version advertisedVersion)
        {
            if (advertisedVersion == null) return currentVersion;
            if (currentVersion == null) return advertisedVersion;
            return CompareProductVersions(advertisedVersion, currentVersion) < 0
                ? currentVersion
                : advertisedVersion;
        }

        internal static int CompareProductVersions(Version left, Version right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            var normalizedLeft = new Version(
                left.Major,
                left.Minor,
                Math.Max(0, left.Build),
                Math.Max(0, left.Revision));
            var normalizedRight = new Version(
                right.Major,
                right.Minor,
                Math.Max(0, right.Build),
                Math.Max(0, right.Revision));
            return normalizedLeft.CompareTo(normalizedRight);
        }

        private static bool IsApprovedReleaseUrl(Uri uri, Version version, string assetName)
        {
            if (uri == null || version == null || uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrWhiteSpace(uri.Query) || !string.IsNullOrWhiteSpace(uri.Fragment))
                return false;

            var normalizedVersion = version.ToString();
            var path = "/xianyumht-cmd/facm/releases/download/v" + normalizedVersion + "/" + assetName;
            if (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                return string.Equals(uri.AbsolutePath, path, StringComparison.OrdinalIgnoreCase);

            var giteePath = "/xymhtcmd/facm/releases/download/v" + normalizedVersion + "/" + assetName;
            return string.Equals(uri.Host, "gitee.com", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(uri.AbsolutePath, giteePath, StringComparison.OrdinalIgnoreCase);
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
