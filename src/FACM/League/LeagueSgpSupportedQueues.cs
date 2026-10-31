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
    // Source-pinned public queue catalog. SGP credentials never reach this transport.
    internal static class LeagueSgpSupportedQueues
    {
        private const string Url = "https://akari-api.yuru-yuri.com/config/v1/sgp/supported-queues";
        private static readonly DateTime BuiltinTimestamp = new DateTime(2026, 7, 18, 4, 0, 0, DateTimeKind.Utc);
        private static readonly int[] Builtin = { 420, 440, 430, 450, 480, 1700, 1750, 490, 1900,
            900, 2300, 2400, 4210, 4220, 4240, 4250, 4260 };
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly object Sync = new object();
        private static int[] _queues = Builtin;
        private static DateTime _updatedAt = BuiltinTimestamp;
        private static DateTime _lastAttempt = DateTime.MinValue;

        internal static int[] Snapshot()
        {
            lock (Sync) return (int[])_queues.Clone();
        }

        internal static Dictionary<int, string> ParseQueueNames(byte[] bytes)
        {
            var names = new Dictionary<int, string>();
            if (bytes == null || bytes.Length == 0 || bytes.Length > 512 * 1024) return names;
            try
            {
                var rows = new JavaScriptSerializer { MaxJsonLength = 512 * 1024 }
                    .DeserializeObject(Encoding.UTF8.GetString(bytes)) as object[];
                if (rows == null || rows.Length > 2048) return names;
                foreach (var entry in rows)
                {
                    var row = entry as Dictionary<string, object>;
                    if (row == null) continue;
                    object rawId, rawName;
                    if (!row.TryGetValue("id", out rawId) || !(rawId is int) ||
                        !row.TryGetValue("name", out rawName)) continue;
                    var id = (int)rawId;
                    var name = rawName as string;
                    if (id <= 0 || id > 100000 || string.IsNullOrWhiteSpace(name) ||
                        name.Length > 80 || name.Any(char.IsControl) || names.ContainsKey(id)) continue;
                    names.Add(id, name.Trim());
                }
            }
            catch (ArgumentException) { names.Clear(); }
            catch (InvalidOperationException) { names.Clear(); }
            return names;
        }

        internal static int? ParseLobbyQueueId(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 64 * 1024) return null;
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 }
                    .DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                object rawConfig, rawId;
                if (root == null || !root.TryGetValue("gameConfig", out rawConfig)) return null;
                var config = rawConfig as Dictionary<string, object>;
                if (config == null || !config.TryGetValue("queueId", out rawId) || !(rawId is int))
                    return null;
                var id = (int)rawId;
                return id > 0 && id <= 100000 ? (int?)id : null;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        internal static bool IsValidTag(string tag)
        {
            if (tag == null || tag == "ranked" || tag == "normal") return true;
            if (!tag.StartsWith("q_", StringComparison.Ordinal)) return false;
            int id;
            if (!int.TryParse(tag.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out id) ||
                id <= 0 || tag != "q_" + id.ToString(CultureInfo.InvariantCulture)) return false;
            lock (Sync) return Array.IndexOf(_queues, id) >= 0;
        }

        internal static async Task<bool> TryRefreshAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (DateTime.UtcNow - _lastAttempt < TimeSpan.FromHours(2)) return false;
                _lastAttempt = DateTime.UtcNow;
                var bytes = await LeaguePublicDataTransport.TryGetBytesAsync(
                    Url, TimeSpan.FromSeconds(4), token).ConfigureAwait(false);
                return TryApply(bytes);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
            finally { Gate.Release(); }
        }

        internal static bool TryApply(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 16 * 1024) return false;
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 }
                    .DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                if (root == null) return false;
                object timestamp, value;
                if (!root.TryGetValue("updatedAt", out timestamp) ||
                    !root.TryGetValue("queues", out value)) return false;
                DateTimeOffset updated;
                if (!DateTimeOffset.TryParse(timestamp as string, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out updated) ||
                    updated.UtcDateTime > DateTime.UtcNow.AddDays(1)) return false;
                var raw = value as object[];
                if (raw == null || raw.Length == 0 || raw.Length > 128) return false;
                var next = new List<int>(raw.Length);
                var seen = new HashSet<int>();
                foreach (var item in raw)
                {
                    if (!(item is int)) return false;
                    var id = (int)item;
                    if (id <= 0 || id > 100000 || !seen.Add(id)) return false;
                    next.Add(id);
                }
                lock (Sync)
                {
                    if (updated.UtcDateTime <= _updatedAt) return false;
                    _queues = next.ToArray();
                    _updatedAt = updated.UtcDateTime;
                }
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        internal static void ResetForSmokeTest()
        {
            lock (Sync)
            {
                _queues = Builtin;
                _updatedAt = BuiltinTimestamp;
                _lastAttempt = DateTime.MinValue;
            }
        }
    }
}
