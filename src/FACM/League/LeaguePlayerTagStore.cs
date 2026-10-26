using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeaguePlayerTagRecord
    {
        public string Puuid { get; set; }
        public string SelfPuuid { get; set; }
        public string Region { get; set; }
        public string PlatformId { get; set; }
        public string Tag { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    internal sealed class LeaguePlayerTagState
    {
        public int Version { get; set; } = 1;
        public List<LeaguePlayerTagRecord> Players { get; set; } = new List<LeaguePlayerTagRecord>();
    }

    // These field names intentionally match Akari 1.5.1's JSON exchange schema.
    internal sealed class AkariTaggedPlayerDocument
    {
        public int databaseVersion { get; set; }
        public string type { get; set; }
        public List<AkariTaggedPlayerEntry> data { get; set; }
    }

    internal sealed class AkariTaggedPlayerEntry
    {
        public string puuid { get; set; }
        public string selfPuuid { get; set; }
        public string region { get; set; }
        public string rsoPlatformId { get; set; }
        public string tag { get; set; }
    }

    // Intentionally separate from AppSettings and CloudSettingsSnapshot: player notes stay local.
    internal sealed class LeaguePlayerTagStore
    {
        private const int MaximumEntries = 3000;
        private const int MaximumFileBytes = 1024 * 1024;
        private const int AkariDatabaseVersion = 15;
        private const string AkariExchangeType = "league-akari-tagged-players";
        internal const int MaximumTagLength = 120;
        private readonly string _path;
        private readonly object _sync = new object();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = MaximumFileBytes };

        internal LeaguePlayerTagStore(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("Data directory is required.", nameof(dataDirectory));
            _path = Path.Combine(Path.GetFullPath(dataDirectory), "league-player-tags.json");
        }

        internal static LeaguePlayerTagStore CreateDefault() { return new LeaguePlayerTagStore(RuntimePaths.DataDirectory); }

        internal IReadOnlyDictionary<string, string> ReadTags(string selfPuuid, string region, string platformId)
        {
            if (!ValidPuuid(selfPuuid)) return new Dictionary<string, string>(StringComparer.Ordinal);
            lock (_sync)
            {
                return ReadState().Players.Where(p => SameScope(p, selfPuuid, region, platformId))
                    .ToDictionary(p => p.Puuid, p => p.Tag, StringComparer.Ordinal);
            }
        }

        internal IReadOnlyList<LeaguePlayerTagRecord> ReadAllTags()
        {
            lock (_sync)
            {
                return ReadState().Players.OrderByDescending(p => p.UpdatedUtc)
                    .ThenBy(p => p.Puuid, StringComparer.Ordinal)
                    .Select(Copy).ToArray();
            }
        }

        internal bool TryEditStoredTag(LeaguePlayerTagRecord expected, string tag)
        {
            if (expected == null || !ValidPuuid(expected.SelfPuuid) || !ValidPuuid(expected.Puuid) ||
                !ValidRegion(expected.Region) || !ValidRegion(expected.PlatformId)) return false;
            tag = (tag ?? string.Empty).Trim();
            if (tag.Length > MaximumTagLength || tag.Any(char.IsControl))
                throw new ArgumentException("Invalid player tag.", nameof(tag));
            lock (_sync)
            {
                var state = ReadState();
                var record = state.Players.FirstOrDefault(p => Key(p) == Key(expected));
                if (record == null || record.Tag != expected.Tag || record.UpdatedUtc != expected.UpdatedUtc)
                    return false;
                if (tag == record.Tag) return true;
                if (tag.Length == 0) state.Players.Remove(record);
                else { record.Tag = tag; record.UpdatedUtc = DateTime.UtcNow; }
                SaveState(state);
                return true;
            }
        }

        internal void SetTag(string selfPuuid, string playerPuuid, string region, string platformId, string tag)
        {
            if (!ValidPuuid(selfPuuid) || !ValidPuuid(playerPuuid) ||
                string.Equals(selfPuuid, playerPuuid, StringComparison.Ordinal) ||
                !ValidRegion(region) || !ValidRegion(platformId))
                throw new ArgumentException("Invalid player identity or region.");
            tag = (tag ?? string.Empty).Trim();
            if (tag.Length > MaximumTagLength || tag.Any(char.IsControl))
                throw new ArgumentException("Invalid player tag.", nameof(tag));
            lock (_sync)
            {
                var state = ReadState();
                var existing = state.Players.FirstOrDefault(p => p.Puuid == playerPuuid &&
                    SameScope(p, selfPuuid, region, platformId));
                if (tag.Length == 0)
                {
                    if (existing == null) return;
                    state.Players.Remove(existing);
                }
                else if (existing != null)
                {
                    existing.Tag = tag;
                    existing.UpdatedUtc = DateTime.UtcNow;
                }
                else
                {
                    if (state.Players.Count >= MaximumEntries) throw new InvalidOperationException("Player tag limit reached.");
                    state.Players.Add(new LeaguePlayerTagRecord { Puuid = playerPuuid, SelfPuuid = selfPuuid,
                        Region = NormalizeRegion(region), PlatformId = NormalizeRegion(platformId),
                        Tag = tag, UpdatedUtc = DateTime.UtcNow });
                }
                SaveState(state);
            }
        }

        internal int ExportAkariJson(string destination)
        {
            var target = CheckedExchangePath(destination);
            if (string.Equals(target, _path, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Export cannot replace the application's player tag store.", nameof(destination));
            lock (_sync)
            {
                var records = ReadState().Players;
                var document = new AkariTaggedPlayerDocument { databaseVersion = AkariDatabaseVersion,
                    type = AkariExchangeType, data = records.Select(p => new AkariTaggedPlayerEntry
                    {
                        puuid = p.Puuid, selfPuuid = p.SelfPuuid, region = p.Region,
                        rsoPlatformId = p.PlatformId, tag = p.Tag
                    }).ToList() };
                var json = _json.Serialize(document);
                if (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
                    throw new InvalidOperationException("Player tag exchange limit reached.");
                WriteAtomic(target, json);
                return records.Count;
            }
        }

        internal int ImportAkariJson(string source)
        {
            var path = CheckedExchangePath(source);
            if (new FileInfo(path).Length > MaximumFileBytes)
                throw new InvalidDataException("Player tag exchange is too large.");
            AkariTaggedPlayerDocument document;
            try { document = _json.Deserialize<AkariTaggedPlayerDocument>(File.ReadAllText(path, Encoding.UTF8)); }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid tagged player exchange.", error); }
            catch (InvalidOperationException error) { throw new InvalidDataException("Invalid tagged player exchange.", error); }
            if (document == null || document.type != AkariExchangeType ||
                document.databaseVersion < 1 || document.databaseVersion > AkariDatabaseVersion ||
                document.data == null || document.data.Count > MaximumEntries ||
                document.data.Any(p => p == null || !ValidPuuid(p.puuid) || !ValidPuuid(p.selfPuuid) ||
                    p.puuid == p.selfPuuid || !ValidRegion(p.region) || !ValidRegion(p.rsoPlatformId) ||
                    string.IsNullOrWhiteSpace(p.tag) || p.tag.Length > MaximumTagLength || p.tag.Any(char.IsControl)))
                throw new InvalidDataException("Invalid tagged player exchange.");
            var incoming = document.data.Select(p => new LeaguePlayerTagRecord
            {
                Puuid = p.puuid, SelfPuuid = p.selfPuuid, Region = NormalizeRegion(p.region),
                PlatformId = NormalizeRegion(p.rsoPlatformId), Tag = p.tag, UpdatedUtc = DateTime.UtcNow
            }).ToList();
            if (incoming.GroupBy(Key, StringComparer.Ordinal).Any(g => g.Count() > 1))
                throw new InvalidDataException("Duplicate player tags in exchange.");
            lock (_sync)
            {
                var state = ReadState();
                var records = state.Players.ToDictionary(Key, StringComparer.Ordinal);
                foreach (var player in incoming) records[Key(player)] = player;
                if (records.Count > MaximumEntries) throw new InvalidOperationException("Player tag limit reached.");
                state.Players = records.Values.OrderBy(p => p.SelfPuuid, StringComparer.Ordinal)
                    .ThenBy(p => p.Puuid, StringComparer.Ordinal).ToList();
                if (incoming.Count > 0) SaveState(state);
                return incoming.Count;
            }
        }

        private LeaguePlayerTagState ReadState()
        {
            if (!File.Exists(_path)) return new LeaguePlayerTagState();
            if (new FileInfo(_path).Length > MaximumFileBytes) throw new InvalidDataException("Player tag file is too large.");
            LeaguePlayerTagState state;
            try { state = _json.Deserialize<LeaguePlayerTagState>(File.ReadAllText(_path, Encoding.UTF8)); }
            catch (ArgumentException error) { throw new InvalidDataException("Player tag file cannot be read.", error); }
            catch (InvalidOperationException error) { throw new InvalidDataException("Player tag file cannot be read.", error); }
            if (state == null || state.Version != 1 || state.Players == null || state.Players.Count > MaximumEntries ||
                state.Players.Any(p => p == null || !ValidPuuid(p.SelfPuuid) || !ValidPuuid(p.Puuid) ||
                    !ValidRegion(p.Region) || !ValidRegion(p.PlatformId) || string.IsNullOrWhiteSpace(p.Tag) ||
                    p.Tag.Length > MaximumTagLength || p.Tag.Any(char.IsControl)) ||
                state.Players.GroupBy(Key, StringComparer.Ordinal).Any(g => g.Count() != 1))
                throw new InvalidDataException("Player tag file contains invalid records.");
            return state;
        }

        private void SaveState(LeaguePlayerTagState state)
        {
            var body = _json.Serialize(state);
            if (Encoding.UTF8.GetByteCount(body) > MaximumFileBytes) throw new InvalidOperationException("Player tag file size limit reached.");
            WriteAtomic(_path, body);
        }

        private static void WriteAtomic(string path, string body)
        {
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, body, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string Key(LeaguePlayerTagRecord player)
        {
            return player.SelfPuuid + "|" + NormalizeRegion(player.Region) + "|" +
                NormalizeRegion(player.PlatformId) + "|" + player.Puuid;
        }

        private static LeaguePlayerTagRecord Copy(LeaguePlayerTagRecord p)
        {
            return new LeaguePlayerTagRecord { Puuid = p.Puuid, SelfPuuid = p.SelfPuuid,
                Region = p.Region, PlatformId = p.PlatformId, Tag = p.Tag, UpdatedUtc = p.UpdatedUtc };
        }

        private static string CheckedExchangePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("File path is required.", nameof(path));
            return Path.GetFullPath(path);
        }

        private static bool SameScope(LeaguePlayerTagRecord item, string selfPuuid, string region, string platformId)
        {
            return item.SelfPuuid == selfPuuid && item.Region == NormalizeRegion(region) &&
                item.PlatformId == NormalizeRegion(platformId);
        }

        private static string NormalizeRegion(string value) { return (value ?? string.Empty).Trim().ToUpperInvariant(); }
        private static bool ValidRegion(string value)
        {
            value = NormalizeRegion(value);
            return value.Length <= 32 && value.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
        }

        private static bool ValidPuuid(string value) { return LeagueDynamicMessageService.SafePuuid(value); }
    }
}
