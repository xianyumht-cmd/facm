using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using FACM.Core.Mayhem;

namespace FACM.Infrastructure.Mayhem;

internal enum TencentMayhemSourceKind
{
    NewsIndex,
    Article
}

internal sealed record TencentMayhemSourceRequest(TencentMayhemSourceKind Kind, long ArticleId = 0);

internal interface ITencentMayhemSource
{
    Task<string?> TryGetStringAsync(TencentMayhemSourceRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Fixed-host Tencent transport. Business code can choose the news index or a numeric article id;
/// it cannot supply an arbitrary URL.
/// </summary>
internal sealed class TencentMayhemHttpSource : ITencentMayhemSource, IDisposable
{
    internal const string NewsIndexUrl = "https://lol.qq.com/news/index.shtml";
    internal const string ArticleBaseUrl = "https://lol.qq.com/gicp/news/410/";
    internal const long KnownFallbackArticleId = 37092739;

    private readonly HttpClient _client;
    private bool _disposed;

    public TencentMayhemHttpSource(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FACM", "4.0"));
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9");
    }

    public async Task<string?> TryGetStringAsync(TencentMayhemSourceRequest request, CancellationToken cancellationToken)
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

    internal static (Uri Uri, TimeSpan Budget) Resolve(TencentMayhemSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Kind switch
        {
            TencentMayhemSourceKind.NewsIndex => (new Uri(NewsIndexUrl), TimeSpan.FromSeconds(1.8)),
            TencentMayhemSourceKind.Article when request.ArticleId > 0 =>
                (new Uri(ArticleBaseUrl + request.ArticleId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".html"), TimeSpan.FromSeconds(2.4)),
            _ => throw new ArgumentException("Tencent Mayhem article requests require a positive numeric article id.", nameof(request))
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
    }
}

/// <summary>
/// FACM 3.5-compatible official CN patch discovery and Mayhem hero-change parser.
/// It owns no UI state and performs no League writes.
/// </summary>
public sealed class TencentMayhemOfficialPatchService : IMayhemOfficialPatchService, IDisposable
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan OverallBudget = TimeSpan.FromSeconds(4);
    public const int MaximumCandidateArticles = 7;

    private readonly ITencentMayhemSource _source;
    private readonly bool _ownsSource;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _cacheSync = new();
    private MayhemOfficialPatchSnapshot? _cache;
    private DateTimeOffset _cacheTime;
    private bool _disposed;

    public TencentMayhemOfficialPatchService()
        : this(new TencentMayhemHttpSource(), ownsSource: true, () => DateTimeOffset.UtcNow)
    {
    }

    internal TencentMayhemOfficialPatchService(
        ITencentMayhemSource source,
        bool ownsSource = false,
        Func<DateTimeOffset>? utcNow = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _ownsSource = ownsSource;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<MayhemOfficialPatchSnapshot?> FetchLatestAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (TryGetCache(out var cached)) return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetCache(out cached)) return cached;

            using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overall.CancelAfter(OverallBudget);
            var budgetToken = overall.Token;

            string? indexHtml = null;
            try
            {
                indexHtml = await _source.TryGetStringAsync(
                    new TencentMayhemSourceRequest(TencentMayhemSourceKind.NewsIndex),
                    budgetToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            var candidates = ExtractArticleIds(indexHtml)
                .Take(MaximumCandidateArticles - 1)
                .Append(TencentMayhemHttpSource.KnownFallbackArticleId)
                .Distinct()
                .Take(MaximumCandidateArticles)
                .ToArray();

            var tasks = candidates.Select(id => ReadAndParseArticleAsync(id, budgetToken, cancellationToken)).ToArray();
            MayhemOfficialPatchSnapshot?[] snapshots;
            try
            {
                snapshots = await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                snapshots = tasks
                    .Where(task => task.Status == TaskStatus.RanToCompletion)
                    .Select(task => task.Result)
                    .ToArray();
            }

            var latest = snapshots
                .Where(snapshot => snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.Patch))
                .OrderByDescending(snapshot => ParseVersion(snapshot!.Patch))
                .FirstOrDefault();
            if (latest is not null) PutCache(latest);
            return latest;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MayhemOfficialPatchSnapshot?> ReadAndParseArticleAsync(
        long articleId,
        CancellationToken budgetToken,
        CancellationToken userToken)
    {
        try
        {
            var html = await _source.TryGetStringAsync(
                new TencentMayhemSourceRequest(TencentMayhemSourceKind.Article, articleId),
                budgetToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(html)
                ? null
                : ParseArticle(html, TencentMayhemHttpSource.ArticleBaseUrl + articleId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".html");
        }
        catch (OperationCanceledException) when (!userToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private bool TryGetCache(out MayhemOfficialPatchSnapshot? snapshot)
    {
        lock (_cacheSync)
        {
            if (_cache is not null && _utcNow() - _cacheTime < CacheDuration)
            {
                snapshot = _cache;
                return true;
            }
        }
        snapshot = null;
        return false;
    }

    private void PutCache(MayhemOfficialPatchSnapshot snapshot)
    {
        lock (_cacheSync)
        {
            _cache = snapshot;
            _cacheTime = _utcNow();
        }
    }

    internal static IReadOnlyList<long> ExtractArticleIdsForSmoke(string? html) => ExtractArticleIds(html);
    internal static MayhemOfficialPatchSnapshot? ParseArticleForSmoke(string? html) => ParseArticle(html, "fixture://tencent-mayhem");

    private static IReadOnlyList<long> ExtractArticleIds(string? html)
    {
        var output = new List<long>();
        foreach (Match match in Regex.Matches(
                     html ?? string.Empty,
                     "(?:https?:)?//lol\\.qq\\.com/gicp/news/410/(?<id>\\d+)\\.html|/gicp/news/410/(?<rid>\\d+)\\.html",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var idText = match.Groups["id"].Success ? match.Groups["id"].Value : match.Groups["rid"].Value;
            if (!long.TryParse(idText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id)) continue;
            if (id <= 0 || output.Contains(id)) continue;
            output.Add(id);
        }
        return output.OrderByDescending(id => id).ToArray();
    }

    private static MayhemOfficialPatchSnapshot? ParseArticle(string? html, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var decoded = WebUtility.HtmlDecode(html);
        var plain = CleanText(decoded);
        var patchMatch = Regex.Match(
            plain,
            "(?:发布|欢迎来到)\\s*(?<v>\\d{1,2}\\.\\d{1,2})\\s*版本",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!patchMatch.Success) return null;

        var sectionStart = FindMayhemHeading(decoded);
        if (sectionStart < 0) return null;
        var sectionEnd = decoded.IndexOf("斗魂竞技场", sectionStart + 5, StringComparison.OrdinalIgnoreCase);
        if (sectionEnd < 0) sectionEnd = Math.Min(decoded.Length, sectionStart + 90000);
        var section = decoded.Substring(sectionStart, sectionEnd - sectionStart);
        var lines = ToLines(section);

        var changes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var inHeroes = false;
        string? champion = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line == "英雄")
            {
                inHeroes = true;
                champion = null;
                continue;
            }
            if (!inHeroes) continue;
            if (line.Contains("强化符文", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("BUG修复", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Bug修复", StringComparison.OrdinalIgnoreCase))
                break;

            if (line.Contains('⇒') || line.Contains('→'))
            {
                if (string.IsNullOrWhiteSpace(champion)) continue;
                var change = NormalizeChange(line);
                if (change.Length == 0) continue;
                if (!changes.TryGetValue(champion, out var championChanges))
                {
                    championChanges = [];
                    changes[champion] = championChanges;
                }
                if (!championChanges.Contains(change, StringComparer.Ordinal)) championChanges.Add(change);
                continue;
            }

            if (LooksLikeChampionHeading(line)) champion = CleanHeading(line);
        }

        return new MayhemOfficialPatchSnapshot
        {
            Patch = patchMatch.Groups["v"].Value,
            SourceUrl = sourceUrl,
            ChampionChanges = changes
        };
    }

    private static int FindMayhemHeading(string? html)
    {
        foreach (Match heading in Regex.Matches(
                     html ?? string.Empty,
                     "<h(?<level>[1-6])\\b[^>]*>(?<body>.*?)</h\\k<level>>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            if (CleanText(heading.Groups["body"].Value).Contains("海克斯大乱斗", StringComparison.OrdinalIgnoreCase))
                return heading.Index;
        }

        var markdown = Regex.Match(
            html ?? string.Empty,
            "(?:^|[\\r\\n])\\s*#{1,6}\\s*海克斯大乱斗\\s*(?:[\\r\\n]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return markdown.Success ? markdown.Index : -1;
    }

    private static string[] ToLines(string? html)
    {
        var text = html ?? string.Empty;
        text = Regex.Replace(text, "<(br|hr)\\b[^>]*>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, "</(li|p|div|h1|h2|h3|h4|h5|h6|blockquote)>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, "<[^>]+>", string.Empty, RegexOptions.CultureInvariant);
        text = WebUtility.HtmlDecode(text)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\u00a0', ' ');
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => Regex.Replace(value, "\\s+", " ", RegexOptions.CultureInvariant).Trim())
            .Where(value => value.Length > 0)
            .ToArray();
    }

    private static bool LooksLikeChampionHeading(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > 24) return false;
        if (line.Contains('：') || line.Contains(':') || line.Contains("版本", StringComparison.Ordinal) || line.Contains("海克斯", StringComparison.Ordinal)) return false;
        if (line.StartsWith("[新]", StringComparison.OrdinalIgnoreCase)) return false;
        return line.Any(ch => ch > 127) && !line.Any(char.IsDigit);
    }

    private static string CleanHeading(string? line) =>
        Regex.Replace(line ?? string.Empty, "^[•●*\\-]+", string.Empty, RegexOptions.CultureInvariant).Trim();

    private static string NormalizeChange(string? line)
    {
        var value = Regex.Replace(line ?? string.Empty, "^[•●*\\-]+", string.Empty, RegexOptions.CultureInvariant).Trim();
        value = value.Replace("⇒", "→", StringComparison.Ordinal);
        value = Regex.Replace(value, "\\s+", " ", RegexOptions.CultureInvariant);
        return value.Length > 120 ? value[..120] : value;
    }

    private static string CleanText(string? html)
    {
        var text = Regex.Replace(html ?? string.Empty, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, "<[^>]+>", " ", RegexOptions.CultureInvariant);
        return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static Version ParseVersion(string? value) => Version.TryParse(value, out var version) ? version : new Version(0, 0);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        if (_ownsSource && _source is IDisposable disposable) disposable.Dispose();
        lock (_cacheSync)
        {
            _cache = null;
            _cacheTime = default;
        }
    }
}
