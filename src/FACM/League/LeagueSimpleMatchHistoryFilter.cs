using System;
using System.Collections.Generic;
using System.Linq;

namespace FACM.League
{
    internal enum LeagueMatchWinLossFilter
    {
        All,
        Win,
        Loss
    }

    internal enum LeagueMatchTimeRangeFilter
    {
        All,
        Last3Hours,
        Last12Hours,
        Last24Hours,
        Last3Days,
        Last7Days,
        Last30Days
    }

    internal sealed class LeagueSimpleMatchHistoryFilter
    {
        internal LeagueMatchWinLossFilter WinLoss;
        internal LeagueMatchTimeRangeFilter TimeRange;
        internal int[] ChampionIds;
        internal string[] PlayerPuuids;

        internal bool Matches(LeaguePlayerMatchSummary match, DateTime nowUtc)
        {
            if (match == null) return false;
            if (WinLoss != LeagueMatchWinLossFilter.All)
            {
                if (!match.ParticipantResolved || !match.StatisticsAvailable) return false;
                if (!string.IsNullOrWhiteSpace(match.WinResult) &&
                    !string.Equals(match.WinResult, WinLoss == LeagueMatchWinLossFilter.Win ? "win" : "loss",
                        StringComparison.OrdinalIgnoreCase)) return false;
                if (string.IsNullOrWhiteSpace(match.WinResult) &&
                    match.Win != (WinLoss == LeagueMatchWinLossFilter.Win)) return false;
            }

            var range = ResolveRange(TimeRange);
            if (range.HasValue)
            {
                if (match.GameCreationLocal == DateTime.MinValue) return false;
                var createdUtc = match.GameCreationLocal.ToUniversalTime();
                if (createdUtc < nowUtc - range.Value || createdUtc > nowUtc) return false;
            }

            if (ChampionIds != null && ChampionIds.Length > 0)
            {
                if (match.MatchChampionIds == null) return false;
                foreach (var id in ChampionIds)
                    if (id <= 0 || Array.IndexOf(match.MatchChampionIds, id) < 0) return false;
            }
            if (PlayerPuuids != null && PlayerPuuids.Length > 0)
            {
                if (match.MatchPlayers == null) return false;
                foreach (var puuid in PlayerPuuids)
                    if (string.IsNullOrWhiteSpace(puuid) || !match.MatchPlayers.Any(player =>
                        player != null && string.Equals(player.PuuId, puuid, StringComparison.Ordinal)))
                        return false;
            }
            return true;
        }

        internal static List<LeaguePlayerMatchSummary> Apply(
            IEnumerable<LeaguePlayerMatchSummary> matches, LeagueSimpleMatchHistoryFilter filter, DateTime nowUtc)
        {
            if (matches == null) return new List<LeaguePlayerMatchSummary>();
            if (filter == null) filter = new LeagueSimpleMatchHistoryFilter();
            return matches.Where(m => filter.Matches(m, nowUtc)).ToList();
        }

        private static TimeSpan? ResolveRange(LeagueMatchTimeRangeFilter range)
        {
            switch (range)
            {
                case LeagueMatchTimeRangeFilter.Last3Hours: return TimeSpan.FromHours(3);
                case LeagueMatchTimeRangeFilter.Last12Hours: return TimeSpan.FromHours(12);
                case LeagueMatchTimeRangeFilter.Last24Hours: return TimeSpan.FromDays(1);
                case LeagueMatchTimeRangeFilter.Last3Days: return TimeSpan.FromDays(3);
                case LeagueMatchTimeRangeFilter.Last7Days: return TimeSpan.FromDays(7);
                case LeagueMatchTimeRangeFilter.Last30Days: return TimeSpan.FromDays(30);
                default: return null;
            }
        }
    }
}
