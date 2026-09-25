using System;

namespace FACM.League
{
    internal sealed class LeaguePersonalStatsViewSnapshot
    {
        public int PlayedAccounts { get; set; }
        public int ActiveDays { get; set; }
        public DateTimeOffset? FirstSeenUtc { get; set; }
        public DateTimeOffset? LastSeenUtc { get; set; }
        public bool PersonalStatsEnabled { get; set; }
        public bool CloudRankingEnabled { get; set; }
        public long CloudRank { get; set; }
        public long CloudRankedUsers { get; set; }
        public double CloudPercentile { get; set; }
        public long CloudPlayedAccounts { get; set; }
    }
}
