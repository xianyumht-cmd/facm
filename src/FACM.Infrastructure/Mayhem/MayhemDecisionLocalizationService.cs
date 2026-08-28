using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FACM.Core.League;
using FACM.Core.Mayhem;

namespace FACM.Infrastructure.Mayhem;

/// <summary>
/// Localizes Mayhem decision/build data from the already-owned LCU read gateway first, then falls
/// back to typed CommunityDragon resources. No additional League discovery/auth/session owner is
/// created here.
/// </summary>
internal sealed class MayhemDecisionLocalizationService
{
    internal static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(20);
    internal static readonly TimeSpan OverallBudget = TimeSpan.FromMilliseconds(1650);

    private readonly ILeagueReadGateway? _league;
    private readonly MayhemCachedPublicDataTransport _publicData;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record CacheEntry(DateTimeOffset CachedUtc, string Json);

    private enum LocalizedResourceKind
    {
        Items,
        Augments,
        Summoners,
        ChampionSummary,
        ChampionDetail
    }

    public MayhemDecisionLocalizationService(
        ILeagueReadGateway? league,
        MayhemCachedPublicDataTransport publicData,
        Func<DateTimeOffset>? utcNow = null)
    {
        _league = league;
        _publicData = publicData ?? throw new ArgumentNullException(nameof(publicData));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task EnrichAsync(MayhemChampionResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OverallBudget);
        var budget = timeout.Token;

        var itemsTask = ReadJsonBestEffortAsync(LocalizedResourceKind.Items, 0, budget, cancellationToken);
        var augmentsTask = ReadJsonBestEffortAsync(LocalizedResourceKind.Augments, 0, budget, cancellationToken);
        var summonersTask = ReadJsonBestEffortAsync(LocalizedResourceKind.Summoners, 0, budget, cancellationToken);
        var championsTask = ReadJsonBestEffortAsync(LocalizedResourceKind.ChampionSummary, 0, budget, cancellationToken);

        await Task.WhenAll(itemsTask, augmentsTask, summonersTask, championsTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        ApplyItems(result, itemsTask.Result);
        ApplyAugments(result, augmentsTask.Result);
        ApplySummoners(result, summonersTask.Result);

        var champion = FindChampion(championsTask.Result, result.ChampionSlug, result.ChampionName);
        if (champion.HasValue)
        {
            var championId = ReadInt(champion.Value, "id");
            var portrait = FirstText(champion.Value, "squarePortraitPath", "iconPath");
            if (!string.IsNullOrWhiteSpace(portrait)) result.ChampionIconUrl = AssetReference(portrait);
            else if (championId > 0)
                result.ChampionIconUrl = "lcu:/lol-game-data/assets/v1/champion-icons/" + championId + ".png";

            if (championId > 0 && !budget.IsCancellationRequested)
            {
                var detail = await ReadJsonBestEffortAsync(
                    LocalizedResourceKind.ChampionDetail,
                    championId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                ApplyChampionSkills(result, detail);
            }
        }

        ReprojectLegacyLists(result);
    }

    internal static void ApplyFixtureForSmoke(
        MayhemChampionResult result,
        string? itemsJson,
        string? augmentsJson,
        string? summonersJson,
        string? championSummaryJson,
        string? championDetailJson)
    {
        ArgumentNullException.ThrowIfNull(result);
        ApplyItems(result, itemsJson);
        ApplyAugments(result, augmentsJson);
        ApplySummoners(result, summonersJson);
        var champion = FindChampion(championSummaryJson, result.ChampionSlug, result.ChampionName);
        if (champion.HasValue)
        {
            var portrait = FirstText(champion.Value, "squarePortraitPath", "iconPath");
            if (!string.IsNullOrWhiteSpace(portrait)) result.ChampionIconUrl = AssetReference(portrait);
        }
        ApplyChampionSkills(result, championDetailJson);
        ReprojectLegacyLists(result);
    }

    private async Task<string?> ReadJsonBestEffortAsync(
        LocalizedResourceKind kind,
        int championId,
        CancellationToken budgetToken,
        CancellationToken userToken)
    {
        var cacheKey = CacheKey(kind, championId);
        if (TryGetCache(cacheKey, out var cached)) return cached;

        try
        {
            if (_league is not null)
            {
                var localPath = ResolveLcuPath(kind, championId);
                var bytes = await _league.TryGetBytesAsync(localPath, budgetToken).ConfigureAwait(false);
                var local = DecodeValidJson(bytes);
                if (local is not null)
                {
                    PutCache(cacheKey, local);
                    return local;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (userToken.IsCancellationRequested) throw;
            return null;
        }
        catch
        {
            // LCU localization is best-effort; public typed data remains the fallback.
        }

        try
        {
            var request = ResolvePublicRequest(kind, championId);
            var response = await _publicData.GetAsync(
                request,
                OverallBudget,
                budgetToken,
                allowStale: true).ConfigureAwait(false);
            var value = response is null ? null : DecodeValidJson(response.Bytes);
            if (value is not null) PutCache(cacheKey, value);
            return value;
        }
        catch (OperationCanceledException)
        {
            if (userToken.IsCancellationRequested) throw;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveLcuPath(LocalizedResourceKind kind, int championId) => kind switch
    {
        LocalizedResourceKind.Items => "/lol-game-data/assets/v1/items.json",
        LocalizedResourceKind.Augments => "/lol-game-data/assets/v1/cherry-augments.json",
        LocalizedResourceKind.Summoners => "/lol-game-data/assets/v1/summoner-spells.json",
        LocalizedResourceKind.ChampionSummary => "/lol-game-data/assets/v1/champion-summary.json",
        LocalizedResourceKind.ChampionDetail when championId > 0 =>
            "/lol-game-data/assets/v1/champions/" + championId + ".json",
        _ => throw new ArgumentException("Localized champion detail requires a positive champion ID.", nameof(championId))
    };

    private static MayhemPublicResourceRequest ResolvePublicRequest(LocalizedResourceKind kind, int championId) => kind switch
    {
        LocalizedResourceKind.Items => new(MayhemPublicResourceKind.CommunityDragonItems),
        LocalizedResourceKind.Augments => new(MayhemPublicResourceKind.CommunityDragonAugments),
        LocalizedResourceKind.Summoners => new(MayhemPublicResourceKind.CommunityDragonSummonerSpells),
        LocalizedResourceKind.ChampionSummary => new(MayhemPublicResourceKind.CommunityDragonChampionSummary),
        LocalizedResourceKind.ChampionDetail when championId > 0 =>
            new(MayhemPublicResourceKind.CommunityDragonChampionDetail, ChampionId: championId),
        _ => throw new ArgumentException("Localized champion detail requires a positive champion ID.", nameof(championId))
    };

    private static void ApplyItems(MayhemChampionResult result, string? json)
    {
        using var document = TryParse(json);
        if (document is null) return;
        var catalog = EnumerateCatalog(document.RootElement).ToArray();
        foreach (var build in result.CoreBuilds) LocalizeItemList(build.Items, catalog);
        LocalizeItemList(result.StarterItems, catalog);
        LocalizeItemList(result.BootItems, catalog);
    }

    private static void LocalizeItemList(IList<MayhemBuildItem> values, IReadOnlyList<JsonElement> catalog)
    {
        foreach (var item in values)
        {
            var row = FindItem(catalog, item);
            if (!row.HasValue) continue;
            var name = FirstText(row.Value, "nameTRA", "name");
            var icon = FirstText(row.Value, "iconPath", "icon");
            if (!string.IsNullOrWhiteSpace(name) && ContainsCjk(name)) item.Name = name.Trim();
            if (!string.IsNullOrWhiteSpace(icon)) item.IconUrl = AssetReference(icon);
            if (string.IsNullOrWhiteSpace(item.Id)) item.Id = ReadString(row.Value, "id");
        }
    }

    private static JsonElement? FindItem(IReadOnlyList<JsonElement> catalog, MayhemBuildItem item)
    {
        var id = FirstNonEmpty(item.Id, ExtractNumericId(item.IconUrl));
        if (!string.IsNullOrWhiteSpace(id))
        {
            foreach (var row in catalog)
                if (string.Equals(ReadString(row, "id"), id, StringComparison.OrdinalIgnoreCase)) return row;
        }

        var key = NormalizeKey(item.Name);
        if (key.Length == 0) return null;
        foreach (var row in catalog)
        {
            if (NormalizeKey(ReadString(row, "name")) == key ||
                NormalizeKey(ReadString(row, "nameTRA")) == key) return row;
        }
        return null;
    }

    private static void ApplyAugments(MayhemChampionResult result, string? json)
    {
        using var document = TryParse(json);
        if (document is null) return;
        var catalog = EnumerateCatalog(document.RootElement).ToArray();
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in result.AugmentRows)
        {
            if (string.IsNullOrWhiteSpace(row.Name)) continue;
            var oldName = row.Name;
            var localized = FindAugment(catalog, row);
            if (!localized.HasValue) continue;

            var name = FirstText(localized.Value, "nameTRA", "name");
            var icon = FirstText(
                localized.Value,
                "augmentSmallIconPath", "augmentIconPath", "augmentLargeIconPath",
                "iconSmall", "iconLarge", "smallIconPath", "iconPath", "icon");
            var description = CleanDescription(FirstText(
                localized.Value,
                "descTRA", "descriptionTRA", "description", "desc", "tooltip"));
            if (!string.IsNullOrWhiteSpace(name) && ContainsCjk(name)) row.Name = name.Trim();
            if (!string.IsNullOrWhiteSpace(icon)) row.IconUrl = AssetReference(icon);
            if (!string.IsNullOrWhiteSpace(description)) row.Description = description;
            if (string.IsNullOrWhiteSpace(row.Id))
                row.Id = FirstText(localized.Value, "id", "augmentId", "apiName");
            if (!string.Equals(oldName, row.Name, StringComparison.OrdinalIgnoreCase)) renamed[oldName] = row.Name;
        }

        foreach (var route in result.AugmentRoutes)
            if (!string.IsNullOrWhiteSpace(route.AugmentName) && renamed.TryGetValue(route.AugmentName, out var localizedName))
                route.AugmentName = localizedName;
    }

    private static JsonElement? FindAugment(IReadOnlyList<JsonElement> catalog, MayhemAugmentRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.Id))
        {
            foreach (var candidate in catalog)
                if (string.Equals(FirstText(candidate, "id", "augmentId"), row.Id, StringComparison.OrdinalIgnoreCase))
                    return candidate;
        }

        var sourceKeys = new[] { NormalizeKey(row.Slug), NormalizeKey(row.Name) }
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceKeys.Length == 0) return null;

        foreach (var candidate in catalog)
        {
            var candidateKeys = new[]
            {
                NormalizeKey(FirstText(candidate, "apiName", "internalName", "slug")),
                NormalizeKey(ReadString(candidate, "name")),
                NormalizeKey(ReadString(candidate, "nameTRA")),
                NormalizeKey(FileToken(FirstText(
                    candidate,
                    "augmentSmallIconPath", "augmentIconPath", "augmentLargeIconPath",
                    "iconSmall", "iconLarge", "smallIconPath", "iconPath", "icon")))
            }
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            foreach (var sourceKey in sourceKeys)
            foreach (var candidateKey in candidateKeys)
            {
                if (candidateKey == sourceKey) return candidate;
                if (sourceKey.Length < 5 || candidateKey.Length < 5) continue;
                if (candidateKey.EndsWith(sourceKey, StringComparison.OrdinalIgnoreCase) ||
                    candidateKey.Contains(sourceKey, StringComparison.OrdinalIgnoreCase) ||
                    sourceKey.EndsWith(candidateKey, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
        }
        return null;
    }

    private static void ApplySummoners(MayhemChampionResult result, string? json)
    {
        using var document = TryParse(json);
        if (document is null) return;
        var catalog = EnumerateCatalog(document.RootElement).ToArray();
        foreach (var spell in result.SummonerSpells)
        {
            var fileKey = NormalizeKey(FileToken(spell.IconUrl));
            var sourceName = NormalizeKey(spell.Name);
            JsonElement? localized = null;
            foreach (var candidate in catalog)
            {
                var iconKey = NormalizeKey(FileToken(FirstText(candidate, "iconPath", "icon")));
                var apiKey = NormalizeKey(FirstText(candidate, "apiName", "alias", "name"));
                if ((fileKey.Length > 0 && iconKey == fileKey) || (sourceName.Length > 0 && apiKey == sourceName))
                {
                    localized = candidate;
                    break;
                }
            }
            if (!localized.HasValue) continue;
            var name = FirstText(localized.Value, "nameTRA", "name");
            var icon = FirstText(localized.Value, "iconPath", "icon");
            if (!string.IsNullOrWhiteSpace(name) && ContainsCjk(name)) spell.Name = name.Trim();
            if (!string.IsNullOrWhiteSpace(icon)) spell.IconUrl = AssetReference(icon);
        }
    }

    private static JsonElement? FindChampion(string? json, string slug, string name)
    {
        using var document = TryParse(json);
        if (document is null) return null;
        var slugKey = NormalizeKey(slug);
        var nameKey = NormalizeKey(name);
        foreach (var row in EnumerateCatalog(document.RootElement))
        {
            var alias = NormalizeKey(ReadString(row, "alias"));
            var display = NormalizeKey(FirstText(row, "nameTRA", "name"));
            if ((slugKey.Length > 0 && alias == slugKey) || (nameKey.Length > 0 && display == nameKey))
                return row.Clone();
        }
        return null;
    }

    private static void ApplyChampionSkills(MayhemChampionResult result, string? json)
    {
        using var document = TryParse(json);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return;
        if (!TryGetProperty(document.RootElement, "spells", out var spells) || spells.ValueKind != JsonValueKind.Array) return;

        foreach (var spell in spells.EnumerateArray())
        {
            if (spell.ValueKind != JsonValueKind.Object) continue;
            var key = ReadString(spell, "spellKey").Trim().ToUpperInvariant();
            if (key is not ("Q" or "W" or "E" or "R")) continue;
            var icon = FirstText(spell, "abilityIconPath", "iconPath");
            var name = FirstText(spell, "nameTRA", "name");
            if (!string.IsNullOrWhiteSpace(icon)) result.SkillIconUrls[key] = AssetReference(icon);
            foreach (var priority in result.SkillPriority.Where(value => string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(icon)) priority.IconUrl = AssetReference(icon);
                if (!string.IsNullOrWhiteSpace(name) && ContainsCjk(name)) priority.Name = name.Trim();
            }
        }
    }

    private static void ReprojectLegacyLists(MayhemChampionResult result)
    {
        if (result.CoreBuilds.Count > 0)
        {
            var first = result.CoreBuilds[0].Items.Take(5).ToList();
            result.CoreItems = first.Select(item => item.Name).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            result.CoreItemIconUrls = first.Select(item => item.IconUrl).ToList();
        }
        if (result.AugmentRows.Count > 0)
        {
            result.Augments = result.AugmentRows.Take(5).Select(row => row.Name).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            result.AugmentIconUrls = result.AugmentRows.Take(5).Select(row => row.IconUrl).ToList();
        }
    }

    private static JsonDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<JsonElement> EnumerateCatalog(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) yield return item;
            yield break;
        }
        if (root.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in root.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Object) yield return property.Value;
    }

    private static string? DecodeValidJson(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        var text = Encoding.UTF8.GetString(bytes);
        using var document = TryParse(text);
        return document is null ? null : text;
    }

    private bool TryGetCache(string key, out string value)
    {
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var entry) && _utcNow() - entry.CachedUtc < CacheLifetime)
            {
                value = entry.Json;
                return true;
            }
            _cache.Remove(key);
        }
        value = string.Empty;
        return false;
    }

    private void PutCache(string key, string json)
    {
        lock (_sync)
        {
            _cache[key] = new CacheEntry(_utcNow(), json);
            if (_cache.Count <= 20) return;
            var expired = _cache.Where(pair => _utcNow() - pair.Value.CachedUtc >= CacheLifetime).Select(pair => pair.Key).ToArray();
            foreach (var keyToRemove in expired) _cache.Remove(keyToRemove);
        }
    }

    private static string CacheKey(LocalizedResourceKind kind, int championId) =>
        kind == LocalizedResourceKind.ChampionDetail ? "champions/" + championId : kind.ToString();

    private static string ReadString(JsonElement source, string key) =>
        TryGetProperty(source, key, out var value) ? ValueText(value) : string.Empty;

    private static int ReadInt(JsonElement source, string key) =>
        int.TryParse(ReadString(source, key), out var value) ? value : 0;

    private static string FirstText(JsonElement source, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ReadString(source, key);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return string.Empty;
    }

    private static bool TryGetProperty(JsonElement source, string key, out JsonElement value)
    {
        if (source.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in source.EnumerateObject())
            {
                if (!string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)) continue;
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string ValueText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => string.Empty
    };

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string ExtractNumericId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var match = Regex.Match(
            value,
            "(?:item[/_-]?|/)(?<id>\\d{3,6})(?:\\.png|\\?|/|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["id"].Value : string.Empty;
    }

    private static string FileToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var clean = value.Split('?')[0].TrimEnd('/');
        var index = clean.LastIndexOf('/');
        if (index >= 0) clean = clean[(index + 1)..];
        var dot = clean.LastIndexOf('.');
        return dot > 0 ? clean[..dot] : clean;
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
            if (char.IsLetterOrDigit(character)) builder.Append(character);
        return builder.ToString();
    }

    private static bool ContainsCjk(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Any(character => character is >= '\u3400' and <= '\u9fff');

    private static string CleanDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = Regex.Replace(value, "<[^>]+>", " ", RegexOptions.CultureInvariant);
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, "\\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static string AssetReference(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var value = path.Trim().Replace('\\', '/');
        if (value.StartsWith("lcu:", StringComparison.OrdinalIgnoreCase)) return value;

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var marker = "/game/assets/";
            var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                marker = "/global/default/";
                index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            }
            if (index < 0) return value;
            value = value[(index + marker.Length)..];
        }

        value = value.TrimStart('/');
        if (value.StartsWith("lol-game-data/assets/", StringComparison.OrdinalIgnoreCase)) return "lcu:/" + value;
        if (value.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) value = value["assets/".Length..];
        return "lcu:/lol-game-data/assets/" + value;
    }
}
