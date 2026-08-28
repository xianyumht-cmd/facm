namespace FACM.Core.Mayhem;

/// <summary>
/// Platform-neutral snapshot of the latest official CN ARAM Mayhem patch article.
/// The source adapter owns article discovery/parsing; consumers only see structured patch facts.
/// </summary>
public sealed class MayhemOfficialPatchSnapshot
{
    public string Patch { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public Dictionary<string, List<string>> ChampionChanges { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> FindChampionChanges(params string?[] names)
    {
        var targets = (names ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(MayhemChampionAliases.Normalize)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targets.Length == 0) return [];

        foreach (var pair in ChampionChanges)
        {
            var key = MayhemChampionAliases.Normalize(pair.Key);
            if (targets.Any(target => key == target || key.Contains(target, StringComparison.Ordinal) || target.Contains(key, StringComparison.Ordinal)))
                return pair.Value.ToArray();
        }
        return [];
    }
}

public interface IMayhemOfficialPatchService
{
    Task<MayhemOfficialPatchSnapshot?> FetchLatestAsync(CancellationToken cancellationToken = default);
}
