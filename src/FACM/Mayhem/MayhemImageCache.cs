using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

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
        private static readonly TimeSpan MemoryLifetime = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DiskLifetime = TimeSpan.FromHours(6);
        private static readonly SemaphoreSlim DiskReadGate = new SemaphoreSlim(4, 4);
        private static readonly SemaphoreSlim DecodeGate = new SemaphoreSlim(4, 4);

        public static async Task<Bitmap> GetAsync(string reference, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            token.ThrowIfCancellationRequested();

            byte[] bytes = ReadMemory(reference);
            if (bytes == null)
                bytes = await ReadDiskAsync(reference, token).ConfigureAwait(false);

            if (bytes == null)
            {
                bytes = await RiotGameDataService.DownloadImageAsync(reference, token).ConfigureAwait(false);
                if (bytes == null || bytes.Length < 64) return null;
                StoreMemory(reference, bytes);
                TryWriteDisk(reference, bytes);
            }
            else
            {
                StoreMemory(reference, bytes);
            }

            var bitmap = await DecodeAsync(bytes, token).ConfigureAwait(false);
            if (bitmap != null) return bitmap;
            TryDeleteDisk(reference);
            lock (Sync) Cache.Remove(reference);
            return null;
        }

        private static async Task<byte[]> ReadDiskAsync(string reference, CancellationToken token)
        {
            await DiskReadGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await Task.Run(delegate { return TryReadDisk(reference); }, token).ConfigureAwait(false);
            }
            finally
            {
                DiskReadGate.Release();
            }
        }

        private static async Task<Bitmap> DecodeAsync(byte[] bytes, CancellationToken token)
        {
            if (bytes == null || bytes.Length < 64) return null;
            await DecodeGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await Task.Run(delegate
                {
                    token.ThrowIfCancellationRequested();
                    return Decode(bytes);
                }, token).ConfigureAwait(false);
            }
            finally
            {
                DecodeGate.Release();
            }
        }

        private static byte[] ReadMemory(string reference)
        {
            lock (Sync)
            {
                CacheEntry entry;
                if (Cache.TryGetValue(reference, out entry) && DateTime.UtcNow - entry.Time < MemoryLifetime)
                    return entry.Bytes;
            }
            return null;
        }

        private static void StoreMemory(string reference, byte[] bytes)
        {
            lock (Sync)
            {
                Cache[reference] = new CacheEntry { Time = DateTime.UtcNow, Bytes = bytes };
                if (Cache.Count > 160) RemoveOldEntries();
            }
        }

        private static Bitmap Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 64) return null;
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

        private static byte[] TryReadDisk(string reference)
        {
            try
            {
                var path = GetDiskPath(reference, false);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                if (age > DiskLifetime)
                {
                    File.Delete(path);
                    return null;
                }
                var bytes = File.ReadAllBytes(path);
                return bytes.Length < 64 ? null : bytes;
            }
            catch
            {
                return null;
            }
        }

        private static void TryWriteDisk(string reference, byte[] bytes)
        {
            try
            {
                var path = GetDiskPath(reference, true);
                if (string.IsNullOrWhiteSpace(path)) return;
                var temp = path + ".tmp";
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                TrimDiskCache(Path.GetDirectoryName(path));
            }
            catch
            {
            }
        }

        private static void TryDeleteDisk(string reference)
        {
            try
            {
                var path = GetDiskPath(reference, false);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static string GetDiskPath(string reference, bool createDirectory)
        {
            try
            {
                var directory = Path.Combine(RuntimePaths.CacheDirectory, "mayhem-images");
                if (createDirectory && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                if (!Directory.Exists(directory) && !createDirectory) return null;
                return Path.Combine(directory, Hash(reference) + ".img");
            }
            catch
            {
                // FACM is intentionally portable. If its own directory is not writable, skip disk caching
                // instead of silently spilling data into the system TEMP/profile directories.
                return null;
            }
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        private static void TrimDiskCache(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
            try
            {
                var files = new DirectoryInfo(directory).GetFiles("*.img");
                foreach (var file in files)
                {
                    if (DateTime.UtcNow - file.LastWriteTimeUtc > DiskLifetime) file.Delete();
                }
                files = new DirectoryInfo(directory).GetFiles("*.img");
                if (files.Length <= 180) return;
                Array.Sort(files, (left, right) => left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc));
                for (var i = 0; i < files.Length - 140; i++) files[i].Delete();
            }
            catch
            {
            }
        }

        private static void RemoveOldEntries()
        {
            var cutoff = DateTime.UtcNow - MemoryLifetime;
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
