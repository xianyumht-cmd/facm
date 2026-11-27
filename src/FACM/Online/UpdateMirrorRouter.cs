using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using FACM.Services;

namespace FACM.Online
{
    internal static class UpdateMirrorRouter
    {
        internal const string CatalogSchema = "facm-update-mirrors.v1";
        internal const string CatalogOriginUrl =
            "https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/mirrors.json";

        internal const string GiteeCatalogOriginUrl =
            "https://gitee.com/xymhtcmd/facm/raw/main/online/mirrors.json";

        private const int MaxRemoteSources = 16;
        private const long MaxCatalogBytes = 64 * 1024;
        private static readonly object CacheSync = new object();
        private static readonly UpdateMirrorSource[] BuiltInSourcesValue =
        {
            new UpdateMirrorSource { Name = "gh-dpik", Prefix = "https://gh.dpik.top/", Enabled = true, Priority = 5 },
            new UpdateMirrorSource { Name = "gh-felicity", Prefix = "https://gh.felicity.ac.cn/", Enabled = true, Priority = 8 },
            new UpdateMirrorSource { Name = "gh-bugdey", Prefix = "https://gh.bugdey.us.kg/", Enabled = true, Priority = 10 },
            new UpdateMirrorSource { Name = "ghfast", Prefix = "https://ghfast.top/", Enabled = true, Priority = 15 },
            new UpdateMirrorSource { Name = "ghproxy-net", Prefix = "https://ghproxy.net/", Enabled = true, Priority = 20 },
            new UpdateMirrorSource { Name = "gh-proxy", Prefix = "https://gh-proxy.com/", Enabled = true, Priority = 25 },
            new UpdateMirrorSource { Name = "gh-ddlc", Prefix = "https://gh.ddlc.top/", Enabled = true, Priority = 30 },
            new UpdateMirrorSource { Name = "gh-xmly", Prefix = "https://gh.xmly.dev/", Enabled = true, Priority = 35 },
            new UpdateMirrorSource { Name = "cors-isteed", Prefix = "https://cors.isteed.cc/", Enabled = true, Priority = 40 },
            new UpdateMirrorSource { Name = "ghproxy-cc", Prefix = "https://ghproxy.cc/", Enabled = true, Priority = 50 },
            new UpdateMirrorSource { Name = "ghfile-geekertao", Prefix = "https://ghfile.geekertao.top/", Enabled = true, Priority = 55 },
            new UpdateMirrorSource { Name = "dockerproxy-link", Prefix = "https://dockerproxy.link/", Enabled = true, Priority = 60 },
            new UpdateMirrorSource { Name = "dockerproxy-net", Prefix = "https://dockerproxy.net/", Enabled = true, Priority = 65 },
            new UpdateMirrorSource { Name = "github", Prefix = string.Empty, Enabled = true, Priority = 100 }
        };

        public static UpdateMirrorSource[] GetBuiltInSources()
        {
            return BuiltInSourcesValue.Select(Clone).ToArray();
        }

        public static UpdateMirrorSource[] LoadCachedAndBuiltInSources()
        {
            UpdateMirrorCatalog cached = null;
            try
            {
                RuntimePaths.Initialize();
                var path = GetCatalogCachePath();
                if (File.Exists(path) && new FileInfo(path).Length <= MaxCatalogBytes)
                {
                    using (var stream = File.OpenRead(path))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(UpdateMirrorCatalog));
                        cached = serializer.ReadObject(stream) as UpdateMirrorCatalog;
                    }
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Update mirror catalog cache read failed", exception);
            }

            return MergeWithBuiltIns(cached == null ? null : cached.Sources);
        }

        public static void SaveCatalog(UpdateMirrorCatalog catalog)
        {
            if (!IsValidCatalog(catalog)) return;

            try
            {
                RuntimePaths.Initialize();
                var normalized = new UpdateMirrorCatalog
                {
                    Schema = CatalogSchema,
                    UpdatedAt = catalog.UpdatedAt,
                    Sources = NormalizeRemoteSources(catalog.Sources)
                };
                var path = GetCatalogCachePath();
                var temporary = path + ".tmp";
                lock (CacheSync)
                {
                    using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(UpdateMirrorCatalog));
                        serializer.WriteObject(stream, normalized);
                    }
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temporary, path);
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Update mirror catalog cache write failed", exception);
            }
        }

        public static bool IsValidCatalog(UpdateMirrorCatalog catalog)
        {
            return catalog != null &&
                   string.Equals(catalog.Schema, CatalogSchema, StringComparison.Ordinal) &&
                   catalog.Sources != null &&
                   NormalizeRemoteSources(catalog.Sources).Length > 0;
        }

        public static UpdateMirrorSource[] MergeWithBuiltIns(IEnumerable<UpdateMirrorSource> remoteSources)
        {
            var combined = new List<UpdateMirrorSource>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in NormalizeRemoteSources(remoteSources))
            {
                AddUnique(combined, seen, source);
            }

            foreach (var source in BuiltInSourcesValue)
            {
                AddUnique(combined, seen, Clone(source));
            }

            return MirrorHealthStore.Order(combined).ToArray();
        }

        public static UpdateDownloadCandidate[] BuildCandidates(
            string originUrl,
            IEnumerable<UpdateMirrorSource> sources, bool package = false)
        {
            Uri origin;
            if (!Uri.TryCreate(originUrl, UriKind.Absolute, out origin) || origin.Scheme != Uri.UriSchemeHttps)
            {
                return new UpdateDownloadCandidate[0];
            }

            var result = new List<UpdateDownloadCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var effectiveSources = MirrorHealthStore.Order(
                (sources ?? GetBuiltInSources()).Where(source => source != null && source.Enabled), package).ToArray();
            foreach (var source in effectiveSources)
            {
                var url = BuildUrl(source, originUrl);
                if (string.IsNullOrWhiteSpace(url) || !seen.Add(url)) continue;
                result.Add(new UpdateDownloadCandidate
                {
                    SourceName = IsGiteeUrl(originUrl) && string.IsNullOrWhiteSpace(source.Prefix)
                        ? "gitee"
                        : string.IsNullOrWhiteSpace(source.Name) ? "mirror" : source.Name.Trim(),
                    Url = url
                });
            }
            return result.ToArray();
        }

        public static string BuildUrl(UpdateMirrorSource source, string originUrl)
        {
            if (source == null || !source.Enabled || !IsHttpsUrl(originUrl)) return null;
            if (string.IsNullOrWhiteSpace(source.Prefix)) return originUrl;
            // GitHub proxy prefixes are not valid Gitee release URLs. Gitee is already a
            // first-party HTTPS origin for FACM, so use it directly instead of producing
            // malformed "proxy/https://gitee.com/..." candidates.
            if (IsGiteeUrl(originUrl)) return null;
            if (!IsSafeMirrorPrefix(source.Prefix)) return null;
            return source.Prefix.TrimEnd('/') + "/" + originUrl;
        }

        public static bool IsSafeMirrorPrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            Uri uri;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
                return false;
            if (string.IsNullOrWhiteSpace(uri.Host) ||
                string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                return false;

            IPAddress address;
            if (IPAddress.TryParse(uri.Host, out address)) return false;
            return true;
        }

        public static void RecordSuccess(string sourceName, long elapsedMilliseconds, bool package = false)
        {
            MirrorHealthStore.Record(sourceName, true, elapsedMilliseconds, package);
        }

        public static void RecordFailure(string sourceName, long elapsedMilliseconds, bool package = false)
        {
            MirrorHealthStore.Record(sourceName, false, elapsedMilliseconds, package);
        }

        private static UpdateMirrorSource[] NormalizeRemoteSources(IEnumerable<UpdateMirrorSource> sources)
        {
            if (sources == null) return new UpdateMirrorSource[0];
            return sources
                .Where(source => source != null && source.Enabled &&
                                 !string.IsNullOrWhiteSpace(source.Name) &&
                                 IsSafeMirrorPrefix(source.Prefix))
                .Select(Clone)
                .OrderBy(source => source.Priority)
                .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxRemoteSources)
                .ToArray();
        }

        private static void AddUnique(
            ICollection<UpdateMirrorSource> target,
            ISet<string> seen,
            UpdateMirrorSource source)
        {
            if (source == null || !source.Enabled) return;
            var key = string.IsNullOrWhiteSpace(source.Prefix)
                ? "<github-direct>"
                : source.Prefix.Trim().TrimEnd('/');
            if (!seen.Add(key)) return;
            target.Add(source);
        }

        private static UpdateMirrorSource Clone(UpdateMirrorSource source)
        {
            return new UpdateMirrorSource
            {
                Name = source.Name,
                Prefix = source.Prefix,
                Enabled = source.Enabled,
                Priority = source.Priority
            };
        }

        private static bool IsHttpsUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps;
        }

        private static bool IsGiteeUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                   uri.Scheme == Uri.UriSchemeHttps &&
                   string.Equals(uri.Host, "gitee.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetCatalogCachePath()
        {
            return Path.Combine(RuntimePaths.CacheDirectory, "update-mirrors.json");
        }

        internal static void ValidateHealthForSmokeTest() { MirrorHealthStore.ValidateForSmokeTest(); }

        [DataContract]
        private sealed class MirrorHealthFile
        {
            [DataMember(Name = "entries")]
            public MirrorHealthEntry[] Entries { get; set; }
        }

        [DataContract]
        private sealed class MirrorHealthEntry
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "successes")]
            public int Successes { get; set; }

            [DataMember(Name = "failures")]
            public int Failures { get; set; }

            [DataMember(Name = "average_ms")]
            public double AverageMilliseconds { get; set; }

            [DataMember(Name = "last_observed_utc")]
            public long LastObservedUtc { get; set; }
        }

        private static class MirrorHealthStore
        {
            private static readonly object Sync = new object();

            private static readonly TimeSpan MaximumAge = TimeSpan.FromDays(7);

            public static IEnumerable<UpdateMirrorSource> Order(IEnumerable<UpdateMirrorSource> sources, bool package = false)
            {
                lock (Sync)
                {
                    var health = Load(package);
                    var now = DateTime.UtcNow;
                    return sources.OrderBy(source => Score(source, health, package, now))
                        .ThenBy(source => source.Priority).ToArray();
                }
            }

            public static void Record(string sourceName, bool success, long elapsedMilliseconds, bool package)
            {
                if (string.IsNullOrWhiteSpace(sourceName)) return;
                try
                {
                    lock (Sync)
                    {
                        var health = Load(package);
                        var now = DateTime.UtcNow;
                        MirrorHealthEntry entry;
                        if (!health.TryGetValue(sourceName, out entry) || !IsRecent(entry, now))
                        {
                            entry = new MirrorHealthEntry { Name = sourceName };
                            health[sourceName] = entry;
                        }
                        if (entry.Successes + entry.Failures >= 16)
                        {
                            entry.Successes /= 2;
                            entry.Failures /= 2;
                        }
                        if (success)
                        {
                            entry.Successes++;
                            var elapsed = Math.Max(1L, Math.Min(package ? 600000L : 60000L, elapsedMilliseconds));
                            entry.AverageMilliseconds = entry.AverageMilliseconds <= 0
                                ? elapsed : entry.AverageMilliseconds * 0.75 + elapsed * 0.25;
                        }
                        else entry.Failures++;
                        entry.LastObservedUtc = now.Ticks;
                        Save(health.Values.Where(item => IsRecent(item, now))
                            .OrderByDescending(item => item.LastObservedUtc).Take(32).ToArray(), package);
                    }
                }
                catch
                {
                    // Mirror scoring is an optimization only; it must never block updating.
                }
            }

            private static bool IsRecent(MirrorHealthEntry entry, DateTime now)
            {
                return entry != null && entry.LastObservedUtc > 0 && entry.LastObservedUtc <= now.Ticks &&
                    now.Ticks - entry.LastObservedUtc <= MaximumAge.Ticks &&
                    entry.Successes >= 0 && entry.Successes <= 16 && entry.Failures >= 0 && entry.Failures <= 16 &&
                    !double.IsNaN(entry.AverageMilliseconds) && !double.IsInfinity(entry.AverageMilliseconds);
            }

            internal static double Score(UpdateMirrorSource source,
                IDictionary<string, MirrorHealthEntry> health, bool package, DateTime now)
            {
                var baseline = package ? 8000d : 800d;
                var priority = Math.Max(0, source.Priority) * (package ? 40d : 10d);
                MirrorHealthEntry entry;
                if (!health.TryGetValue(source.Name ?? string.Empty, out entry) || !IsRecent(entry, now))
                    return baseline + priority;

                var attempts = Math.Max(1, entry.Successes + entry.Failures);
                var failureRate = (double)entry.Failures / attempts;
                var latency = entry.AverageMilliseconds <= 0 ? baseline : entry.AverageMilliseconds;
                var weight = Math.Max(0, 1d - (now.Ticks - entry.LastObservedUtc) / (double)MaximumAge.Ticks);
                return priority + baseline * (1 - weight) +
                    (failureRate * (package ? 30000d : 5000d) + latency) * weight;
            }

            internal static void ValidateForSmokeTest()
            {
                var source = new UpdateMirrorSource { Name = "sample", Priority = 5 };
                var now = new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc);
                var empty = new Dictionary<string, MirrorHealthEntry>();
                var metadata = new Dictionary<string, MirrorHealthEntry> {
                    { "sample", new MirrorHealthEntry { Name = "sample", Successes = 4,
                        AverageMilliseconds = 100, LastObservedUtc = now.Ticks } } };
                var packages = new Dictionary<string, MirrorHealthEntry> {
                    { "sample", new MirrorHealthEntry { Name = "sample", Failures = 4,
                        LastObservedUtc = now.Ticks } } };
                if (Score(source, metadata, false, now) >= Score(source, empty, false, now) ||
                    Score(source, packages, true, now) <= Score(source, empty, true, now) ||
                    Score(source, packages, true, now.AddDays(8)) != Score(source, empty, true, now.AddDays(8)))
                    throw new InvalidOperationException("Mirror health isolation or expiration failed.");
            }

            private static Dictionary<string, MirrorHealthEntry> Load(bool package)
            {
                var result = new Dictionary<string, MirrorHealthEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    RuntimePaths.Initialize();
                    var path = Path.Combine(RuntimePaths.CacheDirectory, package ? "update-package-health-v2.json" : "update-metadata-health-v2.json");
                    if (!File.Exists(path) || new FileInfo(path).Length > MaxCatalogBytes) return result;
                    using (var stream = File.OpenRead(path))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(MirrorHealthFile));
                        var file = serializer.ReadObject(stream) as MirrorHealthFile;
                        if (file == null || file.Entries == null) return result;
                        foreach (var entry in file.Entries)
                        {
                            if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) continue;
                            result[entry.Name] = entry;
                        }
                    }
                }
                catch
                {
                }
                return result;
            }

            private static void Save(MirrorHealthEntry[] entries, bool package)
            {
                RuntimePaths.Initialize();
                var path = Path.Combine(RuntimePaths.CacheDirectory, package ? "update-package-health-v2.json" : "update-metadata-health-v2.json");
                var temporary = path + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var serializer = new DataContractJsonSerializer(typeof(MirrorHealthFile));
                    serializer.WriteObject(stream, new MirrorHealthFile { Entries = entries });
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
        }
    }
}
