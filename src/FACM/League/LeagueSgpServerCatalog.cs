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
        private static DateTime _lastSuccess = DateTime.MinValue;

        // Explicit refresh only: failed remote catalogs can retry after five minutes;
        // accepted catalogs retain their two-hour budget. No background polling.
        internal static bool ShouldRefresh(DateTime now, DateTime lastAttempt, DateTime lastSuccess)
        {
            return now - lastAttempt >= TimeSpan.FromMinutes(5) &&
                now - lastSuccess >= TimeSpan.FromHours(2);
        }

        internal static Task<bool> TryRefreshAsync(CancellationToken token)
        {
            return TryRefreshAsync(token, t => LeaguePublicDataTransport.TryGetBytesAsync(
                CatalogUrl, TimeSpan.FromSeconds(4), t));
        }

        internal static async Task<bool> TryRefreshAsync(CancellationToken token,
            Func<CancellationToken, Task<byte[]>> load)
        {
            if (load == null) throw new ArgumentNullException(nameof(load));
            token.ThrowIfCancellationRequested();
            await RefreshGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                if (!ShouldRefresh(now, _lastAttempt, _lastSuccess)) return false;
                _lastAttempt = now;
                var data = await load(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var applied = TryApply(data);
                if (applied) _lastSuccess = DateTime.UtcNow;
                return applied;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // A cancelled account/session read must not suppress the next explicit refresh.
                _lastAttempt = DateTime.MinValue;
                throw;
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
                if (remote == null || remote.Count > 128) return false;
                // An explicitly empty, newer source catalog disables all SGP destinations.
                // Do not retain old credential destinations after a full provider revocation.
                if (remote.Count == 0)
                {
                    lock (Sync)
                    {
                        if (updated.UtcDateTime <= _updatedAt) return false;
                        LeagueSgpMatchHistoryApiClient.Servers = Array.Empty<LeagueSgpServer>();
                        _updatedAt = updated.UtcDateTime;
                    }
                    return true;
                }
                // Rebuild from the pinned baseline so a later trusted catalog can revoke
                // an added platform rather than keeping its old credential destination.
                var existing = LeagueSgpMatchHistoryApiClient.BuiltinServers;
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
                            !SafeKnownEndpoint(history as string, server, true) ||
                            !SafeKnownEndpoint(common as string, server, false) ||
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
                    if (existing.Any(server => server.Id == entry.Key)) continue;
                    var added = entry.Key.StartsWith("TENCENT_", StringComparison.Ordinal)
                        ? TryCreateTencentServer(entry.Key, entry.Value)
                        : TryCreateRiotServer(entry.Key, entry.Value);
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
        // hostname templates. Riot additions use pinned gateways and exact region-owned hosts.
        private static LeagueSgpServer TryCreateTencentServer(string id, object value)
        {
            var platform = id.Substring("TENCENT_".Length);
            if (platform.Length < 2 || platform.Length > 12 ||
                platform.Any(c => (c < 'A' || c > 'Z') && (c < '0' || c > '9')))
                return null;
            var entry = value as Dictionary<string, object>;
            if (entry == null) return null;
            object history, common, tencent, region;
            if (!entry.TryGetValue("matchHistory", out history) ||
                !entry.TryGetValue("common", out common) ||
                !entry.TryGetValue("isTencent", out tencent) ||
                !(tencent is bool) || !(bool)tencent ||
                entry.TryGetValue("regionPathParam", out region) &&
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

        // Riot additions must use a source-announced region with its exact common
        // hostname and a match-history gateway already present in the pinned catalog.
        private static LeagueSgpServer TryCreateRiotServer(string id, object value)
        {
            if (id == null || id.Length < 2 || id.Length > 8 ||
                id[0] < 'A' || id[0] > 'Z' || id[1] < 'A' || id[1] > 'Z' ||
                id.Any(c => (c < 'A' || c > 'Z') && (c < '0' || c > '9')))
                return null;
            var entry = value as Dictionary<string, object>;
            if (entry == null) return null;
            object history, common, tencent, region;
            if (!entry.TryGetValue("matchHistory", out history) ||
                !entry.TryGetValue("common", out common) ||
                !entry.TryGetValue("isTencent", out tencent) ||
                !(tencent is bool) || (bool)tencent ||
                entry.TryGetValue("regionPathParam", out region) &&
                !SafeRiotRegionPath(id, region as string))
                return null;
            var pathRegion = region as string ?? id;
            var server = new LeagueSgpServer
            {
                Id = id, Name = id, PathRegion = pathRegion, IsTencent = false,
                MatchHistory = history as string, Common = common as string
            };
            if (!SafeEndpointShape(server.MatchHistory, false) ||
                !SafeEndpointShape(server.Common, false)) return null;
            var historyHost = new Uri(server.MatchHistory).Host;
            var commonHost = new Uri(server.Common).Host;
            if (!string.Equals(commonHost, id.ToLowerInvariant() + "-red.lol.sgp.pvp.net",
                StringComparison.OrdinalIgnoreCase) ||
                !LeagueSgpMatchHistoryApiClient.BuiltinServers.Any(builtin =>
                    !builtin.IsTencent && string.Equals(
                        new Uri(builtin.MatchHistory).Host, historyHost,
                        StringComparison.OrdinalIgnoreCase)))
                return null;
            return server;
        }

        // Source regionPathParam may differ from a new Riot server ID (e.g. ME -> ME1).
        // Accept only the exact ID or a bounded numeric platform suffix.
        private static bool SafeRiotRegionPath(string id, string region)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(region) ||
                region.Length > 12 || !region.StartsWith(id, StringComparison.Ordinal))
                return false;
            var suffix = region.Substring(id.Length);
            return suffix.Length == 0 || suffix.Length <= 3 &&
                suffix.All(c => c >= '0' && c <= '9');
        }

        // Riot platform IDs may use different pinned common-host aliases (NA1/na, OC1/oce).
        // Never route League-session credentials to a history gateway or another region.
        private static bool SafeKnownEndpoint(string value, LeagueSgpServer server, bool history)
        {
            if (!SafeEndpointShape(value, server.IsTencent)) return false;
            if (server.IsTencent) return SafeEndpoint(value, server);
            var baseline = LeagueSgpMatchHistoryApiClient.BuiltinServers
                .FirstOrDefault(item => !item.IsTencent && item.Id == server.Id);
            if (baseline == null) return false;
            var host = new Uri(value).Host;
            return history
                ? LeagueSgpMatchHistoryApiClient.BuiltinServers.Any(item =>
                    !item.IsTencent && string.Equals(new Uri(item.MatchHistory).Host,
                        host, StringComparison.OrdinalIgnoreCase))
                : string.Equals(new Uri(baseline.Common).Host, host,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool SafeEndpointShape(string value, bool tencent)
        {
            Uri uri;
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
                Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 &&
                uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
                uri.AbsolutePath == "/" && uri.Port == (tencent ? 21019 : 443) &&
                uri.Host.EndsWith(tencent ? ".lol.qq.com" : ".sgp.pvp.net",
                    StringComparison.OrdinalIgnoreCase);
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
            if (!SafeEndpointShape(value, tencent)) return false;
            var uri = new Uri(value);
            if (!tencent) return uri.Host.StartsWith(
                server.PathRegion.ToLowerInvariant() + "-", StringComparison.OrdinalIgnoreCase);
            var prefix = server.PathRegion.ToLowerInvariant() + "-";
            if (!uri.Host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            var suffix = uri.Host.Substring(prefix.Length).ToLowerInvariant();
            if (suffix == "sgp.lol.qq.com") return true;
            const string ending = "-sgp.lol.qq.com";
            if (!suffix.EndsWith(ending, StringComparison.Ordinal)) return false;
            var variant = suffix.Substring(0, suffix.Length - ending.Length);
            return variant.Length > 0 && variant.Length <= 16 &&
                variant.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9');
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
                _lastAttempt = DateTime.MinValue;
                _lastSuccess = DateTime.MinValue;
            }
        }
    }
}
