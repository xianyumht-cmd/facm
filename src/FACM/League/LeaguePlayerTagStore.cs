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

    // Intentionally separate from AppSettings and CloudSettingsSnapshot: player notes stay local.
    internal sealed class LeaguePlayerTagStore
    {
        private const int MaximumEntries = 3000;
        private const int MaximumFileBytes = 1024 * 1024;
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
                state.Players.GroupBy(p => p.SelfPuuid + "|" + p.Region + "|" + p.PlatformId + "|" + p.Puuid,
                    StringComparer.Ordinal).Any(g => g.Count() != 1))
                throw new InvalidDataException("Player tag file contains invalid records.");
            return state;
        }

        private void SaveState(LeaguePlayerTagState state)
        {
            var body = _json.Serialize(state);
            if (Encoding.UTF8.GetByteCount(body) > MaximumFileBytes) throw new InvalidOperationException("Player tag file size limit reached.");
            var directory = Path.GetDirectoryName(_path);
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, body, new UTF8Encoding(false));
                if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak", true);
                else File.Move(temporary, _path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
