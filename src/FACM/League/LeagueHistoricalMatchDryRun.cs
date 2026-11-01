using System;
using System.Collections.Generic;
using System.Linq;

namespace FACM.League
{

    internal sealed class LeagueHistoricalRosterSelection
    {
        internal string PuuId;
        internal int TeamId;
        internal int ChampionId;
        internal string Position;
        internal string PrimaryPosition;
        internal string SecondaryPosition;
        internal string DraftName;
    }

    internal sealed class LeagueHistoricalCardAnalysis
    {
        internal int Games, Wins, Losses, Remakes, Aborts;
        internal int NormalWins, NormalLosses, CherryWins, CherryLosses;
        internal double? NormalWinRate => NormalWins + NormalLosses > 0
            ? (double?)(100d * NormalWins / (NormalWins + NormalLosses)) : null;
        internal double? CherryWinRate => CherryWins + CherryLosses > 0
            ? (double?)(100d * CherryWins / (CherryWins + CherryLosses)) : null;
        internal int StatisticsGames, Kills, Deaths, Assists;
        internal double? KdaCv;
        internal double? AverageGameKda;
        internal int ArenaPlacements, ArenaFirstPlaces;
        internal readonly Dictionary<string, int> Positions =
            new Dictionary<string, int>(StringComparer.Ordinal);
        internal IReadOnlyList<KeyValuePair<string, int>> MostPlayedPositions =>
            Positions.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Take(3).ToArray();
        internal double? WinRate => Wins + Losses > 0
            ? (double?) (100d * Wins / (Wins + Losses)) : null;
        internal double? Kda => StatisticsGames > 0
            ? (double?) ((Kills + Assists) / (double)Math.Max(1, Deaths)) : null;
    }


    // Pinned Akari v1.5.1 uses average per-match KDA, interpolated quartiles and
    // a 0.65 IQR threshold to compare players in the same ongoing-game roster.
    internal static class LeagueOngoingKdaOutliers
    {
        internal static IReadOnlyDictionary<string, string> Project(LeagueDynamicMessageSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Players.Count < 5 ||
                snapshot.Players.Any(p => p?.Player == null ||
                    !LeagueDynamicMessageService.SafePuuid(p.Player.PuuId)) ||
                snapshot.Players.Select(p => p.Player.PuuId)
                    .Distinct(StringComparer.Ordinal).Count() != snapshot.Players.Count)
                return new Dictionary<string, string>(StringComparer.Ordinal);

            var averages = new Dictionary<string, double?>(StringComparer.Ordinal);
            foreach (var player in snapshot.Players)
            {
                var id = player.Player.PuuId;
                var matches = player.RosterCardHistory == null
                    ? LeagueHistoricalMatchDryRun.VerifiedRecentMatches(player)
                    : LeagueOngoingHistoryFilter.Select(player.RosterCardHistory, id, 0, "");
                var analysis = LeagueHistoricalMatchDryRun.CardAnalysis(matches, id);
                averages[id] = analysis?.AverageGameKda;
            }
            return Detect(averages);
        }

        internal static IReadOnlyDictionary<string, string> Detect(
            IReadOnlyDictionary<string, double?> averages)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (averages == null || averages.Count < 5 ||
                averages.Keys.Any(id => !LeagueDynamicMessageService.SafePuuid(id)) ||
                averages.Values.Any(value => !value.HasValue ||
                    double.IsNaN(value.Value) || double.IsInfinity(value.Value) ||
                    value.Value < 0d)) return result;

            var sorted = averages.Values.Select(value => value.Value).OrderBy(value => value).ToArray();
            var q1 = Percentile(sorted, 0.25d);
            var q3 = Percentile(sorted, 0.75d);
            var low = q1 - 0.65d * (q3 - q1);
            var high = q3 + 0.65d * (q3 - q1);
            foreach (var entry in averages)
            {
                if (entry.Value.Value < low) result[entry.Key] = "below";
                else if (entry.Value.Value > high) result[entry.Key] = "over";
            }
            return result;
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            var index = percentile * (sorted.Length - 1);
            var lower = (int)Math.Floor(index);
            var fraction = index - lower;
            return lower == sorted.Length - 1 ? sorted[lower] :
                sorted[lower] + fraction * (sorted[lower + 1] - sorted[lower]);
        }
    }

    // Read-only draft from one verified historical match; never writes to LCU or a live game.
    internal static class LeagueHistoricalMatchDryRun
    {
        // Derived only from the verified card sample, never from unbounded live-client reads.
        internal static LeagueHistoricalCardAnalysis CardAnalysis(
            IReadOnlyList<LeaguePlayerMatchSummary> matches, string puuid)
        {
            if (matches == null || matches.Count > 200 ||
                !LeagueDynamicMessageService.SafePuuid(puuid)) return null;
            var result = new LeagueHistoricalCardAnalysis();
            var perGameKda = new List<double>();
            var seen = new HashSet<long>();
            foreach (var match in matches)
            {
                if (match == null || match.GameId <= 0 || match.ChampionId <= 0 ||
                    !seen.Add(match.GameId) ||
                    !string.IsNullOrEmpty(match.PuuId) &&
                    !string.Equals(match.PuuId, puuid, StringComparison.Ordinal) ||
                    !string.IsNullOrWhiteSpace(match.GameType) &&
                    !string.Equals(match.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase))
                    return null;
                var outcome = LeaguePlayerHistoryAnalysis.Outcome(match);
                if (outcome == LeagueHistoryOutcome.Unknown) return null;
                result.Games++;
                var eligible = (outcome == LeagueHistoryOutcome.Win ||
                    outcome == LeagueHistoryOutcome.Loss) &&
                    !string.Equals(match.GameMode, "PRACTICETOOL", StringComparison.OrdinalIgnoreCase);
                switch (outcome)
                {
                    case LeagueHistoryOutcome.Win: if (eligible) result.Wins++; break;
                    case LeagueHistoryOutcome.Loss: if (eligible) result.Losses++; break;
                    case LeagueHistoryOutcome.Remake: result.Remakes++; break;
                    case LeagueHistoryOutcome.Abort: result.Aborts++; break;
                }
                if (!eligible) continue;
                if (string.Equals(match.GameMode, "CHERRY", StringComparison.OrdinalIgnoreCase))
                {
                    if (outcome == LeagueHistoryOutcome.Win) result.CherryWins++;
                    else result.CherryLosses++;
                }
                else
                {
                    if (outcome == LeagueHistoryOutcome.Win) result.NormalWins++;
                    else result.NormalLosses++;
                }
                if (new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" }
                    .Contains(match.Position, StringComparer.Ordinal))
                {
                    int count;
                    result.Positions.TryGetValue(match.Position, out count);
                    result.Positions[match.Position] = count + 1;
                }
                if (match.StatisticsAvailable && match.ParticipantResolved &&
                    match.Kills >= 0 && match.Kills <= 999 &&
                    match.Deaths >= 0 && match.Deaths <= 999 &&
                    match.Assists >= 0 && match.Assists <= 999)
                {
                    result.StatisticsGames++;
                    result.Kills += match.Kills;
                    result.Deaths += match.Deaths;
                    result.Assists += match.Assists;
                    perGameKda.Add((match.Kills + match.Assists) /
                        (double)Math.Max(1, match.Deaths));
                }
                if (string.Equals(match.GameMode, "CHERRY", StringComparison.OrdinalIgnoreCase) &&
                    LeagueCherryTeams.ValidPlacement(match.SubteamPlacement).HasValue)
                {
                    result.ArenaPlacements++;
                    if (match.SubteamPlacement == 1) result.ArenaFirstPlaces++;
                }
            }
            if (perGameKda.Count > 0)
            {
                var mean = perGameKda.Average();
                result.AverageGameKda = mean;
                if (mean > 0d)
                    result.KdaCv = Math.Sqrt(perGameKda.Average(
                        value => (value - mean) * (value - mean))) / mean;
            }
            return result;
        }

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
                    g.Select(m => m.DataSource).Distinct().Count() == 1 &&
                    g.Select(m => m.QueueId).Where(id => id > 0).Distinct().Count() <= 1 &&
                    g.Select(m => m.MapId).Where(id => id > 0).Distinct().Count() <= 1 &&
                    g.Select(m => m.GameCreationEpochMs).Where(time => time > 0).Distinct().Count() <= 1 &&
                    g.Select(m => m.GameMode).Where(mode => !string.IsNullOrWhiteSpace(mode))
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1 &&
                    g.Select(m => m.SgpServerId ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                .Select(g => g.First()).ToArray();
        }

        // Read-only card history includes source-classified remakes and aborts,
        // but never uses ambiguous or contradictory duplicate games.
        internal static IReadOnlyList<LeaguePlayerMatchSummary> CardRecentMatches(
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
                    LeaguePlayerHistoryAnalysis.Outcome(m) != LeagueHistoryOutcome.Unknown) &&
                    g.Select(m => m.ChampionId).Distinct().Count() == 1 &&
                    g.Select(m => LeaguePlayerHistoryAnalysis.Outcome(m)).Distinct().Count() == 1 &&
                    g.Select(m => m.DataSource).Distinct().Count() == 1 &&
                    g.Select(m => m.QueueId).Where(id => id > 0).Distinct().Count() <= 1 &&
                    g.Select(m => m.MapId).Where(id => id > 0).Distinct().Count() <= 1 &&
                    g.Select(m => m.GameCreationEpochMs).Where(time => time > 0).Distinct().Count() <= 1 &&
                    g.Select(m => m.GameMode).Where(mode => !string.IsNullOrWhiteSpace(mode))
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1 &&
                    g.Select(m => m.SgpServerId ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                .Select(g => g.First()).OrderByDescending(m => m.GameCreationEpochMs)
                .ThenByDescending(m => m.GameId).ToArray();
        }

        internal static int[] AutomaticCardPageOffsets(int initialCount, int pages)
        {
            if (initialCount < 2 || initialCount > LeaguePlayerDataService.MaximumMatchCount ||
                pages < 0 || pages > 10) return new int[0];
            var offsets = new List<int>();
            for (var index = initialCount; index < 200 && offsets.Count < pages; index += 20)
                offsets.Add(index);
            return offsets.ToArray();
        }

        // Reject conflicting cross-page identities before mutating the displayed card.
        internal static IReadOnlyList<LeaguePlayerMatchSummary> CardPageAdditions(
            IList<LeaguePlayerMatchSummary> current,
            IReadOnlyList<LeaguePlayerMatchSummary> page, string puuid)
        {
            if (!LeagueDynamicMessageService.SafePuuid(puuid) || current == null ||
                page == null || current.Count > 200 || page.Count > 20) return null;
            var known = new Dictionary<long, LeaguePlayerMatchSummary>();
            foreach (var row in current)
            {
                if (row == null || row.GameId <= 0 || known.ContainsKey(row.GameId)) return null;
                known.Add(row.GameId, row);
            }
            var additions = new List<LeaguePlayerMatchSummary>();
            foreach (var row in page)
            {
                if (row == null || row.GameId <= 0 || row.ChampionId <= 0 ||
                    (!string.IsNullOrEmpty(row.PuuId) &&
                     !string.Equals(row.PuuId, puuid, StringComparison.Ordinal)) ||
                    (!string.IsNullOrWhiteSpace(row.GameType) &&
                     !string.Equals(row.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase)) ||
                    LeaguePlayerHistoryAnalysis.Outcome(row) == LeagueHistoryOutcome.Unknown)
                    return null;
                LeaguePlayerMatchSummary prior;
                if (known.TryGetValue(row.GameId, out prior))
                {
                    if (prior.ChampionId != row.ChampionId ||
                        prior.DataSource != row.DataSource ||
                        !string.Equals(prior.SgpServerId ?? "", row.SgpServerId ?? "",
                            StringComparison.OrdinalIgnoreCase) ||
                        LeaguePlayerHistoryAnalysis.Outcome(prior) !=
                            LeaguePlayerHistoryAnalysis.Outcome(row) ||
                        (prior.QueueId > 0 && row.QueueId > 0 && prior.QueueId != row.QueueId) ||
                        (prior.MapId > 0 && row.MapId > 0 && prior.MapId != row.MapId) ||
                        (!string.IsNullOrWhiteSpace(prior.GameMode) &&
                         !string.IsNullOrWhiteSpace(row.GameMode) &&
                         !string.Equals(prior.GameMode, row.GameMode, StringComparison.OrdinalIgnoreCase)) ||
                        (prior.GameCreationEpochMs > 0 && row.GameCreationEpochMs > 0 &&
                         prior.GameCreationEpochMs != row.GameCreationEpochMs)) return null;
                    continue;
                }
                known.Add(row.GameId, row);
                additions.Add(row);
            }
            return current.Count + additions.Count <= 200 ? additions : null;
        }


        // Atomically apply an entire verified historical lineup. No participant may be
        // added, removed, or impersonated; the target may view either draft side.
        internal static bool TryApplyRosterDraft(LeagueDynamicMessageSnapshot snapshot,
            IEnumerable<LeagueHistoricalRosterSelection> selections)
        {
            return TryApplyRosterDraft(snapshot, selections, snapshot?.SelfPuuid);
        }

        internal static bool TryApplyRosterDraft(LeagueDynamicMessageSnapshot snapshot,
            IEnumerable<LeagueHistoricalRosterSelection> selections, string targetPuuid)
        {
            return TryApplyRosterDraft(snapshot, selections, targetPuuid,
                snapshot == null ? 0 : snapshot.HistoricalQueueId);
        }

        internal static bool TryApplyRosterDraft(LeagueDynamicMessageSnapshot snapshot,
            IEnumerable<LeagueHistoricalRosterSelection> selections, string targetPuuid, int queueId)
        {
            if (!ValidTeamDraft(snapshot) || selections == null ||
                !LeagueDynamicMessageService.SafePuuid(targetPuuid) ||
                queueId < 0 || queueId > 100000) return false;
            var requested = selections.ToArray();
            if (requested.Length != snapshot.Players.Count ||
                requested.Any(p => p == null || !LeagueDynamicMessageService.SafePuuid(p.PuuId)) ||
                requested.Select(p => p.PuuId).Distinct(StringComparer.Ordinal).Count() != requested.Length)
                return false;

            var current = snapshot.Players.ToDictionary(p => p.Player.PuuId, StringComparer.Ordinal);
            var originalCounts = snapshot.Players.GroupBy(p => p.HistoricalOriginalTeamId)
                .ToDictionary(g => g.Key, g => g.Count());
            var requestedCounts = requested.GroupBy(p => p.TeamId)
                .ToDictionary(g => g.Key, g => g.Count());
            if (requested.Any(p => !current.ContainsKey(p.PuuId)) ||
                requestedCounts.Count != originalCounts.Count ||
                originalCounts.Any(g => !requestedCounts.ContainsKey(g.Key) ||
                    requestedCounts[g.Key] != g.Value) ||
                !current.ContainsKey(targetPuuid))
                return false;

            var cherry = snapshot.HistoricalGameMode == "CHERRY";
            var roles = new[] { "", "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
            if (requested.Any(p => p.DraftName != null &&
                (p.DraftName.Trim().Length == 0 || p.DraftName.Trim().Length > 48 ||
                 p.DraftName.Any(char.IsControl)))) return false;
            if (requested.Any(p => p.ChampionId < 1 || p.ChampionId > 9999 ||
                p.Position == null || p.PrimaryPosition == null || p.SecondaryPosition == null ||
                (cherry ? p.Position != "" || p.PrimaryPosition != "" ||
                    p.SecondaryPosition != "" :
                    !roles.Contains(p.Position, StringComparer.Ordinal) ||
                    !roles.Contains(p.PrimaryPosition, StringComparer.Ordinal) ||
                    !roles.Contains(p.SecondaryPosition, StringComparer.Ordinal) ||
                    (p.PrimaryPosition != "" && p.PrimaryPosition == p.SecondaryPosition))))
                return false;

            foreach (var selection in requested)
            {
                var player = current[selection.PuuId];
                var draftName = selection.DraftName == null ? null : selection.DraftName.Trim();
                if (draftName != null && draftName != player.Player.GameName)
                {
                    if (player.HistoricalOriginalPlayer == null)
                    {
                        var draftId = "draft-" + Guid.NewGuid().ToString("N");
                        var replacement = new LeagueDynamicMessagePlayer {
                            HistoricalOriginalPlayer = player,
                            HistoricalOriginalTeamId = player.HistoricalOriginalTeamId,
                            HistoricalOriginalChampionId = player.HistoricalOriginalChampionId,
                            HistoricalOriginalChampionName = player.HistoricalOriginalChampionName,
                            HistoricalOriginalPosition = player.HistoricalOriginalPosition,
                            HistoricalTeamId = player.HistoricalTeamId,
                            HistoricalPrimaryPosition = player.HistoricalPrimaryPosition,
                            HistoricalSecondaryPosition = player.HistoricalSecondaryPosition,
                            ChampionName = player.ChampionName,
                            Friendly = player.Friendly,
                            Player = new LeagueLivePlayerRow {
                                PuuId = draftId, CellId = player.Player.CellId,
                                GameName = draftName, ChampionId = player.Player.ChampionId,
                                Position = player.Player.Position, Side = player.Player.Side
                            }
                        };
                        snapshot.Players[snapshot.Players.IndexOf(player)] = replacement;
                        if (targetPuuid == selection.PuuId) targetPuuid = draftId;
                        player = replacement;
                    }
                    else player.Player.GameName = draftName;
                }
                if (player.Player.ChampionId != selection.ChampionId)
                {
                    player.ChampionName = selection.ChampionId == player.HistoricalOriginalChampionId
                        ? player.HistoricalOriginalChampionName : "#" + selection.ChampionId;
                }
                player.Player.ChampionId = selection.ChampionId;
                player.Player.Position = selection.Position;
                player.HistoricalPrimaryPosition = selection.PrimaryPosition;
                player.HistoricalSecondaryPosition = selection.SecondaryPosition;
                player.HistoricalTeamId = selection.TeamId;
            }
            snapshot.SelfPuuid = targetPuuid;
            snapshot.HistoricalQueueId = queueId;
            RefreshDraftSides(snapshot);
            return true;
        }

        // A swap only changes the bounded preview; the source match is never modified.
        internal static bool TrySwapTeams(LeagueDynamicMessageSnapshot snapshot,
            string firstPuuid, string secondPuuid)
        {
            if (!ValidTeamDraft(snapshot) || firstPuuid == secondPuuid ||
                !LeagueDynamicMessageService.SafePuuid(firstPuuid) ||
                !LeagueDynamicMessageService.SafePuuid(secondPuuid)) return false;
            var first = snapshot.Players.SingleOrDefault(p => p.Player.PuuId == firstPuuid);
            var second = snapshot.Players.SingleOrDefault(p => p.Player.PuuId == secondPuuid);
            if (first == null || second == null || first.HistoricalTeamId == second.HistoricalTeamId)
                return false;
            var oldTeam = first.HistoricalTeamId;
            first.HistoricalTeamId = second.HistoricalTeamId;
            second.HistoricalTeamId = oldTeam;
            RefreshDraftSides(snapshot);
            return true;
        }

        internal static bool TryRestoreOriginalTeams(LeagueDynamicMessageSnapshot snapshot)
        {
            if (!ValidTeamDraft(snapshot)) return false;
            foreach (var player in snapshot.Players)
                player.HistoricalTeamId = player.HistoricalOriginalTeamId;
            RefreshDraftSides(snapshot);
            return true;
        }

        internal static bool TryRestoreOriginalDraft(LeagueDynamicMessageSnapshot snapshot)
        {
            if (!ValidTeamDraft(snapshot) ||
                !LeagueDynamicMessageService.SafePuuid(snapshot.HistoricalOriginalTargetPuuid))
                return false;
            var originals = snapshot.Players.Select(p => p.HistoricalOriginalPlayer ?? p).ToArray();
            if (!originals.Any(p => p.Player.PuuId == snapshot.HistoricalOriginalTargetPuuid) ||
                originals.Select(p => p.Player.PuuId).Distinct(StringComparer.Ordinal).Count() != originals.Length ||
                originals.Any(p => p.HistoricalOriginalChampionId < 1 ||
                    p.HistoricalOriginalChampionId > 9999 ||
                    p.HistoricalOriginalChampionName == null ||
                    (p.HistoricalOriginalPosition != "" &&
                     !new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" }
                        .Contains(p.HistoricalOriginalPosition, StringComparer.Ordinal))))
                return false;
            for (var index = 0; index < originals.Length; index++)
                snapshot.Players[index] = originals[index];
            foreach (var player in snapshot.Players)
            {
                player.HistoricalTeamId = player.HistoricalOriginalTeamId;
                player.Player.ChampionId = player.HistoricalOriginalChampionId;
                player.ChampionName = player.HistoricalOriginalChampionName;
                player.Player.Position = player.HistoricalOriginalPosition;
                player.HistoricalPrimaryPosition = "";
                player.HistoricalSecondaryPosition = "";
            }
            snapshot.SelfPuuid = snapshot.HistoricalOriginalTargetPuuid;
            snapshot.HistoricalQueueId = snapshot.HistoricalOriginalQueueId;
            RefreshDraftSides(snapshot);
            return true;
        }

        private static void RefreshDraftSides(LeagueDynamicMessageSnapshot snapshot)
        {
            var selfTeam = snapshot.Players.Single(p => p.Player.PuuId == snapshot.SelfPuuid)
                .HistoricalTeamId;
            foreach (var player in snapshot.Players)
            {
                player.Friendly = player.HistoricalTeamId == selfTeam;
                player.Player.Side = player.Friendly ? "ally" : "enemy";
            }
        }

        private static bool ValidTeamDraft(LeagueDynamicMessageSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Phase != "HistoricalPreview" ||
                snapshot.HistoricalQueueId < 0 || snapshot.HistoricalQueueId > 100000 ||
                snapshot.HistoricalOriginalQueueId < 0 ||
                snapshot.HistoricalOriginalQueueId > 100000 ||
                !LeagueDynamicMessageService.SafePuuid(snapshot.SelfPuuid) ||
                (snapshot.HistoricalGameMode != "CLASSIC" && snapshot.HistoricalGameMode != "CHERRY") ||
                (snapshot.HistoricalGameMode == "CLASSIC" ? snapshot.Players.Count != 10 :
                    snapshot.Players.Count != 8 && snapshot.Players.Count != 16) ||
                snapshot.Players.Any(p => p?.Player == null ||
                    !LeagueDynamicMessageService.SafePuuid(p.Player.PuuId)) ||
                snapshot.Players.Select(p => p.Player.PuuId).Distinct(StringComparer.Ordinal).Count() !=
                    snapshot.Players.Count) return false;
            var self = snapshot.Players.SingleOrDefault(p => p.Player.PuuId == snapshot.SelfPuuid);
            if (self == null) return false;
            var cherry = snapshot.HistoricalGameMode == "CHERRY";
            Func<int, bool> validTeam = id => cherry ? LeagueCherryTeams.ValidSubteamId(id).HasValue :
                id == 100 || id == 200;
            if (snapshot.Players.Any(p => !validTeam(p.HistoricalTeamId) ||
                !validTeam(p.HistoricalOriginalTeamId) ||
                p.Friendly != (p.HistoricalTeamId == self.HistoricalTeamId) ||
                p.Player.Side != (p.Friendly ? "ally" : "enemy"))) return false;
            foreach (var original in new[] { false, true })
            {
                var groups = snapshot.Players.GroupBy(p => original ?
                    p.HistoricalOriginalTeamId : p.HistoricalTeamId).ToArray();
                if (cherry ? groups.Length != snapshot.Players.Count / 2 ||
                    groups.Any(g => g.Count() != 2) :
                    groups.Length != 2 || groups.Any(g => g.Count() != 5))
                    return false;
            }
            return true;
        }

        // Only changes an ephemeral historical preview, never a match record or LCU state.
        internal static bool TryEditSelection(LeagueDynamicMessageSnapshot snapshot,
            string puuid, int championId, string position, string championName = null,
            string primaryPosition = null, string secondaryPosition = null)
        {
            var roles = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
            if (snapshot == null || snapshot.Phase != "HistoricalPreview" ||
                !LeagueDynamicMessageService.SafePuuid(snapshot.SelfPuuid) ||
                !LeagueDynamicMessageService.SafePuuid(puuid) ||
                championId < 1 || championId > 9999 || position == null ||
                (snapshot.HistoricalGameMode != "CLASSIC" &&
                 snapshot.HistoricalGameMode != "CHERRY") ||
                (snapshot.HistoricalGameMode == "CHERRY" && (position.Length != 0 ||
                    !string.IsNullOrEmpty(primaryPosition) || !string.IsNullOrEmpty(secondaryPosition))) ||
                (snapshot.HistoricalGameMode == "CLASSIC" && position.Length != 0 &&
                 !roles.Contains(position, StringComparer.Ordinal)) ||
                (primaryPosition != null && primaryPosition.Length != 0 &&
                 !roles.Contains(primaryPosition, StringComparer.Ordinal)) ||
                (secondaryPosition != null && secondaryPosition.Length != 0 &&
                 !roles.Contains(secondaryPosition, StringComparer.Ordinal)) ||
                (primaryPosition != null && secondaryPosition != null &&
                 primaryPosition.Length != 0 && primaryPosition == secondaryPosition) ||
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
            {
                var safeName = new string((championName ?? string.Empty)
                    .Where(c => !char.IsControl(c)).Take(48).ToArray()).Trim();
                player.ChampionName = string.IsNullOrWhiteSpace(safeName)
                    ? "#" + championId : safeName;
            }
            player.Player.ChampionId = championId;
            player.Player.Position = position;
            if (primaryPosition != null) player.HistoricalPrimaryPosition = primaryPosition;
            if (secondaryPosition != null) player.HistoricalSecondaryPosition = secondaryPosition;
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
                HistoricalOriginalTargetPuuid = targetPuuid,
                HistoricalGameMode = cherry ? "CHERRY" : "CLASSIC",
                HistoricalQueueId = match.QueueId > 0 && match.QueueId <= 100000
                    ? match.QueueId : 0,
                HistoricalOriginalQueueId = match.QueueId > 0 && match.QueueId <= 100000
                    ? match.QueueId : 0,
                RosterKey = "historical:" + match.GameId,
                CreatedUtc = DateTime.UtcNow
            };
            foreach (var participant in roster.OrderBy(p => p.ParticipantId))
            {
                var name = new string((participant.PlayerName ?? string.Empty)
                    .Where(c => !char.IsControl(c)).Take(64).ToArray()).Trim();
                var championName = new string((participant.ChampionName ?? string.Empty)
                    .Where(c => !char.IsControl(c)).Take(48).ToArray()).Trim();
                var sourcePosition = !cherry && new[] {
                    "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"
                }.Contains(participant.Position, StringComparer.Ordinal)
                    ? participant.Position : string.Empty;
                snapshot.Players.Add(new LeagueDynamicMessagePlayer {
                    HistoricalOriginalTeamId = cherry ? participant.PlayerSubteamId.Value : participant.TeamId,
                    HistoricalOriginalChampionId = participant.ChampionId,
                    HistoricalOriginalChampionName = string.IsNullOrWhiteSpace(championName)
                        ? "#" + participant.ChampionId : championName,
                    HistoricalOriginalPosition = sourcePosition,
                    Friendly = cherry ? participant.PlayerSubteamId == target.PlayerSubteamId :
                        participant.TeamId == match.TeamId,
                    HistoricalTeamId = cherry ? participant.PlayerSubteamId.Value : participant.TeamId,
                    ChampionName = string.IsNullOrWhiteSpace(championName)
                        ? "#" + participant.ChampionId : championName,
                    Player = new LeagueLivePlayerRow {
                        PuuId = participant.PuuId,
                        CellId = participant.ParticipantId,
                        Side = (cherry ? participant.PlayerSubteamId == target.PlayerSubteamId :
                            participant.TeamId == match.TeamId) ? "ally" : "enemy",
                        GameName = string.IsNullOrWhiteSpace(name)
                            ? "#" + participant.ParticipantId : name,
                        ChampionId = participant.ChampionId,
                        Position = sourcePosition,
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
