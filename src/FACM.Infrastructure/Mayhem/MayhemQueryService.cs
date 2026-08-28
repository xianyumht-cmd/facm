using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using FACM.Core.Mayhem;

namespace FACM.Infrastructure.Mayhem;

internal enum MayhemSourceKind
{
    HexdataHeroes,
    RankingHome,
    RankingBuild,
    OpggIndex,
    OpggBuild
}

internal sealed record MayhemSourceRequest(MayhemSourceKind Kind, string ChampionSlug = "");

internal interface IMayhemPublicSource
{
    Task<string?> TryGetStringAsync(MayhemSourceRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Fixed-destination public-data transport. Callers select a small source kind; they cannot supply
/// arbitrary URLs. Every destination is HTTPS and every request has the same per-source budget used
/// by the FACM 3.5 Mayhem query path.
/// </summary>
internal sealed class MayhemHttpSource : IMayhemPublicSource, IDisposable
{
    internal const string HexdataHeroesUrl = "https://hexdata.com.cn/heroes";
    internal const string OpggBaseUrl = "https://op.gg/zh-cn/lol/modes/aram-mayhem";
    internal const string RankingBaseUrl = "https://arammayhem.com";

    private readonly HttpClient _client;
    private bool _disposed;

    public MayhemHttpSource(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FACM", "4.0"));
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.7");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.7");
    }

    public async Task<string?> TryGetStringAsync(MayhemSourceRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        var (uri, budget) = Resolve(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    internal static (Uri Uri, TimeSpan Budget) Resolve(MayhemSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var slug = NormalizeSlug(request.ChampionSlug);
        return request.Kind switch
        {
            MayhemSourceKind.HexdataHeroes => (new Uri(HexdataHeroesUrl), TimeSpan.FromSeconds(2.8)),
            MayhemSourceKind.RankingHome => (new Uri(RankingBaseUrl + "/"), TimeSpan.FromSeconds(3.4)),
            MayhemSourceKind.RankingBuild when slug.Length > 0 =>
                (new Uri(RankingBaseUrl + "/build/" + Uri.EscapeDataString(slug) + "/"), TimeSpan.FromSeconds(3.8)),
            MayhemSourceKind.OpggIndex => (new Uri(OpggBaseUrl), TimeSpan.FromSeconds(1.5)),
            MayhemSourceKind.OpggBuild when slug.Length > 0 =>
                (new Uri(OpggBaseUrl + "/" + Uri.EscapeDataString(slug) + "/build"), TimeSpan.FromSeconds(2.2)),
            _ => throw new ArgumentException("The Mayhem source request requires a known source and valid champion slug.", nameof(request))
        };
    }

    private static string NormalizeSlug(string? value)
    {
        var slug = MayhemChampionAliases.Slugify(value);
        return Regex.IsMatch(slug, "^[a-z0-9-]{1,80}$", RegexOptions.CultureInvariant) ? slug : string.Empty;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
    }
}

/// <summary>
/// User-driven resilient ARAM Mayhem query migrated from FACM 3.5.15. This stage intentionally
/// covers the base ranking/build pipeline only; Tencent patch validation, rich augment enrichment,
/// local Riot assets and card rendering are composed as later enrichment/presentation layers.
/// </summary>
public sealed class MayhemQueryService : IMayhemQueryService, IDisposable
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan OverallBudget = TimeSpan.FromSeconds(5.5);

    private readonly IMayhemPublicSource _source;
    private readonly bool _ownsSource;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _cacheSync = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _queryGate = new(1, 1);
    private bool _disposed;

    private sealed record CacheEntry(DateTimeOffset CachedUtc, MayhemChampionResult Value);
    private sealed record HexdataChampionRow(int Rank, string Name, string Slug, double? WinRate);

    public MayhemQueryService()
        : this(new MayhemHttpSource(), ownsSource: true, () => DateTimeOffset.UtcNow)
    {
    }

    internal MayhemQueryService(
        IMayhemPublicSource source,
        bool ownsSource = false,
        Func<DateTimeOffset>? utcNow = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _ownsSource = ownsSource;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<MayhemChampionResult> QueryAsync(
        string input,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var query = (input ?? string.Empty).Trim();
        if (query.Length == 0)
            return new MayhemChampionResult { ErrorMessage = "请输入英雄名称或别名。" };

        if (TryGetCached(query, out var cached))
        {
            progress?.Report("已命中 10 分钟本地缓存");
            return cached;
        }

        await _queryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetCached(query, out cached))
            {
                progress?.Report("已命中 10 分钟本地缓存");
                return cached;
            }

            using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overall.CancelAfter(OverallBudget);
            var token = overall.Token;
            var result = new MayhemChampionResult { Query = query };

            try
            {
                progress?.Report("正在读取国内排行…");
                var hexdataTask = _source.TryGetStringAsync(new MayhemSourceRequest(MayhemSourceKind.HexdataHeroes), token);

                string? hexdataHtml = null;
                string slug;
                if (!MayhemChampionAliases.TryResolve(query, out slug))
                {
                    hexdataHtml = await hexdataTask.ConfigureAwait(false);
                    slug = ResolveSlugFromHexdata(hexdataHtml, query);
                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        var opggIndex = await _source.TryGetStringAsync(
                            new MayhemSourceRequest(MayhemSourceKind.OpggIndex), token).ConfigureAwait(false);
                        slug = ResolveSlugFromOpgg(opggIndex, query);
                    }
                }

                if (string.IsNullOrWhiteSpace(slug))
                {
                    result.ErrorMessage = "没有识别到这个英雄，请尝试官方中文名、英文名或常见简称。";
                    return result;
                }

                result.ChampionSlug = slug;
                result.SourceUrl = MayhemHttpSource.OpggBaseUrl + "/" + slug + "/build";
                result.RankingSourceUrl = MayhemHttpSource.RankingBaseUrl + "/build/" + slug + "/";

                progress?.Report("正在并行读取排行、平衡与攻略补充…");
                var rankingTask = _source.TryGetStringAsync(new MayhemSourceRequest(MayhemSourceKind.RankingBuild, slug), token);
                var rankingTopTask = _source.TryGetStringAsync(new MayhemSourceRequest(MayhemSourceKind.RankingHome), token);
                var opggTask = _source.TryGetStringAsync(new MayhemSourceRequest(MayhemSourceKind.OpggBuild, slug), token);

                hexdataHtml ??= await hexdataTask.ConfigureAwait(false);
                await Task.WhenAll(rankingTask, rankingTopTask, opggTask).ConfigureAwait(false);

                progress?.Report("正在合并国内排行、当前平衡和攻略字段…");
                var hexRows = ParseHexdataRows(hexdataHtml);
                var hexTargetFound = ApplyHexdata(hexRows, slug, query, result);
                ParseRankingChampion(rankingTask.Result, result);
                if (result.TopTen.Count < 10)
                {
                    var fallbackTop = ParseTopTen(rankingTopTask.Result);
                    if (fallbackTop.Count > result.TopTen.Count) result.TopTen = fallbackTop;
                }
                ParseOpggChampion(opggTask.Result, result);

                var current = result.TopTen.FirstOrDefault(item =>
                    string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(MayhemChampionAliases.Normalize(item.Name), MayhemChampionAliases.Normalize(result.ChampionName), StringComparison.OrdinalIgnoreCase));
                if (current is not null)
                {
                    result.Rank ??= current.Rank;
                    result.WinRate ??= current.WinRate;
                    if (string.IsNullOrWhiteSpace(result.Tier)) result.Tier = current.Tier;
                }

                if (string.IsNullOrWhiteSpace(result.ChampionName)) result.ChampionName = Title(slug);
                if (string.IsNullOrWhiteSpace(result.Tier) && result.Rank.HasValue)
                    result.Tier = InferTier(result.Rank.Value);

                var anyPrimary = hexTargetFound || !string.IsNullOrWhiteSpace(rankingTask.Result) || !string.IsNullOrWhiteSpace(opggTask.Result);
                if (!anyPrimary)
                {
                    result.ErrorMessage = "暂时没有读取到可用排行，请稍后重试。";
                    return result;
                }

                result.SourceNote = BuildSourceNote(
                    hexTargetFound,
                    !string.IsNullOrWhiteSpace(rankingTask.Result),
                    !string.IsNullOrWhiteSpace(opggTask.Result));
                PutCache(query, result);
                progress?.Report("查询完成");
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result.ErrorMessage = "查询超过 5.5 秒，已返回前仍未得到可用结果。";
                return result;
            }
        }
        finally
        {
            _queryGate.Release();
        }
    }

    private bool TryGetCached(string query, out MayhemChampionResult result)
    {
        lock (_cacheSync)
        {
            if (_cache.TryGetValue(query, out var entry) && _utcNow() - entry.CachedUtc < CacheDuration)
            {
                result = entry.Value;
                return true;
            }
            if (entry is not null) _cache.Remove(query);
        }
        result = null!;
        return false;
    }

    private void PutCache(string query, MayhemChampionResult result)
    {
        lock (_cacheSync) _cache[query] = new CacheEntry(_utcNow(), result);
    }

    internal static IReadOnlyList<MayhemTopChampion> ParseHexdataTopForSmoke(string? html) =>
        ParseHexdataRows(html).Take(10).Select(row => new MayhemTopChampion
        {
            Rank = row.Rank,
            Name = row.Name,
            Slug = row.Slug,
            WinRate = row.WinRate,
            Tier = InferTier(row.Rank)
        }).ToArray();

    internal static IReadOnlyList<MayhemTopChampion> ParseRankingTopForSmoke(string? html) => ParseTopTen(html);
    internal static string ResolveSlugFromOpggForSmoke(string? html, string query) => ResolveSlugFromOpgg(html, query);

    private static bool ApplyHexdata(
        IReadOnlyList<HexdataChampionRow> rows,
        string slug,
        string query,
        MayhemChampionResult result)
    {
        if (rows.Count == 0) return false;
        result.TopTen = rows.Take(10).Select(row => new MayhemTopChampion
        {
            Rank = row.Rank,
            Name = row.Name,
            Slug = row.Slug,
            WinRate = row.WinRate,
            Tier = InferTier(row.Rank)
        }).ToList();

        var normalizedQuery = MayhemChampionAliases.Normalize(query);
        var target = rows.FirstOrDefault(row => string.Equals(row.Slug, slug, StringComparison.OrdinalIgnoreCase));
        target ??= rows.FirstOrDefault(row =>
        {
            var name = MayhemChampionAliases.Normalize(row.Name);
            return normalizedQuery.Length > 0 && (name.Contains(normalizedQuery, StringComparison.Ordinal) || normalizedQuery.Contains(name, StringComparison.Ordinal));
        });
        if (target is null) return false;

        result.ChampionName = target.Name;
        result.Rank = target.Rank;
        result.WinRate = target.WinRate;
        result.Tier = InferTier(target.Rank);
        return true;
    }

    private static List<HexdataChampionRow> ParseHexdataRows(string? html)
    {
        var output = new List<HexdataChampionRow>();
        if (string.IsNullOrWhiteSpace(html)) return output;
        var normalized = NormalizeEscapedHtml(html);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anchors = Regex.Matches(
            normalized,
            "<a\\b[^>]*href\\s*=\\s*[\"'](?<href>[^\"']*/hero/(?<id>\\d+)-(?<slug>[a-z0-9-]+))[^\"']*[\"'][^>]*>(?<body>.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        foreach (Match anchor in anchors)
        {
            var slug = WebUtility.HtmlDecode(anchor.Groups["slug"].Value).Trim();
            var name = ExtractPreferredChampionName(CleanText(anchor.Groups["body"].Value));
            if (slug.Length == 0 || name.Length == 0 || string.Equals(name, "查看详情", StringComparison.OrdinalIgnoreCase) || !seen.Add(slug))
                continue;

            var windowLength = Math.Min(700, normalized.Length - anchor.Index);
            var window = CleanText(normalized.Substring(anchor.Index, windowLength));
            var winText = First(
                MatchValue(window, "胜率\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%"),
                MatchValue(window, "(?<v>\\d{1,2}(?:\\.\\d+)?)%\\s*[·•]?\\s*样本"));
            var winRate = Rate(winText);
            if (!winRate.HasValue) continue;
            output.Add(new HexdataChampionRow(output.Count + 1, name, slug, winRate));
        }
        return output;
    }

    private static string ResolveSlugFromHexdata(string? html, string query)
    {
        var target = MayhemChampionAliases.Normalize(query);
        if (target.Length == 0) return string.Empty;
        foreach (var row in ParseHexdataRows(html))
        {
            var name = MayhemChampionAliases.Normalize(row.Name);
            if (name == target || name.Contains(target, StringComparison.Ordinal) || target.Contains(name, StringComparison.Ordinal))
                return row.Slug;
        }
        return string.Empty;
    }

    private static void ParseRankingChampion(string? html, MayhemChampionResult result)
    {
        if (string.IsNullOrWhiteSpace(html)) return;
        var text = CleanText(NormalizeEscapedHtml(html));
        result.RankingPatch = First(
            MatchValue(text, "Patch\\s*:\\s*(?<v>\\d{1,2}\\.\\d{1,2})"),
            MatchValue(text, "patch\\s*(?<v>\\d{1,2}\\.\\d{1,2})"),
            result.RankingPatch);
        if (string.IsNullOrWhiteSpace(result.Tier))
            result.Tier = MatchValue(text, "\\b(?<v>S\\+|S|A|B|C|D|F)\\s+Tier\\s+ARAM");
        result.WinRate ??= FirstRate(
            MatchValue(text, "(?<v>\\d{1,2}(?:\\.\\d+)?)%\\s*WR"),
            MatchValue(text, "win rate\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%"));
        result.PickRate ??= FirstRate(
            MatchValue(text, "(?<v>\\d{1,2}(?:\\.\\d+)?)%\\s*PR"),
            MatchValue(text, "pick rate\\s*(?<v>\\d{1,2}(?:\\.\\d+)?)%"));
        if (!result.Rank.HasValue && int.TryParse(
                MatchValue(text, "Rank\\s*:\\s*(?<v>\\d{1,3})"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var rank))
            result.Rank = rank;
        result.BalanceSummary = ParseBalanceAdjustments(text);
    }

    private static List<MayhemTopChampion> ParseTopTen(string? html)
    {
        var output = new List<MayhemTopChampion>();
        if (string.IsNullOrWhiteSpace(html)) return output;
        var text = CleanText(NormalizeEscapedHtml(html));
        var marker = text.IndexOf("TOP 10 Highest Win Rate Champions", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) marker = text.IndexOf("TOP 10", StringComparison.OrdinalIgnoreCase);
        var section = marker < 0 ? text : text.Substring(marker, Math.Min(2200, text.Length - marker));
        foreach (Match match in Regex.Matches(
                     section,
                     "(?<!\\d)(?<r>10|[1-9])\\s+(?<n>[A-Za-z][A-Za-z0-9' .-]{1,30}?)\\s+(?<w>\\d{1,2}\\.\\d{1,2})%",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (!int.TryParse(match.Groups["r"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank)) continue;
            var name = match.Groups["n"].Value.Trim();
            var win = Rate(match.Groups["w"].Value);
            if (!win.HasValue || output.Any(item => item.Rank == rank)) continue;
            output.Add(new MayhemTopChampion
            {
                Rank = rank,
                Name = name,
                Slug = MayhemChampionAliases.Slugify(name),
                WinRate = win,
                Tier = rank <= 7 ? "S+" : "S"
            });
        }
        return output.OrderBy(item => item.Rank).Take(10).ToList();
    }

    private static string ResolveSlugFromOpgg(string? html, string query)
    {
        var target = MayhemChampionAliases.Normalize(query);
        if (target.Length == 0 || string.IsNullOrWhiteSpace(html)) return string.Empty;
        var normalized = NormalizeEscapedHtml(html);
        foreach (Match match in Regex.Matches(normalized, "/aram-mayhem/(?<slug>[^/\"'?]+)/build", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var slug = match.Groups["slug"].Value;
            var start = Math.Max(0, match.Index - 300);
            var length = Math.Min(normalized.Length - start, 700);
            var window = normalized.Substring(start, length);
            if (MayhemChampionAliases.Normalize(slug) == target ||
                MayhemChampionAliases.Normalize(CleanText(window)).Contains(target, StringComparison.Ordinal))
                return slug;
        }
        return string.Empty;
    }

    private static void ParseOpggChampion(string? html, MayhemChampionResult result)
    {
        if (string.IsNullOrWhiteSpace(html)) return;
        var normalized = NormalizeEscapedHtml(html);
        var text = CleanText(normalized);
        var h1 = CleanText(MatchValue(normalized, "<h1[^>]*>(?<v>.*?)</h1>"));
        if (string.IsNullOrWhiteSpace(result.ChampionName)) result.ChampionName = CleanName(h1);
        if (string.IsNullOrWhiteSpace(result.Patch))
            result.Patch = First(
                MatchValue(text, "(?:在|Patch\\s*)?(?<v>\\d{1,2}\\.\\d{1,2})\\s*(?:版本|Patch)"),
                MatchValue(text, "(?:版本|Patch)\\s*(?<v>\\d{1,2}\\.\\d{1,2})"),
                result.Patch);
        if (string.IsNullOrWhiteSpace(result.SkillOrder)) result.SkillOrder = ExtractSkillOrder(text);

        var items = ExtractAltSection(
            normalized,
            ["核心装备", "核心出装", "Builds Table", "Core builds", "Core Items"],
            ["广告", "增幅装置", "Augments", "召唤师技能", "Summoner"],
            4);
        if (items.Count > 0) result.CoreItems = items;
        var augments = ExtractAltSection(
            normalized,
            [" 增幅装置", "增幅装置", "强化符文", "Augments"],
            ["召唤师技能", "Summoner", "技能加点", "Skills"],
            8);
        if (augments.Count > 0) result.Augments = augments;
    }

    private static string ExtractSkillOrder(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var start = IndexOfAny(text, ["技能加点", "Skill order", "SkillOrder Table"], 0);
        var section = start < 0 ? text : text.Substring(start, Math.Min(700, text.Length - start));
        var match = Regex.Match(section, "(?<v>(?:\\b[QWER]\\b[\\s>→·,/|-]*){10,18})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return string.Empty;
        var letters = Regex.Matches(match.Groups["v"].Value.ToUpperInvariant(), "[QWER]")
            .Cast<Match>().Select(item => item.Value).Take(18).ToArray();
        return letters.Length < 8 ? string.Empty : string.Join(" → ", letters);
    }

    private static List<string> ExtractAltSection(string html, string[] startMarkers, string[] endMarkers, int max)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(html)) return result;
        var start = IndexOfAny(html, startMarkers, 0);
        if (start < 0) return result;
        var end = IndexOfAny(html, endMarkers, start + 2);
        if (end < 0 || end <= start) end = Math.Min(html.Length, start + 18000);
        var section = html.Substring(start, Math.Min(end - start, 18000));
        foreach (Match match in Regex.Matches(
                     section,
                     "(?:alt\\s*=\\s*[\"']|\"alt\"\\s*:\\s*\")(?<v>[^\"']{1,100})[\"']",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var value = WebUtility.HtmlDecode(match.Groups["v"].Value).Trim();
            if (!IsUsefulName(value) || result.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(value);
            if (result.Count >= max) break;
        }
        return result;
    }

    private static bool IsUsefulName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2) return false;
        var lower = value.ToLowerInvariant();
        if (lower.Contains("logo", StringComparison.Ordinal) || lower.Contains("advert", StringComparison.Ordinal) ||
            lower.Contains("op.gg", StringComparison.Ordinal) || lower == "image") return false;
        if (Regex.IsMatch(value, "^(技能|装备|出装|强化|增幅|表格|table|闪现|标记)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;
        return !Regex.IsMatch(value, "^[QWER]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ParseBalanceAdjustments(string text)
    {
        var values = new List<string>();
        const string pattern = "(?<name>Damage\\s+Dealt|Damage\\s+Taken|Attack\\s+Speed|Ability\\s+Haste|Cooldown\\s+Reduction|Healing|Shielding|Tenacity|Minion\\s+Damage)\\s*(?<v>[+-]?\\d+(?:\\.\\d+)?%?)";
        foreach (Match match in Regex.Matches(text ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var item = TranslateBalanceName(match.Groups["name"].Value) + " " + match.Groups["v"].Value;
            if (!values.Any(value => string.Equals(value, item, StringComparison.OrdinalIgnoreCase))) values.Add(item);
            if (values.Count >= 10) break;
        }
        return values.Count == 0 ? string.Empty : string.Join("  ·  ", values);
    }

    private static string TranslateBalanceName(string value) => Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), "\\s+", " ") switch
    {
        "attack speed" => "攻击速度",
        "damage dealt" => "造成伤害",
        "damage taken" => "承受伤害",
        "ability haste" or "cooldown reduction" => "技能急速",
        "healing" => "治疗",
        "shielding" => "护盾",
        "tenacity" => "韧性",
        "minion damage" => "对小兵伤害",
        _ => value.Trim()
    };

    private static string BuildSourceNote(bool hexdata, bool ranking, bool opgg)
    {
        var parts = new List<string>
        {
            hexdata ? "排行：Hexdata 国内优先" : ranking ? "排行：ARAMMayhem 备用" : "排行：部分降级",
            opgg ? "攻略：OP.GG 已补充" : "攻略：OP.GG 未连接也可查询",
            ranking ? "平衡：ARAMMayhem 完整状态" : "平衡：完整状态未连接",
            "国服版本：等待腾讯校验层"
        };
        return string.Join("；", parts);
    }

    private static string ExtractPreferredChampionName(string value)
    {
        var text = (value ?? string.Empty).Trim();
        var match = Regex.Match(text, "[（(](?<v>[^）)]+)[）)]", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["v"].Value.Trim() : text;
    }

    internal static string InferTierForSmoke(int rank) => InferTier(rank);
    private static string InferTier(int rank) => rank switch
    {
        <= 10 => "S+",
        <= 30 => "S",
        <= 60 => "A",
        <= 100 => "B",
        _ => "C"
    };

    private static string NormalizeEscapedHtml(string? value) => (value ?? string.Empty)
        .Replace("\\u003c", "<", StringComparison.OrdinalIgnoreCase)
        .Replace("\\u003e", ">", StringComparison.OrdinalIgnoreCase)
        .Replace("\\u0026", "&", StringComparison.OrdinalIgnoreCase)
        .Replace("\\\"", "\"", StringComparison.Ordinal)
        .Replace("\\/", "/", StringComparison.Ordinal);

    private static int IndexOfAny(string text, IEnumerable<string> markers, int startIndex)
    {
        var indexes = markers.Where(marker => !string.IsNullOrWhiteSpace(marker))
            .Select(marker => text.IndexOf(marker, Math.Max(0, startIndex), StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0).ToArray();
        return indexes.Length == 0 ? -1 : indexes.Min();
    }

    private static string MatchValue(string? source, string pattern)
    {
        var match = Regex.Match(source ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["v"].Value).Trim() : string.Empty;
    }

    private static string CleanText(string? html)
    {
        var text = Regex.Replace(html ?? string.Empty, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, "<[^>]+>", " ", RegexOptions.CultureInvariant);
        return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static string CleanName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var name = Regex.Replace(value, "\\s*(极地大乱斗|ARAM|Build|构建|出装).*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        return Regex.Replace(name, "^(Image:|图片:)\\s*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
    }

    private static string First(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    private static double? FirstRate(string first, string second) => Rate(first) ?? Rate(second);

    private static double? Rate(string? value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return null;
        if (number > 0 && number <= 1) number *= 100d;
        return number is >= 0 and <= 100 ? number : null;
    }

    private static string Title(string slug) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase((slug ?? string.Empty).Replace("-", " ", StringComparison.Ordinal));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queryGate.Dispose();
        if (_ownsSource && _source is IDisposable disposable) disposable.Dispose();
        lock (_cacheSync) _cache.Clear();
    }
}
