using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.Mayhem
{
    /// <summary>
    /// A small, local-only cache for the public Mayhem top-ten ranking.
    /// Cached results are not asserted to be the latest Chinese patch.
    /// No account data, tokens, LCU metadata or personal statistics are stored.
    /// </summary>
    internal static class MayhemCompactRankingCache
    {
        internal static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(20);
        internal static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);
        private const int SchemaVersion = 1;
        private const int MaximumFileBytes = 32768;

        internal sealed class Entry
        {
            internal MayhemRankingSnapshot Ranking;
            internal DateTime SavedUtc;
            internal bool Fresh;
        }

        private sealed class CacheDocument
        {
            public int Schema { get; set; }
            public string Mode { get; set; }
            public string Source { get; set; }
            public string Patch { get; set; }
            public string SavedUtc { get; set; }
            public List<MayhemTopChampion> TopTen { get; set; }
        }

        internal static string DefaultFilePath
        {
            get
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(root, "GGman", "cache", "mayhem-top10-v1.json");
            }
        }

        internal static Entry TryLoad(string path = null, DateTime? utcNow = null)
        {
            try
            {
                path = path ?? DefaultFilePath;
                var file = new FileInfo(path);
                if (!file.Exists || file.Length == 0 || file.Length > MaximumFileBytes) return null;
                var json = File.ReadAllText(path, Encoding.UTF8);
                var record = new JavaScriptSerializer().Deserialize<CacheDocument>(json);
                if (record == null || record.Schema != SchemaVersion ||
                    !string.Equals(record.Mode, "aram-mayhem", StringComparison.Ordinal) ||
                    !IsApprovedSource(record.Source)) return null;

                DateTime saved;
                if (!DateTime.TryParseExact(record.SavedUtc, "o",
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out saved))
                    return null;
                saved = saved.ToUniversalTime();
                var now = (utcNow ?? DateTime.UtcNow).ToUniversalTime();
                var age = now - saved;
                if (age < TimeSpan.FromMinutes(-5) || age > MaximumAge) return null;
                var ranking = new MayhemRankingSnapshot
                {
                    Source = record.Source,
                    Patch = record.Patch,
                    TopTen = record.TopTen
                };
                if (!MayhemRankingSourceService.IsComplete(ranking)) return null;
                return new Entry { Ranking = ranking, SavedUtc = saved, Fresh = age >= TimeSpan.Zero && age <= FreshFor };
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (Exception) { return null; } // Corrupt/untrusted local cache is never fatal.
        }

        internal static bool TrySave(MayhemRankingSnapshot ranking, string path = null, DateTime? savedUtc = null)
        {
            if (!MayhemRankingSourceService.IsComplete(ranking) ||
                !IsApprovedSource(ranking.Source)) return false;
            path = path ?? DefaultFilePath;
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var document = new CacheDocument
                {
                    Schema = SchemaVersion, Mode = "aram-mayhem",
                    Source = ranking.Source, Patch = ranking.Patch,
                    SavedUtc = (savedUtc ?? DateTime.UtcNow).ToUniversalTime()
                        .ToString("o", CultureInfo.InvariantCulture),
                    TopTen = ranking.TopTen
                };
                var json = new JavaScriptSerializer().Serialize(document);
                if (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes) return false;
                var directory = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory)) return false;
                Directory.CreateDirectory(directory);
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (Exception) { return false; }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static bool IsApprovedSource(string source)
        {
            return string.Equals(source, "Hexdata", StringComparison.Ordinal) ||
                   string.Equals(source, "ARAMGG", StringComparison.Ordinal) ||
                   string.Equals(source, "ARAMMayhem", StringComparison.Ordinal);
        }

        internal static void ValidateForSmokeTest()
        {
            var path = Path.Combine(Path.GetTempPath(),
                "ggman-ranking-fixture-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var source = new MayhemRankingSnapshot
                {
                    Source = "Hexdata", Patch = "26.21", TopTen = new List<MayhemTopChampion>()
                };
                for (var i = 0; i < 10; i++)
                    source.TopTen.Add(new MayhemTopChampion {
                        Rank = i + 1, Name = "Champion" + i, Slug = "champion" + i,
                        Tier = "A", WinRate = 50 + i
                    });
                var now = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
                if (!TrySave(source, path, now)) throw new InvalidOperationException("Compact rank cache write failed.");
                var fresh = TryLoad(path, now.AddMinutes(5));
                if (fresh == null || !fresh.Fresh || fresh.Ranking.TopTen.Count != 10)
                    throw new InvalidOperationException("Valid compact rank cache was rejected.");
                var stale = TryLoad(path, now.AddHours(2));
                if (stale == null || stale.Fresh)
                    throw new InvalidOperationException("Valid historical rank lost its stale label.");
                if (TryLoad(path, now.AddHours(25)) != null)
                    throw new InvalidOperationException("Expired cache was displayed as active ranking.");
                var invalidSource = new MayhemRankingSnapshot
                {
                    Source = "invalid", Patch = source.Patch, TopTen = source.TopTen
                };
                if (TrySave(invalidSource, path, now))
                    throw new InvalidOperationException("Untrusted ranking provider reached disk.");
                File.WriteAllText(path, "{\"Schema\":1,\"TopTen\":[]}");
                if (TryLoad(path, now) != null)
                    throw new InvalidOperationException("Corrupt rank cache was accepted.");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
