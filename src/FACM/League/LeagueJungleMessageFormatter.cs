using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FACM.League
{
    internal static class LeagueJungleMessageFormatter
    {
        internal static IReadOnlyList<string> Build(LeagueDynamicMessageSnapshot snapshot,
            LeagueDynamicMessageOptions options, ISet<string> selected)
        {
            var lines = new List<string>();
            if (snapshot == null || options == null || selected == null || selected.Count == 0) return lines;
            var players = snapshot.Players.Where(p => p.Player != null && selected.Contains(p.Player.PuuId) &&
                (options.Target == "all" || options.Target == "friendly" && p.Friendly || options.Target == "enemy" && !p.Friendly))
                .OrderByDescending(p => p.Friendly).ToArray();
            if (players.Length == 0 || players.Any(p => p.JungleSamples == null)) return lines;
            var counts = players.Where(p => p.Player.ChampionId > 0).GroupBy(p => p.Player.ChampionId)
                .ToDictionary(g => g.Key, g => g.Count());
            foreach (var player in players)
            {
                var name = LeagueDynamicMessageFormatter.DisplayName(player, options.JungleNameStrategy, counts);
                var samples = player.JungleSamples;
                var selectedSamples = options.JungleShowCurrentChampion ?
                    samples.Where(s => s.ChampionId == player.Player.ChampionId).ToArray() : samples.ToArray();
                var stats = LeagueJungleTimelineAnalysis.Aggregate(selectedSamples);
                if (options.JungleShowCurrentChampion && player.Player.ChampionId <= 0)
                {
                    lines.Add(name + "：尚未选择英雄");
                    continue;
                }
                if (stats == null)
                {
                    lines.Add(name + "：" + (options.JungleShowCurrentChampion ? "当前英雄暂无打野记录" : "暂无打野记录"));
                    continue;
                }
                var parts = new List<string> { "打野样本" + stats.Games.ToString(CultureInfo.InvariantCulture) + "场" };
                if (options.JungleActivityPreference)
                {
                    var lanes = new[] { Tuple.Create("上", stats.TopRate), Tuple.Create("中", stats.MidRate), Tuple.Create("下", stats.BotRate) }
                        .OrderByDescending(l => l.Item2).ToArray();
                    var preference = lanes[0].Item2 == 0 ? "不明显" : lanes[1].Item2 >= lanes[0].Item2 * 0.65 ?
                        lanes[0].Item1 + "/" + lanes[1].Item1 : lanes[0].Item1;
                    parts.Add("抓线偏好" + preference + "(上" + Rate(stats.TopRate) + "/中" + Rate(stats.MidRate) +
                        "/下" + Rate(stats.BotRate) + ")");
                }
                if (options.JungleShowCurrentChampion && options.JungleFirstClearDistribution)
                {
                    foreach (var side in new[] { "blue", "red" })
                    foreach (var kind in new[] { "normal", "invade" })
                    {
                        var sideGames = selectedSamples.Count(s => side == "blue" ? s.TeamId == 100 : s.TeamId == 200);
                        var prefix = side + "/" + kind + "/";
                        var camps = stats.FirstCamps.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal))
                            .OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).ToArray();
                        var total = camps.Sum(c => c.Value);
                        var title = (side == "blue" ? "蓝方" : "红方") + (kind == "normal" ? "常规开野" : "入侵开野");
                        var distribution = string.Join("、", camps.Select(c => Camp(c.Key.Substring(prefix.Length)) +
                            Rate(c.Value / (double)total)));
                        parts.Add(title + (sideGames > 0 ? Rate(total / (double)sideGames) : "-") +
                            (total > 0 ? "[" + distribution + "]" : ""));
                    }
                }
                if (options.JungleEarlyGank)
                {
                    parts.Add("三级抓人" + Rate(stats.Level3GankRate));
                    parts.Add("四级抓人" + Rate(stats.Level4GankRate));
                }
                if (options.JungleDragonControl)
                {
                    var first = stats.AverageFirstDragonSeconds;
                    var time = first.HasValue ? "(" + ((int)Math.Round(first.Value) / 60).ToString(CultureInfo.InvariantCulture) + ":" +
                        ((int)Math.Round(first.Value) % 60).ToString("D2", CultureInfo.InvariantCulture) + ")" : "";
                    parts.Add("首龙控制" + Rate(stats.FirstDragonRate) + time);
                    parts.Add("场均小龙" + Number(stats.AverageDragons));
                }
                if (options.JungleMonsterControl)
                    parts.Add("场均巢虫" + Number(stats.AverageVoidgrubs) + "/先锋" + Number(stats.AverageHeralds) +
                        "/大龙" + Number(stats.AverageBarons));
                if (!options.JungleShowCurrentChampion && options.JungleMainChampions)
                {
                    var candidates = samples.GroupBy(s => s.ChampionId).Where(g => g.Key > 0 && g.Count() >= 2)
                        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToArray();
                    var leaders = candidates.Take(3).ToArray();
                    if (candidates.Length > 3)
                    {
                        var limit = -1;
                        for (var index = 2; index >= 0; index--)
                            if (candidates[index].Count() >= candidates[index + 1].Count() * 1.5)
                            {
                                limit = index;
                                break;
                            }
                        leaders = limit < 0 ? Array.Empty<IGrouping<int, LeagueJungleTimelineSample>>() : leaders.Take(limit + 1).ToArray();
                    }
                    if (leaders.Length > 0)
                        parts.Add("常用打野" + string.Join("、", leaders.Select(g => Champion(player, g.Key))));
                }
                AddBounded(lines, name, parts);
            }
            return lines;
        }

        private static string Champion(LeagueDynamicMessagePlayer player, int id)
        {
            var name = player.History?.Matches.FirstOrDefault(m => m.ChampionId == id)?.ChampionName;
            return string.IsNullOrWhiteSpace(name) ? "#" + id.ToString(CultureInfo.InvariantCulture) :
                new string(name.Where(c => !char.IsControl(c)).Take(48).ToArray());
        }

        private static string Camp(string name)
        {
            switch (name)
            {
                case "blue": return "蓝BUFF";
                case "red": return "红BUFF";
                case "wolves": return "三狼";
                case "raptors": return "锋喙鸟";
                default: return "其他";
            }
        }

        private static string Rate(double fraction) { return (fraction * 100).ToString("F0", CultureInfo.InvariantCulture) + "%"; }
        private static string Number(double value) { return value.ToString("F1", CultureInfo.InvariantCulture); }

        private static void AddBounded(List<string> lines, string name, IEnumerable<string> parts)
        {
            var prefix = name + "：";
            var line = prefix;
            foreach (var part in parts)
            {
                var remaining = part;
                if (line != prefix && line.Length + remaining.Length + 1 > LeagueQuickMessagePresets.MaximumMessageLength)
                {
                    lines.Add(line.TrimEnd());
                    line = prefix;
                }
                while (line.Length + remaining.Length > LeagueQuickMessagePresets.MaximumMessageLength)
                {
                    var count = LeagueQuickMessagePresets.MaximumMessageLength - line.Length;
                    if (count <= 0) break;
                    if (count < remaining.Length && char.IsHighSurrogate(remaining[count - 1])) count--;
                    if (count <= 0) break;
                    lines.Add(line + remaining.Substring(0, count));
                    remaining = remaining.Substring(count);
                    line = prefix;
                }
                if (remaining.Length > 0) line += remaining + " ";
            }
            if (line.Length > prefix.Length) lines.Add(line.TrimEnd());
        }
    }
}
