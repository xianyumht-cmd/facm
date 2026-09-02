namespace FACM.Core.Mayhem;

/// <summary>
/// The icon-first projection used by the automatic ChampSelect surface. The result contract keeps
/// every source row; this helper only groups and paginates the rows that are visible at once.
/// </summary>
public static class MayhemAutomaticGuideProjection
{
    public const int AugmentsPerPage = 6;

    public static IReadOnlyList<string> SupportedRarities { get; } =
        new[] { "棱彩", "黄金", "白银" };

    public static IReadOnlyList<MayhemAugmentRow> NormalizeAugments(
        IEnumerable<MayhemAugmentRow>? rows)
    {
        if (rows is null) return Array.Empty<MayhemAugmentRow>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return rows
            .Where(row => row is not null && !string.IsNullOrWhiteSpace(row.Name))
            .Select((row, index) => (Row: row, Index: index))
            .OrderBy(item => item.Row.Rank > 0 ? item.Row.Rank : int.MaxValue)
            .ThenBy(item => item.Index)
            .Where(item => seen.Add(Identity(item.Row)))
            .Select(item => item.Row)
            .ToArray();
    }

    public static IReadOnlyList<MayhemAugmentRow> ForRarity(
        IEnumerable<MayhemAugmentRow>? rows,
        string rarity)
    {
        if (string.IsNullOrWhiteSpace(rarity)) return Array.Empty<MayhemAugmentRow>();
        return NormalizeAugments(rows)
            .Where(row => string.Equals(row.Rarity, rarity.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static IReadOnlyList<MayhemAugmentRow> Page(
        IEnumerable<MayhemAugmentRow>? rows,
        string rarity,
        int page)
    {
        var filtered = ForRarity(rows, rarity);
        var safePage = Math.Max(0, page);
        return filtered.Skip(safePage * AugmentsPerPage).Take(AugmentsPerPage).ToArray();
    }

    public static int PageCount(IEnumerable<MayhemAugmentRow>? rows, string rarity)
    {
        var count = ForRarity(rows, rarity).Count;
        return count == 0 ? 0 : (count + AugmentsPerPage - 1) / AugmentsPerPage;
    }

    public static bool IsCurrentGeneration(long requestGeneration, long currentGeneration) =>
        requestGeneration > 0 && requestGeneration == currentGeneration;

    private static string Identity(MayhemAugmentRow row)
    {
        var value = !string.IsNullOrWhiteSpace(row.Id)
            ? row.Id
            : !string.IsNullOrWhiteSpace(row.Slug) ? row.Slug : row.Name;
        return value.Trim();
    }
}
