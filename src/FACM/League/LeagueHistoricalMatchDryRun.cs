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

        // Source recent-champion usage: up to nine heroes ranked by verified match count.
        // Conflicting duplicate games and unknown outcomes are not counted as losses.
        internal static IReadOnlyList<LeaguePlayerChampionStat> RecentChampionUsage(
            LeagueDynamicMessagePlayer player)
        {
            if (player?.Player == null || player.History == null ||
                !LeagueDynamicMessageService.SafePuuid(player.Player.PuuId))
                return new LeaguePlayerChampionStat[0];
            var rows = VerifiedRecentMatches(player);
            return rows.GroupBy(m => m.ChampionId)
                .Select(g => new LeaguePlayerChampionStat {
                    ChampionId = g.Key,
                    ChampionName = new string((g.Select(m => m.ChampionName)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? string.Empty)
                        .Where(c => !char.IsControl(c)).Take(48).ToArray()),
                    Games = g.Count(),
                    Wins = g.Count(m => LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win)
                })
                .OrderByDescending(c => c.Games).ThenBy(c => c.ChampionId).Take(9).ToArray();
        }

        internal static IReadOnlyList<LeaguePlayerMatchSummary> VerifiedRecentMatches(
            LeagueDynamicMessagePlayer player)
        {
            if (player?.Player == null || player.History == null ||
                !LeagueDynamicMessageService.SafePuuid(player.Player.PuuId))
                return new LeaguePlayerMatchSummary[0];
            return player.History.Matches.Take(LeaguePlayerDataService.MaximumMatchCount)
                .Where(m => m != null && m.GameId > 0)
                .GroupBy(m => m.GameId)
                .Where(g => g.All(m => m.ChampionId > 0 &&
                    (string.IsNullOrEmpty(m.PuuId) ||
                     string.Equals(m.PuuId, player.Player.PuuId, StringComparison.Ordinal)) &&
                    (string.IsNullOrWhiteSpace(m.GameType) ||
                     string.Equals(m.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase)) &&
                    (LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win ||
                     LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Loss)) &&
                    g.Select(m => m.ChampionId).Distinct().Count() == 1 &&
                    g.Select(m => LeaguePlayerHistoryAnalysis.Outcome(m)).Distinct().Count() == 1 &&
                    g.Select(m => m.SgpServerId ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                .Select(g => g.First()).ToArray();
        }

        // Only changes an ephemeral historical preview, never a match record or LCU state.
        internal static bool TryEditSelection(LeagueDynamicMessageSnapshot snapshot,
            string puuid, int championId, string position)
        {
            if (snapshot == null || snapshot.Phase != "HistoricalPreview" ||
                !LeagueDynamicMessageService.SafePuuid(snapshot.SelfPuuid) ||
                !LeagueDynamicMessageService.SafePuuid(puuid) ||
                championId < 1 || championId > 9999 || position == null ||
                (snapshot.HistoricalGameMode != "CLASSIC" &&
                 snapshot.HistoricalGameMode != "CHERRY") ||
                (snapshot.HistoricalGameMode == "CHERRY" && position.Length != 0) ||
                (snapshot.HistoricalGameMode == "CLASSIC" && position.Length != 0 &&
                 !new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" }
                     .Contains(position, StringComparer.Ordinal)) ||
                snapshot.Players.Count != (snapshot.HistoricalGameMode == "CLASSIC" ? 10 :
                    snapshot.Players.Count == 8 ? 8 : 16) ||
                snapshot.Players.Any(p => p?.Player == null ||
                    !LeagueDynamicMessageService.SafePuuid(p.Player.PuuId)) ||
                snapshot.Players.Select(p => p.Player.PuuId)
                    .Distinct(StringComparer.Ordinal).Count() != snapshot.Players.Count ||
                !snapshot.Players.Any(p => p.Player.PuuId == snapshot.SelfPuuid))
                return false;
            var player = snapshot.Players.SingleOrDefault(p => p.Player.PuuId == puuid);
            if (player == null) return false;
            if (player.Player.ChampionId != championId)
                player.ChampionName = "#" + championId;
            player.Player.ChampionId = championId;
            player.Player.Position = position;
            return true;
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
            if ((cherry ? roster.Count != 8 && roster.Count != 16 : roster.Count != 10) ||
                roster.Any(p => p == null ||
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
                if (teams.Length != roster.Count / 2 || teams.Any(g => g.Count() != 2 ||
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
