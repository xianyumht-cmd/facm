using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueEndOfGamePlayerLine
    {
        public int TeamId { get; set; }
        public int ChampionId { get; set; }
        public string DisplayName { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public bool KdaKnown { get; set; }
        public bool DamageKnown { get; set; }
        public bool GoldKnown { get; set; }
        public long DamageToChampions { get; set; }
        public long GoldEarned { get; set; }
        public int MinionsKilled { get; set; }
        public bool MinionsKnown { get; set; }
        public bool IsLocalPlayer { get; set; }
    }

    internal sealed class LeagueEndOfGameSummary
    {
        public static readonly LeagueEndOfGameSummary Unavailable = new LeagueEndOfGameSummary();

        public bool Available { get; set; }
        public long GameId { get; set; }
        public int GameLengthSeconds { get; set; }
        public string GameMode { get; set; }
        public int QueueId { get; set; }
        public IReadOnlyList<LeagueEndOfGamePlayerLine> Players { get; set; }

        public string DurationText
        {
            get
            {
                if (GameLengthSeconds <= 0) return "-";
                return GameLengthSeconds / 60 + ":" + (GameLengthSeconds % 60).ToString("00", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Reads the post-game stats block that the stock client only surfaces as a basic table.
    /// The payload shape changes across queues, so every field is optional and parsed defensively.
    /// </summary>
    internal sealed class LeagueEndOfGameStatsService
    {
        internal const string StatsPath = "/lol-end-of-game/v1/eog-stats-block";

        private readonly ILeagueClientApi _reader;

        public LeagueEndOfGameStatsService(ILeagueClientApi reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public async Task<LeagueEndOfGameSummary> ReadAsync(CancellationToken cancellationToken)
        {
            var bytes = await _reader.TryGetBytesAsync(StatsPath, cancellationToken).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0) return LeagueEndOfGameSummary.Unavailable;

            Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes))
                    as Dictionary<string, object>;
            }
            catch (ArgumentException) { return LeagueEndOfGameSummary.Unavailable; }
            catch (InvalidOperationException) { return LeagueEndOfGameSummary.Unavailable; }
            if (root == null) return LeagueEndOfGameSummary.Unavailable;

            var localPlayer = root.ContainsKey("localPlayer") ? root["localPlayer"] as Dictionary<string, object> : null;
            long localId;
            var localKnown = TryReadNonnegativeLong(localPlayer, "summonerId", out localId) && localId > 0;

            var players = new List<LeagueEndOfGamePlayerLine>();
            var teams = root.ContainsKey("teams") ? root["teams"] as object[] : null;
            if (teams != null)
            {
                foreach (var teamItem in teams)
                {
                    var team = teamItem as Dictionary<string, object>;
                    if (team == null) continue;
                    var teamId = (int)ReadDouble(team, "teamId");
                    var members = team.ContainsKey("players") ? team["players"] as object[] : null;
                    if (members == null) continue;
                    foreach (var memberItem in members)
                    {
                        var member = memberItem as Dictionary<string, object>;
                        if (member == null) continue;
                        var stats = member.ContainsKey("stats") ? member["stats"] as Dictionary<string, object> : null;
                        long kills, deaths, assists, damage, gold, minions;
                        var killsKnown = TryReadNonnegativeLong(stats, "CHAMPIONS_KILLED", out kills) && kills <= int.MaxValue;
                        var deathsKnown = TryReadNonnegativeLong(stats, "NUM_DEATHS", out deaths) && deaths <= int.MaxValue;
                        var assistsKnown = TryReadNonnegativeLong(stats, "ASSISTS", out assists) && assists <= int.MaxValue;
                        var damageKnown = TryReadNonnegativeLong(stats, "TOTAL_DAMAGE_DEALT_TO_CHAMPIONS", out damage);
                        var goldKnown = TryReadNonnegativeLong(stats, "GOLD_EARNED", out gold);
                        var minionsKnown = TryReadNonnegativeLong(stats, "MINIONS_KILLED", out minions) && minions <= int.MaxValue;
                        var line = new LeagueEndOfGamePlayerLine
                        {
                            TeamId = teamId,
                            ChampionId = (int)ReadDouble(member, "championId"),
                            DisplayName = ReadString(member, "summonerName") ?? ReadString(member, "riotIdGameName"),
                            Kills = killsKnown ? (int)kills : 0,
                            Deaths = deathsKnown ? (int)deaths : 0,
                            Assists = assistsKnown ? (int)assists : 0,
                            KdaKnown = killsKnown && deathsKnown && assistsKnown,
                            DamageKnown = damageKnown,
                            GoldKnown = goldKnown,
                            DamageToChampions = damageKnown ? damage : 0,
                            GoldEarned = goldKnown ? gold : 0,
                            MinionsKnown = minionsKnown,
                            MinionsKilled = minionsKnown ? (int)minions : 0
                        };
                        long memberId;
                        line.IsLocalPlayer = localKnown && TryReadNonnegativeLong(member, "summonerId", out memberId) && memberId == localId;
                        players.Add(line);
                    }
                }
            }

            long gameId;
            if (!TryReadNonnegativeLong(root, "gameId", out gameId) || gameId == 0 || players.Count == 0)
                return LeagueEndOfGameSummary.Unavailable;
            var summary = new LeagueEndOfGameSummary
            {
                Available = true,
                GameId = gameId,
                GameLengthSeconds = (int)ReadDouble(root, "gameLength"),
                GameMode = ReadString(root, "gameMode"),
                QueueId = (int)ReadDouble(root, "queueId"),
                Players = players
            };
            AppLog.Info("End-of-game stats: gameId=" + summary.GameId.ToString(CultureInfo.InvariantCulture) +
                        "; players=" + players.Count.ToString(CultureInfo.InvariantCulture));
            return summary;
        }

        private static string ReadString(Dictionary<string, object> value, string key)
        {
            object entry;
            return value != null && value.TryGetValue(key, out entry) && entry != null
                ? Convert.ToString(entry, CultureInfo.InvariantCulture)
                : null;
        }

        private static double ReadDouble(Dictionary<string, object> value, string key)
        {
            object entry;
            if (value == null || !value.TryGetValue(key, out entry) || entry == null) return 0;
            double parsed;
            return double.TryParse(Convert.ToString(entry, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static bool TryReadNonnegativeLong(Dictionary<string, object> value, string key, out long result)
        {
            return long.TryParse(ReadString(value, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result >= 0;
        }

        private static double ReadStat(Dictionary<string, object> stats, string key)
        {
            return ReadDouble(stats, key);
        }
    }
}
