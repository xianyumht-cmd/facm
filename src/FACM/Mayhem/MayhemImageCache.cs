using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.Mayhem
{
    internal static class MayhemImageCache
    {
        private sealed class CacheEntry
        {
            public DateTime Time { get; set; }
            public byte[] Bytes { get; set; }
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public static async Task<Bitmap> GetAsync(string reference, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            byte[] bytes = null;
            lock (Sync)
            {
                CacheEntry entry;
                if (Cache.TryGetValue(reference, out entry) && DateTime.UtcNow - entry.Time < TimeSpan.FromMinutes(10))
                    bytes = entry.Bytes;
            }

            if (bytes == null)
            {
                bytes = await RiotGameDataService.DownloadImageAsync(reference, token).ConfigureAwait(false);
                if (bytes == null || bytes.Length < 64) return null;
                lock (Sync)
                {
                    Cache[reference] = new CacheEntry { Time = DateTime.UtcNow, Bytes = bytes };
                    if (Cache.Count > 160) RemoveOldEntries();
                }
            }

            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var original = Image.FromStream(stream, true, true))
                    return new Bitmap(original);
            }
            catch
            {
                return null;
            }
        }

        private static void RemoveOldEntries()
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(10);
            var remove = new List<string>();
            foreach (var pair in Cache)
            {
                if (pair.Value.Time < cutoff) remove.Add(pair.Key);
            }
            foreach (var key in remove) Cache.Remove(key);
            if (Cache.Count <= 160) return;
            remove.Clear();
            foreach (var pair in Cache)
            {
                remove.Add(pair.Key);
                if (Cache.Count - remove.Count <= 120) break;
            }
            foreach (var key in remove) Cache.Remove(key);
        }
    }
}
