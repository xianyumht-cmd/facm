using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal sealed class LeagueJungleTimelineSample
    {
        internal long GameId;
        internal int ChampionId;
        internal int TeamId;
        internal int TopWeight, MidWeight, BotWeight, TotalWeight;
        internal int TopGanks, MidGanks, BotGanks;
        internal bool Level3Gank, Level4Gank;
        internal string FirstCamp, FirstCampSide;
        internal bool? FirstDragonWon;
        internal int Dragons, SoloDragons, Voidgrubs, Heralds, Barons;
        internal double? FirstDragonSeconds, FirstVoidgrubSeconds, FirstHeraldSeconds, FirstBaronSeconds;
    }

    internal sealed class LeagueJungleTimelineAggregate
    {
        internal int Games;
        internal double TopRate, MidRate, BotRate;
        internal double Level3GankRate, Level4GankRate;
        internal double FirstDragonRate, AverageDragons, AverageVoidgrubs, AverageHeralds, AverageBarons;
        internal double? AverageFirstDragonSeconds;
        internal readonly Dictionary<string, int> FirstCamps = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    internal static class LeagueJungleTimelineAnalysis
    {
        private const int AnalysisMinutes = 14;
        private const int KillWeight = 5;
        private static readonly Tuple<int, int, string, string>[] Camps =
        {
            Tuple.Create(3830, 7880, "blue", "blue"), Tuple.Create(3800, 6440, "wolves", "blue"),
            Tuple.Create(7760, 4010, "red", "blue"), Tuple.Create(6970, 5460, "raptors", "blue"),
            Tuple.Create(10990, 7000, "blue", "red"), Tuple.Create(11020, 8440, "wolves", "red"),
            Tuple.Create(7060, 10870, "red", "red"), Tuple.Create(7850, 9420, "raptors", "red")
        };

        internal static LeagueJungleTimelineSample Parse(byte[] json, long gameId, int championId,
            int participantId, int teamId, int mapId, bool isJungler)
        {
            if (!isJungler || mapId != 11 || gameId <= 0 || participantId < 1 || participantId > 10 ||
                (teamId != 100 && teamId != 200) || json == null || json.Length == 0 || json.Length > 8 * 1024 * 1024) return null;
            Dictionary<string, object> root;
            try { root = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.DeserializeObject(Encoding.UTF8.GetString(json)) as Dictionary<string, object>; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            if (root == null) return null;
            var framesRoot = Object(root, "json") ?? root;
            var frames = Objects(framesRoot, "frames").Take(301).ToArray();
            if (frames.Length == 0 || frames.Length > 300) return null;
            var sample = new LeagueJungleTimelineSample { GameId = gameId, ChampionId = championId, TeamId = teamId };

            for (var minute = 1; minute < Math.Min(frames.Length, AnalysisMinutes + 1); minute++)
            {
                var position = Position(ParticipantFrame(frames[minute], participantId));
                if (position == null) continue;
                CountZone(sample, position.Item1, position.Item2, 1);
            }
            var first = frames.Length > 1 ? Position(ParticipantFrame(frames[1], participantId)) : null;
            if (first != null)
            {
                var nearest = Camps.OrderBy(c => (long)(c.Item1 - first.Item1) * (c.Item1 - first.Item1) +
                    (long)(c.Item2 - first.Item2) * (c.Item2 - first.Item2)).First();
                sample.FirstCamp = nearest.Item3;
                sample.FirstCampSide = nearest.Item4;
            }

            var earlyKill3 = false;
            var earlyKill4 = false;
            var firstDragonTeam = 0;
            foreach (var frame in frames)
            foreach (var entry in Objects(frame, "events").Take(400))
            {
                var kind = String(entry, "type");
                var timestamp = Long(entry, "timestamp");
                if (kind == "CHAMPION_KILL" && timestamp >= 0 && timestamp <= AnalysisMinutes * 60000L)
                {
                    var assists = Values(entry, "assistingParticipantIds").Select(v => ToInt(v)).ToArray();
                    if (Int(entry, "killerId") != participantId && !assists.Contains(participantId)) continue;
                    var pos = Position(entry);
                    if (pos == null) continue;
                    CountZone(sample, pos.Item1, pos.Item2, KillWeight);
                    var lane = GankLane(pos.Item1, pos.Item2);
                    if (lane == "top") sample.TopGanks++;
                    else if (lane == "mid") sample.MidGanks++;
                    else if (lane == "bot") sample.BotGanks++;
                    if (timestamp <= 180000L) earlyKill3 = true;
                    else if (timestamp <= 240000L) earlyKill4 = true;
                }
                else if (kind == "ELITE_MONSTER_KILL" && timestamp >= 0)
                {
                    var killerTeam = Int(entry, "killerTeamId");
                    if (killerTeam != 100 && killerTeam != 200)
                        killerTeam = Int(entry, "killerId") >= 1 && Int(entry, "killerId") <= 5 ? 100 : 200;
                    var ours = killerTeam == teamId;
                    var seconds = timestamp / 1000d;
                    switch (String(entry, "monsterType"))
                    {
                        case "DRAGON":
                            if (firstDragonTeam == 0) firstDragonTeam = killerTeam;
                            if (ours)
                            {
                                sample.Dragons++;
                                if (sample.FirstDragonSeconds == null) sample.FirstDragonSeconds = seconds;
                                if (Int(entry, "killerId") == participantId && !Values(entry, "assistingParticipantIds").Any()) sample.SoloDragons++;
                            }
                            break;
                        case "HORDE": if (ours) { sample.Voidgrubs++; if (sample.FirstVoidgrubSeconds == null) sample.FirstVoidgrubSeconds = seconds; } break;
                        case "RIFTHERALD": if (ours) { sample.Heralds++; if (sample.FirstHeraldSeconds == null) sample.FirstHeraldSeconds = seconds; } break;
                        case "BARON_NASHOR": if (ours) { sample.Barons++; if (sample.FirstBaronSeconds == null) sample.FirstBaronSeconds = seconds; } break;
                    }
                }
            }
            if (firstDragonTeam != 0) sample.FirstDragonWon = firstDragonTeam == teamId;

            if (frames.Length > 3)
            {
                var three = ParticipantFrame(frames[3], participantId);
                var cs = Int(three, "minionsKilled") + Int(three, "jungleMinionsKilled");
                var damage3 = Int(Object(three, "damageStats"), "totalDamageDoneToChampions");
                var detailed3 = Object(three, "damageStats") != null && Object(three, "championStats") != null;
                var hasDamage3 = detailed3 ? damage3 > 0 : earlyKill3;
                sample.Level3Gank = cs >= 12 && cs < 20 && Int(three, "level") == 3 && hasDamage3;
                if (three != null && frames.Length > 4)
                {
                    var four = ParticipantFrame(frames[4], participantId);
                    var damage4 = Object(four, "damageStats");
                    sample.Level4Gank = four != null && (damage4 != null && Object(four, "championStats") != null ?
                        Int(damage4, "totalDamageDoneToChampions") > damage3 || earlyKill4 : earlyKill4);
                }
            }
            return sample;
        }

        internal static LeagueJungleTimelineAggregate Aggregate(IEnumerable<LeagueJungleTimelineSample> samples)
        {
            var games = (samples ?? Enumerable.Empty<LeagueJungleTimelineSample>()).Where(s => s != null && s.GameId > 0)
                .GroupBy(s => s.GameId).Select(g => g.First()).Take(LeaguePlayerDataService.MaximumMatchCount).ToArray();
            if (games.Length == 0) return null;
            var result = new LeagueJungleTimelineAggregate { Games = games.Length };
            var totalWeight = games.Sum(g => g.TotalWeight);
            if (totalWeight > 0)
            {
                result.TopRate = games.Sum(g => g.TopWeight) / (double)totalWeight;
                result.MidRate = games.Sum(g => g.MidWeight) / (double)totalWeight;
                result.BotRate = games.Sum(g => g.BotWeight) / (double)totalWeight;
            }
            result.Level3GankRate = games.Count(g => g.Level3Gank) / (double)games.Length;
            result.Level4GankRate = games.Count(g => g.Level4Gank) / (double)games.Length;
            var dragonGames = games.Where(g => g.FirstDragonWon.HasValue).ToArray();
            result.FirstDragonRate = dragonGames.Length == 0 ? 0 : dragonGames.Count(g => g.FirstDragonWon.Value) / (double)dragonGames.Length;
            result.AverageDragons = games.Average(g => g.Dragons);
            result.AverageVoidgrubs = games.Average(g => g.Voidgrubs);
            result.AverageHeralds = games.Average(g => g.Heralds);
            result.AverageBarons = games.Average(g => g.Barons);
            var times = games.Where(g => g.FirstDragonSeconds.HasValue).Select(g => g.FirstDragonSeconds.Value).ToArray();
            result.AverageFirstDragonSeconds = times.Length == 0 ? (double?)null : times.Average();
            foreach (var game in games.Where(g => g.FirstCamp != null))
            {
                var side = game.TeamId == 100 ? "blue" : "red";
                var key = side + "/" + (game.FirstCampSide == side ? "normal" : "invade") + "/" + game.FirstCamp;
                int count;
                result.FirstCamps.TryGetValue(key, out count);
                result.FirstCamps[key] = count + 1;
            }
            return result;
        }

        private static void CountZone(LeagueJungleTimelineSample sample, int x, int y, int weight)
        {
            sample.TotalWeight += weight;
            var lane = MapZone(x, y);
            if (lane == "top") sample.TopWeight += weight;
            else if (lane == "bot") sample.BotWeight += weight;
            else sample.MidWeight += weight;
        }

        private static string MapZone(int x, int y)
        {
            if (x < 5000 && y > 9000) return "top";
            if (x > 9000 && y < 5000) return "bot";
            return Math.Abs(y - x) <= 3500 ? "mid" : y > x ? "top" : "bot";
        }

        private static string GankLane(int x, int y)
        {
            if (x < 5000 && y > 9000) return "top";
            if (x > 9000 && y < 5000) return "bot";
            return Math.Abs(y - x) < 4000 && (x + y) / 2d > 3000 && (x + y) / 2d < 12000 ? "mid" : null;
        }

        private static Dictionary<string, object> ParticipantFrame(Dictionary<string, object> frame, int participantId)
        {
            return Object(Object(frame, "participantFrames"), participantId.ToString(CultureInfo.InvariantCulture));
        }

        private static Tuple<int, int> Position(Dictionary<string, object> frame)
        {
            var pos = Object(frame, "position");
            if (pos == null || !pos.ContainsKey("x") || !pos.ContainsKey("y")) return null;
            var x = Int(pos, "x"); var y = Int(pos, "y");
            return x >= 0 && x <= 16000 && y >= 0 && y <= 16000 ? Tuple.Create(x, y) : null;
        }

        private static Dictionary<string, object> Object(Dictionary<string, object> node, string key)
        {
            object value;
            return node != null && node.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static IEnumerable<object> Values(Dictionary<string, object> node, string key)
        {
            object value;
            var list = node != null && node.TryGetValue(key, out value) ? value as IEnumerable : null;
            return list == null || list is string ? Enumerable.Empty<object>() : list.Cast<object>().Take(4096);
        }

        private static IEnumerable<Dictionary<string, object>> Objects(Dictionary<string, object> node, string key)
        {
            return Values(node, key).OfType<Dictionary<string, object>>();
        }

        private static string String(Dictionary<string, object> node, string key)
        {
            object value;
            return node != null && node.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        private static int Int(Dictionary<string, object> node, string key)
        {
            object value;
            return node != null && node.TryGetValue(key, out value) ? ToInt(value) : 0;
        }

        private static int ToInt(object value)
        {
            int result;
            return value != null && int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        private static long Long(Dictionary<string, object> node, string key)
        {
            object value;
            long result;
            return node != null && node.TryGetValue(key, out value) && value != null &&
                long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out result) ? result : -1;
        }
    }
}
