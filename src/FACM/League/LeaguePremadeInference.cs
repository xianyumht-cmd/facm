using System;
using System.Collections.Generic;
using System.Linq;

namespace FACM.League
{
    internal static class LeaguePremadeInference
    {
        // Source v1.5.1 defaults to five shared games. This projection never requests more history.
        internal static IReadOnlyList<string[]> Infer(LeagueDynamicMessageSnapshot snapshot, int threshold = 5)
        {
            var result = new List<string[]>();
            if (snapshot == null || threshold < 2 || threshold > LeaguePlayerDataService.MaximumMatchCount ||
                snapshot.Players.Count > 20) return result;

            var roster = snapshot.Players.Where(p => p != null && p.Player != null &&
                LeagueDynamicMessageService.SafePuuid(p.Player.PuuId)).ToList();
            if (roster.Select(p => p.Player.PuuId).Distinct(StringComparer.Ordinal).Count() != roster.Count) return result;
            var byId = roster.ToDictionary(p => p.Player.PuuId, StringComparer.Ordinal);
            var pairGames = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
            var observations = new Dictionary<string, Dictionary<long, HashSet<string>>>(StringComparer.Ordinal);
            foreach (var player in roster)
            {
                var seen = new Dictionary<long, HashSet<string>>();
                foreach (var match in (player.History?.Matches ?? new List<LeaguePlayerMatchSummary>())
                    .Where(m => m != null && m.GameId > 0 && m.SameTeamPuuids != null)
                    .Take(LeaguePlayerDataService.MaximumMatchCount))
                {
                    var members = new HashSet<string>(match.SameTeamPuuids.Where(id =>
                        byId.ContainsKey(id) && byId[id].Friendly == player.Friendly), StringComparer.Ordinal);
                    if (!members.Contains(player.Player.PuuId))
                    {
                        seen[match.GameId] = null;
                        continue;
                    }
                    HashSet<string> prior;
                    if (seen.TryGetValue(match.GameId, out prior))
                    {
                        if (prior == null || !prior.SetEquals(members)) seen[match.GameId] = null;
                    }
                    else seen[match.GameId] = members;
                }
                observations[player.Player.PuuId] = seen;
            }

            foreach (var player in roster)
            {
                foreach (var match in (player.History?.Matches ?? new List<LeaguePlayerMatchSummary>())
                    .Where(m => m != null && m.GameId > 0 && m.SameTeamPuuids != null)
                    .Take(LeaguePlayerDataService.MaximumMatchCount))
                {
                    if (!match.SameTeamPuuids.Contains(player.Player.PuuId, StringComparer.Ordinal)) continue;
                    var members = match.SameTeamPuuids.Where(id => byId.ContainsKey(id) &&
                        byId[id].Friendly == player.Friendly).Distinct(StringComparer.Ordinal)
                        .OrderBy(id => id, StringComparer.Ordinal).ToArray();
                    for (var i = 0; i < members.Length; i++)
                    for (var j = i + 1; j < members.Length; j++)
                    {
                        var pair = members[i] + "|" + members[j];
                        HashSet<long> games;
                        if (!pairGames.TryGetValue(pair, out games)) pairGames[pair] = games = new HashSet<long>();
                        games.Add(match.GameId);
                    }
                }
            }

            // A conflicting historical team roster is not evidence of a shared game.
            foreach (var pair in pairGames)
            {
                var members = pair.Key.Split('|');
                foreach (var gameId in pair.Value.ToArray())
                {
                    if (members.Any(id =>
                    {
                        HashSet<string> observed;
                        return observations[id].TryGetValue(gameId, out observed) &&
                            (observed == null || !observed.Contains(members[0] == id ? members[1] : members[0]));
                    })) pair.Value.Remove(gameId);
                }
            }

            var adjacent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var pair in pairGames.Where(p => p.Value.Count >= threshold))
            {
                var members = pair.Key.Split('|');
                if (members.Length != 2) continue;
                HashSet<string> links;
                if (!adjacent.TryGetValue(members[0], out links)) adjacent[members[0]] = links = new HashSet<string>(StringComparer.Ordinal);
                links.Add(members[1]);
                if (!adjacent.TryGetValue(members[1], out links)) adjacent[members[1]] = links = new HashSet<string>(StringComparer.Ordinal);
                links.Add(members[0]);
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in adjacent.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!visited.Add(id)) continue;
                var group = new List<string>();
                var pending = new Queue<string>();
                pending.Enqueue(id);
                while (pending.Count > 0)
                {
                    var member = pending.Dequeue();
                    group.Add(member);
                    foreach (var other in adjacent[member])
                        if (visited.Add(other)) pending.Enqueue(other);
                }
                if (group.Count >= 2) result.Add(group.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            }
            return result;
        }
    }

    internal static class LeaguePremadePlayerTagProjection
    {
        internal static IDictionary<string, string> Project(LeagueDynamicMessageSnapshot snapshot, int threshold)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            if (snapshot == null) return labels;
            var byId = snapshot.Players.Where(p => p != null && p.Player != null &&
                LeagueDynamicMessageService.SafePuuid(p.Player.PuuId))
                .GroupBy(p => p.Player.PuuId, StringComparer.Ordinal)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var groups = LeaguePremadeInference.Infer(snapshot, threshold)
                .OrderByDescending(g => byId[g[0]].Friendly)
                .ThenBy(g => LeaguePremadeMessageFormatter.GroupKey(g), StringComparer.Ordinal).ToArray();
            for (var index = 0; index < groups.Length && index < 26; index++)
                foreach (var id in groups[index])
                    labels[id] = LeagueEfficiencyText.Get(null, LeagueEfficiencyUiTextKeys.OngoingPremadeTeam)
                        .Replace("{0}", ((char)('A' + index)).ToString());
            return labels;
        }
    }

    internal sealed class LeaguePremadeTeamAggregate
    {
        internal int Wins;
        internal int Games;
        internal double? Kda;

        internal static LeaguePremadeTeamAggregate Project(IReadOnlyList<LeagueDynamicMessagePlayer> players)
        {
            if (players == null || players.Count < 2 || players.Count > 20 ||
                players.Any(p => p == null || p.Player == null || p.History == null ||
                    !LeagueDynamicMessageService.SafePuuid(p.Player.PuuId)) ||
                players.Select(p => p.Player.PuuId).Distinct(StringComparer.Ordinal).Count() != players.Count ||
                players.Any(p => p.Friendly != players[0].Friendly)) return null;

            var result = new LeaguePremadeTeamAggregate();
            long kills = 0, deaths = 0, assists = 0;
            var completeStats = true;
            foreach (var player in players)
            {
                var seen = new Dictionary<long, LeaguePlayerMatchSummary>();
                foreach (var match in player.History.Matches.Take(LeaguePlayerDataService.MaximumMatchCount))
                {
                    if (match == null || match.GameId <= 0 ||
                        !string.IsNullOrWhiteSpace(match.GameType) &&
                        !string.Equals(match.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase)) continue;
                    LeaguePlayerMatchSummary prior;
                    if (seen.TryGetValue(match.GameId, out prior))
                    {
                        if (!string.Equals(prior.WinResult, match.WinResult, StringComparison.OrdinalIgnoreCase) ||
                            prior.StatisticsAvailable != match.StatisticsAvailable ||
                            prior.Kills != match.Kills || prior.Deaths != match.Deaths ||
                            prior.Assists != match.Assists) return null;
                    }
                    else seen.Add(match.GameId, match);
                }
                var classified = seen.Values.Where(m =>
                    LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win ||
                    LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Loss).ToArray();
                if (classified.Length == 0) return null;
                result.Games += classified.Length;
                result.Wins += classified.Count(m => LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win);
                foreach (var match in classified)
                {
                    if (!match.StatisticsAvailable || match.Kills < 0 || match.Deaths < 0 || match.Assists < 0)
                    {
                        completeStats = false;
                        continue;
                    }
                    kills += match.Kills;
                    deaths += match.Deaths;
                    assists += match.Assists;
                }
            }
            if (completeStats) result.Kda = (double)(kills + assists) / Math.Max(1L, deaths);
            return result;
        }
    }

    internal static class LeaguePremadeTeamTagProjection
    {
        internal const string WinRateTeam = "win-rate-team";
        internal const string LossRateTeam = "loss-rate-team";

        // Pinned Akari 1.5.1 team tag thresholds. Only loaded, classified match outcomes count.
        internal static string Classify(IReadOnlyList<LeagueDynamicMessagePlayer> members)
        {
            if (members == null || members.Count < 2 || members.Count > 20 ||
                members.Any(p => p == null || p.Player == null || p.History == null ||
                    !LeagueDynamicMessageService.SafePuuid(p.Player.PuuId)) ||
                members.Select(p => p.Player.PuuId).Distinct(StringComparer.Ordinal).Count() != members.Count ||
                members.Any(p => p.Friendly != members[0].Friendly)) return null;

            var winRates = new List<double>();
            var games = new List<int>();
            var streaks = new List<int>();
            foreach (var member in members)
            {
                var seen = new Dictionary<long, LeaguePlayerMatchSummary>();
                foreach (var match in member.History.Matches.Take(LeaguePlayerDataService.MaximumMatchCount))
                {
                    if (match == null || match.GameId <= 0 ||
                        !string.IsNullOrWhiteSpace(match.GameType) &&
                        !string.Equals(match.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase)) continue;
                    LeaguePlayerMatchSummary prior;
                    if (seen.TryGetValue(match.GameId, out prior))
                    {
                        if (!string.Equals(prior.WinResult, match.WinResult, StringComparison.OrdinalIgnoreCase) ||
                            prior.GameCreationEpochMs != match.GameCreationEpochMs) return null;
                    }
                    else seen.Add(match.GameId, match);
                }
                var classified = seen.Values.Where(m =>
                    LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win ||
                    LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Loss).ToArray();
                var wins = classified.Count(m => LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win);
                games.Add(classified.Length);
                winRates.Add(classified.Length == 0 ? double.NaN : (double)wins / classified.Length);
                // A streak is only meaningful when the full observed sequence is timestamped and classified.
                var complete = seen.Count == classified.Length &&
                    classified.All(m => m.GameCreationEpochMs > 0) &&
                    classified.Select(m => m.GameCreationEpochMs).Distinct().Count() == classified.Length;
                var streak = 0;
                if (complete)
                    foreach (var match in classified.OrderByDescending(m => m.GameCreationEpochMs))
                    {
                        if (LeaguePlayerHistoryAnalysis.Outcome(match) != LeagueHistoryOutcome.Win) break;
                        streak++;
                    }
                streaks.Add(streak);
            }

            if (members.Count >= 3)
            {
                for (var i = 0; i < members.Count; i++)
                {
                    if (games[i] < 13 || winRates[i] < 0.9) continue;
                    var others = streaks.Where((value, index) => index != i).ToArray();
                    if (others.Average() >= 4) return WinRateTeam;
                }
            }
            if (games.All(n => n >= 2) && winRates.All(rate => rate <= 0.25))
                return LossRateTeam;
            return null;
        }
    }

    internal static class LeaguePremadeMessageFormatter
    {
        internal static string GroupKey(IEnumerable<string> members)
        {
            return string.Join("|", members.OrderBy(id => id, StringComparer.Ordinal));
        }

        internal static IReadOnlyList<string> Build(LeagueDynamicMessageSnapshot snapshot, LeagueDynamicMessageOptions options,
            ISet<string> selectedGroups = null)
        {
            var lines = new List<string>();
            if (snapshot == null || options == null) return lines;
            var byId = snapshot.Players.Where(p => p != null && p.Player != null &&
                LeagueDynamicMessageService.SafePuuid(p.Player.PuuId))
                .GroupBy(p => p.Player.PuuId, StringComparer.Ordinal)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var groups = LeaguePremadeInference.Infer(snapshot, options.PremadeMatchThreshold).Select(g => new
            {
                Key = GroupKey(g),
                Players = g.Select(id => byId[id]).ToArray(),
                Friendly = byId[g[0]].Friendly
            }).Where(g => (selectedGroups == null || selectedGroups.Contains(g.Key)) &&
                (options.Target == "all" || options.Target == "friendly" && g.Friendly ||
                    options.Target == "enemy" && !g.Friendly)).OrderByDescending(g => g.Friendly).ThenBy(g => g.Key).ToList();
            var championCounts = groups.SelectMany(g => g.Players).Where(p => p.Player.ChampionId > 0)
                .GroupBy(p => p.Player.ChampionId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var side in new[] { true, false })
            {
                var prefix = LeagueEfficiencyText.Get(null, side ? LeagueEfficiencyUiTextKeys.DynamicFriendly :
                    LeagueEfficiencyUiTextKeys.DynamicEnemy) + LeagueEfficiencyText.Get(null, LeagueEfficiencyUiTextKeys.PremadeMessageTitle) + "：";
                var line = prefix;
                foreach (var group in groups.Where(g => g.Friendly == side))
                {
                    var names = group.Players.Select(p => LeagueDynamicMessageFormatter.DisplayName(p,
                        options.PremadeNameStrategy, championCounts)).ToArray();
                    var nameLimit = Math.Min(40, Math.Max(1, (LeagueQuickMessagePresets.MaximumMessageLength -
                        prefix.Length - 2 - Math.Max(0, names.Length - 1)) / Math.Max(1, names.Length)));
                    var part = "[" + string.Join("、", names.Select(name => name.Length > nameLimit ? name.Substring(0, nameLimit) : name)) + "]";
                    var addition = line == prefix ? part : "；" + part;
                    if (line.Length + addition.Length > LeagueQuickMessagePresets.MaximumMessageLength)
                    {
                        lines.Add(line);
                        line = prefix + part;
                    }
                    else line += addition;
                }
                if (line != prefix) lines.Add(line);
            }
            return lines;
        }
    }
}
