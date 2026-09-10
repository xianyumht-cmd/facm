using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal interface IOpggBuildApi
    {
        Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken);
    }

    internal sealed class OpggBuildApiClient : IOpggBuildApi, IDisposable
    {
        private readonly HttpClient _client;

        public OpggBuildApiClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://lol-api-champion.op.gg", UriKind.Absolute),
                Timeout = Timeout.InfiniteTimeSpan
            };
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FACM/3.2 OP.GG Build Advisor");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        }

        public async Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using (var response = await _client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        AppLog.Info("OP.GG build request returned HTTP " + (int)response.StatusCode + "; path=" + path);
                        return null;
                    }
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        await input.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
                        return output.ToArray();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLog.Info("OP.GG build request skipped; path=" + path + "; error=" + exception.Message);
                return null;
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }

    internal sealed class LeagueBuildAdvisorDataService : IDisposable
    {
        internal const string ChampionSummaryPath = "/lol-game-data/assets/v1/champion-summary.json";
        internal const string ItemsPath = "/lol-game-data/assets/v1/items.json";
        internal const string SummonerSpellsPath = "/lol-game-data/assets/v1/summoner-spells.json";
        internal const string PerksPath = "/lol-game-data/assets/v1/perks.json";
        internal const string DefaultOpggTier = "all";
        internal static readonly TimeSpan BuildCacheDuration = TimeSpan.FromMinutes(10);
        private const int AlternativeRowLimit = 3;
        private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan VersionCacheDuration = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan RankedPositionCacheDuration = TimeSpan.FromMinutes(30);

        private readonly object _sync = new object();
        private readonly ILeagueClientApi _client;
        private readonly PerformanceBudgetProvider _budgets;
        private readonly LeagueLiveDataService _live;
        private readonly IOpggBuildApi _opgg;
        private readonly bool _ownsOpgg;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, CacheEntry> _buildCache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, VersionEntry> _versionCache = new Dictionary<string, VersionEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RankedPositionEntry> _rankedPositionCache = new Dictionary<string, RankedPositionEntry>(StringComparer.OrdinalIgnoreCase);
        private LeagueBuildAdvisorCatalog _catalog;
        private DateTime _catalogCachedUtc = DateTime.MinValue;
        private bool _disposed;

        private sealed class CacheEntry
        {
            public DateTime CachedUtc { get; set; }
            public LeagueBuildRecommendation Recommendation { get; set; }
        }

        private sealed class VersionEntry
        {
            public DateTime CachedUtc { get; set; }
            public string Version { get; set; }
        }

        private sealed class RankedPositionEntry
        {
            public DateTime CachedUtc { get; set; }
            public string Position { get; set; }
        }

        public LeagueBuildAdvisorDataService(ILeagueClientApi client, PerformanceBudgetProvider budgets)
            : this(client, budgets, new OpggBuildApiClient(), true)
        {
        }

        internal LeagueBuildAdvisorDataService(
            ILeagueClientApi client,
            PerformanceBudgetProvider budgets,
            IOpggBuildApi opgg,
            bool ownsOpgg = false)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
            _opgg = opgg ?? throw new ArgumentNullException(nameof(opgg));
            _ownsOpgg = ownsOpgg;
            _live = new LeagueLiveDataService(client, budgets);
        }

        public async Task<LeagueBuildAdvisorSnapshot> RefreshAsync(bool force, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var live = await _live.RefreshAsync(cancellationToken).ConfigureAwait(false);
                var snapshot = CreateBaseSnapshot(live);
                if (live == null || !live.Connected)
                {
                    snapshot.Status = "client-required";
                    return snapshot;
                }

                var local = live.Players.FirstOrDefault(row => row.IsLocalPlayer);
                var championId = ResolveChampionId(live, local);
                var mode = ResolveOpggMode(live.QueueId, live.GameMode);
                var position = ResolveOpggPosition(local == null ? null : local.Position, mode);

                snapshot.ChampionId = championId;
                snapshot.Mode = mode;
                snapshot.Position = position;
                snapshot.Source = "OP.GG Global";

                if (live.Activity == LeagueActivityLevel.InGame)
                {
                    var cached = FindFreshBuild(championId, mode, position, null);
                    if (cached != null)
                    {
                        snapshot.Recommendation = CloneRecommendation(cached.Recommendation);
                        snapshot.FromCache = true;
                        snapshot.Status = "in-game-cache";
                    }
                    else
                    {
                        snapshot.Status = "in-game-no-cache";
                    }
                    return snapshot;
                }

                if (championId <= 0)
                {
                    snapshot.Status = "waiting-champion";
                    return snapshot;
                }
                if (string.IsNullOrWhiteSpace(mode))
                {
                    snapshot.Status = "unsupported-mode";
                    return snapshot;
                }

                var catalog = await EnsureCatalogAsync(cancellationToken).ConfigureAwait(false);
                snapshot.ChampionName = ResolveName(catalog == null ? null : catalog.Champions, championId, "#" + championId);

                if (live.Activity != LeagueActivityLevel.ChampSelect)
                {
                    snapshot.Status = "waiting-champ-select";
                    return snapshot;
                }

                var version = await ResolveVersionAsync(mode, force, cancellationToken).ConfigureAwait(false);
                snapshot.Version = version;
                if (string.Equals(mode, "ranked", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(position, "all", StringComparison.OrdinalIgnoreCase))
                {
                    position = await ResolveRankedPositionAsync(championId, version, force, cancellationToken).ConfigureAwait(false);
                    snapshot.Position = position;
                }

                var cachedBuild = force ? null : FindFreshBuild(championId, mode, position, version);
                if (cachedBuild != null)
                {
                    snapshot.Recommendation = CloneRecommendation(cachedBuild.Recommendation);
                    snapshot.FromCache = true;
                    snapshot.Status = "ready";
                    return snapshot;
                }

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    var path = BuildPath(championId, mode, position, version);
                    var bytes = await _opgg.TryGetBytesAsync(path, timeout.Token).ConfigureAwait(false);
                    var recommendation = ParseBuild(bytes, catalog);
                    if (recommendation == null)
                    {
                        snapshot.Status = "opgg-unavailable";
                        return snapshot;
                    }

                    snapshot.Recommendation = recommendation;
                    snapshot.Status = "ready";
                    var key = BuildCacheKey(championId, mode, position, version);
                    lock (_sync)
                    {
                        _buildCache[key] = new CacheEntry
                        {
                            CachedUtc = DateTime.UtcNow,
                            Recommendation = CloneRecommendation(recommendation)
                        };
                    }
                    return snapshot;
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                return new LeagueBuildAdvisorSnapshot
                {
                    Source = "OP.GG Global",
                    Status = "timeout",
                    BudgetName = _budgets.Current.Name,
                    UpdatedAtUtc = DateTime.UtcNow
                };
            }
            finally
            {
                _requestGate.Release();
            }
        }

        internal LeagueBuildRecommendation ParseBuild(byte[] bytes, LeagueBuildAdvisorCatalog catalog)
        {
            var root = ParseObject(bytes);
            var data = ReadDictionary(root, "data");
            if (data == null) return null;

            var output = new LeagueBuildRecommendation();
            var summary = ReadDictionary(data, "summary");
            var average = ReadDictionary(summary, "average_stats");
            output.WinRate = ReadDoubleNullable(average, "win_rate");
            output.PickRate = ReadDoubleNullable(average, "pick_rate");
            output.BanRate = ReadDoubleNullable(average, "ban_rate");
            var tierData = ReadDictionary(average, "tier_data");
            var tier = ReadInt(tierData, "tier");
            output.Tier = tier > 0 ? "T" + tier : null;
            output.Rank = ReadInt(tierData, "rank");

            // Preserve the source ordering. Row zero remains the default consumed by existing Apply
            // owners; up to two additional rows are display-only alternatives. No extra request is made.
            AddPickRows(output, "summoner-spells", ReadValue(data, "summoner_spells"), catalog == null ? null : catalog.Spells, AlternativeRowLimit);
            AddRuneRows(output, ReadValue(data, "runes"), ReadValue(data, "rune_pages"), catalog == null ? null : catalog.Perks, AlternativeRowLimit);
            AddPickRows(output, "starter-items", ReadValue(data, "starter_items"), catalog == null ? null : catalog.Items, AlternativeRowLimit);
            AddPickRows(output, "boots", ReadValue(data, "boots"), catalog == null ? null : catalog.Items, AlternativeRowLimit);
            AddPickRows(output, "core-items", ReadValue(data, "core_items"), catalog == null ? null : catalog.Items, AlternativeRowLimit);
            AddSkillRows(output, ReadValue(data, "skill_masteries"), AlternativeRowLimit);
            AddCounterRow(output, ReadValue(data, "counters"), catalog == null ? null : catalog.Champions);
            return output;
        }

        internal LeagueBuildAdvisorCatalog ParseCatalog(
            byte[] championsBytes,
            byte[] itemsBytes,
            byte[] spellsBytes,
            byte[] perksBytes)
        {
            var catalog = new LeagueBuildAdvisorCatalog();
            ParseIdNameArray(championsBytes, catalog.Champions);
            ParseIdNameArray(itemsBytes, catalog.Items);
            ParseIdNameArray(spellsBytes, catalog.Spells);
            ParseIdNameArray(perksBytes, catalog.Perks);
            return catalog;
        }

        internal string ParsePrimaryRankedPosition(byte[] bytes, int championId)
        {
            var root = ParseObject(bytes);
            var rows = EnumerateDictionaries(ReadValue(root, "data"));
            var champion = rows.FirstOrDefault(row => ReadInt(row, "id") == championId);
            if (champion == null) return null;

            string best = null;
            var bestRoleRate = double.MinValue;
            var bestPlay = int.MinValue;
            foreach (var position in EnumerateDictionaries(ReadValue(champion, "positions")))
            {
                var mapped = MapOpggPositionName(ReadString(position, "name"));
                if (string.IsNullOrWhiteSpace(mapped)) continue;
                var stats = ReadDictionary(position, "stats");
                var roleRate = ReadDoubleNullable(stats, "role_rate") ?? 0.0;
                var play = ReadInt(stats, "play");
                if (best == null || roleRate > bestRoleRate ||
                    (Math.Abs(roleRate - bestRoleRate) < 0.000001 && play > bestPlay))
                {
                    best = mapped;
                    bestRoleRate = roleRate;
                    bestPlay = play;
                }
            }
            return best;
        }

        internal static int ResolveChampionId(LeagueLiveSnapshot live, LeagueLivePlayerRow local)
        {
            if (local != null && local.ChampionId > 0) return local.ChampionId;
            if (local != null && local.ChampionPickIntent > 0) return local.ChampionPickIntent;
            return live != null && live.LocalActionChampionId > 0 ? live.LocalActionChampionId : 0;
        }

        internal static string ResolveOpggMode(int queueId, string gameMode)
        {
            if (queueId == 450 || string.Equals(gameMode, "ARAM", StringComparison.OrdinalIgnoreCase)) return "aram";
            if (string.Equals(gameMode, "URF", StringComparison.OrdinalIgnoreCase)) return "urf";
            if (queueId == 400 || queueId == 420 || queueId == 430 || queueId == 440 || queueId == 0 ||
                string.IsNullOrWhiteSpace(gameMode) || string.Equals(gameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase))
                return "ranked";
            return null;
        }

        internal static string ResolveOpggPosition(string position, string mode)
        {
            if (!string.Equals(mode, "ranked", StringComparison.OrdinalIgnoreCase)) return "none";
            if (string.IsNullOrWhiteSpace(position)) return "all";
            switch (position.Trim().ToUpperInvariant())
            {
                case "TOP": return "top";
                case "JUNGLE": return "jungle";
                case "MIDDLE":
                case "MID": return "mid";
                case "BOTTOM":
                case "ADC": return "adc";
                case "UTILITY":
                case "SUPPORT": return "support";
                default: return "all";
            }
        }

        internal static string BuildPath(int championId, string mode, string position, string version)
        {
            var lane = string.IsNullOrWhiteSpace(position) ? "none" : position;
            var path = "/api/global/champions/" + Uri.EscapeDataString(mode ?? "ranked") + "/" + championId + "/" +
                       Uri.EscapeDataString(lane);
            return AppendOpggQuery(path, version);
        }

        internal static string ChampionsPath(string mode, string version)
        {
            return AppendOpggQuery(
                "/api/global/champions/" + Uri.EscapeDataString(mode ?? "ranked"),
                version);
        }

        private static string AppendOpggQuery(string path, string version)
        {
            var query = "?tier=" + Uri.EscapeDataString(DefaultOpggTier);
            if (!string.IsNullOrWhiteSpace(version))
                query += "&version=" + Uri.EscapeDataString(version);
            return path + query;
        }

        private async Task<LeagueBuildAdvisorCatalog> EnsureCatalogAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_catalog != null && DateTime.UtcNow - _catalogCachedUtc < CatalogCacheDuration)
                    return CloneCatalog(_catalog);
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    var champions = await _client.TryGetBytesAsync(ChampionSummaryPath, timeout.Token).ConfigureAwait(false);
                    var items = await _client.TryGetBytesAsync(ItemsPath, timeout.Token).ConfigureAwait(false);
                    var spells = await _client.TryGetBytesAsync(SummonerSpellsPath, timeout.Token).ConfigureAwait(false);
                    var perks = await _client.TryGetBytesAsync(PerksPath, timeout.Token).ConfigureAwait(false);
                    var parsed = ParseCatalog(champions, items, spells, perks);
                    if (parsed.Champions.Count == 0) return parsed;
                    lock (_sync)
                    {
                        _catalog = CloneCatalog(parsed);
                        _catalogCachedUtc = DateTime.UtcNow;
                    }
                    return parsed;
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    lock (_sync) return CloneCatalog(_catalog);
                }
            }
        }

        private async Task<string> ResolveVersionAsync(string mode, bool force, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                VersionEntry cached;
                if (!force && _versionCache.TryGetValue(mode, out cached) &&
                    DateTime.UtcNow - cached.CachedUtc < VersionCacheDuration)
                    return cached.Version;
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(2.5));
                var bytes = await _opgg.TryGetBytesAsync(
                    "/api/global/champions/" + Uri.EscapeDataString(mode) + "/versions",
                    timeout.Token).ConfigureAwait(false);
                var root = ParseObject(bytes);
                var values = EnumerateValues(ReadValue(root, "data"));
                var version = values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (!string.IsNullOrWhiteSpace(version))
                {
                    lock (_sync)
                    {
                        _versionCache[mode] = new VersionEntry { CachedUtc = DateTime.UtcNow, Version = version };
                    }
                }
                return version;
            }
        }

        private async Task<string> ResolveRankedPositionAsync(
            int championId,
            string version,
            bool force,
            CancellationToken cancellationToken)
        {
            var key = championId + "|" + (version ?? string.Empty);
            lock (_sync)
            {
                RankedPositionEntry cached;
                if (!force && _rankedPositionCache.TryGetValue(key, out cached) &&
                    DateTime.UtcNow - cached.CachedUtc < RankedPositionCacheDuration &&
                    !string.IsNullOrWhiteSpace(cached.Position))
                    return cached.Position;
            }

            string resolved = null;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    var bytes = await _opgg.TryGetBytesAsync(ChampionsPath("ranked", version), timeout.Token).ConfigureAwait(false);
                    resolved = ParsePrimaryRankedPosition(bytes, championId);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                }
            }

            if (string.IsNullOrWhiteSpace(resolved)) resolved = "top";
            lock (_sync)
            {
                _rankedPositionCache[key] = new RankedPositionEntry
                {
                    CachedUtc = DateTime.UtcNow,
                    Position = resolved
                };
            }
            return resolved;
        }

        private LeagueBuildAdvisorSnapshot CreateBaseSnapshot(LeagueLiveSnapshot live)
        {
            return new LeagueBuildAdvisorSnapshot
            {
                Connected = live != null && live.Connected,
                Phase = live == null ? null : live.Phase,
                Activity = live == null ? LeagueActivityLevel.None : live.Activity,
                BudgetName = live == null ? _budgets.Current.Name : live.BudgetName,
                QueueId = live == null ? 0 : live.QueueId,
                Source = "OP.GG Global",
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        private CacheEntry FindFreshBuild(int championId, string mode, string position, string version)
        {
            if (championId <= 0 || string.IsNullOrWhiteSpace(mode)) return null;
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(version))
                {
                    CacheEntry exact;
                    if (_buildCache.TryGetValue(BuildCacheKey(championId, mode, position, version), out exact) &&
                        DateTime.UtcNow - exact.CachedUtc < BuildCacheDuration)
                        return exact;
                    return null;
                }

                var positionPart = string.Equals(position, "all", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : (position ?? string.Empty) + "|";
                var prefix = championId + "|" + mode + "|" + positionPart;
                return _buildCache
                    .Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                   DateTime.UtcNow - pair.Value.CachedUtc < BuildCacheDuration)
                    .OrderByDescending(pair => pair.Value.CachedUtc)
                    .Select(pair => pair.Value)
                    .FirstOrDefault();
            }
        }

        private static string BuildCacheKey(int championId, string mode, string position, string version)
        {
            return championId + "|" + (mode ?? string.Empty) + "|" + (position ?? string.Empty) + "|" + (version ?? string.Empty);
        }

        private void AddPickRows(
            LeagueBuildRecommendation output,
            string category,
            object value,
            IDictionary<int, string> names,
            int limit)
        {
            if (output == null || limit <= 0) return;
            var added = 0;
            foreach (var row in EnumerateDictionaries(value))
            {
                var ids = ReadIntArray(ReadValue(row, "ids"));
                if (ids.Count == 0) continue;
                output.Rows.Add(new LeagueBuildAdvisorRow
                {
                    Category = category,
                    Recommendation = JoinNames(ids, names),
                    Evidence = BuildEvidence(row, null)
                });
                if (++added >= limit) break;
            }
        }

        private void AddRuneRows(
            LeagueBuildRecommendation output,
            object runesValue,
            object runePagesValue,
            IDictionary<int, string> names,
            int limit)
        {
            if (output == null || limit <= 0) return;
            var pages = EnumerateDictionaries(runesValue).ToList();
            if (pages.Count == 0) pages = EnumerateDictionaries(runePagesValue).ToList();

            var added = 0;
            foreach (var page in pages)
            {
                var builds = EnumerateDictionaries(ReadValue(page, "builds")).ToList();
                if (builds.Count == 0) builds.Add(page);
                foreach (var build in builds)
                {
                    var ids = new List<int>();
                    ids.AddRange(ReadIntArray(ReadValue(build, "primary_rune_ids")));
                    ids.AddRange(ReadIntArray(ReadValue(build, "secondary_rune_ids")));
                    if (ids.Count == 0) continue;
                    output.Rows.Add(new LeagueBuildAdvisorRow
                    {
                        Category = "runes",
                        Recommendation = JoinNames(ids, names),
                        Evidence = BuildEvidence(build, page)
                    });
                    if (++added >= limit) return;
                }
            }
        }

        private void AddSkillRows(LeagueBuildRecommendation output, object value, int limit)
        {
            if (output == null || limit <= 0) return;
            var added = 0;
            foreach (var row in EnumerateDictionaries(value))
            {
                var ids = EnumerateValues(ReadValue(row, "ids"))
                    .Select(item => Convert.ToString(item, CultureInfo.InvariantCulture))
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
                if (ids.Length == 0) continue;
                output.Rows.Add(new LeagueBuildAdvisorRow
                {
                    Category = "skills",
                    Recommendation = string.Join(" > ", ids),
                    Evidence = BuildEvidence(row, null)
                });
                if (++added >= limit) break;
            }
        }

        private void AddCounterRow(LeagueBuildRecommendation output, object value, IDictionary<int, string> championNames)
        {
            var rows = EnumerateDictionaries(value).Take(5).ToArray();
            if (rows.Length == 0) return;
            var labels = new List<string>();
            foreach (var row in rows)
            {
                var id = ReadInt(row, "champion_id");
                if (id <= 0) continue;
                labels.Add(ResolveName(championNames, id, "#" + id));
            }
            if (labels.Count == 0) return;
            output.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "counters",
                Recommendation = string.Join(" · ", labels),
                Evidence = rows.Sum(row => Math.Max(0, ReadInt(row, "play"))) + " games"
            });
        }

        private string BuildEvidence(Dictionary<string, object> row, Dictionary<string, object> fallback)
        {
            var pick = ReadDoubleNullable(row, "pick_rate") ?? ReadDoubleNullable(fallback, "pick_rate");
            var play = ReadInt(row, "play");
            if (play <= 0) play = ReadInt(fallback, "play");
            var winRate = ResolveWinRate(row, play);
            if (!winRate.HasValue && fallback != null) winRate = ResolveWinRate(fallback, play);

            var parts = new List<string>();
            if (pick.HasValue) parts.Add("pick " + FormatRate(pick));
            if (winRate.HasValue) parts.Add("win " + FormatRate(winRate));
            if (play > 0) parts.Add(play.ToString("N0", CultureInfo.InvariantCulture) + " games");
            return string.Join(" · ", parts);
        }

        private static double? ResolveWinRate(Dictionary<string, object> row, int play)
        {
            if (row == null) return null;
            var rate = ReadDoubleNullable(row, "win_rate");
            if (rate.HasValue) return rate;

            var rawWins = ReadValue(row, "win") ?? ReadValue(row, "wins");
            if (rawWins == null || play <= 0) return null;
            int wins;
            if (!int.TryParse(
                    Convert.ToString(rawWins, CultureInfo.InvariantCulture),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out wins) || wins < 0)
                return null;
            return (double)wins / play;
        }

        private void ParseIdNameArray(byte[] bytes, IDictionary<int, string> output)
        {
            if (bytes == null || bytes.Length == 0 || output == null) return;
            object decoded;
            try { decoded = _json.DeserializeObject(Encoding.UTF8.GetString(bytes)); }
            catch { return; }
            foreach (var row in EnumerateDictionaries(decoded))
            {
                var id = ReadInt(row, "id");
                var name = ReadString(row, "name");
                if (id > 0 && !string.IsNullOrWhiteSpace(name)) output[id] = name.Trim();
            }
        }

        private Dictionary<string, object> ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            return ReadValue(source, key) as Dictionary<string, object>;
        }

        private static object ReadValue(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static int ReadInt(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            int parsed;
            return value != null && int.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out parsed)
                ? parsed
                : 0;
        }

        private static double? ReadDoubleNullable(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            double parsed;
            return value != null && double.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out parsed)
                ? (double?)parsed
                : null;
        }

        private static List<int> ReadIntArray(object value)
        {
            var output = new List<int>();
            foreach (var item in EnumerateValues(value))
            {
                int parsed;
                if (item != null && int.TryParse(Convert.ToString(item, CultureInfo.InvariantCulture), out parsed)) output.Add(parsed);
            }
            return output;
        }

        private static IEnumerable<object> EnumerateValues(object value)
        {
            if (value == null) yield break;
            var array = value as object[];
            if (array != null)
            {
                foreach (var item in array) yield return item;
                yield break;
            }
            var list = value as ArrayList;
            if (list != null)
            {
                foreach (var item in list) yield return item;
                yield break;
            }
            var enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string))
            {
                foreach (var item in enumerable) yield return item;
            }
        }

        private static IEnumerable<Dictionary<string, object>> EnumerateDictionaries(object value)
        {
            var direct = value as Dictionary<string, object>;
            if (direct != null)
            {
                yield return direct;
                yield break;
            }
            foreach (var item in EnumerateValues(value))
            {
                var row = item as Dictionary<string, object>;
                if (row != null) yield return row;
            }
        }

        private static string MapOpggPositionName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            switch (value.Trim().ToUpperInvariant())
            {
                case "TOP": return "top";
                case "JUNGLE": return "jungle";
                case "MID":
                case "MIDDLE": return "mid";
                case "ADC":
                case "BOTTOM": return "adc";
                case "SUPPORT":
                case "UTILITY": return "support";
                default: return null;
            }
        }

        private static string JoinNames(IEnumerable<int> ids, IDictionary<int, string> names)
        {
            return string.Join(" · ", ids.Select(id => ResolveName(names, id, "#" + id)));
        }

        private static string ResolveName(IDictionary<int, string> names, int id, string fallback)
        {
            string name;
            return names != null && names.TryGetValue(id, out name) && !string.IsNullOrWhiteSpace(name) ? name : fallback;
        }

        private static string FormatRate(double? rate)
        {
            if (!rate.HasValue) return "--";
            var value = rate.Value;
            if (Math.Abs(value) <= 1.0) value *= 100.0;
            return value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static LeagueBuildRecommendation CloneRecommendation(LeagueBuildRecommendation source)
        {
            if (source == null) return null;
            var clone = new LeagueBuildRecommendation
            {
                Tier = source.Tier,
                Rank = source.Rank,
                WinRate = source.WinRate,
                PickRate = source.PickRate,
                BanRate = source.BanRate
            };
            foreach (var row in source.Rows)
            {
                clone.Rows.Add(new LeagueBuildAdvisorRow
                {
                    Category = row.Category,
                    Recommendation = row.Recommendation,
                    Evidence = row.Evidence
                });
            }
            return clone;
        }

        private static LeagueBuildAdvisorCatalog CloneCatalog(LeagueBuildAdvisorCatalog source)
        {
            if (source == null) return null;
            var clone = new LeagueBuildAdvisorCatalog();
            Copy(source.Champions, clone.Champions);
            Copy(source.Items, clone.Items);
            Copy(source.Spells, clone.Spells);
            Copy(source.Perks, clone.Perks);
            return clone;
        }

        private static void Copy(IDictionary<int, string> source, IDictionary<int, string> target)
        {
            if (source == null || target == null) return;
            foreach (var pair in source) target[pair.Key] = pair.Value;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _requestGate.Dispose();
            if (_ownsOpgg)
            {
                var disposable = _opgg as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LeagueBuildAdvisorDataService));
        }
    }
}
