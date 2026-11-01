using System;
using System.Collections.Generic;
using System.Linq;

namespace FACM.League
{
    // Read-only draft from one verified historical match; never writes to LCU or a live game.
    internal static class LeagueHistoricalMatchDryRun
    {
        internal static LeagueDynamicMessageSnapshot Build(LeaguePlayerMatchSummary match,
            IEnumerable<LeaguePlayerMatchParticipant> participants, string targetPuuid)
        {
            if (match == null || match.GameId <= 0 || match.MapId != 11 ||
                !string.Equals(match.GameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase) ||
                (match.TeamId != 100 && match.TeamId != 200) ||
                !LeagueDynamicMessageService.SafePuuid(targetPuuid) ||
                !string.Equals(match.PuuId, targetPuuid, StringComparison.Ordinal) ||
                participants == null) return null;
            var roster = participants.ToList();
            if (roster.Count != 10 || roster.Any(p => p == null ||
                p.ParticipantId < 1 || p.ParticipantId > 10 ||
                (p.TeamId != 100 && p.TeamId != 200) ||
                !LeagueDynamicMessageService.SafePuuid(p.PuuId) ||
                p.ChampionId <= 0) ||
                roster.Select(p => p.ParticipantId).Distinct().Count() != 10 ||
                roster.Select(p => p.PuuId).Distinct(StringComparer.Ordinal).Count() != 10 ||
                roster.Count(p => p.TeamId == 100) != 5 ||
                roster.Count(p => p.TeamId == 200) != 5 ||
                roster.Count(p => string.Equals(p.PuuId, targetPuuid, StringComparison.Ordinal) &&
                    p.TeamId == match.TeamId) != 1) return null;

            var snapshot = new LeagueDynamicMessageSnapshot {
                Phase = "HistoricalPreview", SelfPuuid = targetPuuid,
                RosterKey = "historical:" + match.GameId,
                CreatedUtc = DateTime.UtcNow
            };
            foreach (var participant in roster.OrderBy(p => p.ParticipantId))
            {
                var name = new string((participant.PlayerName ?? string.Empty)
                    .Where(c => !char.IsControl(c)).Take(64).ToArray()).Trim();
                snapshot.Players.Add(new LeagueDynamicMessagePlayer {
                    Friendly = participant.TeamId == match.TeamId,
                    ChampionName = string.IsNullOrWhiteSpace(participant.ChampionName)
                        ? "#" + participant.ChampionId : participant.ChampionName,
                    Player = new LeagueLivePlayerRow {
                        PuuId = participant.PuuId,
                        CellId = participant.ParticipantId,
                        Side = participant.TeamId == match.TeamId ? "ally" : "enemy",
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
