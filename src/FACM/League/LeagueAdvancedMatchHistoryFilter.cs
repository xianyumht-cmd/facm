using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FACM.League
{
    internal enum LeagueAdvancedMatchKind
    {
        And,
        Or,
        Not,
        IsGameMode,
        IsQueue,
        IsMap,
        DurationBetween,
        IsWin,
        IsLoss,
        HasChampion,
        HasPlayer,
        KillsBetween,
        DeathsBetween,
        AssistsBetween,
        CsBetween
    }

    internal sealed class LeagueAdvancedMatchNode
    {
        internal LeagueAdvancedMatchKind Kind;
        internal string Value;
        internal readonly List<LeagueAdvancedMatchNode> Children = new List<LeagueAdvancedMatchNode>();

        internal static bool IsGroup(LeagueAdvancedMatchKind kind)
        {
            return kind == LeagueAdvancedMatchKind.And || kind == LeagueAdvancedMatchKind.Or ||
                kind == LeagueAdvancedMatchKind.Not;
        }
    }

    // Read-only, local evaluation over the page already loaded by LeaguePlayerDataService.
    // Unknown evidence remains unknown under NOT, rather than turning a partial match into a false positive.
    internal sealed class LeagueAdvancedMatchHistoryFilter
    {
        private enum Evidence { No, Yes, Unknown }

        private const int MaximumNodes = 40;
        private const int MaximumDepth = 7;
        internal readonly LeagueAdvancedMatchNode Root;

        private LeagueAdvancedMatchHistoryFilter(LeagueAdvancedMatchNode root)
        {
            Root = root;
        }

        internal static bool TryCreate(LeagueAdvancedMatchNode root, out LeagueAdvancedMatchHistoryFilter filter,
            out string error)
        {
            filter = null;
            error = null;
            if (root == null || (root.Kind != LeagueAdvancedMatchKind.And && root.Kind != LeagueAdvancedMatchKind.Or))
            {
                error = "筛选根节点必须是 AND 或 OR。";
                return false;
            }
            var visited = new HashSet<LeagueAdvancedMatchNode>();
            var count = 0;
            LeagueAdvancedMatchNode copy;
            if (!TryCopy(root, 0, visited, ref count, out copy, out error)) return false;
            filter = new LeagueAdvancedMatchHistoryFilter(copy);
            return true;
        }

        private static bool TryCopy(LeagueAdvancedMatchNode source, int depth,
            HashSet<LeagueAdvancedMatchNode> visited, ref int count,
            out LeagueAdvancedMatchNode result, out string error)
        {
            result = null;
            error = null;
            if (source == null || depth > MaximumDepth || ++count > MaximumNodes || !visited.Add(source))
            {
                error = "筛选条件过多、嵌套过深或存在循环引用。";
                return false;
            }
            var copy = new LeagueAdvancedMatchNode { Kind = source.Kind, Value = source.Value };
            if (LeagueAdvancedMatchNode.IsGroup(source.Kind))
            {
                if (source.Kind == LeagueAdvancedMatchKind.Not && source.Children.Count != 1)
                {
                    error = "NOT 条件组必须包含且只包含一个条件。";
                    return false;
                }
                if (depth > 0 && source.Children.Count == 0)
                {
                    error = "非根条件组不能留空。";
                    return false;
                }
                foreach (var child in source.Children)
                {
                    LeagueAdvancedMatchNode childCopy;
                    if (!TryCopy(child, depth + 1, visited, ref count, out childCopy, out error)) return false;
                    copy.Children.Add(childCopy);
                }
            }
            else
            {
                if (source.Children.Count != 0)
                {
                    error = "单个条件不能包含子条件。";
                    return false;
                }
                int number;
                int minimum;
                int maximum;
                switch (source.Kind)
                {
                    case LeagueAdvancedMatchKind.IsWin:
                    case LeagueAdvancedMatchKind.IsLoss:
                        break;
                    case LeagueAdvancedMatchKind.IsQueue:
                    case LeagueAdvancedMatchKind.IsMap:
                    case LeagueAdvancedMatchKind.HasChampion:
                        if (!int.TryParse(source.Value, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number <= 0)
                        {
                            error = "队列、地图和英雄需要填写正整数 ID。";
                            return false;
                        }
                        copy.Value = number.ToString(CultureInfo.InvariantCulture);
                        break;
                    case LeagueAdvancedMatchKind.DurationBetween:
                    case LeagueAdvancedMatchKind.KillsBetween:
                    case LeagueAdvancedMatchKind.DeathsBetween:
                    case LeagueAdvancedMatchKind.AssistsBetween:
                    case LeagueAdvancedMatchKind.CsBetween:
                        if (!TryRange(source.Value, out minimum, out maximum))
                        {
                            error = "范围必须填写两个非负整数：最小值,最大值（包含边界）。";
                            return false;
                        }
                        copy.Value = minimum.ToString(CultureInfo.InvariantCulture) + "," +
                            maximum.ToString(CultureInfo.InvariantCulture);
                        break;
                    case LeagueAdvancedMatchKind.IsGameMode:
                    case LeagueAdvancedMatchKind.HasPlayer:
                        if (string.IsNullOrWhiteSpace(source.Value) || source.Value.Length > 128 ||
                            source.Value.Any(char.IsControl))
                        {
                            error = "游戏模式或玩家 PUUID 不能为空、过长或包含控制字符。";
                            return false;
                        }
                        copy.Value = source.Value.Trim();
                        break;
                    default:
                        error = "不支持的高级筛选条件。";
                        return false;
                }
            }
            result = copy;
            return true;
        }

        private static bool TryRange(string value, out int minimum, out int maximum)
        {
            minimum = maximum = 0;
            if (string.IsNullOrEmpty(value)) return false;
            var parts = value.Split(',');
            return parts.Length == 2 &&
                int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out minimum) &&
                int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out maximum) &&
                minimum >= 0 && maximum >= minimum;
        }

        internal bool Matches(LeaguePlayerMatchSummary match)
        {
            return match != null && Evaluate(Root, match) == Evidence.Yes;
        }

        internal static List<LeaguePlayerMatchSummary> Apply(IEnumerable<LeaguePlayerMatchSummary> matches,
            LeagueAdvancedMatchHistoryFilter filter)
        {
            if (matches == null) return new List<LeaguePlayerMatchSummary>();
            return matches.Where(match => match != null && (filter == null || filter.Matches(match))).ToList();
        }

        private static Evidence Evaluate(LeagueAdvancedMatchNode node, LeaguePlayerMatchSummary match)
        {
            switch (node.Kind)
            {
                case LeagueAdvancedMatchKind.And:
                {
                    var unknown = false;
                    foreach (var child in node.Children)
                    {
                        var state = Evaluate(child, match);
                        if (state == Evidence.No) return Evidence.No;
                        if (state == Evidence.Unknown) unknown = true;
                    }
                    return unknown ? Evidence.Unknown : Evidence.Yes;
                }
                case LeagueAdvancedMatchKind.Or:
                {
                    if (node.Children.Count == 0) return Evidence.Yes; // Akari's empty-root OR UI convention.
                    var unknown = false;
                    foreach (var child in node.Children)
                    {
                        var state = Evaluate(child, match);
                        if (state == Evidence.Yes) return Evidence.Yes;
                        if (state == Evidence.Unknown) unknown = true;
                    }
                    return unknown ? Evidence.Unknown : Evidence.No;
                }
                case LeagueAdvancedMatchKind.Not:
                {
                    var inner = Evaluate(node.Children[0], match);
                    return inner == Evidence.Yes ? Evidence.No : inner == Evidence.No ? Evidence.Yes : Evidence.Unknown;
                }
                case LeagueAdvancedMatchKind.IsGameMode:
                    return string.IsNullOrWhiteSpace(match.GameMode) ? Evidence.Unknown :
                        Match(string.Equals(match.GameMode, node.Value, StringComparison.Ordinal));
                case LeagueAdvancedMatchKind.IsQueue:
                    return match.QueueId <= 0 ? Evidence.Unknown : Match(match.QueueId == ParseId(node.Value));
                case LeagueAdvancedMatchKind.IsMap:
                    return match.MapId <= 0 ? Evidence.Unknown : Match(match.MapId == ParseId(node.Value));
                case LeagueAdvancedMatchKind.DurationBetween:
                    return match.GameDurationSeconds <= 0 ? Evidence.Unknown : InRange(node.Value, match.GameDurationSeconds);
                case LeagueAdvancedMatchKind.IsWin:
                    return !match.ParticipantResolved || !match.StatisticsAvailable ? Evidence.Unknown : Match(match.Win);
                case LeagueAdvancedMatchKind.IsLoss:
                    return !match.ParticipantResolved || !match.StatisticsAvailable ? Evidence.Unknown : Match(!match.Win);
                case LeagueAdvancedMatchKind.HasChampion:
                    return match.MatchChampionIds == null ? Evidence.Unknown :
                        Match(Array.IndexOf(match.MatchChampionIds, ParseId(node.Value)) >= 0);
                case LeagueAdvancedMatchKind.HasPlayer:
                    return match.MatchPlayers == null ? Evidence.Unknown :
                        Match(match.MatchPlayers.Any(p => p != null && string.Equals(p.PuuId, node.Value, StringComparison.Ordinal)));
                case LeagueAdvancedMatchKind.KillsBetween:
                    return !match.ParticipantResolved || !match.StatisticsAvailable ? Evidence.Unknown : InRange(node.Value, match.Kills);
                case LeagueAdvancedMatchKind.DeathsBetween:
                    return !match.ParticipantResolved || !match.StatisticsAvailable ? Evidence.Unknown : InRange(node.Value, match.Deaths);
                case LeagueAdvancedMatchKind.AssistsBetween:
                    return !match.ParticipantResolved || !match.StatisticsAvailable ? Evidence.Unknown : InRange(node.Value, match.Assists);
                case LeagueAdvancedMatchKind.CsBetween:
                    return !match.ParticipantResolved || !match.StatisticsAvailable ? Evidence.Unknown : InRange(node.Value, match.CreepScore);
                default:
                    return Evidence.Unknown;
            }
        }

        private static Evidence Match(bool valid) { return valid ? Evidence.Yes : Evidence.No; }

        private static int ParseId(string value)
        {
            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        private static Evidence InRange(string value, int actual)
        {
            int minimum;
            int maximum;
            return TryRange(value, out minimum, out maximum) && actual >= minimum && actual <= maximum ?
                Evidence.Yes : Evidence.No;
        }
    }
}
