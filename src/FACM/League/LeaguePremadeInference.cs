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
}
