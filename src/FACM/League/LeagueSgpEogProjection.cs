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
        internal double? GameLengthSeconds;
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
        internal Dictionary<string, double> Statistics;
        internal Dictionary<string, string> Metadata;
    }

    internal sealed class LeagueSgpEogTeamSummary
    {
        internal int TeamId;
        internal int PlayerCount;
        internal long? Kills;
        internal long? Deaths;
        internal long? Assists;
        internal long? Damage;
        internal long? Gold;
        internal long? CreepScore;
        internal long? Vision;
        internal bool? Win;
    }

    internal sealed class LeagueSgpEogDerivedStats
    {
        internal double? Kda;
        internal double? KillParticipation;
        internal double? DamageShare;
        internal double? GoldShare;
        internal double? DamagePerMinute;
        internal double? GoldPerMinute;
        internal double? CsPerMinute;
        internal double? VisionPerMinute;
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
                    var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in stats)
                    {
                        var stat = item as Dictionary<string, object>;
                        if (stat == null) return null;
                        var key = Text(stat, "statTypeName");
                        var value = StatisticNumber(stat, "value");
                        if (string.IsNullOrEmpty(key) || key.Length > 64 ||
                            !value.HasValue || !values.TryAddSafe(key, value.Value)) return null;
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
                        Win = Stat(values, "WIN").HasValue ? Stat(values, "WIN") == 1 : (bool?)null,
                        Statistics = values,
                        Metadata = ReadMetadata(row)
                    });
                }
                if (!seen.Contains(ownerPuuid)) return null;
                var queue = Number(root, "queueId");
                var length = StatisticNumber(root, "gameLength");
                return new LeagueSgpEogResult
                {
                    GameId = gameId, QueueId = queue.HasValue && queue > 0 && queue <= 100000
                        ? (int)queue.Value : 0,
                    GameMode = Text(root, "gameMode"), Players = players,
                    GameLengthSeconds = length > 0 && length <= 86400 ? length : null
                };
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
        }

        internal static IReadOnlyList<LeagueSgpEogTeamSummary> SummarizeTeams(LeagueSgpEogResult match)
        {
            if (match == null || match.Players == null || match.Players.Count == 0 ||
                match.Players.Any(p => p == null)) return Array.Empty<LeagueSgpEogTeamSummary>();
            return match.Players.GroupBy(p => p.TeamId).OrderBy(g => g.Key)
                .Select(group =>
                {
                    var members = group.ToArray();
                    return new LeagueSgpEogTeamSummary
                    {
                        TeamId = group.Key, PlayerCount = members.Length,
                        Kills = CompleteSum(members, p => p.Kills),
                        Deaths = CompleteSum(members, p => p.Deaths),
                        Assists = CompleteSum(members, p => p.Assists),
                        Damage = CompleteSum(members, p => p.Damage),
                        Gold = CompleteSum(members, p => p.Gold),
                        CreepScore = members.All(p => p.Minions.HasValue && p.NeutralMinions.HasValue)
                            ? (long?)members.Sum(p => (long)p.Minions.Value + p.NeutralMinions.Value) : null,
                        Vision = CompleteSum(members, p => p.Vision),
                        Win = members.All(p => p.Win.HasValue) &&
                            members.All(p => p.Win.Value == members[0].Win.Value)
                            ? members[0].Win : null
                    };
                }).ToArray();
        }

        private static long? CompleteSum(LeagueSgpEogPlayer[] players,
            Func<LeagueSgpEogPlayer, int?> metric)
        {
            return players.All(p => metric(p).HasValue)
                ? (long?)players.Sum(p => (long)metric(p).Value) : null;
        }

        internal static LeagueSgpEogDerivedStats Analyze(LeagueSgpEogResult match,
            LeagueSgpEogPlayer selected)
        {
            var result = new LeagueSgpEogDerivedStats();
            if (match == null || match.Players == null || selected == null ||
                !match.Players.Contains(selected)) return result;
            var team = match.Players.Where(p => p.TeamId == selected.TeamId).ToArray();
            if (team.Length == 0) return result;
            if (selected.Kills.HasValue && selected.Deaths.HasValue && selected.Assists.HasValue)
                result.Kda = (selected.Kills.Value + (double)selected.Assists.Value) /
                    Math.Max(1, selected.Deaths.Value);
            if (selected.Kills.HasValue && selected.Assists.HasValue &&
                team.All(p => p.Kills.HasValue))
            {
                var total = team.Sum(p => (long)p.Kills.Value);
                var involved = (long)selected.Kills.Value + selected.Assists.Value;
                if (total >= involved)
                    result.KillParticipation = total == 0 ? 0d : involved / (double)total;
            }
            if (selected.Damage.HasValue && team.All(p => p.Damage.HasValue))
            {
                var total = team.Sum(p => (long)p.Damage.Value);
                if (total >= selected.Damage.Value)
                    result.DamageShare = total == 0 ? 0d : selected.Damage.Value / (double)total;
            }
            if (selected.Gold.HasValue && team.All(p => p.Gold.HasValue))
            {
                var total = team.Sum(p => (long)p.Gold.Value);
                if (total >= selected.Gold.Value)
                    result.GoldShare = total == 0 ? 0d : selected.Gold.Value / (double)total;
            }
            if (match.GameLengthSeconds.HasValue &&
                match.GameLengthSeconds.Value > 0 && match.GameLengthSeconds.Value <= 86400)
            {
                var minutes = match.GameLengthSeconds.Value / 60d;
                if (selected.Damage.HasValue) result.DamagePerMinute = selected.Damage.Value / minutes;
                if (selected.Gold.HasValue) result.GoldPerMinute = selected.Gold.Value / minutes;
                if (selected.Vision.HasValue) result.VisionPerMinute = selected.Vision.Value / minutes;
                if (selected.Minions.HasValue && selected.NeutralMinions.HasValue)
                    result.CsPerMinute = (selected.Minions.Value + (double)selected.NeutralMinions.Value) / minutes;
            }
            return result;
        }

        private static Dictionary<string, string> ReadMetadata(Dictionary<string, object> row)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in new[] { "level", "wins", "losses" })
            {
                var number = Number(row, entry);
                var limit = entry == "level" ? 1000 : 1000000;
                if (number.HasValue && number.Value <= limit)
                    result.Add(entry.ToUpperInvariant(), number.Value.ToString(CultureInfo.InvariantCulture));
            }
            var change = StatisticNumber(row, "eloChange");
            if (change.HasValue && Math.Abs(change.Value) <= 100000)
                result.Add("ELO_CHANGE", change.Value.ToString("G", CultureInfo.InvariantCulture));
            foreach (var entry in new[] { "leaver", "botPlayer" })
            {
                object value;
                if (row.TryGetValue(entry, out value) && value is bool)
                    result.Add(entry == "leaver" ? "LEAVER" : "BOT_PLAYER",
                        (bool)value ? "true" : "false");
            }
            return result;
        }

        private static int? Stat(Dictionary<string, double> stats, string key)
        {
            double value;
            return stats.TryGetValue(key, out value) && value >= 0 &&
                value <= int.MaxValue && Math.Floor(value) == value
                ? (int)value : (int?)null;
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
            if (string.IsNullOrWhiteSpace(text) ||
                text.Length > (key == "puuid" ? 128 : 80) ||
                text.Any(char.IsControl)) return null;
            return text.Trim();
        }

        private static double? StatisticNumber(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value) || value == null ||
                !(value is int || value is long || value is double || value is decimal)) return null;
            try
            {
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return !double.IsNaN(number) && !double.IsInfinity(number) &&
                    Math.Abs(number) <= 1000000000000d ? number : (double?)null;
            }
            catch (OverflowException) { return null; }
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

        private static bool TryAddSafe(this Dictionary<string, double> stats, string key, double value)
        {
            if (stats.ContainsKey(key)) return false;
            stats.Add(key, value);
            return true;
        }
    }
}
