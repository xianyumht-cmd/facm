using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FACM.Core.Mayhem;

namespace FACM.Infrastructure.Mayhem;

/// <summary>
/// FACM 3.5.15 Mayhem augment enrichment migrated onto the typed 4.0 public-data transport.
/// Rich OP.GG rows win only when they include a usable icon; ARAMMayhem remains a bounded legacy
/// fallback. The service never accepts a caller-provided URL.
/// </summary>
internal sealed class MayhemAugmentEnrichmentService
{
    private const string KiwiIconBase =
        "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/assets/ux/kiwi/augments/icons/";

    private readonly MayhemCachedPublicDataTransport _transport;

    public MayhemAugmentEnrichmentService(MayhemCachedPublicDataTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task EnrichAsync(MayhemChampionResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var slug = MayhemChampionAliases.Slugify(result.ChampionSlug);
        if (string.IsNullOrWhiteSpace(slug)) return;

        var richRequest = new MayhemPublicResourceRequest(MayhemPublicResourceKind.MayhemAugments, slug);
        var richResponse = await _transport.GetAsync(
            richRequest,
            TimeSpan.FromSeconds(5.5),
            cancellationToken,
            allowStale: true).ConfigureAwait(false);

        var richRows = ParseOpggRows(richResponse?.ReadUtf8()).Take(12).ToList();
        if (richRows.Count > 0)
        {
            ApplyRich(result, richRows);
            result.AugmentSourceUrl = MayhemCachedPublicDataTransport.Resolve(richRequest).AbsoluteUri;
            result.AugmentSourceRoute = richResponse?.Route ?? string.Empty;
            result.AugmentSourceStale = richResponse?.IsStale ?? false;
            return;
        }

        var fallbackRequest = new MayhemPublicResourceRequest(MayhemPublicResourceKind.RankingBuild, slug);
        var fallback = await _transport.GetAsync(
            fallbackRequest,
            TimeSpan.FromSeconds(3),
            cancellationToken,
            allowStale: true).ConfigureAwait(false);
        var legacy = ParseLegacyPicks(fallback?.ReadUtf8()).Take(5).ToList();
        if (legacy.Count == 0) return;

        ApplyLegacy(result, legacy);
        result.AugmentSourceUrl = MayhemCachedPublicDataTransport.Resolve(fallbackRequest).AbsoluteUri;
        result.AugmentSourceRoute = fallback?.Route ?? string.Empty;
        result.AugmentSourceStale = fallback?.IsStale ?? false;
    }

    internal static IReadOnlyList<MayhemAugmentRow> ParseOpggRowsForSmoke(string? html) => ParseOpggRows(html).ToArray();

    internal static int ApplyHtmlForSmoke(MayhemChampionResult result, string? html)
    {
        ArgumentNullException.ThrowIfNull(result);
        var rich = ParseOpggRows(html).Take(12).ToList();
        if (rich.Count > 0)
        {
            ApplyRich(result, rich);
            return rich.Count;
        }

        var legacy = ParseLegacyPicks(html).Take(5).ToList();
        ApplyLegacy(result, legacy);
        return legacy.Count;
    }

    private static IEnumerable<MayhemAugmentRow> ParseOpggRows(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return Array.Empty<MayhemAugmentRow>();
        var normalized = NormalizeEscapedHtml(html);
        var best = new List<MayhemAugmentRow>();
        var bestScore = -1;
        var searchFrom = 0;

        while (searchFrom < normalized.Length)
        {
            var marker = FindNextArrayMarker(normalized, searchFrom);
            if (marker < 0) break;
            var open = normalized.IndexOf('[', marker);
            if (open < 0) break;
            var close = FindBalancedEnd(normalized, open, '[', ']');
            if (close <= open)
            {
                searchFrom = open + 1;
                continue;
            }

            var parsed = ParseArray(normalized.Substring(open, close - open + 1));
            var score = ScoreRows(parsed);
            if (score > bestScore)
            {
                best = parsed;
                bestScore = score;
            }
            searchFrom = close + 1;
        }

        return best;
    }

    private static int ScoreRows(IReadOnlyCollection<MayhemAugmentRow> rows)
    {
        if (rows.Count == 0) return 0;
        var stats = rows.Count(row => row.WinRate.HasValue || row.PickRate.HasValue);
        var samples = rows.Count(row => row.Games is > 0);
        return rows.Count * 10 + stats * 4 + samples;
    }

    private static int FindNextArrayMarker(string text, int start)
    {
        var markers = new[] { "\"augments\"", "\"data\"", "\"items\"" };
        var result = -1;
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, start, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (result < 0 || index < result)) result = index;
        }
        return result;
    }

    private static List<MayhemAugmentRow> ParseArray(string json)
    {
        var rows = new List<MayhemAugmentRow>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            if (document.RootElement.ValueKind != JsonValueKind.Array) return rows;
            var rank = 1;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var row = ParseRow(item, rank);
                if (row is null) continue;
                rows.Add(row);
                rank++;
            }
        }
        catch (JsonException)
        {
        }
        return rows;
    }

    private static MayhemAugmentRow? ParseRow(JsonElement item, int fallbackRank)
    {
        var name = FirstText(item, "name", "augmentName", "title");
        if (string.IsNullOrWhiteSpace(name)) return null;
        var icon = FirstText(item, "largeIcon", "smallIcon", "icon", "iconUrl", "image", "imageUrl");

        // Preserve the 3.5 fail-closed rule: generic Next.js arrays may look like augment rows but
        // are not rich data unless the source supplied a usable asset.
        if (string.IsNullOrWhiteSpace(icon)) return null;

        var winRate = NormalizePercent(FirstNumber(item, "performance", "winRate", "win_rate", "rate"));
        var pickRate = NormalizePercent(FirstNumber(item, "popular", "pickRate", "pick_rate", "popularity"));
        var id = FirstText(item, "id", "augmentId", "augment_id");
        var slug = FirstText(item, "slug", "key");
        var rank = FirstInteger(item, "rank", "order") ?? fallbackRank;
        if (string.IsNullOrWhiteSpace(slug)) slug = Slugify(name);

        return new MayhemAugmentRow
        {
            Id = id,
            Rank = Math.Max(1, rank),
            Name = WebUtility.HtmlDecode(name).Trim(),
            Slug = slug,
            Rarity = NormalizeRarity(FirstText(item, "rarity", "grade", "tier")),
            WinRate = winRate,
            PickRate = pickRate,
            Games = FirstInteger(item, "games", "gameCount", "sampleCount", "sampleSize", "totalGames", "count"),
            Description = CleanDescription(FirstText(item, "description", "desc", "tooltip")),
            IconUrl = CleanIconUrl(icon)
        };
    }

    private static void ApplyRich(MayhemChampionResult result, IEnumerable<MayhemAugmentRow> rows)
    {
        result.AugmentRows = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Take(12)
            .ToList();
        result.Augments = result.AugmentRows.Take(5).Select(row => row.Name).ToList();
        result.AugmentIconUrls = result.AugmentRows.Take(5).Select(row => row.IconUrl).ToList();
        result.AugmentRoutes = MayhemAugmentDecisionPolicy.BuildRoutes(result.AugmentRows);
    }

    private static void ApplyLegacy(MayhemChampionResult result, IReadOnlyList<LegacyAugment> picks)
    {
        if (picks.Count == 0) return;
        result.Augments.Clear();
        result.AugmentIconUrls.Clear();
        result.AugmentRows.Clear();
        for (var index = 0; index < picks.Count && index < 5; index++)
        {
            var pick = picks[index];
            result.Augments.Add(pick.Name);
            result.AugmentIconUrls.Add(pick.IconUrl);
            result.AugmentRows.Add(new MayhemAugmentRow
            {
                Rank = index + 1,
                Name = pick.Name,
                Slug = pick.Slug,
                WinRate = pick.WinRate,
                IconUrl = pick.IconUrl,
                Rarity = "未知"
            });
        }
        result.AugmentRoutes = MayhemAugmentDecisionPolicy.BuildRoutes(result.AugmentRows);
    }

    private static IEnumerable<LegacyAugment> ParseLegacyPicks(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return Array.Empty<LegacyAugment>();
        var normalized = NormalizeEscapedHtml(html);
        var start = normalized.IndexOf("Best Augments for", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return Array.Empty<LegacyAugment>();
        var end = normalized.IndexOf("Augment Combos", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0 || end <= start) end = Math.Min(normalized.Length, start + 18000);
        var section = normalized.Substring(start, Math.Min(end - start, 18000));
        var picks = new List<LegacyAugment>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(
                     section,
                     "<a\\b[^>]*href\\s*=\\s*[\"'](?<href>[^\"']*/augments/(?<slug>[^/\"']+)/?)[\"'][^>]*>(?<body>.*?)</a>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            var slug = WebUtility.HtmlDecode(match.Groups["slug"].Value).Trim();
            var body = match.Groups["body"].Value;
            var name = MatchAttribute(body, "alt");
            var text = CleanText(body);
            if (string.IsNullOrWhiteSpace(name))
            {
                var nameMatch = Regex.Match(
                    text,
                    "^(?<n>.+?)(?<w>\\d{1,2}(?:\\.\\d+)?)%",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant);
                if (nameMatch.Success) name = nameMatch.Groups["n"].Value.Trim();
            }
            name = WebUtility.HtmlDecode(name ?? string.Empty).Trim();
            if (name.Length < 2 || !seen.Add(name)) continue;

            var rateMatch = Regex.Match(text, "(?<w>\\d{1,2}(?:\\.\\d+)?)%", RegexOptions.CultureInvariant);
            var winRate = rateMatch.Success && double.TryParse(
                rateMatch.Groups["w"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var rate)
                ? rate
                : (double?)null;
            picks.Add(new LegacyAugment(
                name,
                slug,
                winRate,
                string.IsNullOrWhiteSpace(slug) ? string.Empty : KiwiIconBase + NormalizeName(slug) + "_small.png"));
        }
        return picks;
    }

    private static string FirstText(JsonElement item, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetProperty(item, key, out var value)) continue;
            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return string.Empty;
    }

    private static double? FirstNumber(JsonElement item, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetProperty(item, key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = (value.GetString() ?? string.Empty).Trim().TrimEnd('%');
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
            }
        }
        return null;
    }

    private static int? FirstInteger(JsonElement item, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetProperty(item, key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(
                    value.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out number)) return number;
        }
        return null;
    }

    private static bool TryGetProperty(JsonElement item, string key, out JsonElement value)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (!string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static double? NormalizePercent(double? value)
    {
        if (!value.HasValue) return null;
        var number = value.Value;
        if (double.IsNaN(number) || double.IsInfinity(number) || number < 0) return null;
        if (number <= 1.00001d) number *= 100d;
        return number <= 100.00001d ? number : null;
    }

    private static string NormalizeRarity(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Contains("prismatic", StringComparison.Ordinal) || text.Contains("prism", StringComparison.Ordinal) || text.Contains("棱彩", StringComparison.Ordinal)) return "棱彩";
        if (text.Contains("gold", StringComparison.Ordinal) || text.Contains("黄金", StringComparison.Ordinal) || text.Contains('金')) return "黄金";
        if (text.Contains("silver", StringComparison.Ordinal) || text.Contains("白银", StringComparison.Ordinal) || text.Contains('银')) return "白银";
        return string.IsNullOrWhiteSpace(value) ? "未知" : value.Trim();
    }

    private static string CleanDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = Regex.Replace(NormalizeEscapedHtml(value), "<[^>]+>", " ", RegexOptions.CultureInvariant);
        return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static string CleanIconUrl(string value)
    {
        var clean = WebUtility.HtmlDecode(value).Trim().Replace("\\/", "/", StringComparison.Ordinal);
        var imageIndex = clean.IndexOf("?image=", StringComparison.OrdinalIgnoreCase);
        return imageIndex > 0 ? clean[..imageIndex] : clean;
    }

    private static string Slugify(string value) =>
        Regex.Replace(WebUtility.HtmlDecode(value).ToLowerInvariant(), "[^a-z0-9]+", "-", RegexOptions.CultureInvariant).Trim('-');

    private static string MatchAttribute(string html, string attribute)
    {
        var match = Regex.Match(
            html,
            "\\b" + Regex.Escape(attribute) + "\\s*=\\s*[\"'](?<v>[^\"']+)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["v"].Value).Trim() : string.Empty;
    }

    private static string NormalizeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in WebUtility.HtmlDecode(value).ToLowerInvariant())
            if (char.IsLetterOrDigit(character)) builder.Append(character);
        return builder.ToString();
    }

    private static string NormalizeEscapedHtml(string? value) => (value ?? string.Empty)
        .Replace("\\u003c", "<", StringComparison.OrdinalIgnoreCase)
        .Replace("\\u003e", ">", StringComparison.OrdinalIgnoreCase)
        .Replace("\\u0026", "&", StringComparison.OrdinalIgnoreCase)
        .Replace("\\\"", "\"", StringComparison.Ordinal)
        .Replace("\\/", "/", StringComparison.Ordinal);

    private static string CleanText(string html)
    {
        var text = Regex.Replace(
            html,
            "<(script|style)[^>]*>.*?</\\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, "<[^>]+>", " ", RegexOptions.CultureInvariant);
        return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static int FindBalancedEnd(string value, int start, char open, char close)
    {
        var depth = 0;
        var quoted = false;
        var escaped = false;
        for (var index = start; index < value.Length; index++)
        {
            var character = value[index];
            if (quoted)
            {
                if (escaped) { escaped = false; continue; }
                if (character == '\\') { escaped = true; continue; }
                if (character == '"') quoted = false;
                continue;
            }
            if (character == '"') { quoted = true; continue; }
            if (character == open) depth++;
            else if (character == close && --depth == 0) return index;
        }
        return -1;
    }

    private sealed record LegacyAugment(string Name, string Slug, double? WinRate, string IconUrl);
}
