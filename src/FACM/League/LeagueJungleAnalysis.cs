using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueJungleAnalysis
    {
        internal readonly int[] ZoneWeights = new int[3];
        internal readonly int[] Ganks = new int[3];
        internal const int MaximumGankMapPoints = 128;
        internal readonly List<PointF> MinutePositions = new List<PointF>();
        internal readonly List<PointF> GankPositions = new List<PointF>();
        internal int OmittedGankMapPoints;
        internal int PositionSamples, AvailableSamples, MissingKillPositions;
        internal string StartCamp;
        internal bool? StartBlue, Level3Gank, Level4Gank;
        internal bool? GotFirstDragon;
        internal bool ObjectivesAvailable;
        internal int Dragons, SoloDragons, Voidgrubs, Heralds, Barons, UnknownObjectiveTeams;
        internal long? FirstDragonTime, FirstVoidgrubTime, FirstHeraldTime, FirstBaronTime;

        internal static int MapZone(long x, long y)
        {
            if (x < 5000 && y > 9000) return 0;
            if (x > 9000 && y < 5000) return 2;
            if (Math.Abs(y - x) <= 3500) return 1;
            return y > x ? 0 : 2;
        }

        internal static int? GankLane(long x, long y)
        {
            if (x < 5000 && y > 9000) return 0;
            if (x > 9000 && y < 5000) return 2;
            var midpoint = (x + y) / 2d;
            return Math.Abs(y - x) < 4000 && midpoint > 3000 && midpoint < 12000 ? (int?)1 : null;
        }

        private static readonly long[][] Camps = {
            new long[] { 3830, 7880, 0, 1 }, new long[] { 3800, 6440, 1, 1 },
            new long[] { 7760, 4010, 2, 1 }, new long[] { 6970, 5460, 3, 1 },
            new long[] { 10990, 7000, 0, 0 }, new long[] { 11020, 8440, 1, 0 },
            new long[] { 7060, 10870, 2, 0 }, new long[] { 7850, 9420, 3, 0 }
        };

        internal static string NearestCamp(long x, long y, out bool blue)
        {
            var nearest = Camps.OrderBy(c => (x - c[0]) * (double)(x - c[0]) +
                (y - c[1]) * (double)(y - c[1])).First();
            blue = nearest[3] == 1;
            return new[] { "blue", "wolves", "red", "raptors" }[(int)nearest[2]];
        }

        private static Dictionary<string, object> Object(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static bool Position(Dictionary<string, object> source, out long x, out long y)
        {
            var position = Object(source, "position");
            var px = LeagueMatchTimelineParser.Number(position, "x");
            var py = LeagueMatchTimelineParser.Number(position, "y");
            x = px ?? 0; y = py ?? 0;
            return px.HasValue && py.HasValue && x <= 20000 && y <= 20000;
        }

        private static Dictionary<string, object> Frame(LeagueMatchTimeline timeline, int index, int player)
        {
            Dictionary<string, object> frame;
            return index < timeline.Frames.Count && timeline.Frames[index].Participants.TryGetValue(player, out frame)
                ? frame : null;
        }

        private static bool Detailed(Dictionary<string, object> frame)
        {
            return frame != null && frame.ContainsKey("damageStats") && frame.ContainsKey("championStats");
        }

        internal static LeagueJungleAnalysis Calculate(LeagueMatchTimeline timeline, LeaguePlayerMatchSummary player)
        {
            if (timeline == null || timeline.Frames.Count == 0 || player == null || timeline.GameId != player.GameId ||
                player.MapId != 11 || player.ParticipantId <= 0 ||
                !(string.Equals(player.Position, "JUNGLE", StringComparison.OrdinalIgnoreCase) ||
                    player.HasSmite || (player.SpellIds != null && player.SpellIds.Contains(11)))) return null;
            var result = new LeagueJungleAnalysis();
            result.ReadObjectives(timeline, player);
            var limit = Math.Min(timeline.Frames.Count, 15);
            result.AvailableSamples = Math.Max(0, limit - 1);
            for (var i = 1; i < limit; i++)
            {
                long x, y;
                if (!Position(Frame(timeline, i, player.ParticipantId), out x, out y)) continue;
                result.PositionSamples++;
                result.MinutePositions.Add(new PointF(x, y));
                result.ZoneWeights[MapZone(x, y)]++;
            }
            long firstX, firstY;
            if (Position(Frame(timeline, 1, player.ParticipantId), out firstX, out firstY))
            {
                bool blue;
                result.StartCamp = NearestCamp(firstX, firstY, out blue);
                result.StartBlue = blue;
            }
            var early3 = 0; var early4 = 0;
            var missing3 = false; var missing4 = false;
            foreach (var entry in timeline.Events)
            {
                if (entry.Type != "CHAMPION_KILL" || entry.Timestamp > 840000 ||
                    (entry.KillerId != player.ParticipantId &&
                    (entry.Assists == null || !entry.Assists.Contains(player.ParticipantId)))) continue;
                long x, y;
                if (!Position(entry.Data, out x, out y))
                {
                    result.MissingKillPositions++;
                    if (entry.Timestamp <= 180000) missing3 = true;
                    else if (entry.Timestamp <= 240000) missing4 = true;
                    continue;
                }
                result.ZoneWeights[MapZone(x, y)] += 5;
                var lane = GankLane(x, y);
                if (lane.HasValue)
                {
                    result.Ganks[lane.Value]++;
                    if (result.GankPositions.Count < MaximumGankMapPoints)
                        result.GankPositions.Add(new PointF(x, y));
                    else result.OmittedGankMapPoints++;
                }
                if (entry.Timestamp <= 180000) early3++;
                else if (entry.Timestamp <= 240000) early4++;
            }
            var pf3 = Frame(timeline, 3, player.ParticipantId);
            var pf4 = Frame(timeline, 4, player.ParticipantId);
            if (pf3 != null)
            {
                var lane = LeagueMatchTimelineParser.Number(pf3, "minionsKilled");
                var jungle = LeagueMatchTimelineParser.Number(pf3, "jungleMinionsKilled");
                var level = LeagueMatchTimelineParser.Number(pf3, "level");
                var damage3 = Detailed(pf3)
                    ? LeagueMatchTimelineParser.Number(Object(pf3, "damageStats"), "totalDamageDoneToChampions")
                    : (long?)0;
                bool? combat3 = Detailed(pf3) ? (damage3.HasValue ? (bool?)(damage3 > 0) : null)
                    : early3 > 0 ? true : missing3 ? (bool?)null : false;
                if (lane.HasValue && jungle.HasValue && level.HasValue && combat3.HasValue)
                    result.Level3Gank = lane.Value + (double)jungle.Value >= 12 &&
                        lane.Value + (double)jungle.Value < 20 && level == 3 && combat3.Value;
                if (pf4 != null)
                {
                    if (Detailed(pf4))
                    {
                        var damage4 = LeagueMatchTimelineParser.Number(Object(pf4, "damageStats"), "totalDamageDoneToChampions");
                        if (early4 > 0) result.Level4Gank = true;
                        else if (damage3.HasValue && damage4.HasValue)
                            result.Level4Gank = damage4 > damage3 ? true : missing4 ? (bool?)null : false;
                    }
                    else result.Level4Gank = early4 > 0 ? true : missing4 ? (bool?)null : false;
                }
            }
            return result;
        }

        private void ReadObjectives(LeagueMatchTimeline timeline, LeaguePlayerMatchSummary player)
        {
            if (player.TeamId != 100 && player.TeamId != 200) return;
            ObjectivesAvailable = true;
            bool dragonSeen = false;
            foreach (var entry in timeline.Events)
            {
                if (entry.Type != "ELITE_MONSTER_KILL") continue;
                var monster = LeagueMatchTimelineParser.Text(entry.Data, "monsterType");
                if (monster != "DRAGON" && monster != "HORDE" &&
                    monster != "RIFTHERALD" && monster != "BARON_NASHOR") continue;
                var team = LeagueMatchTimelineParser.Number(entry.Data, "killerTeamId");
                if (!team.HasValue && entry.KillerId >= 1 && entry.KillerId <= 10)
                    team = entry.KillerId <= 5 ? 100 : 200;
                var knownTeam = team == 100 || team == 200;
                if (!knownTeam) UnknownObjectiveTeams++;
                if (monster == "DRAGON" && !dragonSeen)
                {
                    dragonSeen = true;
                    GotFirstDragon = knownTeam ? (bool?)(team == player.TeamId) : null;
                }
                if (!knownTeam || team != player.TeamId) continue;
                switch (monster)
                {
                    case "DRAGON":
                        Dragons++;
                        if (entry.KillerId == player.ParticipantId &&
                            (entry.Assists == null || entry.Assists.Length == 0)) SoloDragons++;
                        if (!FirstDragonTime.HasValue) FirstDragonTime = entry.Timestamp;
                        break;
                    case "HORDE":
                        Voidgrubs++;
                        if (!FirstVoidgrubTime.HasValue) FirstVoidgrubTime = entry.Timestamp;
                        break;
                    case "RIFTHERALD":
                        Heralds++;
                        if (!FirstHeraldTime.HasValue) FirstHeraldTime = entry.Timestamp;
                        break;
                    case "BARON_NASHOR":
                        Barons++;
                        if (!FirstBaronTime.HasValue) FirstBaronTime = entry.Timestamp;
                        break;
                }
            }
        }

        internal string Report(UiTextCatalog ui)
        {
            Func<bool?, string> boolean = value => !value.HasValue ? "--" :
                ui.Get(value.Value ? UiTextKeys.LeagueJungleDetected : UiTextKeys.LeagueJungleNotDetected);
            var total = ZoneWeights.Sum();
            var campKey = StartCamp == "blue" ? UiTextKeys.LeagueJungleCampBlue :
                StartCamp == "wolves" ? UiTextKeys.LeagueJungleCampWolves :
                StartCamp == "red" ? UiTextKeys.LeagueJungleCampRed : UiTextKeys.LeagueJungleCampRaptors;
            var camp = StartCamp == null ? "--" : ui.Get(StartBlue == true ?
                UiTextKeys.LeagueJungleBlueSide : UiTextKeys.LeagueJungleRedSide) + " · " + ui.Get(campKey);
            var lines = new List<string> { ui.Get(UiTextKeys.LeagueJungleScope), "",
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleCoverage),
                    PositionSamples, AvailableSamples, MissingKillPositions),
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleFirstCamp), camp),
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleEarly),
                    boolean(Level3Gank), boolean(Level4Gank)), "" };
            var zones = new[] { UiTextKeys.LeaguePlayerAnalysisPositionTop,
                UiTextKeys.LeaguePlayerAnalysisPositionMiddle, UiTextKeys.LeaguePlayerAnalysisPositionBottom };
            for (var i = 0; i < zones.Length; i++)
                lines.Add(string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleZone),
                    ui.Get(zones[i]), total == 0 ? "--" :
                        (100d * ZoneWeights[i] / total).ToString("0.0", CultureInfo.CurrentCulture) + "%",
                    Ganks[i]));
            lines.Add("");
            lines.Add(ui.Get(UiTextKeys.LeagueJungleObjectivesScope));
            if (!ObjectivesAvailable)
            { lines.Add("--"); return string.Join(Environment.NewLine, lines); }
            lines.Add(string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleFirstDragon),
                boolean(GotFirstDragon), SoloDragons, Dragons, UnknownObjectiveTeams));
            var names = new[] { UiTextKeys.LeagueJungleDragons, UiTextKeys.LeagueJungleGrubs,
                UiTextKeys.LeagueJungleHeralds, UiTextKeys.LeagueJungleBarons };
            var counts = new[] { Dragons, Voidgrubs, Heralds, Barons };
            var times = new[] { FirstDragonTime, FirstVoidgrubTime, FirstHeraldTime, FirstBaronTime };
            for (var i = 0; i < names.Length; i++)
                lines.Add(string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeagueJungleObjectiveRow),
                    ui.Get(names[i]), counts[i], times[i].HasValue
                        ? LeagueMatchTimelineParser.Time(times[i].Value) : "--"));
            return string.Join(Environment.NewLine, lines);
        }
    }
}
