namespace FACM.Core.Mayhem;

/// <summary>
/// Product/domain decision projection preserved from FACM 3.5.15. The policy consumes already
/// parsed augment statistics; network/HTML details stay outside Core.
/// </summary>
public static class MayhemAugmentDecisionPolicy
{
    public const string StableRouteTitle = "稳定赢法";
    public const string StableRouteHint = "胜率和热门度都不错，没把握时优先考虑";
    public const string HighWinRouteTitle = "高上限玩法";
    public const string HighWinRouteHint = "单强化胜率更突出，适合追求强度";
    public const string PopularRouteTitle = "热门好上手";
    public const string PopularRouteHint = "选择率更高，实战更常见";

    public const double StableWinWeight = 0.72d;
    public const double StablePickWeight = 0.28d;

    public static List<MayhemDecisionRoute> BuildRoutes(IEnumerable<MayhemAugmentRow>? rows)
    {
        var usable = (rows ?? Array.Empty<MayhemAugmentRow>())
            .Where(row => row is not null && !string.IsNullOrWhiteSpace(row.Name) &&
                          (row.WinRate.HasValue || row.PickRate.HasValue))
            .ToList();
        var routes = new List<MayhemDecisionRoute>();
        if (usable.Count == 0) return routes;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRoute(
            routes,
            used,
            usable.OrderByDescending(row => StableScore(row)),
            StableRouteTitle,
            StableRouteHint,
            StableScore);
        AddRoute(
            routes,
            used,
            usable.OrderByDescending(row => row.WinRate ?? -1d),
            HighWinRouteTitle,
            HighWinRouteHint,
            row => row.WinRate ?? 0d);
        AddRoute(
            routes,
            used,
            usable.OrderByDescending(row => row.PickRate ?? -1d),
            PopularRouteTitle,
            PopularRouteHint,
            row => row.PickRate ?? 0d);
        return routes;
    }

    public static double StableScore(MayhemAugmentRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return (row.WinRate ?? 0d) * StableWinWeight + (row.PickRate ?? 0d) * StablePickWeight;
    }

    private static void AddRoute(
        ICollection<MayhemDecisionRoute> routes,
        ISet<string> used,
        IEnumerable<MayhemAugmentRow> ordered,
        string title,
        string hint,
        Func<MayhemAugmentRow, double> score)
    {
        foreach (var row in ordered)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || !used.Add(row.Name)) continue;
            routes.Add(new MayhemDecisionRoute
            {
                Title = title,
                AugmentName = row.Name,
                Hint = hint,
                Score = score(row)
            });
            return;
        }
    }
}
