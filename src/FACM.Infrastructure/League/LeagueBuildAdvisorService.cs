using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.State;

namespace FACM.Infrastructure.League;

internal interface IOpggBuildSource
{
    Task<byte[]?> TryGetBytesAsync(string path, CancellationToken cancellationToken);
}

internal sealed class OpggBuildHttpSource : IOpggBuildSource, IDisposable
{
    private readonly HttpClient _client;

    public OpggBuildHttpSource(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://lol-api-champion.op.gg", UriKind.Absolute),
            Timeout = Timeout.InfiniteTimeSpan
        };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FACM", "4.0"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<byte[]?> TryGetBytesAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var response = await _client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
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

    public void Dispose() => _client.Dispose();
}

/// <summary>
/// Visible, user-driven Build Advisor. It reuses the shared Workbench/LCU owners and owns only the
/// external OP.GG transport. No polling loop is created here. In-game is cache-only by contract.
/// </summary>
public sealed class LeagueBuildAdvisorService : ILeagueBuildAdvisorService, IDisposable
{
    internal const string ChampionSummaryPath = "/lol-game-data/assets/v1/champion-summary.json";
    internal const string ItemsPath = "/lol-game-data/assets/v1/items.json";
    internal const string SummonerSpellsPath = "/lol-game-data/assets/v1/summoner-spells.json";
    internal const string PerksPath = "/lol-game-data/assets/v1/perks.json";
    internal const string DefaultOpggTier = "all";
    internal static readonly TimeSpan BuildCacheDuration = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan VersionCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RankedPositionCacheDuration = TimeSpan.FromMinutes(30);

    private readonly ILeagueWorkbenchDataSource _workbench;
    private readonly ILeagueReadGateway _lcu;
    private readonly PerformanceBudgetProvider _performance;
    private readonly IOpggBuildSource _opgg;
    private readonly bool _ownsOpgg;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<string, BuildCacheEntry> _buildCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TimedString> _versionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TimedString> _positionCache = new(StringComparer.OrdinalIgnoreCase);
    private BuildCatalog? _catalog;
    private DateTimeOffset _catalogCachedUtc = DateTimeOffset.MinValue;
    private BuildContext? _lastContext;
    private bool _disposed;

    private sealed record BuildCacheEntry(DateTimeOffset CachedUtc, LeagueBuildRecommendation Recommendation);
    private sealed record TimedString(DateTimeOffset CachedUtc, string Value);
    private sealed record BuildContext(int ChampionId, string Mode, string Position, string Version, DateTimeOffset UpdatedAtUtc);

    private sealed class BuildCatalog
    {
        public Dictionary<int, string> Champions { get; } = [];
        public Dictionary<int, string> Items { get; } = [];
        public Dictionary<int, string> Spells { get; } = [];
        public Dictionary<int, string> Perks { get; } = [];
    }

    public LeagueBuildAdvisorService(
        ILeagueWorkbenchDataSource workbench,
        ILeagueReadGateway lcu,
        PerformanceBudgetProvider performance)
        : this(workbench, lcu, performance, new OpggBuildHttpSource(), ownsOpgg: true)
    {
    }

    internal LeagueBuildAdvisorService(
        ILeagueWorkbenchDataSource workbench,
        ILeagueReadGateway lcu,
        PerformanceBudgetProvider performance,
        IOpggBuildSource opgg,
        bool ownsOpgg = false)
    {
        _workbench = workbench ?? throw new ArgumentNullException(nameof(workbench));
        _lcu = lcu ?? throw new ArgumentNullException(nameof(lcu));
        _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        _opgg = opgg ?? throw new ArgumentNullException(nameof(opgg));
        _ownsOpgg = ownsOpgg;
    }

    public async Task<LeagueBuildAdvisorSnapshot> RefreshAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var live = await _workbench.LoadLiveAsync(cancellationToken).ConfigureAwait(false);
            var phase = live.Phase;
            if (live.State == LeagueWorkbenchDataState.Unavailable)
                return LeagueBuildAdvisorSnapshot.Unavailable(phase, live.Detail);

            var local = live.Players.FirstOrDefault(player => player.IsLocalPlayer);
            var championId = ResolveChampionId(live, local);
            var mode = ResolveOpggMode(live.Queue?.QueueId ?? 0, live.Queue?.GameMode ?? string.Empty);
            var position = ResolveOpggPosition(local?.Position, mode);
            var version = string.Empty;

            if (IsInGamePhase(phase))
            {
                var context = ResolveInGameContext(championId, mode, position);
                var cached = context is null
                    ? null
                    : FindFreshBuild(context.ChampionId, context.Mode, context.Position, null);
                if (cached is null)
                {
                    return CreateSnapshot(
                        LeagueBuildAdvisorState.InGameNoCache,
                        live,
                        context?.ChampionId ?? championId,
                        string.Empty,
                        context?.Mode ?? mode,
                        context?.Position ?? position,
                        context?.Version ?? string.Empty,
                        fromCache: false,
                        recommendation: null,
                        "in-game-no-cache");
                }

                return CreateSnapshot(
                    LeagueBuildAdvisorState.InGameCache,
                    live,
                    context!.ChampionId,
                    ResolveCachedChampionName(context.ChampionId),
                    context.Mode,
                    context.Position,
                    context.Version,
                    fromCache: true,
                    cached.Recommendation,
                    "in-game-cache");
            }

            if (championId <= 0)
                return CreateSnapshot(LeagueBuildAdvisorState.WaitingChampion, live, 0, string.Empty, mode, position, string.Empty, false, null, "waiting-champion");
            if (string.IsNullOrWhiteSpace(mode))
                return CreateSnapshot(LeagueBuildAdvisorState.UnsupportedMode, live, championId, string.Empty, string.Empty, position, string.Empty, false, null, "unsupported-mode");

            var catalog = await EnsureCatalogAsync(cancellationToken).ConfigureAwait(false);
            var championName = ResolveName(catalog?.Champions, championId, "#" + championId.ToString(CultureInfo.InvariantCulture));

            if (!IsChampSelectPhase(phase))
                return CreateSnapshot(LeagueBuildAdvisorState.WaitingChampSelect, live, championId, championName, mode, position, string.Empty, false, null, "waiting-champ-select");

            try
            {
                version = await ResolveVersionAsync(mode, force, cancellationToken).ConfigureAwait(false);
                if (string.Equals(mode, "ranked", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(position, "all", StringComparison.OrdinalIgnoreCase))
                {
                    position = await ResolveRankedPositionAsync(championId, version, force, cancellationToken).ConfigureAwait(false);
                }

                var cached = force ? null : FindFreshBuild(championId, mode, position, version);
                if (cached is not null)
                {
                    RememberContext(championId, mode, position, version);
                    return CreateSnapshot(
                        LeagueBuildAdvisorState.Ready,
                        live,
                        championId,
                        championName,
                        mode,
                        position,
                        version,
                        true,
                        cached.Recommendation,
                        "ready");
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                var bytes = await _opgg.TryGetBytesAsync(BuildPath(championId, mode, position, version), timeout.Token)
                    .ConfigureAwait(false);
                var recommendation = ParseBuild(bytes, catalog);
                if (recommendation is null)
                {
                    return CreateSnapshot(
                        LeagueBuildAdvisorState.ProviderUnavailable,
                        live,
                        championId,
                        championName,
                        mode,
                        position,
                        version,
                        false,
                        null,
                        "opgg-unavailable");
                }

                lock (_sync)
                {
                    _buildCache[BuildCacheKey(championId, mode, position, version)] =
                        new BuildCacheEntry(DateTimeOffset.UtcNow, recommendation);
                }
                RememberContext(championId, mode, position, version);
                return CreateSnapshot(
                    LeagueBuildAdvisorState.Ready,
                    live,
                    championId,
                    championName,
                    mode,
                    position,
                    version,
                    false,
                    recommendation,
                    "ready");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return CreateSnapshot(
                    LeagueBuildAdvisorState.Timeout,
                    live,
                    championId,
                    championName,
                    mode,
                    position,
                    version,
                    false,
                    null,
                    "timeout");
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    internal LeagueBuildRecommendation? ParseBuild(byte[]? bytes, object? catalogForTests = null)
    {
        var catalog = catalogForTests as BuildCatalog ?? _catalog;
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetObject(document.RootElement, "data", out var data)) return null;

        double? winRate = null;
        double? pickRate = null;
        double? banRate = null;
        var tier = string.Empty;
        var rank = 0;
        if (TryGetObject(data, "summary", out var summary) &&
            TryGetObject(summary, "average_stats", out var average))
        {
            winRate = ReadDoubleNullable(average, "win_rate");
            pickRate = ReadDoubleNullable(average, "pick_rate");
            banRate = ReadDoubleNullable(average, "ban_rate");
            if (TryGetObject(average, "tier_data", out var tierData))
            {
                var tierNumber = ReadInt(tierData, "tier");
                tier = tierNumber > 0 ? "T" + tierNumber.ToString(CultureInfo.InvariantCulture) : string.Empty;
                rank = ReadInt(tierData, "rank");
            }
        }

        var rows = new List<LeagueBuildAdvisorRow>();
        AddPickRow(rows, data, "summoner_spells", "summoner-spells", catalog?.Spells);
        AddRuneRow(rows, data, catalog?.Perks);
        AddPickRow(rows, data, "starter_items", "starter-items", catalog?.Items);
        AddPickRow(rows, data, "boots", "boots", catalog?.Items);
        AddPickRow(rows, data, "core_items", "core-items", catalog?.Items);
        AddSkillRow(rows, data);
        AddCounterRow(rows, data, catalog?.Champions);

        return new LeagueBuildRecommendation(tier, rank, winRate, pickRate, banRate, rows);
    }

    internal static string ResolveOpggMode(int queueId, string? gameMode)
    {
        if (queueId == 450 || string.Equals(gameMode, "ARAM", StringComparison.OrdinalIgnoreCase)) return "aram";
        if (string.Equals(gameMode, "URF", StringComparison.OrdinalIgnoreCase)) return "urf";
        if (queueId is 400 or 420 or 430 or 440 or 0 ||
            string.IsNullOrWhiteSpace(gameMode) ||
            string.Equals(gameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase))
            return "ranked";
        return string.Empty;
    }

    internal static string ResolveOpggPosition(string? position, string? mode)
    {
        if (!string.Equals(mode, "ranked", StringComparison.OrdinalIgnoreCase)) return "none";
        if (string.IsNullOrWhiteSpace(position)) return "all";
        return position.Trim().ToUpperInvariant() switch
        {
            "TOP" => "top",
            "JUNGLE" => "jungle",
            "MIDDLE" or "MID" => "mid",
            "BOTTOM" or "ADC" => "adc",
            "UTILITY" or "SUPPORT" => "support",
            _ => "all"
        };
    }

    internal static string BuildPath(int championId, string? mode, string? position, string? version)
    {
        var lane = string.IsNullOrWhiteSpace(position) ? "none" : position;
        var path = "/api/global/champions/" + Uri.EscapeDataString(mode ?? "ranked") + "/" +
                   championId.ToString(CultureInfo.InvariantCulture) + "/" + Uri.EscapeDataString(lane);
        return AppendOpggQuery(path, version);
    }

    internal static string ChampionsPath(string? mode, string? version) =>
        AppendOpggQuery("/api/global/champions/" + Uri.EscapeDataString(mode ?? "ranked"), version);

    private async Task<BuildCatalog?> EnsureCatalogAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_catalog is not null && DateTimeOffset.UtcNow - _catalogCachedUtc < CatalogCacheDuration)
                return _catalog;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var champions = await _lcu.TryGetBytesAsync(ChampionSummaryPath, timeout.Token).ConfigureAwait(false);
            var items = await _lcu.TryGetBytesAsync(ItemsPath, timeout.Token).ConfigureAwait(false);
            var spells = await _lcu.TryGetBytesAsync(SummonerSpellsPath, timeout.Token).ConfigureAwait(false);
            var perks = await _lcu.TryGetBytesAsync(PerksPath, timeout.Token).ConfigureAwait(false);
            var catalog = ParseCatalog(champions, items, spells, perks);
            if (catalog.Champions.Count == 0) return catalog;
            lock (_sync)
            {
                _catalog = catalog;
                _catalogCachedUtc = DateTimeOffset.UtcNow;
            }
            return catalog;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            lock (_sync) return _catalog;
        }
    }

    private async Task<string> ResolveVersionAsync(string mode, bool force, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!force && _versionCache.TryGetValue(mode, out var cached) &&
                DateTimeOffset.UtcNow - cached.CachedUtc < VersionCacheDuration)
                return cached.Value;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var bytes = await _opgg.TryGetBytesAsync(
            "/api/global/champions/" + Uri.EscapeDataString(mode) + "/versions",
            timeout.Token).ConfigureAwait(false);
        using var document = ParseDocument(bytes);
        if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return string.Empty;
        var version = data.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? string.Empty)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(version))
        {
            lock (_sync) _versionCache[mode] = new TimedString(DateTimeOffset.UtcNow, version);
        }
        return version;
    }

    private async Task<string> ResolveRankedPositionAsync(
        int championId,
        string version,
        bool force,
        CancellationToken cancellationToken)
    {
        var key = championId.ToString(CultureInfo.InvariantCulture) + "|" + version;
        lock (_sync)
        {
            if (!force && _positionCache.TryGetValue(key, out var cached) &&
                DateTimeOffset.UtcNow - cached.CachedUtc < RankedPositionCacheDuration)
                return cached.Value;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var bytes = await _opgg.TryGetBytesAsync(ChampionsPath("ranked", version), timeout.Token).ConfigureAwait(false);
        var resolved = ParsePrimaryRankedPosition(bytes, championId);
        if (string.IsNullOrWhiteSpace(resolved)) resolved = "top";
        lock (_sync) _positionCache[key] = new TimedString(DateTimeOffset.UtcNow, resolved);
        return resolved;
    }

    internal static string ParsePrimaryRankedPosition(byte[]? bytes, int championId)
    {
        using var document = ParseDocument(bytes);
        if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return string.Empty;

        foreach (var champion in data.EnumerateArray())
        {
            if (champion.ValueKind != JsonValueKind.Object || ReadInt(champion, "id") != championId) continue;
            if (!champion.TryGetProperty("positions", out var positions) || positions.ValueKind != JsonValueKind.Array)
                return string.Empty;

            var best = string.Empty;
            var bestRoleRate = double.MinValue;
            var bestPlay = int.MinValue;
            foreach (var position in positions.EnumerateArray())
            {
                if (position.ValueKind != JsonValueKind.Object) continue;
                var mapped = MapOpggPositionName(ReadString(position, "name"));
                if (string.IsNullOrWhiteSpace(mapped)) continue;
                var roleRate = 0d;
                var play = 0;
                if (TryGetObject(position, "stats", out var stats))
                {
                    roleRate = ReadDoubleNullable(stats, "role_rate") ?? 0d;
                    play = ReadInt(stats, "play");
                }
                if (string.IsNullOrWhiteSpace(best) || roleRate > bestRoleRate ||
                    (Math.Abs(roleRate - bestRoleRate) < 0.000001d && play > bestPlay))
                {
                    best = mapped;
                    bestRoleRate = roleRate;
                    bestPlay = play;
                }
            }
            return best;
        }
        return string.Empty;
    }

    private static BuildCatalog ParseCatalog(byte[]? champions, byte[]? items, byte[]? spells, byte[]? perks)
    {
        var catalog = new BuildCatalog();
        ParseIdNameArray(champions, catalog.Champions);
        ParseIdNameArray(items, catalog.Items);
        ParseIdNameArray(spells, catalog.Spells);
        ParseIdNameArray(perks, catalog.Perks);
        return catalog;
    }

    private static void ParseIdNameArray(byte[]? bytes, IDictionary<int, string> target)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Array) return;
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            var id = ReadInt(row, "id");
            var name = ReadString(row, "name");
            if (id > 0 && !string.IsNullOrWhiteSpace(name)) target[id] = name.Trim();
        }
    }

    private static void AddPickRow(
        ICollection<LeagueBuildAdvisorRow> rows,
        JsonElement data,
        string property,
        string category,
        IDictionary<int, string>? names)
    {
        if (!TryGetFirstObject(data, property, out var row)) return;
        var ids = ReadIntArray(row, "ids");
        if (ids.Count == 0) return;
        rows.Add(new LeagueBuildAdvisorRow(category, JoinNames(ids, names), BuildEvidence(row)));
    }

    private static void AddRuneRow(
        ICollection<LeagueBuildAdvisorRow> rows,
        JsonElement data,
        IDictionary<int, string>? names)
    {
        JsonElement runePage;
        if (!TryGetFirstObject(data, "runes", out runePage) && !TryGetFirstObject(data, "rune_pages", out runePage))
            return;
        var build = TryGetFirstObject(runePage, "builds", out var buildRow) ? buildRow : runePage;
        var ids = ReadIntArray(build, "primary_rune_ids");
        ids.AddRange(ReadIntArray(build, "secondary_rune_ids"));
        if (ids.Count == 0) return;
        rows.Add(new LeagueBuildAdvisorRow("runes", JoinNames(ids, names), BuildEvidence(build)));
    }

    private static void AddSkillRow(ICollection<LeagueBuildAdvisorRow> rows, JsonElement data)
    {
        if (!TryGetFirstObject(data, "skill_masteries", out var row) ||
            !row.TryGetProperty("ids", out var ids) || ids.ValueKind != JsonValueKind.Array)
            return;
        var values = ids.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (values.Length == 0) return;
        rows.Add(new LeagueBuildAdvisorRow("skills", string.Join(" > ", values!), BuildEvidence(row)));
    }

    private static void AddCounterRow(
        ICollection<LeagueBuildAdvisorRow> rows,
        JsonElement data,
        IDictionary<int, string>? championNames)
    {
        if (!data.TryGetProperty("counters", out var counters) || counters.ValueKind != JsonValueKind.Array) return;
        var labels = new List<string>();
        var plays = 0;
        foreach (var row in counters.EnumerateArray().Take(5))
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            var id = ReadInt(row, "champion_id");
            if (id <= 0) continue;
            labels.Add(ResolveName(championNames, id, "#" + id.ToString(CultureInfo.InvariantCulture)));
            plays += Math.Max(0, ReadInt(row, "play"));
        }
        if (labels.Count == 0) return;
        rows.Add(new LeagueBuildAdvisorRow("counters", string.Join(" · ", labels), plays + " games"));
    }

    private static string BuildEvidence(JsonElement row)
    {
        var parts = new List<string>();
        var pickRate = ReadDoubleNullable(row, "pick_rate");
        if (pickRate.HasValue) parts.Add("pick " + FormatRate(pickRate.Value));
        var play = ReadInt(row, "play");
        if (play > 0) parts.Add(play.ToString(CultureInfo.InvariantCulture) + " games");
        return string.Join(" · ", parts);
    }

    private static string FormatRate(double rate)
    {
        var normalized = rate <= 1.0d ? rate * 100d : rate;
        return normalized.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static string JoinNames(IEnumerable<int> ids, IDictionary<int, string>? names) =>
        string.Join(" · ", ids.Select(id => ResolveName(names, id, "#" + id.ToString(CultureInfo.InvariantCulture))));

    private static string ResolveName(IDictionary<int, string>? names, int id, string fallback) =>
        names is not null && names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : fallback;

    private string ResolveCachedChampionName(int championId)
    {
        lock (_sync) return ResolveName(_catalog?.Champions, championId, "#" + championId.ToString(CultureInfo.InvariantCulture));
    }

    private BuildContext? ResolveInGameContext(int championId, string mode, string position)
    {
        lock (_sync)
        {
            if (championId > 0 && !string.IsNullOrWhiteSpace(mode))
            {
                var version = _lastContext is not null && _lastContext.ChampionId == championId &&
                              string.Equals(_lastContext.Mode, mode, StringComparison.OrdinalIgnoreCase)
                    ? _lastContext.Version
                    : string.Empty;
                return new BuildContext(championId, mode, position, version, DateTimeOffset.UtcNow);
            }
            return _lastContext is not null && DateTimeOffset.UtcNow - _lastContext.UpdatedAtUtc < BuildCacheDuration
                ? _lastContext
                : null;
        }
    }

    private void RememberContext(int championId, string mode, string position, string version)
    {
        lock (_sync) _lastContext = new BuildContext(championId, mode, position, version, DateTimeOffset.UtcNow);
    }

    private BuildCacheEntry? FindFreshBuild(int championId, string mode, string position, string? version)
    {
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(version))
            {
                return _buildCache.TryGetValue(BuildCacheKey(championId, mode, position, version), out var exact) &&
                       DateTimeOffset.UtcNow - exact.CachedUtc < BuildCacheDuration
                    ? exact
                    : null;
            }

            var prefix = championId.ToString(CultureInfo.InvariantCulture) + "|" + mode + "|" + position + "|";
            return _buildCache
                .Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                               DateTimeOffset.UtcNow - pair.Value.CachedUtc < BuildCacheDuration)
                .OrderByDescending(pair => pair.Value.CachedUtc)
                .Select(pair => pair.Value)
                .FirstOrDefault();
        }
    }

    private static string BuildCacheKey(int championId, string mode, string position, string version) =>
        championId.ToString(CultureInfo.InvariantCulture) + "|" + mode + "|" + position + "|" + version;

    private static int ResolveChampionId(LeagueWorkbenchLiveSnapshot live, LeagueWorkbenchLivePlayer? local)
    {
        if (local is not null && local.ChampionId > 0) return local.ChampionId;
        if (local is not null && local.ChampionPickIntent > 0) return local.ChampionPickIntent;
        return live.LocalActionChampionId > 0 ? live.LocalActionChampionId : 0;
    }

    private static bool IsChampSelectPhase(string? phase) =>
        string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase);

    private static bool IsInGamePhase(string? phase) =>
        string.Equals(phase, "InProgress", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(phase, "WatchInProgress", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(phase, "Reconnect", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(phase, "GameStart", StringComparison.OrdinalIgnoreCase);

    private LeagueBuildAdvisorSnapshot CreateSnapshot(
        LeagueBuildAdvisorState state,
        LeagueWorkbenchLiveSnapshot live,
        int championId,
        string championName,
        string mode,
        string position,
        string version,
        bool fromCache,
        LeagueBuildRecommendation? recommendation,
        string detail) =>
        new(
            state,
            live.Phase,
            live.Queue?.QueueId ?? 0,
            championId,
            championName,
            mode,
            position,
            "OP.GG Global",
            version,
            fromCache,
            recommendation,
            detail + ";budget=" + _performance.Current.Name,
            DateTimeOffset.UtcNow);

    private static string AppendOpggQuery(string path, string? version)
    {
        var query = "?tier=" + Uri.EscapeDataString(DefaultOpggTier);
        if (!string.IsNullOrWhiteSpace(version)) query += "&version=" + Uri.EscapeDataString(version);
        return path + query;
    }

    private static string MapOpggPositionName(string? name) =>
        (name ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "TOP" => "top",
            "JUNGLE" => "jungle",
            "MID" or "MIDDLE" => "mid",
            "ADC" or "BOTTOM" => "adc",
            "SUPPORT" or "UTILITY" => "support",
            _ => string.Empty
        };

    private static JsonDocument? ParseDocument(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try { return JsonDocument.Parse(bytes); }
        catch (JsonException) { return null; }
    }

    private static bool TryGetObject(JsonElement source, string property, out JsonElement value)
    {
        if (source.ValueKind == JsonValueKind.Object &&
            source.TryGetProperty(property, out value) &&
            value.ValueKind == JsonValueKind.Object)
            return true;
        value = default;
        return false;
    }

    private static bool TryGetFirstObject(JsonElement source, string property, out JsonElement value)
    {
        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in array.EnumerateArray())
            {
                if (candidate.ValueKind != JsonValueKind.Object) continue;
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static List<int> ReadIntArray(JsonElement source, string property)
    {
        var result = new List<int>();
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var value in array.EnumerateArray())
        {
            int id;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out id) && id > 0)
                result.Add(id);
            else if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out id) && id > 0)
                result.Add(id);
        }
        return result;
    }

    private static string ReadString(JsonElement source, string property)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(property, out var value)) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static int ReadInt(JsonElement source, string property)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : 0;
    }

    private static double? ReadDoubleNullable(JsonElement source, string property)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _requestGate.Dispose();
        lock (_sync)
        {
            _buildCache.Clear();
            _versionCache.Clear();
            _positionCache.Clear();
            _catalog = null;
            _lastContext = null;
        }
        if (_ownsOpgg && _opgg is IDisposable disposable) disposable.Dispose();
    }
}
