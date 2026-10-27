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
        HasAugment,
        IsPosition,
        KdaBetween,
        LevelBetween,
        GoldBetween,
        GoldSpentBetween,
        DamageToChampionsBetween,
        PhysicalDamageToChampionsBetween,
        MagicDamageToChampionsBetween,
        TrueDamageToChampionsBetween,
        DamageTakenBetween,
        PhysicalDamageTakenBetween,
        MagicDamageTakenBetween,
        TrueDamageTakenBetween,
        DamageToTowersBetween,
        HealBetween,
        VisionScoreBetween,
        TimeCCingOthersBetween,
        SoloKillsBetween,
        KillParticipationBetween,
        DamageGoldEfficiencyBetween,
        ChampionDamageShareBetween,
        DamageTakenShareBetween,
        GoldShareBetween,
        DoubleKillsBetween,
        TripleKillsBetween,
        QuadraKillsBetween,
        PentaKillsBetween,
        Player,
        Anyone,
        Everyone,
        All,
        Allies,
        Enemies
    }

    internal sealed class LeagueAdvancedMatchNode
    {
        internal LeagueAdvancedMatchKind Kind;
        internal string Value;
        internal readonly List<LeagueAdvancedMatchNode> Children = new List<LeagueAdvancedMatchNode>();

        internal static bool IsGroup(LeagueAdvancedMatchKind kind)
        {
            return kind == LeagueAdvancedMatchKind.And || kind == LeagueAdvancedMatchKind.Or ||
                kind == LeagueAdvancedMatchKind.Not || kind == LeagueAdvancedMatchKind.Player ||
                kind == LeagueAdvancedMatchKind.Anyone || kind == LeagueAdvancedMatchKind.Everyone ||
                kind == LeagueAdvancedMatchKind.All || kind == LeagueAdvancedMatchKind.Allies ||
                kind == LeagueAdvancedMatchKind.Enemies;
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
                if (source.Kind != LeagueAdvancedMatchKind.And && source.Kind != LeagueAdvancedMatchKind.Or &&
                    source.Children.Count != 1)
                {
                    error = UiTextKeys.LeaguePlayerAdvancedErrorNot;
                    return false;
                }
                if (source.Kind == LeagueAdvancedMatchKind.Player || source.Kind == LeagueAdvancedMatchKind.Allies ||
                    source.Kind == LeagueAdvancedMatchKind.Enemies)
                {
                    if (string.IsNullOrWhiteSpace(source.Value) || source.Value.Length > 128 ||
                        source.Value.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                    {
                        error = UiTextKeys.LeaguePlayerAdvancedErrorIdentity;
                        return false;
                    }
                    copy.Value = source.Value.Trim();
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
                        if (!TryRange(source.Value, out minimum, out maximum))
                        {
                            error = UiTextKeys.LeaguePlayerAdvancedErrorRange;
                            return false;
                        }
                        copy.Value = minimum.ToString(CultureInfo.InvariantCulture) + "," +
                            maximum.ToString(CultureInfo.InvariantCulture);
                        break;
                    case LeagueAdvancedMatchKind.KillsBetween:
                    case LeagueAdvancedMatchKind.DeathsBetween:
                    case LeagueAdvancedMatchKind.AssistsBetween:
                    case LeagueAdvancedMatchKind.CsBetween:
                    case LeagueAdvancedMatchKind.KdaBetween:
                    case LeagueAdvancedMatchKind.LevelBetween:
                    case LeagueAdvancedMatchKind.GoldBetween:
                    case LeagueAdvancedMatchKind.GoldSpentBetween:
                    case LeagueAdvancedMatchKind.DamageToChampionsBetween:
                    case LeagueAdvancedMatchKind.PhysicalDamageToChampionsBetween:
                    case LeagueAdvancedMatchKind.MagicDamageToChampionsBetween:
                    case LeagueAdvancedMatchKind.TrueDamageToChampionsBetween:
                    case LeagueAdvancedMatchKind.DamageTakenBetween:
                    case LeagueAdvancedMatchKind.PhysicalDamageTakenBetween:
                    case LeagueAdvancedMatchKind.MagicDamageTakenBetween:
                    case LeagueAdvancedMatchKind.TrueDamageTakenBetween:
                    case LeagueAdvancedMatchKind.DamageToTowersBetween:
                    case LeagueAdvancedMatchKind.HealBetween:
                    case LeagueAdvancedMatchKind.VisionScoreBetween:
                    case LeagueAdvancedMatchKind.TimeCCingOthersBetween:
                    case LeagueAdvancedMatchKind.SoloKillsBetween:
                    case LeagueAdvancedMatchKind.KillParticipationBetween:
                    case LeagueAdvancedMatchKind.DamageGoldEfficiencyBetween:
                    case LeagueAdvancedMatchKind.ChampionDamageShareBetween:
                    case LeagueAdvancedMatchKind.DamageTakenShareBetween:
                    case LeagueAdvancedMatchKind.GoldShareBetween:
                    case LeagueAdvancedMatchKind.DoubleKillsBetween:
                    case LeagueAdvancedMatchKind.TripleKillsBetween:
                    case LeagueAdvancedMatchKind.QuadraKillsBetween:
                    case LeagueAdvancedMatchKind.PentaKillsBetween:
                        double first;
                        double last;
                        string mode;
                        if (!TryMeasuredRange(source.Value, MetricKey(source.Kind) != null,
                            out mode, out first, out last))
                        {
                            error = UiTextKeys.LeaguePlayerAdvancedErrorDecimalRange;
                            return false;
                        }
                        copy.Value = (mode == null ? string.Empty : mode + ":") +
                            first.ToString("R", CultureInfo.InvariantCulture) + "," +
                            last.ToString("R", CultureInfo.InvariantCulture);
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
                    case LeagueAdvancedMatchKind.IsPosition:
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

        private static bool TryDecimalRange(string value, out double minimum, out double maximum)
        {
            minimum = maximum = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var parts = value.Split(',');
            return parts.Length == 2 &&
                double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out minimum) &&
                double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out maximum) &&
                !double.IsNaN(minimum) && !double.IsInfinity(minimum) &&
                !double.IsNaN(maximum) && !double.IsInfinity(maximum) &&
                minimum >= 0 && maximum >= minimum;
        }

        private static bool TryMeasuredRange(string value, bool allowRelative,
            out string mode, out double minimum, out double maximum)
        {
            mode = null;
            minimum = maximum = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var colon = value.IndexOf(':');
            if (colon < 0) return TryDecimalRange(value, out minimum, out maximum);
            if (!allowRelative || colon == 0 || value.IndexOf(':', colon + 1) >= 0) return false;
            var candidate = value.Substring(0, colon).Trim();
            if (candidate != "teamShare" && candidate != "teamMaxShare" &&
                candidate != "gameShare" && candidate != "gameMaxShare") return false;
            if (!TryDecimalRange(value.Substring(colon + 1), out minimum, out maximum)) return false;
            mode = candidate;
            return true;
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
            return match != null && Evaluate(Root, match, match,
                match.ParticipantRoster, false, false) == Evidence.Yes;
        }

        internal static List<LeaguePlayerMatchSummary> Apply(IEnumerable<LeaguePlayerMatchSummary> matches,
            LeagueAdvancedMatchHistoryFilter filter)
        {
            if (matches == null) return new List<LeaguePlayerMatchSummary>();
            return matches.Where(match => match != null && (filter == null || filter.Matches(match))).ToList();
        }

        private static Evidence Evaluate(LeagueAdvancedMatchNode node, LeaguePlayerMatchSummary root,
            LeaguePlayerMatchSummary subject, LeaguePlayerMatchSummary[] scope,
            bool participantScope, bool scopedSet)
        {
            var match = participantScope || scopedSet ? subject : root;
            switch (node.Kind)
            {
                case LeagueAdvancedMatchKind.And:
                {
                    var unknown = false;
                    foreach (var child in node.Children)
                    {
                        var state = Evaluate(child, root, subject, scope, participantScope, scopedSet);
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
                        var state = Evaluate(child, root, subject, scope, participantScope, scopedSet);
                        if (state == Evidence.Yes) return Evidence.Yes;
                        if (state == Evidence.Unknown) unknown = true;
                    }
                    return unknown ? Evidence.Unknown : Evidence.No;
                }
                case LeagueAdvancedMatchKind.Not:
                {
                    var inner = Evaluate(node.Children[0], root, subject, scope, participantScope, scopedSet);
                    return inner == Evidence.Yes ? Evidence.No : inner == Evidence.No ? Evidence.Yes : Evidence.Unknown;
                }
                case LeagueAdvancedMatchKind.All:
                    return root.ParticipantRoster == null ? Evidence.Unknown :
                        Evaluate(node.Children[0], root,
                            root.ParticipantRoster.FirstOrDefault() ?? root, root.ParticipantRoster, false, true);
                case LeagueAdvancedMatchKind.Player:
                {
                    if (scope == null) return Evidence.Unknown;
                    var selected = FindParticipant(scope, node.Value, root);
                    return selected != null ? Evaluate(node.Children[0], root, selected, scope, true, true) :
                        root.RosterIdentitiesComplete ? Evidence.No : Evidence.Unknown;
                }
                case LeagueAdvancedMatchKind.Anyone:
                case LeagueAdvancedMatchKind.Everyone:
                {
                    if (scope == null) return Evidence.Unknown;
                    var any = node.Kind == LeagueAdvancedMatchKind.Anyone;
                    var unknown = false;
                    foreach (var member in scope)
                    {
                        var state = Evaluate(node.Children[0], root, member, scope, true, true);
                        if (any && state == Evidence.Yes) return Evidence.Yes;
                        if (!any && state == Evidence.No) return Evidence.No;
                        if (state == Evidence.Unknown) unknown = true;
                    }
                    return unknown ? Evidence.Unknown : any ? Evidence.No : Evidence.Yes;
                }
                case LeagueAdvancedMatchKind.Allies:
                case LeagueAdvancedMatchKind.Enemies:
                {
                    if (scope == null) return Evidence.Unknown;
                    var reference = FindParticipant(scope, node.Value, root);
                    if (reference == null) return root.RosterIdentitiesComplete ? Evidence.No : Evidence.Unknown;
                    if (reference.TeamId <= 0 || scope.Any(member => member.TeamId <= 0))
                        return Evidence.Unknown;
                    var same = node.Kind == LeagueAdvancedMatchKind.Allies;
                    var members = scope.Where(member => (member.TeamId == reference.TeamId) == same).ToArray();
                    return Evaluate(node.Children[0], root, members.FirstOrDefault() ?? root, members, false, true);
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
                    if (participantScope)
                        return match.ChampionId <= 0 ? Evidence.Unknown : Match(match.ChampionId == ParseId(node.Value));
                    if (scopedSet)
                    {
                        if (scope == null) return Evidence.Unknown;
                        if (scope.Any(p => p.ChampionId == ParseId(node.Value))) return Evidence.Yes;
                        return scope.Any(p => p.ChampionId <= 0) ? Evidence.Unknown : Evidence.No;
                    }
                    return match.MatchChampionIds == null ? Evidence.Unknown :
                        Match(Array.IndexOf(match.MatchChampionIds, ParseId(node.Value)) >= 0);
                case LeagueAdvancedMatchKind.HasPlayer:
                    if (scopedSet)
                    {
                        if (scope == null) return Evidence.Unknown;
                        if (scope.Any(p => string.Equals(p.PuuId, node.Value, StringComparison.Ordinal))) return Evidence.Yes;
                        return root.RosterIdentitiesComplete ? Evidence.No : Evidence.Unknown;
                    }
                    return match.MatchPlayers == null ? Evidence.Unknown :
                        Match(match.MatchPlayers.Any(p => p != null && string.Equals(p.PuuId, node.Value, StringComparison.Ordinal)));
                case LeagueAdvancedMatchKind.KillsBetween:
                    return !match.ParticipantResolved || !match.StatisticsAvailable ? Evidence.Unknown :
                        MeasuredRange(node.Value, match, node.Kind, match.Kills);
                case LeagueAdvancedMatchKind.DeathsBetween:
                    return !match.ParticipantResolved || !match.StatisticsAvailable ? Evidence.Unknown :
                        MeasuredRange(node.Value, match, node.Kind, match.Deaths);
                case LeagueAdvancedMatchKind.AssistsBetween:
                    return !match.ParticipantResolved || !match.StatisticsAvailable ? Evidence.Unknown :
                        MeasuredRange(node.Value, match, node.Kind, match.Assists);
                case LeagueAdvancedMatchKind.CsBetween:
                    return !match.ParticipantResolved || !match.StatisticsAvailable ? Evidence.Unknown :
                        MeasuredRange(node.Value, match, node.Kind, match.CreepScore);
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
                case LeagueAdvancedMatchKind.IsPosition:
                    return string.IsNullOrWhiteSpace(match.Position) ? Evidence.Unknown :
                        Match(string.Equals(match.Position, node.Value, StringComparison.Ordinal));
                case LeagueAdvancedMatchKind.KdaBetween:
                    return !match.ParticipantResolved || !match.StatisticsAvailable ? Evidence.Unknown :
                        InDecimalRange(node.Value, ((double)match.Kills + match.Assists) / Math.Max(1, match.Deaths));
                case LeagueAdvancedMatchKind.LevelBetween: return MeasuredRange(node.Value, match, node.Kind, match.Level);
                case LeagueAdvancedMatchKind.GoldBetween: return MeasuredRange(node.Value, match, node.Kind, match.GoldEarned);
                case LeagueAdvancedMatchKind.GoldSpentBetween: return MeasuredRange(node.Value, match, node.Kind, match.GoldSpent);
                case LeagueAdvancedMatchKind.DamageToChampionsBetween: return MeasuredRange(node.Value, match, node.Kind, match.DamageDealtToChampions);
                case LeagueAdvancedMatchKind.PhysicalDamageToChampionsBetween: return MeasuredRange(node.Value, match, node.Kind, match.PhysicalDamageToChampions);
                case LeagueAdvancedMatchKind.MagicDamageToChampionsBetween: return MeasuredRange(node.Value, match, node.Kind, match.MagicDamageToChampions);
                case LeagueAdvancedMatchKind.TrueDamageToChampionsBetween: return MeasuredRange(node.Value, match, node.Kind, match.TrueDamageToChampions);
                case LeagueAdvancedMatchKind.DamageTakenBetween: return MeasuredRange(node.Value, match, node.Kind, match.DamageTaken);
                case LeagueAdvancedMatchKind.PhysicalDamageTakenBetween: return MeasuredRange(node.Value, match, node.Kind, match.PhysicalDamageTaken);
                case LeagueAdvancedMatchKind.MagicDamageTakenBetween: return MeasuredRange(node.Value, match, node.Kind, match.MagicDamageTaken);
                case LeagueAdvancedMatchKind.TrueDamageTakenBetween: return MeasuredRange(node.Value, match, node.Kind, match.TrueDamageTaken);
                case LeagueAdvancedMatchKind.DamageToTowersBetween: return MeasuredRange(node.Value, match, node.Kind, match.DamageToTowers);
                case LeagueAdvancedMatchKind.HealBetween: return MeasuredRange(node.Value, match, node.Kind, match.TotalHeal);
                case LeagueAdvancedMatchKind.VisionScoreBetween: return MeasuredRange(node.Value, match, node.Kind, match.VisionScore);
                case LeagueAdvancedMatchKind.TimeCCingOthersBetween: return MeasuredRange(node.Value, match, node.Kind, match.TimeCCingOthers);
                case LeagueAdvancedMatchKind.SoloKillsBetween: return MeasuredRange(node.Value, match, node.Kind, match.SoloKills);
                case LeagueAdvancedMatchKind.KillParticipationBetween: return InDecimalRange(node.Value, Percent(match.KillParticipation));
                case LeagueAdvancedMatchKind.DamageGoldEfficiencyBetween: return InDecimalRange(node.Value, Percent(match.DamageGoldEfficiency));
                case LeagueAdvancedMatchKind.ChampionDamageShareBetween: return InDecimalRange(node.Value, Percent(match.ChampionDamageShare));
                case LeagueAdvancedMatchKind.DamageTakenShareBetween: return InDecimalRange(node.Value, Percent(match.DamageTakenShare));
                case LeagueAdvancedMatchKind.GoldShareBetween: return InDecimalRange(node.Value, Percent(match.GoldShare));
                case LeagueAdvancedMatchKind.DoubleKillsBetween: return MeasuredRange(node.Value, match, node.Kind, match.DoubleKills);
                case LeagueAdvancedMatchKind.TripleKillsBetween: return MeasuredRange(node.Value, match, node.Kind, match.TripleKills);
                case LeagueAdvancedMatchKind.QuadraKillsBetween: return MeasuredRange(node.Value, match, node.Kind, match.QuadraKills);
                case LeagueAdvancedMatchKind.PentaKillsBetween: return MeasuredRange(node.Value, match, node.Kind, match.PentaKills);
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

        private static LeaguePlayerMatchSummary FindParticipant(LeaguePlayerMatchSummary[] scope,
            string puuid, LeaguePlayerMatchSummary root)
        {
            if (scope == null) return null;
            if (string.Equals(puuid, "self", StringComparison.Ordinal))
                return root.ParticipantId <= 0 ? null :
                    scope.FirstOrDefault(p => p.ParticipantId == root.ParticipantId);
            return scope.FirstOrDefault(p => string.Equals(p.PuuId, puuid, StringComparison.Ordinal));
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

        private static double? Percent(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) ?
                (double?)(value.Value * 100d) : null;
        }

        private static string MetricKey(LeagueAdvancedMatchKind kind)
        {
            switch (kind)
            {
                case LeagueAdvancedMatchKind.KillsBetween: return "kills";
                case LeagueAdvancedMatchKind.DeathsBetween: return "deaths";
                case LeagueAdvancedMatchKind.AssistsBetween: return "assists";
                case LeagueAdvancedMatchKind.CsBetween: return "cs";
                case LeagueAdvancedMatchKind.LevelBetween: return "champLevel";
                case LeagueAdvancedMatchKind.GoldBetween: return "goldEarned";
                case LeagueAdvancedMatchKind.GoldSpentBetween: return "goldSpent";
                case LeagueAdvancedMatchKind.DamageToChampionsBetween: return "totalDamageDealtToChampions";
                case LeagueAdvancedMatchKind.PhysicalDamageToChampionsBetween: return "physicalDamageDealtToChampions";
                case LeagueAdvancedMatchKind.MagicDamageToChampionsBetween: return "magicDamageDealtToChampions";
                case LeagueAdvancedMatchKind.TrueDamageToChampionsBetween: return "trueDamageDealtToChampions";
                case LeagueAdvancedMatchKind.DamageTakenBetween: return "totalDamageTaken";
                case LeagueAdvancedMatchKind.PhysicalDamageTakenBetween: return "physicalDamageTaken";
                case LeagueAdvancedMatchKind.MagicDamageTakenBetween: return "magicDamageTaken";
                case LeagueAdvancedMatchKind.TrueDamageTakenBetween: return "trueDamageTaken";
                case LeagueAdvancedMatchKind.DamageToTowersBetween: return "damageDealtToTurrets";
                case LeagueAdvancedMatchKind.HealBetween: return "totalHeal";
                case LeagueAdvancedMatchKind.VisionScoreBetween: return "visionScore";
                case LeagueAdvancedMatchKind.TimeCCingOthersBetween: return "timeCCingOthers";
                case LeagueAdvancedMatchKind.SoloKillsBetween: return "soloKills";
                case LeagueAdvancedMatchKind.DoubleKillsBetween: return "doubleKills";
                case LeagueAdvancedMatchKind.TripleKillsBetween: return "tripleKills";
                case LeagueAdvancedMatchKind.QuadraKillsBetween: return "quadraKills";
                case LeagueAdvancedMatchKind.PentaKillsBetween: return "pentaKills";
                default: return null;
            }
        }

        private static Evidence MeasuredRange(string expression, LeaguePlayerMatchSummary match,
            LeagueAdvancedMatchKind kind, double? ownValue)
        {
            if (!ownValue.HasValue || double.IsNaN(ownValue.Value) || double.IsInfinity(ownValue.Value))
                return Evidence.Unknown;
            string mode;
            double minimum;
            double maximum;
            if (!TryMeasuredRange(expression, true, out mode, out minimum, out maximum)) return Evidence.Unknown;
            if (mode == null) return Match(ownValue.Value >= minimum && ownValue.Value <= maximum);
            double[] baselines;
            var key = MetricKey(kind);
            if (key == null || match.MetricBaselines == null ||
                !match.MetricBaselines.TryGetValue(key, out baselines) || baselines == null ||
                baselines.Length != 4) return Evidence.Unknown;
            var index = mode == "teamShare" ? 0 : mode == "teamMaxShare" ? 1 :
                mode == "gameShare" ? 2 : 3;
            var baseline = baselines[index];
            if (double.IsNaN(baseline) || double.IsInfinity(baseline) || baseline < 0) return Evidence.Unknown;
            var percent = baseline == 0 ? 0 : ownValue.Value / baseline * 100d;
            if (double.IsNaN(percent) || double.IsInfinity(percent)) return Evidence.Unknown;
            return Match(percent >= minimum && percent <= maximum);
        }

        private static Evidence InDecimalRange(string expression, double? value)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                return Evidence.Unknown;
            double minimum;
            double maximum;
            return Match(TryDecimalRange(expression, out minimum, out maximum) &&
                value.Value >= minimum && value.Value <= maximum);
        }

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
