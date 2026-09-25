using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.Services
{
    internal sealed class PersonalStatsAccountRecord
    {
        public string AccountKeyHash { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string FirstSeenUtc { get; set; } = string.Empty;
        public string LastSeenUtc { get; set; } = string.Empty;
        public int SeenCount { get; set; } = 1;
    }

    internal sealed class PersonalStatsState
    {
        public int SchemaVersion { get; set; } = 1;
        public string FirstSeenUtc { get; set; } = string.Empty;
        public string LastSeenUtc { get; set; } = string.Empty;
        public List<string> ActiveDays { get; set; } = new List<string>();
        public List<PersonalStatsAccountRecord> Accounts { get; set; } = new List<PersonalStatsAccountRecord>();
    }

    internal sealed class PersonalStatsSnapshot
    {
        public int PlayedAccounts { get; set; }
        public int ActiveDays { get; set; }
        public DateTimeOffset? FirstSeenUtc { get; set; }
        public DateTimeOffset? LastSeenUtc { get; set; }
    }

    internal sealed class PersonalStatsStore
    {
        private const int MoveFileReplaceExisting = 0x1;
        private const int MoveFileWriteThrough = 0x8;
        private const int MaxFileBytes = 8 * 1024 * 1024;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly object _sync = new object();
        private readonly string _path;
        private readonly string _recoveryPath;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = MaxFileBytes };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        public PersonalStatsStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Personal stats directory is required.", nameof(directory));

            var fullDirectory = Path.GetFullPath(directory);
            _path = Path.Combine(fullDirectory, "personal-stats.json");
            _recoveryPath = Path.Combine(fullDirectory, "personal-stats.last-known-good.json");
        }

        public static PersonalStatsStore CreateDefault()
        {
            return new PersonalStatsStore(RuntimePaths.DataDirectory);
        }

        public PersonalStatsSnapshot RecordLaunch(DateTimeOffset now)
        {
            lock (_sync)
            {
                var state = LoadOrCreate(now);
                TouchUsageDay(state, now);
                Save(state);
                return BuildSnapshot(state);
            }
        }

        public PersonalStatsSnapshot ReadSnapshot(DateTimeOffset now)
        {
            lock (_sync)
            {
                return BuildSnapshot(LoadOrCreate(now));
            }
        }

        public PersonalStatsSnapshot RecordAccount(
            string accountKeyHash,
            string region,
            DateTimeOffset now,
            out bool isNewAccount)
        {
            if (!IsValidAccountHash(accountKeyHash))
                throw new ArgumentException("A valid account hash is required.", nameof(accountKeyHash));

            lock (_sync)
            {
                var state = LoadOrCreate(now);
                TouchUsageDay(state, now);

                var record = state.Accounts.FirstOrDefault(item =>
                    item != null &&
                    string.Equals(item.AccountKeyHash, accountKeyHash, StringComparison.OrdinalIgnoreCase));

                isNewAccount = record == null;
                if (record == null)
                {
                    record = new PersonalStatsAccountRecord
                    {
                        AccountKeyHash = accountKeyHash.ToLowerInvariant(),
                        Region = NormalizeRegion(region),
                        FirstSeenUtc = now.UtcDateTime.ToString("o"),
                        LastSeenUtc = now.UtcDateTime.ToString("o"),
                        SeenCount = 1
                    };
                    state.Accounts.Add(record);
                }
                else
                {
                    record.LastSeenUtc = now.UtcDateTime.ToString("o");
                    record.SeenCount = Math.Max(1, record.SeenCount) + 1;
                    var normalizedRegion = NormalizeRegion(region);
                    if (!string.IsNullOrWhiteSpace(normalizedRegion)) record.Region = normalizedRegion;
                }

                Save(state);
                return BuildSnapshot(state);
            }
        }

        public static string CreateAccountKeyHash(string deviceId, string puuid)
        {
            Guid parsedDeviceId;
            if (!Guid.TryParse(deviceId, out parsedDeviceId))
                throw new ArgumentException("A valid device ID is required.", nameof(deviceId));

            var normalizedPuuid = (puuid ?? string.Empty).Trim();
            if (normalizedPuuid.Length < 8)
                throw new ArgumentException("A valid player identifier is required.", nameof(puuid));

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(parsedDeviceId.ToString("D"))))
            {
                return ToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(normalizedPuuid))).ToLowerInvariant();
            }
        }

        internal IReadOnlyList<PersonalStatsAccountRecord> ReadAccountsForSync(DateTimeOffset now)
        {
            lock (_sync)
            {
                var state = LoadOrCreate(now);
                return state.Accounts
                    .Where(item => item != null && IsValidAccountHash(item.AccountKeyHash))
                    .Select(CloneAccount)
                    .ToArray();
            }
        }

        private PersonalStatsState LoadOrCreate(DateTimeOffset now)
        {
            PersonalStatsState state;
            if (TryLoad(_path, out state))
            {
                SaveRecovery(state);
                return state;
            }

            if (TryLoad(_recoveryPath, out state))
            {
                WriteAtomically(_path, Serialize(state));
                return state;
            }

            return new PersonalStatsState
            {
                SchemaVersion = 1,
                FirstSeenUtc = now.UtcDateTime.ToString("o"),
                LastSeenUtc = now.UtcDateTime.ToString("o"),
                ActiveDays = new List<string>(),
                Accounts = new List<PersonalStatsAccountRecord>()
            };
        }

        private bool TryLoad(string path, out PersonalStatsState state)
        {
            state = null;
            try
            {
                if (!File.Exists(path)) return false;
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaxFileBytes) return false;
                state = Normalize(_json.Deserialize<PersonalStatsState>(File.ReadAllText(path, Encoding.UTF8)));
                return true;
            }
            catch
            {
                state = null;
                return false;
            }
        }

        private static PersonalStatsState Normalize(PersonalStatsState state)
        {
            if (state == null || state.SchemaVersion != 1)
                throw new InvalidDataException("Unsupported personal stats state.");

            state.ActiveDays = (state.ActiveDays ?? new List<string>())
                .Where(IsValidDay)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();

            state.Accounts = (state.Accounts ?? new List<PersonalStatsAccountRecord>())
                .Where(item => item != null && IsValidAccountHash(item.AccountKeyHash))
                .GroupBy(item => item.AccountKeyHash, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var source = group.OrderByDescending(item => ParseUtc(item.LastSeenUtc) ?? DateTimeOffset.MinValue).First();
                    return new PersonalStatsAccountRecord
                    {
                        AccountKeyHash = source.AccountKeyHash.ToLowerInvariant(),
                        Region = NormalizeRegion(source.Region),
                        FirstSeenUtc = NormalizeUtcText(source.FirstSeenUtc),
                        LastSeenUtc = NormalizeUtcText(source.LastSeenUtc),
                        SeenCount = Math.Max(1, source.SeenCount)
                    };
                })
                .OrderBy(item => item.FirstSeenUtc, StringComparer.Ordinal)
                .ToList();

            var first = ParseUtc(state.FirstSeenUtc);
            var last = ParseUtc(state.LastSeenUtc);
            state.FirstSeenUtc = first.HasValue ? first.Value.UtcDateTime.ToString("o") : string.Empty;
            state.LastSeenUtc = last.HasValue ? last.Value.UtcDateTime.ToString("o") : state.FirstSeenUtc;
            return state;
        }

        private static void TouchUsageDay(PersonalStatsState state, DateTimeOffset now)
        {
            var day = now.ToLocalTime().ToString("yyyy-MM-dd");
            if (!state.ActiveDays.Contains(day, StringComparer.Ordinal))
                state.ActiveDays.Add(day);
            state.ActiveDays.Sort(StringComparer.Ordinal);

            if (!ParseUtc(state.FirstSeenUtc).HasValue)
                state.FirstSeenUtc = now.UtcDateTime.ToString("o");
            state.LastSeenUtc = now.UtcDateTime.ToString("o");
        }

        private void Save(PersonalStatsState state)
        {
            var normalized = Normalize(state);
            var text = Serialize(normalized);
            WriteAtomically(_path, text);
            WriteAtomically(_recoveryPath, text);
        }

        private void SaveRecovery(PersonalStatsState state)
        {
            try { WriteAtomically(_recoveryPath, Serialize(Normalize(state))); }
            catch { }
        }

        private string Serialize(PersonalStatsState state)
        {
            return _json.Serialize(state) + Environment.NewLine;
        }

        private static PersonalStatsSnapshot BuildSnapshot(PersonalStatsState state)
        {
            return new PersonalStatsSnapshot
            {
                PlayedAccounts = state == null || state.Accounts == null ? 0 : state.Accounts.Count,
                ActiveDays = state == null || state.ActiveDays == null ? 0 : state.ActiveDays.Count,
                FirstSeenUtc = state == null ? null : ParseUtc(state.FirstSeenUtc),
                LastSeenUtc = state == null ? null : ParseUtc(state.LastSeenUtc)
            };
        }

        private static PersonalStatsAccountRecord CloneAccount(PersonalStatsAccountRecord source)
        {
            return new PersonalStatsAccountRecord
            {
                AccountKeyHash = source.AccountKeyHash ?? string.Empty,
                Region = source.Region ?? string.Empty,
                FirstSeenUtc = source.FirstSeenUtc ?? string.Empty,
                LastSeenUtc = source.LastSeenUtc ?? string.Empty,
                SeenCount = Math.Max(1, source.SeenCount)
            };
        }

        private static DateTimeOffset? ParseUtc(string value)
        {
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(value, out parsed) ? parsed.ToUniversalTime() : (DateTimeOffset?)null;
        }

        private static string NormalizeUtcText(string value)
        {
            var parsed = ParseUtc(value);
            return parsed.HasValue ? parsed.Value.UtcDateTime.ToString("o") : string.Empty;
        }

        private static string NormalizeRegion(string value)
        {
            var region = (value ?? string.Empty).Trim();
            if (region.Length > 32) region = region.Substring(0, 32);
            return region;
        }

        private static bool IsValidAccountHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F')))
                    return false;
            }
            return true;
        }

        private static bool IsValidDay(string value)
        {
            DateTime parsed;
            return !string.IsNullOrWhiteSpace(value) &&
                   DateTime.TryParseExact(value, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out parsed);
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder((bytes == null ? 0 : bytes.Length) * 2);
            if (bytes != null)
            {
                foreach (var value in bytes) builder.Append(value.ToString("X2"));
            }
            return builder.ToString();
        }

        private static void WriteAtomically(string path, string text)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidDataException("Personal stats directory is unavailable.");

            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, Utf8NoBom))
                {
                    writer.Write(text ?? string.Empty);
                    writer.Flush();
                    stream.Flush(true);
                }

                var flags = MoveFileWriteThrough | (File.Exists(fullPath) ? MoveFileReplaceExisting : 0);
                if (!MoveFileEx(temporary, fullPath, flags))
                    throw new IOException("Personal stats atomic replacement failed.", Marshal.GetHRForLastWin32Error());
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }

        internal static void ValidateForSmokeTest()
        {
            var root = Path.Combine(Path.GetTempPath(), "GGman-PersonalStats-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store = new PersonalStatsStore(root);
                var deviceId = Guid.NewGuid().ToString("D");
                var firstNow = new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.FromHours(8));
                var secondNow = firstNow.AddDays(1);

                var launch = store.RecordLaunch(firstNow);
                Require(launch.ActiveDays == 1 && launch.PlayedAccounts == 0,
                    "Personal stats launch tracking drifted.");

                var hash1 = CreateAccountKeyHash(deviceId, "puuid-example-one");
                var hash2 = CreateAccountKeyHash(deviceId, "puuid-example-two");
                Require(hash1.Length == 64 && hash1 != hash2,
                    "Personal stats account hashing drifted.");
                Require(hash1.IndexOf("puuid", StringComparison.OrdinalIgnoreCase) < 0,
                    "Personal stats hash exposed the player identifier.");

                bool isNew;
                var afterFirst = store.RecordAccount(hash1, "HN1", firstNow, out isNew);
                Require(isNew && afterFirst.PlayedAccounts == 1,
                    "Personal stats did not add the first unique account.");

                var afterRepeat = store.RecordAccount(hash1, "HN1", secondNow, out isNew);
                Require(!isNew && afterRepeat.PlayedAccounts == 1 && afterRepeat.ActiveDays == 2,
                    "Personal stats did not deduplicate a repeated account.");

                var afterSecond = store.RecordAccount(hash2, "HN1", secondNow, out isNew);
                Require(isNew && afterSecond.PlayedAccounts == 2,
                    "Personal stats did not add the second unique account.");

                File.WriteAllText(Path.Combine(root, "personal-stats.json"), "{ broken");
                var recovered = new PersonalStatsStore(root).ReadSnapshot(secondNow);
                Require(recovered.PlayedAccounts == 2,
                    "Personal stats last-known-good recovery lost account history.");

                var raw = File.ReadAllText(Path.Combine(root, "personal-stats.last-known-good.json"));
                Require(raw.IndexOf("puuid-example", StringComparison.OrdinalIgnoreCase) < 0,
                    "Personal stats persisted a raw player identifier.");
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
