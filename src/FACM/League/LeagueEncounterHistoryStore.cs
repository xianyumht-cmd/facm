using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueEncounterRecord
    {
        public long GameId { get; set; }
        public string SelfPuuid { get; set; }
        public string Puuid { get; set; }
        public string Region { get; set; }
        public string PlatformId { get; set; }
        public string QueueType { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    internal sealed class LeagueEncounterHistoryState
    {
        public int Version { get; set; } = 1;
        public List<LeagueEncounterRecord> Games { get; set; } = new List<LeagueEncounterRecord>();
    }

    internal sealed class LeagueEncounterHistoryPage
    {
        public IReadOnlyList<LeagueEncounterRecord> Records { get; set; }
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    // Encounter metadata stays on this computer, separate from cloud preferences and tagged-player exchange.
    internal sealed class LeagueEncounterHistoryStore
    {
        private const int MaximumEntries = 10000;
        private const int MaximumBytes = 4 * 1024 * 1024;
        private readonly string _path;
        private readonly object _sync = new object();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = MaximumBytes };

        internal LeagueEncounterHistoryStore(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("Data directory required.", nameof(dataDirectory));
            _path = Path.Combine(Path.GetFullPath(dataDirectory), "league-encounters.json");
        }

        internal static LeagueEncounterHistoryStore CreateDefault()
        {
            return new LeagueEncounterHistoryStore(RuntimePaths.DataDirectory);
        }

        internal int SaveGame(long gameId, string selfPuuid, string region, string platformId,
            string queueType, IEnumerable<string> participants)
        {
            if (gameId <= 0 || !ValidPuuid(selfPuuid) || !ValidRegion(region) || !ValidRegion(platformId) ||
                string.IsNullOrWhiteSpace(queueType) || queueType.Length > 64 || queueType.Any(char.IsControl) ||
                participants == null) throw new ArgumentException("Invalid encountered game metadata.");
            var players = participants.Take(11).ToList();
            if (players.Count < 2 || players.Count > 10 || players.Any(p => !ValidPuuid(p)) ||
                players.Distinct(StringComparer.Ordinal).Count() != players.Count || !players.Contains(selfPuuid))
                throw new ArgumentException("Invalid encountered game participants.", nameof(participants));
            var scopeRegion = Normalize(region);
            var scopePlatform = Normalize(platformId);
            lock (_sync)
            {
                var state = ReadState();
                var existing = new HashSet<string>(state.Games.Select(Key), StringComparer.Ordinal);
                var updated = DateTime.UtcNow;
                var added = 0;
                foreach (var player in players.Where(p => p != selfPuuid))
                {
                    var record = new LeagueEncounterRecord { GameId = gameId, Puuid = player, SelfPuuid = selfPuuid,
                        Region = scopeRegion, PlatformId = scopePlatform, QueueType = queueType,
                        UpdatedUtc = updated };
                    if (existing.Add(Key(record))) { state.Games.Add(record); added++; }
                }
                if (added == 0) return 0;
                state.Games = state.Games.OrderByDescending(p => p.UpdatedUtc)
                    .ThenByDescending(p => p.GameId).Take(MaximumEntries).ToList();
                SaveState(state);
                return added;
            }
        }

        internal IReadOnlyList<LeagueEncounterRecord> ReadPlayer(string selfPuuid, string playerPuuid,
            string region, string platformId, int maximum = 40)
        {
            return QueryPlayer(selfPuuid, playerPuuid, region, platformId, 1, maximum).Records;
        }

        // Mirrors Akari's player-scoped encountered-game query: optional exact queue.type,
        // stable time ordering and bounded pagination. Never returns another account's data.
        internal LeagueEncounterHistoryPage QueryPlayer(string selfPuuid, string playerPuuid,
            string region, string platformId, int page = 1, int pageSize = 40,
            string queueType = null, bool newestFirst = true)
        {
            page = Math.Max(1, Math.Min(10000, page));
            pageSize = Math.Max(1, Math.Min(100, pageSize));
            var result = new LeagueEncounterHistoryPage { Page = page, PageSize = pageSize,
                Records = new LeagueEncounterRecord[0] };
            if (!ValidPuuid(selfPuuid) || !ValidPuuid(playerPuuid) || !ValidRegion(region) || !ValidRegion(platformId))
                return result;
            if (queueType != null && (queueType.Length > 64 || queueType.Any(char.IsControl)))
                return result;
            var scopeRegion = Normalize(region);
            var scopePlatform = Normalize(platformId);
            lock (_sync)
            {
                var matches = ReadState().Games.Where(p => p.SelfPuuid == selfPuuid && p.Puuid == playerPuuid &&
                    p.Region == scopeRegion && p.PlatformId == scopePlatform &&
                    (string.IsNullOrEmpty(queueType) || p.QueueType == queueType));
                var sorted = newestFirst ? matches.OrderByDescending(p => p.UpdatedUtc).ThenByDescending(p => p.GameId) :
                    matches.OrderBy(p => p.UpdatedUtc).ThenBy(p => p.GameId);
                result.Total = matches.Count();
                result.Records = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
                return result;
            }
        }

        internal bool DeleteGame(long gameId, string selfPuuid, string playerPuuid, string region, string platformId)
        {
            if (gameId <= 0 || !ValidPuuid(selfPuuid) || !ValidPuuid(playerPuuid) ||
                !ValidRegion(region) || !ValidRegion(platformId)) return false;
            var scopeRegion = Normalize(region);
            var scopePlatform = Normalize(platformId);
            lock (_sync)
            {
                var state = ReadState();
                var removed = state.Games.RemoveAll(p => p.GameId == gameId && p.SelfPuuid == selfPuuid &&
                    p.Puuid == playerPuuid && p.Region == scopeRegion && p.PlatformId == scopePlatform);
                if (removed > 0) SaveState(state);
                return removed > 0;
            }
        }

        private LeagueEncounterHistoryState ReadState()
        {
            if (!File.Exists(_path)) return new LeagueEncounterHistoryState();
            if (new FileInfo(_path).Length > MaximumBytes) throw new InvalidDataException("Encounter history is too large.");
            LeagueEncounterHistoryState state;
            try { state = _json.Deserialize<LeagueEncounterHistoryState>(File.ReadAllText(_path, Encoding.UTF8)); }
            catch (ArgumentException e) { throw new InvalidDataException("Encounter history cannot be parsed.", e); }
            catch (InvalidOperationException e) { throw new InvalidDataException("Encounter history cannot be parsed.", e); }
            if (state == null || state.Version != 1 || state.Games == null || state.Games.Count > MaximumEntries ||
                state.Games.Any(p => p == null || p.GameId <= 0 || !ValidPuuid(p.SelfPuuid) ||
                    !ValidPuuid(p.Puuid) || p.Puuid == p.SelfPuuid || !ValidRegion(p.Region) ||
                    !ValidRegion(p.PlatformId) || string.IsNullOrWhiteSpace(p.QueueType) ||
                    p.QueueType.Length > 64 || p.QueueType.Any(char.IsControl)) ||
                state.Games.GroupBy(Key, StringComparer.Ordinal).Any(g => g.Count() > 1))
                throw new InvalidDataException("Encounter history contains invalid entries.");
            return state;
        }

        private void SaveState(LeagueEncounterHistoryState state)
        {
            var body = _json.Serialize(state);
            if (Encoding.UTF8.GetByteCount(body) > MaximumBytes)
                throw new InvalidOperationException("Encounter history size limit reached.");
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, body, new UTF8Encoding(false));
                if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak", true);
                else File.Move(temporary, _path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string Key(LeagueEncounterRecord p)
        {
            return p.SelfPuuid + "|" + Normalize(p.Region) + "|" + Normalize(p.PlatformId) +
                "|" + p.GameId + "|" + p.Puuid;
        }

        private static string Normalize(string value) { return (value ?? string.Empty).Trim().ToUpperInvariant(); }
        private static bool ValidRegion(string value)
        {
            value = Normalize(value);
            return value.Length <= 32 && value.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
        }
        private static bool ValidPuuid(string value) { return LeagueDynamicMessageService.SafePuuid(value); }
    }
}
