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
            if (snapshot == null || threshold < 1 || threshold > LeaguePlayerDataService.MaximumMatchCount ||
                snapshot.Players.Count > 20) return result;

            var roster = snapshot.Players.Where(p => p != null && p.Player != null &&
                LeagueDynamicMessageService.SafePuuid(p.Player.PuuId)).ToList();
            if (roster.Select(p => p.Player.PuuId).Distinct(StringComparer.Ordinal).Count() != roster.Count) return result;
            var byId = roster.ToDictionary(p => p.Player.PuuId, StringComparer.Ordinal);
            var pairGames = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);

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
            var groups = LeaguePremadeInference.Infer(snapshot).Select(g => new
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
