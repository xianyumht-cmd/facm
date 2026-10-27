using System;
using System.Collections.Generic;
using System.Linq;

namespace FACM.League
{
    internal sealed class LeagueSavedPlayerOverviewRecord
    {
        public string Puuid { get; set; }
        public string SelfPuuid { get; set; }
        public string Region { get; set; }
        public string PlatformId { get; set; }
        public string Tag { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime? LastMetUtc { get; set; }
        public int EncounterCount { get; set; }
    }

    internal static class LeagueSavedPlayerOverview
    {
        // Materializes Akari's SavedPlayers-style index from existing independent local stores.
        // The composite key keeps identical target PUUIDs from other accounts/regions distinct.
        internal static IReadOnlyList<LeagueSavedPlayerOverviewRecord> Build(
            IEnumerable<LeaguePlayerTagRecord> tags, IEnumerable<LeagueEncounterRecord> encounters)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            if (encounters == null) throw new ArgumentNullException(nameof(encounters));
            var index = new Dictionary<Tuple<string, string, string, string>, LeagueSavedPlayerOverviewRecord>();
            foreach (var tag in tags)
            {
                if (tag == null) continue;
                var key = Key(tag.SelfPuuid, tag.Region, tag.PlatformId, tag.Puuid);
                if (!index.TryGetValue(key, out var record))
                    index.Add(key, record = NewRecord(key));
                record.Tag = tag.Tag;
                if (tag.UpdatedUtc > record.UpdatedUtc) record.UpdatedUtc = tag.UpdatedUtc;
            }
            foreach (var encounter in encounters)
            {
                if (encounter == null) continue;
                var key = Key(encounter.SelfPuuid, encounter.Region, encounter.PlatformId, encounter.Puuid);
                if (!index.TryGetValue(key, out var record))
                    index.Add(key, record = NewRecord(key));
                record.EncounterCount++;
                if (encounter.UpdatedUtc > record.UpdatedUtc) record.UpdatedUtc = encounter.UpdatedUtc;
                if (!record.LastMetUtc.HasValue || encounter.UpdatedUtc > record.LastMetUtc.Value)
                    record.LastMetUtc = encounter.UpdatedUtc;
            }
            return index.Values.OrderByDescending(p => p.UpdatedUtc)
                .ThenBy(p => p.SelfPuuid, StringComparer.Ordinal)
                .ThenBy(p => p.Puuid, StringComparer.Ordinal).ToArray();
        }

        private static Tuple<string, string, string, string> Key(string self, string region,
            string platform, string target)
        {
            return Tuple.Create(self ?? "", (region ?? "").Trim().ToUpperInvariant(),
                (platform ?? "").Trim().ToUpperInvariant(), target ?? "");
        }

        private static LeagueSavedPlayerOverviewRecord NewRecord(Tuple<string, string, string, string> key)
        {
            return new LeagueSavedPlayerOverviewRecord
            {
                SelfPuuid = key.Item1, Region = key.Item2, PlatformId = key.Item3, Puuid = key.Item4
            };
        }
    }
}
