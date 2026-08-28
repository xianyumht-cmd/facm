using System.Net;
using System.Text.RegularExpressions;
using FACM.Core.Mayhem;

namespace FACM.Infrastructure.Mayhem;

/// <summary>
/// Detailed OP.GG Mayhem build projection preserved from FACM 3.5.15. Network access is restricted
/// to the typed MayhemBuild resource; existing base-query fields remain the fallback.
/// </summary>
internal sealed class MayhemBuildDetailsService
{
    internal const int MaximumCoreBuilds = 2;
    private readonly MayhemCachedPublicDataTransport _transport;

    public MayhemBuildDetailsService(MayhemCachedPublicDataTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task EnrichAsync(MayhemChampionResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (HasDetailedBuild(result))
        {
            EnsureFallbackSkillPriority(result);
            return;
        }

        var slug = MayhemChampionAliases.Slugify(result.ChampionSlug);
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var request = new MayhemPublicResourceRequest(MayhemPublicResourceKind.MayhemBuild, slug);
            var response = await _transport.GetAsync(
                request,
                TimeSpan.FromSeconds(1.8),
                cancellationToken,
                allowStale: true).ConfigureAwait(false);
            if (response is not null)
            {
                ApplyHtml(result, response.ReadUtf8());
                result.BuildSourceRoute = response.Route;
                result.BuildSourceStale = response.IsStale;
                result.BuildSourceStatus = HasDetailedBuild(result) ? "ok" : "empty";
            }
            else
            {
                result.BuildSourceStatus = "unavailable";
            }
        }

        ProjectLegacyBuild(result);
        EnsureFallbackSkillPriority(result);
    }

    internal static void ApplyHtmlForSmoke(MayhemChampionResult result, string? html)
    {
        ArgumentNullException.ThrowIfNull(result);
        ApplyHtml(result, html);
        ProjectLegacyBuild(result);
        EnsureFallbackSkillPriority(result);
    }

    internal static bool HasDetailedBuildForSmoke(MayhemChampionResult result) => HasDetailedBuild(result);

    private static void ApplyHtml(MayhemChampionResult result, string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return;
        var normalized = NormalizeEscapedHtml(html);

        var core = ExtractItemRows(normalized, "core_items", 3, 5).Take(MaximumCoreBuilds).ToList();
        var starterRows = ExtractItemRows(normalized, "starter_items", 2, 3);
        var bootRows = ExtractItemRows(normalized, "boots", 2, 1);
        var summoner = ExtractSummonerSpells(normalized);
        var skills = ExtractSkillPriority(normalized);

        if (core.Count > 0) result.CoreBuilds = core;
        if (starterRows.Count > 0) result.StarterItems = starterRows[0].Items.Take(3).ToList();
        if (bootRows.Count > 0) result.BootItems = bootRows[0].Items.Take(1).ToList();
        if (summoner.Count > 0) result.SummonerSpells = summoner.Take(2).ToList();
        if (skills.Count > 0) result.SkillPriority = skills.Take(3).ToList();

        if (result.CoreBuilds.Count > 0)
        {
            var first = result.CoreBuilds[0].Items.Take(5).ToList();
            result.CoreItems = first.Select(item => item.Name)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            result.CoreItemIconUrls = first.Select(item => item.IconUrl).ToList();
        }
    }

    private static List<MayhemBuildPath> ExtractItemRows(string html, string prefix, int limit, int maxItems)
    {
        var output = new List<MayhemBuildPath>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(html)) return output;

        for (var index = 0; index < limit; index++)
        {
            var marker = prefix + "_" + index;
            var start = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) continue;
            var nextMarker = prefix + "_" + (index + 1);
            var next = html.IndexOf(nextMarker, start + marker.Length, StringComparison.OrdinalIgnoreCase);
            var length = next > start ? next - start : Math.Min(9000, html.Length - start);
            if (length <= 0) continue;
            var segment = html.Substring(start, Math.Min(length, 9000));
            var items = ExtractJsonItems(segment, maxItems);
            if (items.Count == 0) items = ExtractHtmlItems(segment, maxItems, requireItemUrl: true);
            if (items.Count == 0) continue;

            var signature = string.Join("|", items.Select(item => FirstNonEmpty(item.Id, item.Name, item.IconUrl)));
            if (signature.Length == 0 || !seen.Add(signature)) continue;
            output.Add(new MayhemBuildPath { Rank = index + 1, Items = items });
        }
        return output;
    }

    private static List<MayhemBuildItem> ExtractJsonItems(string segment, int maxItems)
    {
        var output = new List<MayhemBuildItem>();
        foreach (Match match in Regex.Matches(
                     segment,
                     "\"metaId\"\\s*:\\s*(?<id>\\d+).*?\"src\"\\s*:\\s*\"(?<src>[^\"]+)\".*?\"alt\"\\s*:\\s*\"(?<name>[^\"]+)\"",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            var item = BuildItem(match.Groups["id"].Value, match.Groups["name"].Value, match.Groups["src"].Value);
            if (!IsUsefulItem(item, requireItemUrl: true) || ContainsItem(output, item)) continue;
            output.Add(item);
            if (output.Count >= maxItems) break;
        }
        return output;
    }

    private static List<MayhemBuildItem> ExtractHtmlItems(string segment, int maxItems, bool requireItemUrl)
    {
        var output = new List<MayhemBuildItem>();
        CollectHtmlImages(
            output,
            segment,
            maxItems,
            requireItemUrl,
            "<img\\b[^>]*src\\s*=\\s*[\\\"'](?<src>[^\\\"']+)[\\\"'][^>]*alt\\s*=\\s*[\\\"'](?<name>[^\\\"']+)[\\\"'][^>]*>");
        if (output.Count < maxItems)
        {
            CollectHtmlImages(
                output,
                segment,
                maxItems,
                requireItemUrl,
                "<img\\b[^>]*alt\\s*=\\s*[\\\"'](?<name>[^\\\"']+)[\\\"'][^>]*src\\s*=\\s*[\\\"'](?<src>[^\\\"']+)[\\\"'][^>]*>");
        }
        return output;
    }

    private static void CollectHtmlImages(
        ICollection<MayhemBuildItem> output,
        string segment,
        int maxItems,
        bool requireItemUrl,
        string pattern)
    {
        foreach (Match match in Regex.Matches(
                     segment,
                     pattern,
                     RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            var item = BuildItem(string.Empty, match.Groups["name"].Value, match.Groups["src"].Value);
            if (!IsUsefulItem(item, requireItemUrl) || ContainsItem(output, item)) continue;
            output.Add(item);
            if (output.Count >= maxItems) return;
        }
    }

    private static List<MayhemBuildItem> ExtractSummonerSpells(string html)
    {
        var output = new List<MayhemBuildItem>();
        var segment = SegmentAfter(html, ["SummonerSpells Table", "Summoner Spells", "召唤师技能"], 7000);
        if (string.IsNullOrWhiteSpace(segment)) return output;

        foreach (var item in ExtractHtmlItems(segment, 12, requireItemUrl: false))
        {
            if (!item.IconUrl.Contains("/spell/Summoner", StringComparison.OrdinalIgnoreCase)) continue;
            if (ContainsItem(output, item)) continue;
            output.Add(item);
            if (output.Count >= 2) break;
        }

        if (output.Count < 2)
        {
            foreach (Match match in Regex.Matches(
                         segment,
                         "\"src\"\\s*:\\s*\"(?<src>[^\"]*/spell/Summoner[^\"]+)\".*?\"alt\"\\s*:\\s*\"(?<name>[^\"]+)\"",
                         RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
            {
                var item = BuildItem(string.Empty, match.Groups["name"].Value, match.Groups["src"].Value);
                if (!IsUsefulItem(item, requireItemUrl: false) || ContainsItem(output, item)) continue;
                output.Add(item);
                if (output.Count >= 2) break;
            }
        }
        return output;
    }

    private static List<MayhemSkillPriority> ExtractSkillPriority(string html)
    {
        var output = new List<MayhemSkillPriority>();
        var segment = SegmentAfter(html, ["SkillOrder Table", "Skill Order", "技能加点"], 19000);
        if (string.IsNullOrWhiteSpace(segment)) return output;

        AddSkillMatches(
            output,
            segment,
            "<img\\b[^>]*alt\\s*=\\s*[\\\"'](?<name>[^\\\"']+)[\\\"'][^>]*src\\s*=\\s*[\\\"'](?<src>[^\\\"']*/spell/[^\\\"']+)[\\\"'][^>]*>.*?<strong[^>]*>\\s*(?<key>[QWER])\\s*</strong>");
        if (output.Count < 3)
        {
            AddSkillMatches(
                output,
                segment,
                "<img\\b[^>]*src\\s*=\\s*[\\\"'](?<src>[^\\\"']*/spell/[^\\\"']+)[\\\"'][^>]*alt\\s*=\\s*[\\\"'](?<name>[^\\\"']+)[\\\"'][^>]*>.*?<strong[^>]*>\\s*(?<key>[QWER])\\s*</strong>");
        }
        if (output.Count < 3) AddSerializedSkillRows(output, segment);
        if (output.Count < 3)
        {
            AddSkillMatches(
                output,
                segment,
                "\"src\"\\s*:\\s*\"(?<src>[^\"]*/spell/[^\"]+)\".*?\"alt\"\\s*:\\s*\"(?<name>[^\"]+)\".*?\"children\"\\s*:\\s*\"(?<key>[QWER])\"");
        }
        return output.Take(3).ToList();
    }

    private static void AddSerializedSkillRows(ICollection<MayhemSkillPriority> output, string segment)
    {
        for (var index = 0; index < 3 && output.Count < 3; index++)
        {
            var marker = "skill_" + index;
            var start = segment.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) continue;
            var nextMarker = "skill_" + (index + 1);
            var next = segment.IndexOf(nextMarker, start + marker.Length, StringComparison.OrdinalIgnoreCase);
            var length = next > start ? next - start : Math.Min(2800, segment.Length - start);
            if (length <= 0) continue;
            var part = segment.Substring(start, Math.Min(length, 2800));
            var keyMatch = Regex.Match(
                part,
                "\"extraData\"\\s*:\\s*\"(?<key>[QWER])\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var iconMatch = Regex.Match(
                part,
                "\"src\"\\s*:\\s*\"(?<src>[^\"]*/spell/[^\"]+)\".*?\"alt\"\\s*:\\s*\"(?<name>[^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (!keyMatch.Success || !iconMatch.Success) continue;
            AddSkill(output, keyMatch.Groups["key"].Value, iconMatch.Groups["name"].Value, iconMatch.Groups["src"].Value);
        }
    }

    private static void AddSkillMatches(ICollection<MayhemSkillPriority> output, string segment, string pattern)
    {
        foreach (Match match in Regex.Matches(
                     segment,
                     pattern,
                     RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            AddSkill(output, match.Groups["key"].Value, match.Groups["name"].Value, match.Groups["src"].Value);
            if (output.Count >= 3) return;
        }
    }

    private static void AddSkill(ICollection<MayhemSkillPriority> output, string keyValue, string name, string src)
    {
        var key = keyValue.Trim().ToUpperInvariant();
        if (key == "R" || (key != "Q" && key != "W" && key != "E")) return;
        if (output.Any(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))) return;
        output.Add(new MayhemSkillPriority
        {
            Key = key,
            Name = CleanValue(name),
            IconUrl = CleanUrl(src)
        });
    }

    private static string SegmentAfter(string html, IEnumerable<string> markers, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var start = -1;
        foreach (var marker in markers)
        {
            var index = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (start < 0 || index < start)) start = index;
        }
        if (start < 0) return string.Empty;
        return html.Substring(start, Math.Min(maxLength, html.Length - start));
    }

    private static MayhemBuildItem BuildItem(string? id, string? name, string? url) => new()
    {
        Id = CleanValue(id),
        Name = CleanValue(name),
        IconUrl = CleanUrl(url)
    };

    private static bool IsUsefulItem(MayhemBuildItem item, bool requireItemUrl)
    {
        if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.IconUrl)) return false;
        if (requireItemUrl && !item.IconUrl.Contains("item", StringComparison.OrdinalIgnoreCase)) return false;
        var lower = item.Name.ToLowerInvariant();
        return !lower.Contains("logo", StringComparison.Ordinal) &&
               !lower.Contains("advert", StringComparison.Ordinal) &&
               !lower.Contains("op.gg", StringComparison.Ordinal);
    }

    private static bool ContainsItem(IEnumerable<MayhemBuildItem> values, MayhemBuildItem item) =>
        values.Any(value =>
            (!string.IsNullOrWhiteSpace(item.Id) && string.Equals(value.Id, item.Id, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(item.IconUrl) && string.Equals(value.IconUrl, item.IconUrl, StringComparison.OrdinalIgnoreCase)));

    private static void ProjectLegacyBuild(MayhemChampionResult result)
    {
        if (result.CoreBuilds.Count > 0 || result.CoreItems.Count == 0) return;
        var items = new List<MayhemBuildItem>();
        for (var index = 0; index < result.CoreItems.Count && index < 5; index++)
        {
            items.Add(new MayhemBuildItem
            {
                Name = result.CoreItems[index],
                IconUrl = index < result.CoreItemIconUrls.Count ? result.CoreItemIconUrls[index] : string.Empty
            });
        }
        if (items.Count > 0) result.CoreBuilds.Add(new MayhemBuildPath { Rank = 1, Items = items });
    }

    private static void EnsureFallbackSkillPriority(MayhemChampionResult result)
    {
        if (result.SkillPriority.Count >= 3 || string.IsNullOrWhiteSpace(result.SkillOrder)) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(
                     result.SkillOrder.ToUpperInvariant(),
                     "[QWER]",
                     RegexOptions.CultureInvariant))
        {
            var key = match.Value;
            if (key == "R" || !seen.Add(key)) continue;
            result.SkillIconUrls.TryGetValue(key, out var icon);
            result.SkillPriority.Add(new MayhemSkillPriority
            {
                Key = key,
                Name = key,
                IconUrl = icon ?? string.Empty
            });
            if (result.SkillPriority.Count >= 3) break;
        }
    }

    private static bool HasDetailedBuild(MayhemChampionResult result) =>
        result.CoreBuilds.Count > 0 ||
        result.StarterItems.Count > 0 ||
        result.BootItems.Count > 0 ||
        result.SummonerSpells.Count > 0 ||
        result.SkillPriority.Count > 0;

    private static string CleanValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : WebUtility.HtmlDecode(value).Trim();

    private static string CleanUrl(string? value)
    {
        var clean = CleanValue(value);
        if (clean.Length == 0) return string.Empty;
        clean = clean.Replace("\\/", "/", StringComparison.Ordinal);
        var imageIndex = clean.IndexOf("?image=", StringComparison.OrdinalIgnoreCase);
        return imageIndex > 0 ? clean[..imageIndex] : clean;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string NormalizeEscapedHtml(string? value) => (value ?? string.Empty)
        .Replace("\\u003c", "<", StringComparison.OrdinalIgnoreCase)
        .Replace("\\u003e", ">", StringComparison.OrdinalIgnoreCase)
        .Replace("\\u0026", "&", StringComparison.OrdinalIgnoreCase)
        .Replace("\\\"", "\"", StringComparison.Ordinal)
        .Replace("\\/", "/", StringComparison.Ordinal);
}
