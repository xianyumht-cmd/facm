using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FACM.Mayhem;
using FACM.Services;

namespace FACM.Online
{
    [DataContract]
    internal sealed class CloudRouteCatalog
    {
        [DataMember(Name = "schema", IsRequired = true)]
        public string Schema { get; set; }
        [DataMember(Name = "revision", IsRequired = true)]
        public long Revision { get; set; }
        [DataMember(Name = "sources", IsRequired = true)]
        public UpdateMirrorSource[] Sources { get; set; }
        [DataMember(Name = "announcement", EmitDefaultValue = false)]
        public AnnouncementManifest Announcement { get; set; }
        [DataMember(Name = "public_sources", EmitDefaultValue = false)]
        public CloudPublicSource[] PublicSources { get; set; }
        [DataMember(Name = "features", EmitDefaultValue = false)]
        public CloudFeatureFlags Features { get; set; }
    }

    [DataContract]
    internal sealed class CloudPublicSource
    {
        [DataMember(Name = "id", IsRequired = true)]
        public string Id { get; set; }
        [DataMember(Name = "enabled", IsRequired = true)]
        public bool Enabled { get; set; }
        [DataMember(Name = "timeout_ms", EmitDefaultValue = false)]
        public int TimeoutMs { get; set; }
    }

    [DataContract]
    internal sealed class CloudFeatureFlags
    {
        [DataMember(Name = "history_timeline", EmitDefaultValue = false)]
        public bool? HistoryTimeline { get; set; }
        [DataMember(Name = "history_radar", EmitDefaultValue = false)]
        public bool? HistoryRadar { get; set; }
    }

    internal static class CloudRouteControl
    {
        internal const string Url = "https://ggman-config-1305353165.cos.ap-guangzhou.myqcloud.com/control.json";
        private const int MaxEnvelopeBytes = 65536, MaxPayloadBytes = 16384;
        private static CloudRouteCatalog _active;
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }) { Timeout = Timeout.InfiniteTimeSpan };

        [DataContract]
        private sealed class Envelope
        {
            [DataMember(Name = "payload", IsRequired = true)]
            public string Payload { get; set; }
            [DataMember(Name = "signature", IsRequired = true)]
            public string Signature { get; set; }
        }

        private sealed class Verified
        {
            internal CloudRouteCatalog Catalog;
            internal byte[] Bytes, Payload;
        }

        internal static async Task<CloudRouteCatalog> LoadAsync(CancellationToken token)
        {
            // Unsigned development builds cannot establish the production signing identity.
            try
            {
                using (var original = X509Certificate.CreateFromSignedFile(Assembly.GetExecutingAssembly().Location))
                using (var certificate = new X509Certificate2(original))
                using (var rsa = certificate.GetRSAPublicKey())
                {
                    if (rsa == null) return null;
                    RuntimePaths.Initialize();
                    var path = Path.Combine(RuntimePaths.CacheDirectory, "cloud-control-signed.json");
                    using (var budget = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        budget.CancelAfter(TimeSpan.FromSeconds(2));
                        var entered = false;
                        try
                        {
                            await Gate.WaitAsync(budget.Token).ConfigureAwait(false);
                            entered = true;
                            var cached = ReadCache(path, rsa);
                            try
                            {
                                using (var request = new HttpRequestMessage(HttpMethod.Get, Url))
                                {
                                    request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                                    using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false))
                                    {
                                        response.EnsureSuccessStatusCode();
                                        var bytes = await CancelableHttpContentReader.ReadBytesAsync(response.Content, budget.Token, MaxEnvelopeBytes).ConfigureAwait(false);
                                        var next = Verify(bytes, rsa);
                                        if (next != null && AcceptRevision(cached, next))
                                        {
                                            if (cached == null || next.Catalog.Revision > cached.Catalog.Revision)
                                            {
                                                // Do not adopt a new revision until it is durably cached.
                                                Save(path, next.Bytes);
                                                cached = next;
                                            }
                                            AppLog.Info("Cloud route control accepted; revision=" + cached.Catalog.Revision);
                                        }
                                        else AppLog.Info("Cloud route control rejected: signature, schema or revision");
                                    }
                                }
                            }
                            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                            catch (Exception ex) when (!(ex is OperationCanceledException))
                            { AppLog.Info("Cloud route control unavailable: " + ex.GetType().Name); }
                            token.ThrowIfCancellationRequested();
                            if (cached != null) Volatile.Write(ref _active, cached.Catalog);
                            return Volatile.Read(ref _active);
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            var cached = ReadCache(path, rsa);
                            return Volatile.Read(ref _active) ?? (cached == null ? null : cached.Catalog);
                        }
                        finally { if (entered) Gate.Release(); }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.Info("Cloud route control skipped: " + ex.GetType().Name);
                return null;
            }
        }

        internal static bool IsValid(CloudRouteCatalog catalog)
        {
            if (catalog == null || catalog.Schema != "ggman-control.v1" || catalog.Revision <= 0 ||
                catalog.Sources == null || catalog.Sources.Length < 1 || catalog.Sources.Length > 16 ||
                !catalog.Sources.Any(s => s != null && s.Enabled)) return false;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in catalog.Sources)
            {
                Uri uri;
                if (source == null || string.IsNullOrWhiteSpace(source.Name) || source.Name.Length > 48 ||
                    source.Name == "github" || source.Name == "gitee" ||
                    !System.Text.RegularExpressions.Regex.IsMatch(source.Name, "^[a-z0-9][a-z0-9-]*$") ||
                    source.Priority < 0 || source.Priority > 1000 ||
                    !Uri.TryCreate(source.Prefix, UriKind.Absolute, out uri) ||
                    uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.IsLoopback ||
                    uri.HostNameType != UriHostNameType.Dns || uri.Host.IndexOf('.') < 0 ||
                    uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                    uri.AbsolutePath != "/" || !names.Add(source.Name) || !prefixes.Add(uri.AbsoluteUri)) return false;
            }
            if (catalog.Announcement != null && !ValidAnnouncement(catalog.Announcement)) return false;
            if (catalog.PublicSources != null)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                if (catalog.PublicSources.Length > 5) return false;
                foreach (var source in catalog.PublicSources)
                    if (source == null || !KnownSource(source.Id) || !ids.Add(source.Id) ||
                        (source.TimeoutMs != 0 && (source.TimeoutMs < 500 || source.TimeoutMs > 5500))) return false;
            }
            return true;
        }

        private static bool ValidAnnouncement(AnnouncementManifest value)
        {
            if ((value.Id ?? "").Length > 96 || (value.Title ?? "").Length > 120 ||
                (value.Body ?? "").Length > 2000 || (value.UpdatedAt ?? "").Length > 64 ||
                (value.LinkUrl ?? "").Length > 512) return false;
            if (value.Enabled && (string.IsNullOrWhiteSpace(value.Id) ||
                string.IsNullOrWhiteSpace(value.Title) || string.IsNullOrWhiteSpace(value.Body))) return false;
            if (value.Level != "info" && value.Level != "warning" && value.Level != "critical") return false;
            Uri uri;
            return string.IsNullOrWhiteSpace(value.LinkUrl) ||
                (Uri.TryCreate(value.LinkUrl, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps &&
                 uri.Port == 443 && !uri.IsLoopback && uri.HostNameType == UriHostNameType.Dns &&
                 uri.UserInfo.Length == 0 && uri.Host.IndexOf('.') >= 0);
        }

        private static bool KnownSource(string id)
        {
            return id == "hexdata" || id == "aramgg" || id == "arammayhem" || id == "opgg" || id == "tencent-news";
        }

        internal static bool PublicSourceAllowed(string url, ref TimeSpan budget)
        {
            return ApplyPublicSource(Volatile.Read(ref _active), url, ref budget);
        }

        private static bool ApplyPublicSource(CloudRouteCatalog catalog, string url, ref TimeSpan budget)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            var id = uri.Host == "hexdata.com.cn" ? "hexdata" :
                uri.Host == "aramgg.com" ? "aramgg" :
                uri.Host == "arammayhem.com" ? "arammayhem" :
                uri.Host == "op.gg" ? "opgg" : uri.Host == "lol.qq.com" ? "tencent-news" : null;
            var policy = catalog == null || catalog.PublicSources == null ? null :
                catalog.PublicSources.FirstOrDefault(s => s.Id == id);
            if (policy == null) return true;
            if (!policy.Enabled) return false;
            if (policy.TimeoutMs > 0 && budget.TotalMilliseconds > policy.TimeoutMs)
                budget = TimeSpan.FromMilliseconds(policy.TimeoutMs);
            return true;
        }

        internal static bool HistoryTimelineEnabled
        {
            get { return TimelineEnabled(Volatile.Read(ref _active)); }
        }

        internal static bool HistoryRadarEnabled
        {
            get { var active = Volatile.Read(ref _active); return active == null || active.Features == null || active.Features.HistoryRadar != false; }
        }

        private static bool TimelineEnabled(CloudRouteCatalog active)
        {
            return active == null || active.Features == null || active.Features.HistoryTimeline != false;
        }

        internal static UpdateMirrorSource[] Resolve(CloudRouteCatalog catalog)
        {
            if (!IsValid(catalog)) return null;
            return catalog.Sources.Where(s => s.Enabled)
                .Concat(new[] { new UpdateMirrorSource { Name = "github", Prefix = "", Enabled = true, Priority = 1000 } }).ToArray();
        }

        private static Verified Verify(byte[] bytes, RSA rsa)
        {
            try
            {
                if (bytes == null || bytes.Length == 0 || bytes.Length > MaxEnvelopeBytes || rsa == null) return null;
                var envelope = Parse<Envelope>(bytes);
                var payload = Convert.FromBase64String(envelope.Payload);
                var signature = Convert.FromBase64String(envelope.Signature);
                if (payload.Length == 0 || payload.Length > MaxPayloadBytes ||
                    signature.Length != rsa.KeySize / 8 ||
                    !rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;
                var catalog = Parse<CloudRouteCatalog>(payload);
                return IsValid(catalog) ? new Verified { Catalog = catalog, Bytes = bytes, Payload = payload } : null;
            }
            catch { return null; }
        }

        private static T Parse<T>(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes, false))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        private static bool AcceptRevision(Verified cached, Verified next)
        {
            return next != null && (cached == null || next.Catalog.Revision > cached.Catalog.Revision ||
                next.Catalog.Revision == cached.Catalog.Revision && next.Payload.SequenceEqual(cached.Payload));
        }

        private static Verified ReadCache(string path, RSA rsa)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaxEnvelopeBytes) return null;
                return Verify(File.ReadAllBytes(path), rsa);
            }
            catch { return null; }
        }

        private static void Save(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static void ValidateForSmokeTest()
        {
            using (var rsa = RSA.Create())
            {
                rsa.KeySize = 2048;
                var catalog = new CloudRouteCatalog { Schema = "ggman-control.v1", Revision = 2,
                    Sources = new[] {
                        new UpdateMirrorSource { Name = "selected", Prefix = "https://mirror.example/", Enabled = true, Priority = 1 },
                        new UpdateMirrorSource { Name = "disabled", Prefix = "https://disabled.example/", Enabled = false, Priority = 2 }
                    } };
                Func<byte[], byte[], byte[]> wrap = (payload, sig) => Serialize(new Envelope {
                    Payload = Convert.ToBase64String(payload), Signature = Convert.ToBase64String(sig) });
                Func<CloudRouteCatalog, byte[]> sign = value => {
                    var payload = Serialize(value);
                    return wrap(payload, rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                };
                var verified = Verify(sign(catalog), rsa);
                if (verified == null) throw new InvalidOperationException("Signed cloud route catalog rejected.");
                var sources = Resolve(verified.Catalog);
                if (sources.Length != 2 || sources.Any(s => s.Name == "disabled") ||
                    UpdateMirrorRouter.BuildCandidates(OnlineService.UpdateManifestUrl, sources).Length != 2)
                    throw new InvalidOperationException("Cloud route selection resurrected an omitted or disabled mirror.");
                catalog.PublicSources = new[] {
                    new CloudPublicSource { Id = "hexdata", Enabled = false },
                    new CloudPublicSource { Id = "opgg", Enabled = true, TimeoutMs = 800 }
                };
                catalog.Features = new CloudFeatureFlags { HistoryTimeline = false };
                catalog.Announcement = new AnnouncementManifest { Enabled = false, Level = "info" };
                catalog.Revision = 3;
                var policy = Verify(sign(catalog), rsa);
                var timeout = TimeSpan.FromSeconds(3);
                if (policy == null || TimelineEnabled(policy.Catalog) ||
                    ApplyPublicSource(policy.Catalog, "https://hexdata.com.cn/heroes", ref timeout) ||
                    !ApplyPublicSource(policy.Catalog, "https://op.gg/guide", ref timeout) ||
                    timeout.TotalMilliseconds != 800 || policy.Catalog.Announcement.Enabled)
                    throw new InvalidOperationException("Signed cloud policy was not applied.");
                catalog.PublicSources[0].Id = "unknown";
                if (Verify(sign(catalog), rsa) != null)
                    throw new InvalidOperationException("Unrecognized public source accepted.");
                catalog.PublicSources = null;
                catalog.Features = null;
                catalog.Announcement = null;
                catalog.Revision = 1;
                if (AcceptRevision(verified, Verify(sign(catalog), rsa)))
                    throw new InvalidOperationException("Cloud route rollback accepted.");
                catalog.Revision = 2;
                catalog.Sources[0].Priority = 3;
                if (AcceptRevision(verified, Verify(sign(catalog), rsa)))
                    throw new InvalidOperationException("Conflicting cloud route revision accepted.");
                if (Verify(wrap(verified.Payload, new byte[rsa.KeySize / 8]), rsa) != null)
                    throw new InvalidOperationException("Forged cloud route signature accepted.");
                catalog.Sources[0].Prefix = "https://127.0.0.1/";
                if (Verify(sign(catalog), rsa) != null)
                    throw new InvalidOperationException("Invalid cloud route endpoint accepted.");
            }
        }

        private static byte[] Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }
    }
}
