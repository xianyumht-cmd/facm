using System;
using System.Collections.Generic;
using System.Linq;

namespace FACM.League
{
    internal static class LeagueCherryTeams
    {
        internal static bool IsCherry(string mode)
        {
            return string.Equals(mode, "CHERRY", StringComparison.OrdinalIgnoreCase);
        }

        internal static int? ValidSubteamId(int? id)
        {
            return id > 0 && id <= 20 ? id : null;
        }

        internal static int? ValidPlacement(int? placement)
        {
            return placement > 0 && placement <= 20 ? placement : null;
        }

        internal static int GroupId(LeaguePlayerMatchParticipant participant, bool cherry)
        {
            return participant == null ? 0 : cherry ? participant.PlayerSubteamId.GetValueOrDefault() : participant.TeamId;
        }

        internal static int GroupId(LeaguePlayerMatchSummary participant, bool cherry)
        {
            return participant == null ? 0 : cherry
                ? ValidSubteamId(participant.PlayerSubteamId).GetValueOrDefault() : participant.TeamId;
        }

        internal static int? Placement(IEnumerable<LeaguePlayerMatchParticipant> players)
        {
            var placements = players.Select(player => player.SubteamPlacement).Distinct().ToArray();
            return placements.Length == 1 ? placements[0] : null;
        }
    }
}
