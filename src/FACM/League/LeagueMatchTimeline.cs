using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal sealed class LeagueMatchTimeline
    {
        internal long GameId;
        internal readonly List<LeagueMatchTimelineFrame> Frames = new List<LeagueMatchTimelineFrame>();
        internal readonly List<LeagueMatchTimelineEvent> Events = new List<LeagueMatchTimelineEvent>();
    }

    internal sealed class LeagueMatchTimelineFrame
    {
        internal long Timestamp;
        internal readonly Dictionary<int, Dictionary<string, object>> Participants =
            new Dictionary<int, Dictionary<string, object>>();
    }

    internal sealed class LeagueMatchTimelineEvent
    {
        internal long Timestamp;
        internal string Type;
        internal int ParticipantId, KillerId, VictimId, CreatorId;
        internal int[] Assists;
        internal Dictionary<string, object> Data;

        internal bool Involves(int participantId)
        {
            return participantId <= 0 || ParticipantId == participantId || KillerId == participantId ||
                VictimId == participantId || CreatorId == participantId || (Assists != null && Assists.Contains(participantId));
        }
    }

    internal static class LeagueMatchTimelineParser
    {
        internal static LeagueMatchTimeline Parse(byte[] bytes, long gameId, bool sgp)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 16 * 1024 * 1024 || gameId <= 0) return null;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 64 };
                var root = serializer.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                if (sgp) root = Object(root, "json");
                if (root == null || (sgp && Long(root, "gameId") != gameId) ||
                    (root.ContainsKey("gameId") && Long(root, "gameId") != gameId)) return null;
                var frames = Array(root, "frames");
                if (frames == null || frames.Length > 600) return null;
                var timeline = new LeagueMatchTimeline { GameId = gameId };
                long previous = -1;
                foreach (var value in frames)
                {
                    var source = value as Dictionary<string, object>;
                    var timestamp = Number(source, "timestamp");
                    if (!timestamp.HasValue || timestamp.Value < previous) return null;
                    previous = timestamp.Value;
                    var frame = new LeagueMatchTimelineFrame { Timestamp = timestamp.Value };
                    var participants = Object(source, "participantFrames");
                    if (participants != null)
                    {
                        if (participants.Count > 20) return null;
                        foreach (var pair in participants)
                        {
                            int id;
                            var data = pair.Value as Dictionary<string, object>;
                            if (!int.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out id) ||
                                id < 1 || id > 20 || data == null ||
                                (data.ContainsKey("participantId") && Long(data, "participantId") != id)) return null;
                            frame.Participants.Add(id, data);
                        }
                    }
                    var events = Array(source, "events");
                    if (events == null) return null;
                    foreach (var entry in events)
                    {
                        if (timeline.Events.Count >= 10000) return null;
                        var data = entry as Dictionary<string, object>;
                        var time = Number(data, "timestamp");
                        var type = Text(data, "type");
                        if (time == null || string.IsNullOrEmpty(type) || type.Length > 80) return null;
                        var assists = Array(data, "assistingParticipantIds") ?? new object[0];
                        if (assists.Length > 20) return null;
                        timeline.Events.Add(new LeagueMatchTimelineEvent { Timestamp = time.Value, Type = type,
                            ParticipantId = Id(data, "participantId"), KillerId = Id(data, "killerId"),
                            VictimId = Id(data, "victimId"), CreatorId = Id(data, "creatorId"),
                            Assists = assists.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture))
                                .Select(a => int.TryParse(a, out var id) && id > 0 && id <= 20 ? id : 0)
                                .Where(id => id > 0).Distinct().ToArray(), Data = data });
                    }
                    timeline.Frames.Add(frame);
                }
                var orderedEvents = timeline.Events.OrderBy(entry => entry.Timestamp).ToArray();
                timeline.Events.Clear();
                timeline.Events.AddRange(orderedEvents);
                return timeline;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        internal static string Time(long milliseconds)
        {
            var seconds = Math.Max(0, milliseconds / 1000);
            return (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("D2");
        }

        internal static long? Number(Dictionary<string, object> data, string key)
        {
            object value;
            long number;
            return data != null && data.TryGetValue(key, out value) && value != null &&
                long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out number) && number >= 0
                ? (long?)number : null;
        }

        internal static string Text(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value as string : null;
        }

        private static int Id(Dictionary<string, object> data, string key)
        {
            var number = Long(data, key);
            return number > 0 && number <= 20 ? (int)number : 0;
        }
        private static long Long(Dictionary<string, object> data, string key) { return Number(data, key) ?? -1; }
        private static Dictionary<string, object> Object(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }
        private static object[] Array(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value as object[] : null;
        }
    }
}
