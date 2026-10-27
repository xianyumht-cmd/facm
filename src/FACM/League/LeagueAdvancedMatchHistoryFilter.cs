using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FACM.Services;

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
        CsBetween,
        HasItem,
        HasSpell,
        HasPerk,
        HasPerkStyle,
        HasAugment
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
                error = UiTextKeys.LeaguePlayerAdvancedErrorRoot;
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
                error = UiTextKeys.LeaguePlayerAdvancedErrorComplex;
                return false;
            }
            var copy = new LeagueAdvancedMatchNode { Kind = source.Kind, Value = source.Value };
            if (LeagueAdvancedMatchNode.IsGroup(source.Kind))
            {
                if (source.Kind == LeagueAdvancedMatchKind.Not && source.Children.Count != 1)
                {
                    error = UiTextKeys.LeaguePlayerAdvancedErrorNot;
                    return false;
                }
                if (depth > 0 && source.Children.Count == 0)
                {
                    error = UiTextKeys.LeaguePlayerAdvancedErrorEmptyGroup;
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
                    error = UiTextKeys.LeaguePlayerAdvancedErrorLeaf;
                    return false;
                }
                int number;
                int minimum;
                int maximum;
                int slot;
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
                            error = UiTextKeys.LeaguePlayerAdvancedErrorId;
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
                            error = UiTextKeys.LeaguePlayerAdvancedErrorRange;
                            return false;
                        }
                        copy.Value = minimum.ToString(CultureInfo.InvariantCulture) + "," +
                            maximum.ToString(CultureInfo.InvariantCulture);
                        break;
                    case LeagueAdvancedMatchKind.HasItem:
                    case LeagueAdvancedMatchKind.HasSpell:
                    case LeagueAdvancedMatchKind.HasPerk:
                    case LeagueAdvancedMatchKind.HasPerkStyle:
                    case LeagueAdvancedMatchKind.HasAugment:
                        var size = source.Kind == LeagueAdvancedMatchKind.HasItem ? 7 :
                            source.Kind == LeagueAdvancedMatchKind.HasSpell ||
                            source.Kind == LeagueAdvancedMatchKind.HasPerkStyle ? 2 : 6;
                        if (!TryIdAndSlot(source.Value, size, out number, out slot))
                        {
                            error = UiTextKeys.LeaguePlayerAdvancedErrorSlot;
                            return false;
                        }
                        copy.Value = number.ToString(CultureInfo.InvariantCulture) +
                            (slot >= 0 ? "@" + slot.ToString(CultureInfo.InvariantCulture) : string.Empty);
                        break;
                    case LeagueAdvancedMatchKind.IsGameMode:
                    case LeagueAdvancedMatchKind.HasPlayer:
                        if (string.IsNullOrWhiteSpace(source.Value) || source.Value.Length > 128 ||
                            source.Value.Any(char.IsControl))
                        {
                            error = UiTextKeys.LeaguePlayerAdvancedErrorIdentity;
                            return false;
                        }
                        copy.Value = source.Value.Trim();
                        break;
                    default:
                        error = UiTextKeys.LeaguePlayerAdvancedErrorUnsupported;
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

        private static bool TryIdAndSlot(string value, int slotCount, out int id, out int slot)
        {
            id = slot = -1;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var parts = value.Trim().Split('@');
            if (parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.None,
                CultureInfo.InvariantCulture, out id) || id <= 0) return false;
            return parts.Length == 1 ||
                (int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out slot) &&
                 slot >= 0 && slot < slotCount);
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
                case LeagueAdvancedMatchKind.HasItem:
                {
                    int id;
                    int slot;
                    TryIdAndSlot(node.Value, 7, out id, out slot);
                    if (match.RoleBoundItemId == id) return Evidence.Yes;
                    var inItems = ContainsSlot(match.ItemIds, id, slot, 7);
                    return inItems == Evidence.No && !match.RoleBoundItemId.HasValue ? Evidence.Unknown : inItems;
                }
                case LeagueAdvancedMatchKind.HasSpell:
                    return MatchSlot(match.SpellIds, node.Value, 2);
                case LeagueAdvancedMatchKind.HasPerk:
                    return MatchSlot(match.PerkIds, node.Value, 6);
                case LeagueAdvancedMatchKind.HasPerkStyle:
                    return MatchSlot(match.PerkStyleIds, node.Value, 2);
                case LeagueAdvancedMatchKind.HasAugment:
                    return MatchSlot(match.AugmentIds, node.Value, 6);
                default:
                    return Evidence.Unknown;
            }
        }

        private static Evidence MatchSlot(int[] values, string expression, int slotCount)
        {
            int id;
            int slot;
            TryIdAndSlot(expression, slotCount, out id, out slot);
            return ContainsSlot(values, id, slot, slotCount);
        }

        private static Evidence ContainsSlot(int[] values, int id, int slot, int expectedCount)
        {
            if (values == null || values.Length != expectedCount) return Evidence.Unknown;
            if (slot >= 0)
            {
                if (slot >= values.Length || values[slot] < 0) return Evidence.Unknown;
                return Match(values[slot] == id);
            }
            if (Array.IndexOf(values, id) >= 0) return Evidence.Yes;
            return values.Any(value => value < 0) ? Evidence.Unknown : Evidence.No;
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
