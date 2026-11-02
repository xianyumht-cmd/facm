using System;
using System.Collections.Generic;
using System.Globalization;
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
        // The selected player's champion/role, not any participant in the match.
        internal int SubjectChampionId;
        internal string SubjectPosition;
        internal string[] SubjectPositions;
        private static readonly string[] SupportedPositions =
            { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };

        internal static bool TryNormalizePositions(IEnumerable<string> requested, out string[] positions)
        {
            positions = null;
            if (requested == null) return false;
            var selected = requested.ToArray();
            if (selected.Length > SupportedPositions.Length ||
                selected.Any(role => Array.IndexOf(SupportedPositions, role) < 0) ||
                selected.Distinct(StringComparer.Ordinal).Count() != selected.Length)
                return false;
            positions = selected;
            return true;
        }

        internal static bool ShouldApplySgpPositions(LeaguePlayerHistorySource source,
            string[] positions)
        {
            return source == LeaguePlayerHistorySource.Sgp &&
                positions != null && positions.Length > 0;
        }
        // Empty = all; NORMAL = known non-Arena match; CHERRY = Arena.
        internal string SubjectMode;

        internal static bool TryParseParticipantFilters(string championCsv, string puuidCsv,
            out int[] championIds, out string[] playerPuuids)
        {
            championIds = null;
            playerPuuids = null;
            if ((championCsv ?? string.Empty).Length > 512 || (puuidCsv ?? string.Empty).Length > 2048)
                return false;
            var champions = string.IsNullOrWhiteSpace(championCsv) ? new string[0] : championCsv.Split(',');
            var players = string.IsNullOrWhiteSpace(puuidCsv) ? new string[0] : puuidCsv.Split(',');
            if (champions.Length > 10 || players.Length > 10) return false;
            var parsedChampions = new List<int>();
            var parsedPlayers = new List<string>();
            foreach (var raw in champions)
            {
                var token = raw.Trim();
                int id;
                if (token.Length == 0 || !int.TryParse(token, NumberStyles.None,
                    CultureInfo.InvariantCulture, out id) || id <= 0 || id > 10000 ||
                    parsedChampions.Contains(id)) return false;
                parsedChampions.Add(id);
            }
            foreach (var raw in players)
            {
                var token = raw.Trim();
                if (!LeagueSgpMatchHistoryApiClient.SafePuuid(token) ||
                    parsedPlayers.Contains(token, StringComparer.Ordinal)) return false;
                parsedPlayers.Add(token);
            }
            championIds = parsedChampions.ToArray();
            playerPuuids = parsedPlayers.ToArray();
            return true;
        }

        internal bool Matches(LeaguePlayerMatchSummary match, DateTime nowUtc)
        {
            if (match == null) return false;
            if (WinLoss != LeagueMatchWinLossFilter.All)
            {
                if (!match.ParticipantResolved || !match.StatisticsAvailable) return false;
                if (!string.Equals(match.WinResult,
                    WinLoss == LeagueMatchWinLossFilter.Win ? "win" : "loss",
                    StringComparison.OrdinalIgnoreCase)) return false;
            }

            var range = ResolveRange(TimeRange);
            if (range.HasValue)
            {
                if (match.GameCreationLocal == DateTime.MinValue) return false;
                var createdUtc = match.GameCreationLocal.ToUniversalTime();
                if (createdUtc < nowUtc - range.Value || createdUtc > nowUtc) return false;
            }

            if (!string.IsNullOrEmpty(SubjectMode))
            {
                if ((SubjectMode != "NORMAL" && SubjectMode != "CHERRY") ||
                    string.IsNullOrWhiteSpace(match.GameMode) ||
                    string.Equals(match.GameMode, "PRACTICETOOL", StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(match.GameType) &&
                     !string.Equals(match.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase)) ||
                    (SubjectMode == "CHERRY") !=
                    string.Equals(match.GameMode, "CHERRY", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            if (SubjectPositions != null && SubjectPositions.Length > 0 &&
                (SubjectPositions.Length > SupportedPositions.Length ||
                 SubjectPositions.Any(role => Array.IndexOf(SupportedPositions, role) < 0) ||
                 !match.ParticipantResolved || string.IsNullOrEmpty(match.Position) ||
                 !SubjectPositions.Contains(match.Position, StringComparer.Ordinal)))
                return false;
            if (SubjectChampionId > 0 && match.ChampionId != SubjectChampionId) return false;
            if (!string.IsNullOrEmpty(SubjectPosition) &&
                !string.Equals(match.Position, SubjectPosition, StringComparison.OrdinalIgnoreCase))
                return false;

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
