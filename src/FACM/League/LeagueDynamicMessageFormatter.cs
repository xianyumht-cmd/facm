using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FACM.League
{
    internal static class LeagueDynamicMessageFormatter
    {
        internal static IReadOnlyList<string> Build(LeagueDynamicMessageSnapshot snapshot, LeagueDynamicMessageOptions options,
            ISet<string> selected = null)
        {
            var result = new List<string>();
            if (snapshot == null || options == null) return result;
            var players = snapshot.Players.Where(p => (selected == null || selected.Contains(p.Player.PuuId)) &&
                (options.Target == "all" || options.Target == "friendly" && p.Friendly || options.Target == "enemy" && !p.Friendly)).ToList();
            var counts = players.Where(p => p.Player.ChampionId > 0).GroupBy(p => p.Player.ChampionId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var player in players.OrderByDescending(p => p.Friendly))
            {
                var name = DisplayName(player, options.NameStrategy, counts);
                if (options.ShowCurrentChampion && player.Player.ChampionId <= 0)
                {
                    result.Add(name + "：" + T(LeagueEfficiencyUiTextKeys.DynamicNoChampion));
                    continue;
                }
                if (player.History == null)
                {
                    result.Add(name + "：" + T(LeagueEfficiencyUiTextKeys.DynamicUnavailable));
                    continue;
                }
                var all = player.History.Matches.Where(m => m != null && m.GameId > 0 && m.ParticipantResolved && m.StatisticsAvailable &&
                    m.Kills >= 0 && m.Deaths >= 0 && m.Assists >= 0).GroupBy(m => m.GameId).Select(g => g.First()).ToList();
                var matches = all.Where(m => !options.ShowCurrentChampion || m.ChampionId == player.Player.ChampionId).ToList();
                if (matches.Count == 0)
                {
                    result.Add(name + "：" + T(all.Count == 0 && player.History.Matches.Count > 0 ? LeagueEfficiencyUiTextKeys.DynamicUnavailable :
                        options.ShowCurrentChampion ? LeagueEfficiencyUiTextKeys.DynamicNoChampionMatches : LeagueEfficiencyUiTextKeys.DynamicNoMatches));
                    continue;
                }
                var parts = new List<string> { T(LeagueEfficiencyUiTextKeys.DynamicMatchCount).Replace("{0}", matches.Count.ToString(CultureInfo.InvariantCulture)) };
                if (options.WinRate) parts.Add(T(LeagueEfficiencyUiTextKeys.DynamicWinRate) + Rate(matches.Count(m => m.Win) / (double)matches.Count));
                if (options.Kda) parts.Add("KDA " + ((matches.Sum(m => (long)m.Kills) + matches.Sum(m => (long)m.Assists)) /
                    (double)Math.Max(1L, matches.Sum(m => (long)m.Deaths))).ToString("F2", CultureInfo.InvariantCulture));
                var solo = Average(matches, m => m.SoloKills);
                if (options.SoloKills && solo.HasValue) parts.Add(T(LeagueEfficiencyUiTextKeys.DynamicSoloKills) + Number(solo, 1));
                Add(parts, options.VisionScore, LeagueEfficiencyUiTextKeys.DynamicVision, Average(matches, m => m.VisionScore), false);
                Add(parts, options.ChampionDamageShare, LeagueEfficiencyUiTextKeys.DynamicDamage, Average(matches, m => m.ChampionDamageShare), true);
                Add(parts, options.DamageTakenShare, LeagueEfficiencyUiTextKeys.DynamicTaken, Average(matches, m => m.DamageTakenShare), true);
                Add(parts, options.GoldShare, LeagueEfficiencyUiTextKeys.DynamicGold, Average(matches, m => m.GoldShare), true);
                Add(parts, options.CsPerMinute, LeagueEfficiencyUiTextKeys.DynamicCs, Average(matches,
                    m => m.GameDurationSeconds > 0 ? (double?)m.CreepScore / (m.GameDurationSeconds / 60d) : null), false);
                Add(parts, options.KillParticipation, LeagueEfficiencyUiTextKeys.DynamicParticipation, Average(matches, m => m.KillParticipation), true);
                Add(parts, options.DamageGoldEfficiency, LeagueEfficiencyUiTextKeys.DynamicEfficiency, Average(matches, m => m.DamageGoldEfficiency), true);
                if (options.MainChampions && !options.ShowCurrentChampion)
                {
                    var champions = matches.Where(m => m.ChampionId > 0).GroupBy(m => m.ChampionId).Where(g => g.Count() >= 2)
                        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
                    var chosen = MainItems(champions, 3, g => g.Count());
                    parts.Add(chosen.Count == 0 ? T(LeagueEfficiencyUiTextKeys.DynamicNoMain) :
                        T(LeagueEfficiencyUiTextKeys.DynamicChampions) + string.Join("、", chosen.Select(g => Clean(g.First().ChampionName, 64, "#" + g.Key))));
                }
                if (options.MainPositions)
                {
                    var positions = matches.Where(m => PositionOrder(m.Position) >= 0).GroupBy(m => m.Position)
                        .OrderByDescending(g => g.Count()).ThenBy(g => PositionOrder(g.Key)).ToList();
                    var chosen = options.ShowCurrentChampion ? positions : MainItems(positions, 2, g => g.Count());
                    if (chosen.Count > 0) parts.Add(T(LeagueEfficiencyUiTextKeys.DynamicPositions) + string.Join("、", chosen.Select(g => PositionName(g.Key))));
                }
                var line = name + "：";
                foreach (var part in parts)
                {
                    if (line.Length > name.Length + 1 && line.Length + part.Length + 1 > LeagueQuickMessagePresets.MaximumMessageLength)
                    {
                        result.Add(line.TrimEnd());
                        line = name + "：";
                    }
                    var remaining = part;
                    while (line.Length + remaining.Length > LeagueQuickMessagePresets.MaximumMessageLength)
                    {
                        var count = LeagueQuickMessagePresets.MaximumMessageLength - line.Length;
                        if (count > 0 && count < remaining.Length && char.IsHighSurrogate(remaining[count - 1])) count--;
                        result.Add(line + remaining.Substring(0, count));
                        remaining = remaining.Substring(count);
                        line = name + "：";
                    }
                    line += remaining + " ";
                }
                result.Add(line.TrimEnd());
            }
            return result;
        }

        internal static string DisplayName(LeagueDynamicMessagePlayer player, string strategy, IDictionary<int, int> counts)
        {
            var id = Clean(player.Player.AccountName, 64, T(LeagueEfficiencyUiTextKeys.DynamicPlayer));
            var champion = Clean(player.ChampionName, 64, "#" + player.Player.ChampionId);
            int count;
            var useChampion = player.Player.ChampionId > 0 && (strategy == "championNameWithName" ||
                strategy == "preferChampionName" && counts.TryGetValue(player.Player.ChampionId, out count) && count == 1);
            var name = useChampion ? strategy == "championNameWithName" ? champion + "(" + id + ")" : champion : id;
            if (name.StartsWith("/", StringComparison.Ordinal)) name = T(LeagueEfficiencyUiTextKeys.DynamicPlayer) + " " + name;
            return name;
        }

        private static double? Average(IReadOnlyList<LeaguePlayerMatchSummary> matches, Func<LeaguePlayerMatchSummary, double?> select)
        {
            var values = matches.Select(select).ToArray();
            return values.Length > 0 && values.All(v => v.HasValue) ? (double?)values.Average(v => v.Value) : null;
        }

        private static List<TItem> MainItems<TItem>(List<TItem> items, int max, Func<TItem, int> count)
        {
            if (items.Count <= max) return items;
            for (var index = max - 1; index >= 0; index--)
                if (count(items[index]) >= count(items[index + 1]) * 1.5) return items.Take(index + 1).ToList();
            return new List<TItem>();
        }

        private static void Add(List<string> parts, bool enabled, string key, double? value, bool rate)
        {
            if (enabled) parts.Add(T(key) + (rate ? value.HasValue ? Rate(value.Value) : "-" : Number(value, 1)));
        }

        private static string Number(double? value, int places) { return value.HasValue ? value.Value.ToString("F" + places, CultureInfo.InvariantCulture) : "-"; }
        private static string Rate(double value) { return (value * 100).ToString("F0", CultureInfo.InvariantCulture) + "%"; }
        private static string T(string key) { return LeagueEfficiencyText.Get(null, key); }
        private static string Clean(string value, int max, string fallback) { return string.IsNullOrWhiteSpace(value) ? fallback : new string(value.Where(c => !char.IsControl(c)).Take(max).ToArray()); }
        private static int PositionOrder(string position) { return Array.IndexOf(new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" }, position); }
        private static string PositionName(string position)
        {
            var keys = new[] { LeagueEfficiencyUiTextKeys.ProfileRoleTop, LeagueEfficiencyUiTextKeys.ProfileRoleJungle,
                LeagueEfficiencyUiTextKeys.ProfileRoleMiddle, LeagueEfficiencyUiTextKeys.ProfileRoleBottom, LeagueEfficiencyUiTextKeys.ProfileRoleSupport };
            var index = PositionOrder(position);
            return index >= 0 ? T(keys[index]) : "-";
        }
    }
}
