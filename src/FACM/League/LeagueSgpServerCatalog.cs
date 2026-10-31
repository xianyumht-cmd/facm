using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    // Source-shaped Akari server catalog, fetched only when regional history is opened.
    // The public-data transport owns HTTP/cache; SGP bearer tokens never reach it.
    internal static class LeagueSgpServerCatalog
    {
        private const string CatalogUrl = "https://akari-api.yuru-yuri.com/config/v1/sgp/league-servers";
        private static readonly DateTime BuiltinTimestamp = new DateTime(2026, 7, 18, 4, 0, 0, DateTimeKind.Utc);
        private static readonly SemaphoreSlim RefreshGate = new SemaphoreSlim(1, 1);
        private static readonly object Sync = new object();
        private static DateTime _updatedAt = BuiltinTimestamp;
        private static DateTime _lastAttempt = DateTime.MinValue;

        internal static async Task<bool> TryRefreshAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await RefreshGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (DateTime.UtcNow - _lastAttempt < TimeSpan.FromHours(2)) return false;
                _lastAttempt = DateTime.UtcNow;
                var data = await LeaguePublicDataTransport.TryGetBytesAsync(
                    CatalogUrl, TimeSpan.FromSeconds(4), token).ConfigureAwait(false);
                return TryApply(data);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
            finally { RefreshGate.Release(); }
        }

        internal static bool TryApply(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > 128 * 1024) return false;
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 }
                    .DeserializeObject(Encoding.UTF8.GetString(data)) as Dictionary<string, object>;
                if (root == null) return false;
                object timestamp, serversValue;
                if (!root.TryGetValue("updatedAt", out timestamp) ||
                    !root.TryGetValue("servers", out serversValue)) return false;
                DateTimeOffset updated;
                if (!DateTimeOffset.TryParse(timestamp as string, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out updated) ||
                    updated.UtcDateTime > DateTime.UtcNow.AddDays(1)) return false;
                var remote = serversValue as Dictionary<string, object>;
                if (remote == null || remote.Count == 0 || remote.Count > 128) return false;
                var existing = LeagueSgpMatchHistoryApiClient.Servers;
                var next = new List<LeagueSgpServer>(existing.Length);
                var changed = 0;
                foreach (var server in existing)
                {
                    var copy = Clone(server);
                    object value;
                    if (remote.TryGetValue(server.Id, out value))
                    {
                        var entry = value as Dictionary<string, object>;
                        object history, common, tencent, region;
                        if (entry == null || !entry.TryGetValue("matchHistory", out history) ||
                            !entry.TryGetValue("common", out common) ||
                            !entry.TryGetValue("isTencent", out tencent) ||
                            !(tencent is bool) || (bool)tencent != server.IsTencent ||
                            !SafeEndpoint(history as string, server) ||
                            !SafeEndpoint(common as string, server) ||
                            entry.TryGetValue("regionPathParam", out region) &&
                            !string.Equals(region as string, server.PathRegion, StringComparison.Ordinal))
                            return false;
                        copy.MatchHistory = history as string;
                        copy.Common = common as string;
                        changed++;
                    }
                    next.Add(copy);
                }
                foreach (var entry in remote)
                {
                    if (existing.Any(server => server.Id == entry.Key) ||
                        !entry.Key.StartsWith("TENCENT_", StringComparison.Ordinal)) continue;
                    var added = TryCreateTencentServer(entry.Key, entry.Value);
                    if (added == null) return false;
                    next.Add(added);
                    changed++;
                }
                if (changed == 0) return false;
                object names;
                if (root.TryGetValue("serverNames", out names))
                {
                    var languages = names as Dictionary<string, object>;
                    object chinese;
                    var localized = languages != null && languages.TryGetValue("zh-CN", out chinese)
                        ? chinese as Dictionary<string, object> : null;
                    if (localized != null)
                        foreach (var server in next)
                        {
                            object value;
                            if (!localized.TryGetValue(server.Id, out value)) continue;
                            var name = value as string;
                            if (string.IsNullOrWhiteSpace(name) || name.Length > 64 ||
                                name.Any(char.IsControl)) return false;
                            server.Name = name;
                        }
                }
                lock (Sync)
                {
                    if (updated.UtcDateTime <= _updatedAt) return false;
                    LeagueSgpMatchHistoryApiClient.Servers = next.ToArray();
                    _updatedAt = updated.UtcDateTime;
                }
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        // Newly announced Tencent platforms can join only under the exact Tencent SGP
        // hostname templates. Riot-region additions remain disabled without a trusted route.
        private static LeagueSgpServer TryCreateTencentServer(string id, object value)
        {
            var platform = id.Substring("TENCENT_".Length);
            if (platform.Length < 2 || platform.Length > 12 ||
                platform.Any(c => c < 'A' || c > 'Z' && (c < '0' || c > '9')))
                return null;
            var entry = value as Dictionary<string, object>;
            if (entry == null) return null;
            object history, common, tencent, region;
            if (!entry.TryGetValue("matchHistory", out history) ||
                !entry.TryGetValue("common", out common) ||
                !entry.TryGetValue("isTencent", out tencent) ||
                !(tencent is bool) || !(bool)tencent ||
                !entry.TryGetValue("regionPathParam", out region) ||
                !string.Equals(region as string, platform, StringComparison.Ordinal))
                return null;
            var server = new LeagueSgpServer
            {
                Id = id, Name = id, PathRegion = platform, IsTencent = true,
                MatchHistory = history as string, Common = common as string
            };
            if (!SafeNewTencentEndpoint(server.MatchHistory, server) ||
                !SafeNewTencentEndpoint(server.Common, server)) return null;
            return server;
        }

        private static bool SafeNewTencentEndpoint(string value, LeagueSgpServer server)
        {
            if (!SafeEndpoint(value, server)) return false;
            var host = new Uri(value).Host;
            var prefix = server.PathRegion.ToLowerInvariant();
            return string.Equals(host, prefix + "-sgp.lol.qq.com", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, prefix + "-k8s-sgp.lol.qq.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SafeEndpoint(string value, LeagueSgpServer server)
        {
            var tencent = server.IsTencent;
            Uri uri;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256 ||
                !Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
                uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                uri.AbsolutePath != "/" || uri.Port != (tencent ? 21019 : 443))
                return false;
            if (!uri.Host.EndsWith(tencent ? ".lol.qq.com" : ".sgp.pvp.net",
                StringComparison.OrdinalIgnoreCase)) return false;
            return !tencent || uri.Host.StartsWith(
                server.PathRegion.ToLowerInvariant() + "-", StringComparison.OrdinalIgnoreCase);
        }

        private static LeagueSgpServer Clone(LeagueSgpServer server)
        {
            return new LeagueSgpServer { Id = server.Id, Name = server.Name,
                MatchHistory = server.MatchHistory, Common = server.Common,
                PathRegion = server.PathRegion, IsTencent = server.IsTencent };
        }

        internal static void ResetForSmokeTest()
        {
            lock (Sync)
            {
                LeagueSgpMatchHistoryApiClient.Servers = LeagueSgpMatchHistoryApiClient.BuiltinServers;
                _updatedAt = BuiltinTimestamp;
            }
        }
    }
}
