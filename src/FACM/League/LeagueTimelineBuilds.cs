using System;
using System.Collections.Generic;

namespace FACM.League
{
    internal sealed class LeagueTimelineBuildStep
    {
        internal LeagueMatchTimelineEvent Event;
        internal int PurchaseGroup;
        internal int? SkillLevel;
        internal bool IsEvolution;
    }

    internal sealed class LeagueTimelineBuilds
    {
        internal readonly List<LeagueTimelineBuildStep> Steps = new List<LeagueTimelineBuildStep>();
        internal readonly Dictionary<int, int> Anvils = new Dictionary<int, int>();

        internal static LeagueTimelineBuilds Project(LeagueMatchTimeline timeline)
        {
            var result = new LeagueTimelineBuilds();
            if (timeline == null) return result;
            var lastPurchase = new Dictionary<int, long>();
            var groups = new Dictionary<int, int>();
            var levels = new Dictionary<int, int>();
            foreach (var entry in timeline.Events)
            {
                var id = entry.ParticipantId;
                if (id <= 0) continue;
                if (entry.Type == "ITEM_PURCHASED")
                {
                    long previous;
                    int group;
                    lastPurchase.TryGetValue(id, out previous);
                    groups.TryGetValue(id, out group);
                    if (group == 0) group = 1;
                    else if (previous != 0 && entry.Timestamp - previous > 30000) group++;
                    lastPurchase[id] = entry.Timestamp;
                    groups[id] = group;
                    var item = LeagueMatchTimelineParser.Number(entry.Data, "itemId");
                    if (item == 6032 || item == 220000)
                    {
                        int count;
                        result.Anvils.TryGetValue(id, out count);
                        result.Anvils[id] = count + 1;
                    }
                    result.Steps.Add(new LeagueTimelineBuildStep { Event = entry, PurchaseGroup = group });
                }
                else if (entry.Type == "SKILL_LEVEL_UP")
                {
                    var evolution = string.Equals(LeagueMatchTimelineParser.Text(entry.Data, "levelUpType"),
                        "EVOLVE", StringComparison.Ordinal);
                    int level;
                    levels.TryGetValue(id, out level);
                    if (!evolution) levels[id] = ++level;
                    result.Steps.Add(new LeagueTimelineBuildStep { Event = entry,
                        IsEvolution = evolution, SkillLevel = evolution ? (int?)null : level });
                }
            }
            return result;
        }
    }
}
