using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal sealed class LeagueSgpEogResult
    {
        internal long GameId;
        internal string GameMode;
        internal int QueueId;
        internal List<LeagueSgpEogPlayer> Players;
    }

    internal sealed class LeagueSgpEogPlayer
    {
        internal int TeamId;
        internal string Puuid;
        internal string Name;
        internal int ChampionId;
        internal string Position;
        internal int Spell1Id;
        internal int Spell2Id;
        internal int? Kills;
        internal int? Deaths;
        internal int? Assists;
        internal int? Damage;
        internal int? Gold;
        internal int? Minions;
        internal int? NeutralMinions;
        internal int? Vision;
        internal bool? Win;
    }

    // Project only documented end-of-game fields. Never retain room passwords, JWTs or credentials.
    internal static class LeagueSgpEogProjection
    {
        internal static LeagueSgpEogResult Parse(byte[] bytes, long gameId, string ownerPuuid)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 1024 * 1024 ||
                gameId <= 0 || !LeagueSgpMatchHistoryApiClient.SafePuuid(ownerPuuid)) return null;
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }
                    .DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                if (root == null || Number(root, "gameId") != gameId ||
                    !string.Equals(Text(root, "puuid"), ownerPuuid, StringComparison.OrdinalIgnoreCase))
                    return null;
                var first = Array(root, "teamPlayerParticipantStats");
                var second = Array(root, "otherTeamPlayerParticipantStats");
                if (first == null || second == null || first.Length + second.Length == 0 ||
                    first.Length + second.Length > 20) return null;
                var players = new List<LeagueSgpEogPlayer>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in first.Concat(second))
                {
                    var row = entry as Dictionary<string, object>;
                    if (row == null) return null;
                    var id = Number(row, "gameId");
                    if (id.HasValue && id.Value != gameId) return null;
                    var team = Number(row, "teamId");
                    var champion = Number(row, "championId");
                    if (!team.HasValue || team < 1 || team > 1000 ||
                        !champion.HasValue || champion < 0 || champion > 10000) return null;
                    var puuid = Text(row, "puuid");
                    if (!string.IsNullOrEmpty(puuid) &&
                        (!LeagueSgpMatchHistoryApiClient.SafePuuid(puuid) || !seen.Add(puuid)))
                        return null;
                    var name = Text(row, "riotIdGameName");
                    var tag = Text(row, "riotIdTagLine");
                    if (string.IsNullOrEmpty(name)) name = Text(row, "summonerName");
                    else if (!string.IsNullOrEmpty(tag)) name += "#" + tag;
                    var stats = Array(row, "statistics");
                    if (stats == null || stats.Length > 128) return null;
                    var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in stats)
                    {
                        var stat = item as Dictionary<string, object>;
                        if (stat == null) return null;
                        var key = Text(stat, "statTypeName");
                        var value = Number(stat, "value");
                        if (string.IsNullOrEmpty(key) || key.Length > 64 ||
                            !value.HasValue || value < 0 || value > int.MaxValue ||
                            !values.TryAddSafe(key, (int)value.Value)) return null;
                    }
                    players.Add(new LeagueSgpEogPlayer
                    {
                        TeamId = (int)team.Value, Puuid = puuid, Name = name,
                        ChampionId = (int)champion.Value,
                        Position = Text(row, "detectedTeamPosition") ?? Text(row, "selectedPosition"),
                        Spell1Id = PositiveId(row, "spell1Id"), Spell2Id = PositiveId(row, "spell2Id"),
                        Kills = Stat(values, "CHAMPIONS_KILLED"), Deaths = Stat(values, "NUM_DEATHS"),
                        Assists = Stat(values, "ASSISTS"),
                        Damage = Stat(values, "TOTAL_DAMAGE_DEALT_TO_CHAMPIONS"),
                        Gold = Stat(values, "GOLD_EARNED"),
                        Minions = Stat(values, "MINIONS_KILLED"),
                        NeutralMinions = Stat(values, "NEUTRAL_MINIONS_KILLED"),
                        Vision = Stat(values, "VISION_SCORE"),
                        Win = Stat(values, "WIN").HasValue ? Stat(values, "WIN") == 1 : (bool?)null
                    });
                }
                if (!seen.Contains(ownerPuuid)) return null;
                var queue = Number(root, "queueId");
                return new LeagueSgpEogResult
                {
                    GameId = gameId, QueueId = queue.HasValue && queue > 0 && queue <= 100000
                        ? (int)queue.Value : 0,
                    GameMode = Text(root, "gameMode"), Players = players
                };
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
        }

        private static int? Stat(Dictionary<string, int> stats, string key)
        {
            int value;
            return stats.TryGetValue(key, out value) ? value : (int?)null;
        }

        private static int PositiveId(Dictionary<string, object> row, string key)
        {
            var id = Number(row, key);
            return id.HasValue && id >= 0 && id <= 100000 ? (int)id.Value : 0;
        }

        private static object[] Array(Dictionary<string, object> row, string key)
        {
            object value;
            return row.TryGetValue(key, out value) ? value as object[] : null;
        }

        private static string Text(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value)) return null;
            var text = value as string;
            if (string.IsNullOrWhiteSpace(text) || text.Length > 80 ||
                text.Any(char.IsControl)) return null;
            return text.Trim();
        }

        private static long? Number(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value) || value == null ||
                !(value is int || value is long || value is double || value is decimal)) return null;
            try
            {
                var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (number != decimal.Truncate(number) || number < 0 || number > long.MaxValue) return null;
                return (long)number;
            }
            catch (OverflowException) { return null; }
        }

        private static bool TryAddSafe(this Dictionary<string, int> stats, string key, int value)
        {
            if (stats.ContainsKey(key)) return false;
            stats.Add(key, value);
            return true;
        }
    }
}
