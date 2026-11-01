using System;
using System.Collections.Generic;
using System.Linq;

namespace FACM.League
{
    // Read-only draft from one verified historical match; never writes to LCU or a live game.
    internal static class LeagueHistoricalMatchDryRun
    {
        internal static IReadOnlyDictionary<string, LeaguePremadeTeamAggregate> TeamAggregates(
            LeagueDynamicMessageSnapshot snapshot, int threshold = 5)
        {
            var result = new Dictionary<string, LeaguePremadeTeamAggregate>(StringComparer.Ordinal);
            if (snapshot == null || snapshot.Phase != "HistoricalPreview" ||
                threshold < 2 || threshold > LeaguePlayerDataService.MaximumMatchCount) return result;
            var players = snapshot.Players.Where(p => p != null && p.Player != null &&
                LeagueDynamicMessageService.SafePuuid(p.Player.PuuId))
                .GroupBy(p => p.Player.PuuId, StringComparer.Ordinal)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First(),
                    StringComparer.Ordinal);
            foreach (var group in LeaguePremadeInference.Infer(snapshot, threshold))
            {
                if (group.Any(id => !players.ContainsKey(id) ||
                    players[id].HistoricalTeamId != players[group[0]].HistoricalTeamId)) continue;
                var aggregate = LeaguePremadeTeamAggregate.Project(group.Select(id => players[id]).ToArray());
                if (aggregate == null || aggregate.Games <= 0) continue;
                foreach (var id in group) result[id] = aggregate;
            }
            return result;
        }

        internal static LeagueDynamicMessageSnapshot Build(LeaguePlayerMatchSummary match,
            IEnumerable<LeaguePlayerMatchParticipant> participants, string targetPuuid)
        {
            if (match == null || match.GameId <= 0 ||
                !(match.MapId == 11 && string.Equals(match.GameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase) ||
                  match.MapId == 30 && LeagueCherryTeams.IsCherry(match.GameMode)) ||
                !LeagueDynamicMessageService.SafePuuid(targetPuuid) ||
                !string.Equals(match.PuuId, targetPuuid, StringComparison.Ordinal) ||
                participants == null) return null;
            var cherry = LeagueCherryTeams.IsCherry(match.GameMode);
            if (!cherry && match.TeamId != 100 && match.TeamId != 200) return null;
            var roster = participants.ToList();
            if (roster.Count != (cherry ? 16 : 10) || roster.Any(p => p == null ||
                p.ParticipantId < 1 || p.ParticipantId > (cherry ? 20 : 10) ||
                (cherry ? !LeagueCherryTeams.ValidSubteamId(p.PlayerSubteamId).HasValue :
                    p.TeamId != 100 && p.TeamId != 200) ||
                !LeagueDynamicMessageService.SafePuuid(p.PuuId) ||
                p.ChampionId <= 0) ||
                roster.Select(p => p.ParticipantId).Distinct().Count() != roster.Count ||
                roster.Select(p => p.PuuId).Distinct(StringComparer.Ordinal).Count() != roster.Count)
                return null;
            var target = roster.SingleOrDefault(p => string.Equals(p.PuuId, targetPuuid, StringComparison.Ordinal));
            if (target == null) return null;
            if (cherry)
            {
                var teams = roster.GroupBy(p => p.PlayerSubteamId.Value).ToArray();
                if (teams.Length != 8 || teams.Any(g => g.Count() != 2 ||
                    g.Any(p => p.SubteamPlacement.HasValue &&
                        !LeagueCherryTeams.ValidPlacement(p.SubteamPlacement).HasValue) ||
                    g.Select(p => p.SubteamPlacement).Where(value => value.HasValue).Distinct().Count() > 1) ||
                    match.PlayerSubteamId.HasValue && match.PlayerSubteamId != target.PlayerSubteamId)
                    return null;
            }
            else if (roster.Count(p => p.TeamId == 100) != 5 ||
                roster.Count(p => p.TeamId == 200) != 5 ||
                target.TeamId != match.TeamId) return null;

            var snapshot = new LeagueDynamicMessageSnapshot {
                Phase = "HistoricalPreview", SelfPuuid = targetPuuid,
                HistoricalGameMode = cherry ? "CHERRY" : "CLASSIC",
                RosterKey = "historical:" + match.GameId,
                CreatedUtc = DateTime.UtcNow
            };
            foreach (var participant in roster.OrderBy(p => p.ParticipantId))
            {
                var name = new string((participant.PlayerName ?? string.Empty)
                    .Where(c => !char.IsControl(c)).Take(64).ToArray()).Trim();
                snapshot.Players.Add(new LeagueDynamicMessagePlayer {
                    Friendly = cherry ? participant.PlayerSubteamId == target.PlayerSubteamId :
                        participant.TeamId == match.TeamId,
                    HistoricalTeamId = cherry ? participant.PlayerSubteamId.Value : participant.TeamId,
                    ChampionName = string.IsNullOrWhiteSpace(participant.ChampionName)
                        ? "#" + participant.ChampionId : participant.ChampionName,
                    Player = new LeagueLivePlayerRow {
                        PuuId = participant.PuuId,
                        CellId = participant.ParticipantId,
                        Side = (cherry ? participant.PlayerSubteamId == target.PlayerSubteamId :
                            participant.TeamId == match.TeamId) ? "ally" : "enemy",
                        GameName = string.IsNullOrWhiteSpace(name)
                            ? "#" + participant.ParticipantId : name,
                        ChampionId = participant.ChampionId,
                        Position = participant.Position,
                        Spell1Id = participant.SpellIds != null && participant.SpellIds.Length > 0
                            ? participant.SpellIds[0] : 0,
                        Spell2Id = participant.SpellIds != null && participant.SpellIds.Length > 1
                            ? participant.SpellIds[1] : 0
                    }
                });
            }
            return snapshot;
        }
    }
}
