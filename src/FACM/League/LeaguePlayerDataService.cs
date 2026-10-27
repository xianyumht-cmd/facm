using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeaguePlayerDataService
    {
        internal const int InitialMatchCount = 10;
        internal const int MaximumMatchCount = 20;
        internal const string ChampionSummaryPath = "/lol-game-data/assets/v1/champion-summary.json";
        private static readonly TimeSpan ChampionMetadataCacheDuration = TimeSpan.FromMinutes(30);
        internal const int MaximumRemoteMatchPageCount = 200;
        private readonly object _cacheSync = new object();
        private readonly ILeagueClientApi _client;
        private readonly ILeagueRiotAliasLookupApi _riotAliases;
        private readonly PerformanceBudgetProvider _budgets;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        private readonly ILeagueSgpMatchHistoryApi _sgpHistory;
        private readonly Func<LeagueClientSession> _session;
        private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
        private LeaguePlayerProfile _cachedProfile;
        private DateTime _profileCachedUtc = DateTime.MinValue;
        private LeaguePlayerMatchPage _cachedPage;
        private string _cachedPagePuuId;
        private LeagueClientSession _cachedPageSession;
        private LeagueClientSession _cachedProfileSession;
        private DateTime _pageCachedUtc = DateTime.MinValue;
        private Dictionary<int, string> _cachedChampionNames;
        private DateTime _championNamesCachedUtc = DateTime.MinValue;
        private LeagueBuildAdvisorCatalog _cachedMatchBuildCatalog;
        private bool _cachedMatchBuildAugmentsRequested;
        private DateTime _matchBuildCatalogCachedUtc = DateTime.MinValue;

        public LeaguePlayerDataService(ILeagueClientApi client, PerformanceBudgetProvider budgets,
            ILeagueRiotAliasLookupApi riotAliases = null, ILeagueSgpMatchHistoryApi sgpHistory = null,
            Func<LeagueClientSession> session = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
            _riotAliases = riotAliases;
            _sgpHistory = sgpHistory;
            _session = session;
        }

        internal static bool TryParseRiotId(string text, out string gameName, out string tagLine)
        {
            gameName = null;
            tagLine = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var separator = text.IndexOf('#');
            if (separator <= 0 || separator != text.LastIndexOf('#')) return false;
            var name = text.Substring(0, separator).Trim();
            var tag = text.Substring(separator + 1).Trim();
            if (name.Length == 0 || name.Length > 32 || tag.Length == 0 || tag.Length > 16 ||
                name.Any(char.IsControl) || tag.Any(char.IsControl)) return false;
            gameName = name;
            tagLine = tag;
            return true;
        }

        internal string ParseRiotAliasPuuid(byte[] bytes, string gameName, string tagLine)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 64 * 1024) return null;
            try
            {
                var root = _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as IEnumerable;
                if (root == null || root is string) return null;
                string found = null;
                var count = 0;
                foreach (var value in root)
                {
                    if (++count > 16) return null;
                    var row = value as Dictionary<string, object>;
                    var alias = ReadObject(row, "alias");
                    var name = ReadString(alias, "game_name");
                    var tag = ReadString(alias, "tag_line");
                    if (!string.Equals(name, gameName, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(tag, tagLine, StringComparison.OrdinalIgnoreCase)) continue;
                    var puuid = ReadString(row, "puuid");
                    if (!IsSafePuuid(puuid)) return null;
                    if (found != null && !string.Equals(found, puuid, StringComparison.Ordinal)) return null;
                    found = puuid;
                }
                return found;
            }
            catch (Exception) { return null; }
        }

        private static bool IsSafePuuid(string puuid)
        {
            return !string.IsNullOrEmpty(puuid) && puuid.Length <= 128 &&
                puuid.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '-' || c == '_');
        }

        internal async Task<LeagueRemotePlayerHistoryResult> LoadRemotePlayerHistoryAsync(
            LeaguePlayerProfile expected, string targetPuuid, CancellationToken cancellationToken,
            int startIndex = 0, int count = InitialMatchCount,
            LeaguePlayerHistorySource source = LeaguePlayerHistorySource.Automatic, string serverId = null)
        {
            if (expected == null || !IsSafePuuid(expected.PuuId) || !IsSafePuuid(targetPuuid) ||
                startIndex < 0 || count < 1 || count > MaximumRemoteMatchPageCount ||
                startIndex > int.MaxValue - count || !Enum.IsDefined(typeof(LeaguePlayerHistorySource), source) ||
                (source == LeaguePlayerHistorySource.Lcu && !string.IsNullOrEmpty(serverId)))
                return new LeagueRemotePlayerHistoryResult { Status = LeagueRiotPlayerSearchStatus.Unavailable };

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    await _requestGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                    try
                    {
                        var before = ParseProfile(await _client.TryGetBytesAsync(
                            LeagueDashboardDetailsService.SummonerPath, timeout.Token).ConfigureAwait(false));
                        if (before == null || string.IsNullOrEmpty(before.PuuId))
                            return new LeagueRemotePlayerHistoryResult { Status = LeagueRiotPlayerSearchStatus.Unavailable };
                        if (!string.Equals(before.PuuId, expected.PuuId, StringComparison.Ordinal))
                            return new LeagueRemotePlayerHistoryResult { Status = LeagueRiotPlayerSearchStatus.AccountChanged };

                        var profile = await LoadTargetProfileAsync(targetPuuid, source, serverId, timeout.Token)
                            .ConfigureAwait(false);
                        if (profile == null)
                            return new LeagueRemotePlayerHistoryResult { Status = LeagueRiotPlayerSearchStatus.NotFound };
                        var page = await ReadHistoryPageAsync(profile, startIndex, count, source, serverId, timeout.Token)
                            .ConfigureAwait(false);
                        var after = ParseProfile(await _client.TryGetBytesAsync(
                            LeagueDashboardDetailsService.SummonerPath, timeout.Token).ConfigureAwait(false));
                        if (after == null || !string.Equals(after.PuuId, expected.PuuId, StringComparison.Ordinal))
                            return new LeagueRemotePlayerHistoryResult { Status = LeagueRiotPlayerSearchStatus.AccountChanged };
                        if (page == null)
                            return new LeagueRemotePlayerHistoryResult { Status = LeagueRiotPlayerSearchStatus.Unavailable };
                        if (page.Matches.Count > count)
                            page.Matches.RemoveRange(count, page.Matches.Count - count);
                        return new LeagueRemotePlayerHistoryResult
                        {
                            Status = LeagueRiotPlayerSearchStatus.Found,
                            Profile = profile,
                            Page = page
                        };
                    }
                    finally { _requestGate.Release(); }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    return new LeagueRemotePlayerHistoryResult { Status = LeagueRiotPlayerSearchStatus.Unavailable };
                }
                catch (Exception)
                {
                    return new LeagueRemotePlayerHistoryResult { Status = LeagueRiotPlayerSearchStatus.Unavailable };
                }
            }
        }

        internal async Task<LeagueRiotPlayerSearchResult> SearchRiotIdAsync(
            LeaguePlayerProfile expected, string riotId, CancellationToken cancellationToken, bool allowRegionalLookup = false)
        {
            string gameName;
            string tagLine;
            if (!TryParseRiotId(riotId, out gameName, out tagLine))
                return new LeagueRiotPlayerSearchResult { Status = LeagueRiotPlayerSearchStatus.InvalidQuery };
            if (_riotAliases == null || expected == null || string.IsNullOrEmpty(expected.PuuId))
                return new LeagueRiotPlayerSearchResult { Status = LeagueRiotPlayerSearchStatus.Unavailable };

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    var before = ParseProfile(await _client.TryGetBytesAsync(
                        LeagueDashboardDetailsService.SummonerPath, timeout.Token).ConfigureAwait(false));
                    if (before == null || string.IsNullOrEmpty(before.PuuId))
                        return new LeagueRiotPlayerSearchResult { Status = LeagueRiotPlayerSearchStatus.Unavailable };
                    if (!string.Equals(before.PuuId, expected.PuuId, StringComparison.Ordinal))
                        return new LeagueRiotPlayerSearchResult { Status = LeagueRiotPlayerSearchStatus.AccountChanged };

                    string foundPuuid;
                    LeaguePlayerProfile resolved;
                    await _requestGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                    try
                    {
                        var aliases = await _riotAliases.TryLookupAsync(gameName, tagLine, timeout.Token)
                            .ConfigureAwait(false);
                        if (aliases == null)
                            return new LeagueRiotPlayerSearchResult { Status = LeagueRiotPlayerSearchStatus.Unavailable };
                        foundPuuid = ParseRiotAliasPuuid(aliases, gameName, tagLine);
                        if (foundPuuid == null)
                            return new LeagueRiotPlayerSearchResult { Status = LeagueRiotPlayerSearchStatus.NotFound };
                        var path = "/lol-summoner/v2/summoners/puuid/" + Uri.EscapeDataString(foundPuuid);
                        resolved = ParseProfile(await _client.TryGetBytesAsync(path, timeout.Token).ConfigureAwait(false));
                        if (!allowRegionalLookup && (resolved == null ||
                            !string.Equals(foundPuuid, resolved.PuuId, StringComparison.Ordinal)))
                            return new LeagueRiotPlayerSearchResult { Status = LeagueRiotPlayerSearchStatus.NotFound };
                    }
                    finally { _requestGate.Release(); }

                    var after = ParseProfile(await _client.TryGetBytesAsync(
                        LeagueDashboardDetailsService.SummonerPath, timeout.Token).ConfigureAwait(false));
                    if (after == null || !string.Equals(after.PuuId, expected.PuuId, StringComparison.Ordinal))
                        return new LeagueRiotPlayerSearchResult { Status = LeagueRiotPlayerSearchStatus.AccountChanged };
                    return new LeagueRiotPlayerSearchResult
                    {
                        Status = LeagueRiotPlayerSearchStatus.Found,
                        Player = new LeaguePlayerMatchIdentity
                        {
                            PuuId = foundPuuid,
                            PlayerName = SafeDisplayName(gameName + "#" + tagLine)
                        }
                    };
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    return new LeagueRiotPlayerSearchResult { Status = LeagueRiotPlayerSearchStatus.Unavailable };
                }
                catch (Exception) { return new LeagueRiotPlayerSearchResult { Status = LeagueRiotPlayerSearchStatus.Unavailable }; }
            }
        }

        public LeaguePlayerProfile TryGetCachedProfile()
        {
            var session = _session?.Invoke();
            lock (_cacheSync) return CacheSessionMatches(_cachedProfileSession, session) ? CloneProfile(_cachedProfile) : null;
        }

        public LeaguePlayerMatchPage TryGetCachedPage()
        {
            var session = _session?.Invoke();
            lock (_cacheSync) return CacheSessionMatches(_cachedPageSession, session) ? ClonePage(_cachedPage) : null;
        }

        internal Dictionary<int, string> TryGetCachedChampionNames()
        {
            lock (_cacheSync) return CloneChampionNames(_cachedChampionNames);
        }

        internal Task<Dictionary<int, string>> LoadChampionOptionsAsync(CancellationToken cancellationToken)
        {
            return LoadChampionNamesAsync(cancellationToken);
        }

        public async Task<LeaguePlayerProfile> LoadProfileAsync(bool force, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _session?.Invoke();
            if (!SessionStillCurrent(session)) return null;
            lock (_cacheSync)
            {
                if (!force && _cachedProfile != null && CacheSessionMatches(_cachedProfileSession, session) && DateTime.UtcNow - _profileCachedUtc < TimeSpan.FromSeconds(15))
                    return CloneProfile(_cachedProfile);
            }

            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var bytes = await _client.TryGetBytesAsync(LeagueDashboardDetailsService.SummonerPath, timeout.Token).ConfigureAwait(false);
                    var profile = ParseProfile(bytes);
                    if (!SessionStillCurrent(session)) return null;
                    if (profile == null || string.IsNullOrWhiteSpace(profile.PuuId)) return profile;
                    lock (_cacheSync)
                    {
                        if (_cachedProfile != null && !string.Equals(_cachedProfile.PuuId, profile.PuuId, StringComparison.OrdinalIgnoreCase))
                        {
                            _cachedPage = null;
                            _cachedPagePuuId = null;
                            _pageCachedUtc = DateTime.MinValue;
                        }
                        _cachedProfile = CloneProfile(profile);
                        _cachedProfileSession = session;
                        _profileCachedUtc = DateTime.UtcNow;
                    }
                    return profile;
                }
            }
            finally
            {
                _requestGate.Release();
            }
        }

        public async Task<LeaguePlayerMatchPage> LoadRecentMatchesAsync(
            LeaguePlayerProfile profile, int startIndex, int count, bool force, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (profile == null || !IsSafePuuid(profile.PuuId)) return null;
            startIndex = Math.Max(0, startIndex);
            count = Math.Max(1, Math.Min(MaximumMatchCount, count));
            if (startIndex > int.MaxValue - count) return null;
            var session = _session?.Invoke();
            if (!SessionStillCurrent(session)) return null;
            lock (_cacheSync)
            {
                if (!force && _cachedPage != null && CacheSessionMatches(_cachedPageSession, session) &&
                    string.Equals(_cachedPagePuuId, profile.PuuId, StringComparison.OrdinalIgnoreCase) &&
                    _cachedPage.StartIndex == startIndex && _cachedPage.RequestedCount == count &&
                    DateTime.UtcNow - _pageCachedUtc < TimeSpan.FromSeconds(45)) return ClonePage(_cachedPage);
            }
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LeaguePlayerMatchPage page = null;
                using (var localDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    localDeadline.CancelAfter(_budgets.Current.NetworkConcurrency <= 1
                        ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(4));
                    try
                    {
                        var path = "/lol-match-history/v1/products/lol/" + Uri.EscapeDataString(profile.PuuId) +
                            "/matches?begIndex=" + startIndex + "&endIndex=" + (startIndex + count - 1);
                        page = ParseMatchPage(await _client.TryGetBytesAsync(path, localDeadline.Token)
                            .ConfigureAwait(false), profile, startIndex, count);
                    }
                    catch (OperationCanceledException) { cancellationToken.ThrowIfCancellationRequested(); }
                }
                if (!SessionStillCurrent(session)) return null;
                if (page == null && _sgpHistory != null)
                {
                    using (var regionalDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        regionalDeadline.CancelAfter(TimeSpan.FromSeconds(8));
                        try
                        {
                            var before = ParseProfile(await _client.TryGetBytesAsync(
                                LeagueDashboardDetailsService.SummonerPath, regionalDeadline.Token).ConfigureAwait(false));
                            if (before == null || !string.Equals(before.PuuId, profile.PuuId, StringComparison.Ordinal)) return null;
                            page = await TryLoadSgpPageAsync(profile, startIndex, count, regionalDeadline.Token).ConfigureAwait(false);
                            var after = ParseProfile(await _client.TryGetBytesAsync(
                                LeagueDashboardDetailsService.SummonerPath, regionalDeadline.Token).ConfigureAwait(false));
                            if (after == null || !string.Equals(after.PuuId, profile.PuuId, StringComparison.Ordinal)) return null;
                        }
                        catch (OperationCanceledException) { cancellationToken.ThrowIfCancellationRequested(); return null; }
                    }
                }
                if (!SessionStillCurrent(session)) return null;
                if (page != null)
                {
                    page.ClientSession = session;
                    CachePage(profile.PuuId, page, session);
                }
                return page;
            }
            finally { _requestGate.Release(); }
        }

        internal Task<LeagueMatchCollectionResult> CollectMatchesAsync(
            LeaguePlayerProfile profile, LeagueMatchCollectionSettings settings,
            LeagueSimpleMatchHistoryFilter simpleFilter, LeagueAdvancedMatchHistoryFilter advancedFilter,
            IProgress<LeagueMatchCollectionProgress> progress, CancellationToken cancellationToken)
        {
            return CollectMatchesAsync(profile, profile, settings, simpleFilter, advancedFilter,
                progress, cancellationToken);
        }

        internal async Task<LeagueMatchCollectionResult> CollectMatchesAsync(
            LeaguePlayerProfile expectedSelf, LeaguePlayerProfile target,
            LeagueMatchCollectionSettings settings,
            LeagueSimpleMatchHistoryFilter simpleFilter, LeagueAdvancedMatchHistoryFilter advancedFilter,
            IProgress<LeagueMatchCollectionProgress> progress, CancellationToken cancellationToken,
            LeaguePlayerHistorySource source = LeaguePlayerHistorySource.Automatic, string serverId = null)
        {
            var session = _session?.Invoke();
            var result = new LeagueMatchCollectionResult();
            if (expectedSelf == null || !IsSafePuuid(expectedSelf.PuuId) ||
                target == null || !IsSafePuuid(target.PuuId) || settings == null || !settings.IsValid)
            {
                result.Status = LeagueMatchCollectionStatus.Unavailable;
                return result;
            }

            var seen = new HashSet<long>();
            var nowUtc = DateTime.UtcNow;
            var filter = simpleFilter ?? new LeagueSimpleMatchHistoryFilter();
            for (var iteration = 0; iteration < settings.MaxIterations &&
                result.Page.Matches.Count < settings.ExpectedCount; iteration++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Status = LeagueMatchCollectionStatus.Stopped;
                    return result;
                }

                // Keep every read behind the Player service's existing LCU request gate.
                // Collection never updates the regular ten/twenty-match page cache.
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(12));
                    try
                    {
                        await _requestGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                        try
                        {
                            var before = ParseProfile(await _client.TryGetBytesAsync(
                                LeagueDashboardDetailsService.SummonerPath, timeout.Token).ConfigureAwait(false));
                            if (!SessionStillCurrent(session) || before == null ||
                                !string.Equals(before.PuuId, expectedSelf.PuuId, StringComparison.Ordinal))
                            {
                                result.Status = LeagueMatchCollectionStatus.AccountChanged;
                                result.Page.Matches.Clear();
                                return result;
                            }

                            var startIndex = iteration * settings.CountPerIteration;
                            var page = await ReadHistoryPageAsync(target, startIndex, settings.CountPerIteration,
                                source, serverId, timeout.Token).ConfigureAwait(false);
                            if (page == null)
                            {
                                result.Status = LeagueMatchCollectionStatus.Unavailable;
                                return result;
                            }

                            var after = ParseProfile(await _client.TryGetBytesAsync(
                                LeagueDashboardDetailsService.SummonerPath, timeout.Token).ConfigureAwait(false));
                            if (!SessionStillCurrent(session) || after == null ||
                                !string.Equals(after.PuuId, expectedSelf.PuuId, StringComparison.Ordinal))
                            {
                                result.Status = LeagueMatchCollectionStatus.AccountChanged;
                                result.Page.Matches.Clear();
                                return result;
                            }

                            result.Iterations++;
                            result.ScannedCount += page.Matches.Count;
                            foreach (var match in page.Matches.Take(settings.CountPerIteration))
                            {
                                if (match == null || match.GameId <= 0 || !seen.Add(match.GameId)) continue;
                                if (!filter.Matches(match, nowUtc) ||
                                    (advancedFilter != null && !advancedFilter.Matches(match))) continue;
                                result.Page.Matches.Add(match);
                                if (result.Page.Matches.Count == settings.ExpectedCount) break;
                            }
                            if (progress != null)
                                progress.Report(new LeagueMatchCollectionProgress
                                {
                                    Iterations = result.Iterations,
                                    ScannedCount = result.ScannedCount,
                                    CollectedCount = result.Page.Matches.Count
                                });
                            if (page.Matches.Count == 0) break;
                        }
                        finally { _requestGate.Release(); }
                    }
                    catch (OperationCanceledException)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                            AppLog.Info("League Player collection page deadline exceeded.");
                        result.Status = cancellationToken.IsCancellationRequested
                            ? LeagueMatchCollectionStatus.Stopped : LeagueMatchCollectionStatus.Unavailable;
                        return result;
                    }
                    catch (Exception exception)
                    {
                        AppLog.Info("League Player collection page unavailable: " + exception.GetType().Name);
                        result.Status = LeagueMatchCollectionStatus.Unavailable;
                        return result;
                    }
                }
            }
            result.Status = cancellationToken.IsCancellationRequested
                ? LeagueMatchCollectionStatus.Stopped : LeagueMatchCollectionStatus.Completed;
            return result;
        }

        public async Task<LeaguePlayerMatchPage> EnrichIncompleteMatchesAsync(
            LeaguePlayerProfile profile,
            LeaguePlayerMatchPage page,
            CancellationToken cancellationToken)
        {
            var session = page?.ClientSession;
            if (!SessionStillCurrent(session)) return null;
            if (profile == null || string.IsNullOrWhiteSpace(profile.PuuId) || page == null || page.Matches.Count == 0)
                return page;

            var budget = _budgets.Current;
            if (!budget.AllowBackgroundPrefetch || budget.MatchHistoryPrefetchCount <= 0)
                return page;

            var result = ClonePage(page);
            var limit = Math.Min(result.Matches.Count, Math.Min(MaximumMatchCount, budget.MatchHistoryPrefetchCount));
            var changed = false;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                for (var index = 0; index < limit; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var current = result.Matches[index];
                    if (current == null || (current.ParticipantResolved && current.StatisticsAvailable) || current.GameId <= 0) continue;

                    Dictionary<string, object> fullGame;
                    try
                    {
                        await _requestGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                        try
                        {
                            fullGame = await LoadGameAsync(profile, current.GameId, timeout.Token,
                                current.SgpServerId, current.DataSource == LeaguePlayerHistorySource.Sgp)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            _requestGate.Release();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested) throw;
                        break;
                    }

                    var enriched = ParseMatch(fullGame, profile);
                    if (enriched == null || enriched.GameId != current.GameId ||
                        !enriched.ParticipantResolved || !enriched.StatisticsAvailable) continue;
                    result.Matches[index] = enriched;
                    changed = true;
                }
            }

            if (!SessionStillCurrent(session)) return null;
            if (changed) CachePage(profile.PuuId, result, session);
            return result;
        }

        internal async Task<LeaguePlayerMatchSummary> LoadMatchDetailAsync(
            LeaguePlayerProfile profile, long gameId, CancellationToken cancellationToken,
            string serverId = null, bool sgpOnly = false, bool allowSgpFallback = true)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.PuuId) || gameId <= 0) return null;
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var game = await LoadGameAsync(profile, gameId, cancellationToken, serverId, sgpOnly, allowSgpFallback).ConfigureAwait(false);
                var match = ParseMatch(game, profile);
                return match != null && match.GameId == gameId && match.ParticipantResolved ? match : null;
            }
            finally { _requestGate.Release(); }
        }

        internal async Task<LeaguePlayerMatchBreakdown> LoadMatchBreakdownAsync(
            LeaguePlayerProfile profile, long gameId, CancellationToken cancellationToken,
            string serverId = null, bool sgpOnly = false, bool allowSgpFallback = true)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.PuuId) || gameId <= 0) return null;
            LeaguePlayerMatchBreakdown breakdown;
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var game = await LoadGameAsync(profile, gameId, cancellationToken, serverId, sgpOnly, allowSgpFallback).ConfigureAwait(false);
                var match = ParseMatch(game, profile);
                if (match == null || match.GameId != gameId || !match.ParticipantResolved || !match.StatisticsAvailable)
                    return null;
                var participants = ParseParticipants(game, match.ParticipantId);
                if (participants == null || !participants.Any(p => p.IsTarget)) return null;
                breakdown = new LeaguePlayerMatchBreakdown
                {
                    Match = match, Participants = participants, Teams = ParseTeamScores(game, participants)
                };
            }
            finally { _requestGate.Release(); }

            var names = await LoadChampionNamesAsync(cancellationToken).ConfigureAwait(false);
            if (names != null)
            {
                breakdown.ChampionNames = names;
                foreach (var participant in breakdown.Participants)
                {
                    string name;
                    if (names.TryGetValue(participant.ChampionId, out name)) participant.ChampionName = name;
                    if (participant.IsTarget) breakdown.Match.ChampionName = participant.ChampionName;
                }
            }
            var needsAugments = breakdown.Participants.Any(p => p.AugmentIds != null &&
                p.AugmentIds.Any(id => id > 0));
            breakdown.BuildCatalog = await LoadMatchBuildCatalogAsync(needsAugments, cancellationToken)
                .ConfigureAwait(false);
            return breakdown;
        }

        internal async Task<LeagueMatchTimeline> LoadMatchTimelineAsync(string selfPuuid,
            LeaguePlayerMatchSummary selected, CancellationToken token, LeagueClientSession expectedSession = null)
        {
            if (!IsSafePuuid(selfPuuid) || selected == null || selected.GameId <= 0) return null;
            var gameId = selected.GameId;
            var regional = selected.DataSource == LeaguePlayerHistorySource.Sgp;
            var serverId = selected.SgpServerId;
            if (regional && (_sgpHistory == null || string.IsNullOrEmpty(serverId))) return null;
            if (expectedSession != null && (_session == null ||
                !LeagueEncounterReplayService.SessionMatches(expectedSession, _session()))) return null;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    await _requestGate.WaitAsync(deadline.Token).ConfigureAwait(false);
                    try
                    {
                        var before = ParseProfile(await _client.TryGetBytesAsync(
                            LeagueDashboardDetailsService.SummonerPath, deadline.Token).ConfigureAwait(false));
                        if (before == null || !string.Equals(before.PuuId, selfPuuid, StringComparison.Ordinal)) return null;
                        var bytes = regional ? await _sgpHistory.TryGetGameDetailsAsync(gameId, deadline.Token, serverId)
                            .ConfigureAwait(false) : await _client.TryGetBytesAsync(
                                "/lol-match-history/v1/game-timelines/" + gameId, deadline.Token).ConfigureAwait(false);
                        var after = ParseProfile(await _client.TryGetBytesAsync(
                            LeagueDashboardDetailsService.SummonerPath, deadline.Token).ConfigureAwait(false));
                        if (after == null || !string.Equals(after.PuuId, selfPuuid, StringComparison.Ordinal) ||
                            (expectedSession != null && !LeagueEncounterReplayService.SessionMatches(expectedSession, _session())))
                            return null;
                        return LeagueMatchTimelineParser.Parse(bytes, gameId, regional);
                    }
                    finally { _requestGate.Release(); }
                }
                catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return null; }
                catch (System.Net.Http.HttpRequestException) { return null; }
            }
        }

        private async Task<LeagueBuildAdvisorCatalog> LoadMatchBuildCatalogAsync(
            bool includeAugments, CancellationToken cancellationToken)
        {
            lock (_cacheSync)
            {
                if (_cachedMatchBuildCatalog != null &&
                    DateTime.UtcNow - _matchBuildCatalogCachedUtc < ChampionMetadataCacheDuration &&
                    (!includeAugments || _cachedMatchBuildAugmentsRequested))
                    return _cachedMatchBuildCatalog;
            }
            if (!_budgets.Current.AllowMaintenanceWork)
            {
                lock (_cacheSync) return _cachedMatchBuildCatalog;
            }

            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_cacheSync)
                {
                    var recent = _cachedMatchBuildCatalog != null &&
                        DateTime.UtcNow - _matchBuildCatalogCachedUtc < ChampionMetadataCacheDuration;
                    if (recent && (!includeAugments || _cachedMatchBuildAugmentsRequested))
                        return _cachedMatchBuildCatalog;
                }
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    LeagueBuildAdvisorCatalog catalog;
                    bool recent;
                    lock (_cacheSync)
                    {
                        recent = _cachedMatchBuildCatalog != null &&
                            DateTime.UtcNow - _matchBuildCatalogCachedUtc < ChampionMetadataCacheDuration;
                        catalog = recent ? LeagueBuildAdvisorDataService.CloneLocalCatalog(_cachedMatchBuildCatalog) : null;
                    }
                    if (catalog == null)
                    {
                        var items = await _client.TryGetBytesAsync(LeagueBuildAdvisorDataService.ItemsPath, timeout.Token)
                            .ConfigureAwait(false);
                        var spells = await _client.TryGetBytesAsync(LeagueBuildAdvisorDataService.SummonerSpellsPath, timeout.Token)
                            .ConfigureAwait(false);
                        var perks = await _client.TryGetBytesAsync(LeagueBuildAdvisorDataService.PerksPath, timeout.Token)
                            .ConfigureAwait(false);
                        catalog = LeagueBuildAdvisorDataService.ParseLocalCatalog(null, items, spells, perks);
                        if (catalog.Items.Count == 0 && catalog.Spells.Count == 0 && catalog.Perks.Count == 0)
                            return null;
                        // Match cards render native primary/secondary tree names from the same LCU metadata as Akari.
                        var styles = await _client.TryGetBytesAsync(LeagueBuildAdvisorDataService.PerkStylesPath, timeout.Token)
                            .ConfigureAwait(false);
                        LeagueBuildAdvisorDataService.ParseLocalPerkStyles(styles, catalog);
                    }
                    var augmentRequested = includeAugments || (recent && _cachedMatchBuildAugmentsRequested);
                    if (includeAugments && !(recent && _cachedMatchBuildAugmentsRequested))
                    {
                        var augments = await _client.TryGetBytesAsync(
                            LeagueBuildAdvisorDataService.CherryAugmentsPath, timeout.Token).ConfigureAwait(false);
                        LeagueBuildAdvisorDataService.ParseLocalAugments(augments, catalog);
                    }
                    lock (_cacheSync)
                    {
                        _cachedMatchBuildCatalog = catalog;
                        _cachedMatchBuildAugmentsRequested = augmentRequested;
                        _matchBuildCatalogCachedUtc = DateTime.UtcNow;
                    }
                    return catalog;
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                lock (_cacheSync) return _cachedMatchBuildCatalog;
            }
            catch (Exception)
            {
                lock (_cacheSync) return _cachedMatchBuildCatalog;
            }
            finally { _requestGate.Release(); }
        }

        private static List<LeaguePlayerMatchParticipant> ParseParticipants(Dictionary<string, object> game, int targetId)
        {
            var identities = new Dictionary<int, Dictionary<string, object>>();
            foreach (var identity in ReadObjects(game, "participantIdentities"))
            {
                var id = ReadInt(identity, "participantId");
                if (id <= 0 || identities.ContainsKey(id)) return null;
                identities.Add(id, ReadObject(identity, "player"));
            }
            var result = new List<LeaguePlayerMatchParticipant>();
            var seen = new HashSet<int>();
            var seenPuuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var participant in ReadObjects(game, "participants"))
            {
                var id = ReadInt(participant, "participantId");
                var stats = ReadObject(participant, "stats");
                if (id <= 0 || !seen.Add(id) || result.Count >= 20 || stats == null ||
                    !stats.ContainsKey("kills") || !stats.ContainsKey("deaths") ||
                    !stats.ContainsKey("assists") || !stats.ContainsKey("win")) return null;
                Dictionary<string, object> identity;
                identities.TryGetValue(id, out identity);
                var puuid = ReadString(identity, "puuid");
                if (!string.IsNullOrWhiteSpace(puuid) && !seenPuuids.Add(puuid)) return null;
                var name = ReadString(identity, "gameName");
                var tag = ReadString(identity, "tagLine");
                if (string.IsNullOrWhiteSpace(name)) name = ReadString(identity, "summonerName");
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(tag)) name += "#" + tag;
                result.Add(new LeaguePlayerMatchParticipant
                {
                    ParticipantId = id,
                    TeamId = ReadInt(participant, "teamId"),
                    PlayerSubteamId = LeagueCherryTeams.ValidSubteamId(ReadOptionalCount(stats, "playerSubteamId")),
                    SubteamPlacement = LeagueCherryTeams.ValidPlacement(ReadOptionalCount(stats, "subteamPlacement")),
                    PuuId = puuid,
                    PlayerName = SafeDisplayName(name),
                    ChampionId = ReadInt(participant, "championId"),
                    ItemIds = Enumerable.Range(0, 7).Select(slot => ReadInt(stats, "item" + slot)).ToArray(),
                    RoleBoundItemId = ReadOptionalNonNegative(stats, "roleBoundItem"),
                    SpellIds = new[] { ReadInt(participant, "spell1Id"), ReadInt(participant, "spell2Id") },
                    PerkIds = Enumerable.Range(0, 6).Select(slot => ReadInt(stats, "perk" + slot)).ToArray(),
                    PrimaryPerkStyleId = ReadInt(stats, "perkPrimaryStyle"),
                    SecondaryPerkStyleId = ReadInt(stats, "perkSubStyle"),
                    PerkEndOfGameValues = ReadPerkEndOfGameValues(stats),
                    StatPerkIds = ReadStatPerkIds(stats),
                    AugmentIds = Enumerable.Range(1, 6).Select(slot => ReadInt(stats, "playerAugment" + slot)).ToArray(),
                    Kills = ReadInt(stats, "kills"), Deaths = ReadInt(stats, "deaths"),
                    Assists = ReadInt(stats, "assists"),
                    CreepScore = (int)Math.Min(int.MaxValue, Math.Max(0L,
                        (long)ReadInt(stats, "totalMinionsKilled") + ReadInt(stats, "neutralMinionsKilled"))),
                    DamageToChampions = ReadInt(stats, "totalDamageDealtToChampions"),
                    DamageAvailable = ReadOptionalCount(stats, "totalDamageDealtToChampions").HasValue,
                    GoldEarned = ReadInt(stats, "goldEarned"),
                    GoldAvailable = ReadOptionalCount(stats, "goldEarned").HasValue,
                    VisionScore = ReadInt(stats, "visionScore"),
                    Win = ReadBool(stats, "win"), IsTarget = id == targetId
                });
            }
            return result.Count == 0 ? null : result.OrderBy(p => p.TeamId)
                .ThenBy(p => p.ParticipantId).ToList();
        }

        private static double?[][] ReadPerkEndOfGameValues(Dictionary<string, object> stats)
        {
            var slots = new double?[6][];
            var hasEvidence = false;
            for (var slot = 0; slot < slots.Length; slot++)
            {
                slots[slot] = new[] {
                    ReadOptionalFiniteNumber(stats, "perk" + slot + "Var1"),
                    ReadOptionalFiniteNumber(stats, "perk" + slot + "Var2"),
                    ReadOptionalFiniteNumber(stats, "perk" + slot + "Var3")
                };
                if (slots[slot].Any(value => value.HasValue)) hasEvidence = true;
            }
            return hasEvidence ? slots : null;
        }

        private static int?[] ReadStatPerkIds(Dictionary<string, object> stats)
        {
            var values = new[] { ReadOptionalCount(stats, "perkStatOffense"),
                ReadOptionalCount(stats, "perkStatFlex"), ReadOptionalCount(stats, "perkStatDefense") };
            return values.Any(value => value.HasValue) ? values : null;
        }

        private static double? ReadOptionalFiniteNumber(Dictionary<string, object> stats, string key)
        {
            object raw;
            double value;
            return stats != null && stats.TryGetValue(key, out raw) && raw != null &&
                double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !double.IsNaN(value) && !double.IsInfinity(value)
                ? (double?)value : null;
        }

        private static List<LeagueMatchTeamScore> ParseTeamScores(Dictionary<string, object> game,
            List<LeaguePlayerMatchParticipant> participants)
        {
            var cherry = LeagueCherryTeams.IsCherry(ReadString(game, "gameMode"));
            var metadata = new Dictionary<int, Dictionary<string, object>>();
            var ambiguous = new HashSet<int>();
            foreach (var team in ReadObjects(game, "teams"))
            {
                var id = ReadInt(team, "teamId");
                if (id <= 0) continue;
                if (metadata.ContainsKey(id)) { ambiguous.Add(id); metadata.Remove(id); }
                else if (!ambiguous.Contains(id)) metadata[id] = team;
            }
            var scores = new List<LeagueMatchTeamScore>();
            var groups = participants.Where(p => LeagueCherryTeams.GroupId(p, cherry) > 0)
                .GroupBy(p => LeagueCherryTeams.GroupId(p, cherry));
            foreach (var group in groups.OrderBy(g => cherry ? LeagueCherryTeams.Placement(g) ?? int.MaxValue : 0)
                .ThenBy(g => g.Key))
            {
                var players = group.ToList();
                var consistentWin = players.All(p => p.Win == players[0].Win);
                Dictionary<string, object> team;
                team = null;
                if (!cherry) metadata.TryGetValue(group.Key, out team);
                scores.Add(new LeagueMatchTeamScore
                {
                    TeamId = group.Key, IsCherry = cherry,
                    SubteamPlacement = cherry ? LeagueCherryTeams.Placement(group) : null,
                    PlayerCount = players.Count,
                    Win = consistentWin ? (bool?)players[0].Win : null,
                    Kills = players.Sum(p => (long)Math.Max(0, p.Kills)),
                    Deaths = players.Sum(p => (long)Math.Max(0, p.Deaths)),
                    Assists = players.Sum(p => (long)Math.Max(0, p.Assists)),
                    DamageToChampions = players.All(p => p.DamageAvailable)
                        ? (long?)players.Sum(p => (long)Math.Max(0, p.DamageToChampions)) : null,
                    GoldEarned = players.All(p => p.GoldAvailable)
                        ? (long?)players.Sum(p => (long)Math.Max(0, p.GoldEarned)) : null,
                    BaronKills = ReadOptionalCount(team, "baronKills"),
                    DragonKills = ReadOptionalCount(team, "dragonKills"),
                    TowerKills = ReadOptionalCount(team, "towerKills"),
                    RiftHeraldKills = ReadOptionalCount(team, "riftHeraldKills"),
                    InhibitorKills = ReadOptionalCount(team, "inhibitorKills"),
                    HordeKills = ReadOptionalCount(team, "hordeKills"),
                    FirstBaron = ReadOptionalBool(team, "firstBaron"),
                    FirstDragon = ReadOptionalBool(team, "firstDargon") ?? ReadOptionalBool(team, "firstDragon"),
                    FirstTower = ReadOptionalBool(team, "firstTower"),
                    FirstInhibitor = ReadOptionalBool(team, "firstInhibitor"),
                    Bans = ParseTeamBans(team)
                });
            }
            return scores;
        }

        private static int? ReadOptionalCount(Dictionary<string, object> source, string key)
        {
            object value;
            int parsed;
            return source != null && source.TryGetValue(key, out value) && value != null &&
                int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) && parsed >= 0
                ? (int?)parsed : null;
        }

        private static bool? ReadOptionalBool(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) return null;
            if (value is bool) return (bool)value;
            bool result;
            var text = value as string;
            return text != null && bool.TryParse(text, out result) ? (bool?)result : null;
        }

        private static List<LeagueMatchTeamBan> ParseTeamBans(Dictionary<string, object> team)
        {
            object value;
            if (team == null || !team.TryGetValue("bans", out value) ||
                value == null || value is string || !(value is IEnumerable)) return null;
            var result = new List<LeagueMatchTeamBan>();
            var scanned = 0;
            foreach (var entry in (IEnumerable)value)
            {
                if (++scanned > 10) return null;
                var ban = entry as Dictionary<string, object>;
                object rawChampionId;
                int championId;
                if (ban == null || !ban.TryGetValue("championId", out rawChampionId) ||
                    rawChampionId == null || !int.TryParse(
                        Convert.ToString(rawChampionId, CultureInfo.InvariantCulture), out championId)) return null;
                if (championId <= 0) continue;
                var pickTurn = ReadOptionalCount(ban, "pickTurn");
                result.Add(new LeagueMatchTeamBan { ChampionId = championId,
                    PickTurn = pickTurn.HasValue && pickTurn.Value > 0 ? pickTurn : null });
            }
            return result;
        }

        private static string SafeDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return new string(name.Where(c => !char.IsControl(c)).Take(80).ToArray()).Trim();
        }

        public async Task<LeaguePlayerMatchPage> EnrichChampionNamesAsync(
            LeaguePlayerProfile profile,
            LeaguePlayerMatchPage page,
            CancellationToken cancellationToken)
        {
            var session = page?.ClientSession;
            if (!SessionStillCurrent(session)) return null;
            if (profile == null || string.IsNullOrWhiteSpace(profile.PuuId) || page == null || page.Matches.Count == 0)
                return page;

            var names = await LoadChampionNamesAsync(cancellationToken).ConfigureAwait(false);
            var result = ClonePage(page);
            var changed = false;
            if (names != null && names.Count > 0)
            {
                foreach (var match in result.Matches)
                {
                    if (match == null || match.ChampionId <= 0) continue;
                    string name;
                    if (!names.TryGetValue(match.ChampionId, out name) || string.IsNullOrWhiteSpace(name)) continue;
                    if (!string.Equals(match.ChampionName, name, StringComparison.Ordinal))
                    {
                        match.ChampionName = name;
                        changed = true;
                    }
                }
            }

            if (!SessionStillCurrent(session)) return null;
            if (changed) CachePage(profile.PuuId, result, session);
            return result;
        }

        private async Task<Dictionary<int, string>> LoadChampionNamesAsync(CancellationToken cancellationToken)
        {
            Dictionary<int, string> names;
            bool cacheFresh;
            lock (_cacheSync)
            {
                names = CloneChampionNames(_cachedChampionNames);
                cacheFresh = _cachedChampionNames != null && _cachedChampionNames.Count > 0 &&
                    DateTime.UtcNow - _championNamesCachedUtc < ChampionMetadataCacheDuration;
            }

            // Champion names improve readability but are not game-critical. Queueing, Champ Select,
            // In Game and hidden/background budgets all disable maintenance work, so those phases
            // may use an existing cache but never start this extra LCU request.
            if (!cacheFresh && _budgets.Current.AllowMaintenanceWork)
            {
                try
                {
                    await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        // Re-check after taking the gate so two overlapping page operations do not
                        // both fetch the same global champion summary.
                        lock (_cacheSync)
                        {
                            cacheFresh = _cachedChampionNames != null && _cachedChampionNames.Count > 0 &&
                                DateTime.UtcNow - _championNamesCachedUtc < ChampionMetadataCacheDuration;
                            if (cacheFresh) names = CloneChampionNames(_cachedChampionNames);
                        }

                        if (!cacheFresh)
                        {
                            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                            {
                                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                                var bytes = await _client.TryGetBytesAsync(ChampionSummaryPath, timeout.Token).ConfigureAwait(false);
                                var parsed = ParseChampionSummary(bytes);
                                if (parsed.Count > 0)
                                {
                                    lock (_cacheSync)
                                    {
                                        _cachedChampionNames = CloneChampionNames(parsed);
                                        _championNamesCachedUtc = DateTime.UtcNow;
                                        names = CloneChampionNames(_cachedChampionNames);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        _requestGate.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    // A metadata timeout is non-fatal; stale cache / champion ID remains usable.
                }
                catch (Exception exception)
                {
                    Services.AppLog.Info("League Player champion metadata skipped: " + exception.Message);
                }
            }

            return names;
        }

        internal LeaguePlayerProfile ParseProfile(byte[] bytes)
        {
            var root = ParseObject(bytes);
            if (root == null) return null;
            return new LeaguePlayerProfile
            {
                PuuId = ReadString(root, "puuid"),
                SummonerId = ReadLong(root, "summonerId"),
                AccountId = ReadLong(root, "accountId"),
                GameName = ReadString(root, "gameName"),
                TagLine = ReadString(root, "tagLine"),
                DisplayName = ReadString(root, "displayName"),
                SummonerLevel = ReadInt(root, "summonerLevel"),
                ProfileIconId = ReadInt(root, "profileIconId")
            };
        }

        internal LeaguePlayerMatchPage ParseMatchPage(byte[] bytes, LeaguePlayerProfile profile, int startIndex, int count)
        {
            var root = ParseObject(bytes);
            if (root == null) return null;
            var gamesRoot = ReadObject(root, "games");
            if (gamesRoot == null) return null;

            var page = new LeaguePlayerMatchPage
            {
                StartIndex = Math.Max(0, startIndex),
                RequestedCount = Math.Max(1, count),
                ReportedGameCount = ReadInt(gamesRoot, "gameCount")
            };

            foreach (var item in ReadObjects(gamesRoot, "games"))
            {
                var summary = ParseMatch(item, profile);
                if (summary != null) page.Matches.Add(summary);
            }
            return page;
        }

        private async Task<LeaguePlayerMatchPage> ReadHistoryPageAsync(LeaguePlayerProfile target,
            int startIndex, int count, LeaguePlayerHistorySource source, string serverId, CancellationToken token)
        {
            var session = _session?.Invoke();
            if (!SessionStillCurrent(session)) return null;
            if (!Enum.IsDefined(typeof(LeaguePlayerHistorySource), source)) return null;
            if (source != LeaguePlayerHistorySource.Sgp && string.IsNullOrEmpty(serverId))
            {
                var path = "/lol-match-history/v1/products/lol/" + Uri.EscapeDataString(target.PuuId) +
                    "/matches?begIndex=" + startIndex + "&endIndex=" + (startIndex + count - 1);
                var page = ParseMatchPage(await _client.TryGetBytesAsync(path, token).ConfigureAwait(false),
                    target, startIndex, count);
                if (page != null)
                {
                    page.ClientSession = session;
                    page = await HydrateSparsePageAsync(target, page, token, source).ConfigureAwait(false);
                    return SessionStillCurrent(session) ? page : null;
                }
                if (source == LeaguePlayerHistorySource.Lcu) return null;
            }
            if (source == LeaguePlayerHistorySource.Lcu) return null;
            var regional = await TryLoadSgpPageAsync(target, startIndex, count, token, serverId).ConfigureAwait(false);
            if (regional != null) regional.ClientSession = session;
            regional = await HydrateSparsePageAsync(target, regional, token, source).ConfigureAwait(false);
            return SessionStillCurrent(session) ? regional : null;
        }

        private async Task<LeaguePlayerMatchPage> HydrateSparsePageAsync(LeaguePlayerProfile target,
            LeaguePlayerMatchPage page, CancellationToken token, LeaguePlayerHistorySource source)
        {
            if (page == null) return null;
            foreach (var match in page.Matches) if (match != null) match.PreferredSource = source;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    for (var index = 0; index < page.Matches.Count; index++)
                    {
                        var current = page.Matches[index];
                        if (current == null || current.GameId <= 0 ||
                            (current.ParticipantResolved && current.StatisticsAvailable)) continue;
                        var game = await LoadGameAsync(target, current.GameId, deadline.Token,
                            current.SgpServerId, current.DataSource == LeaguePlayerHistorySource.Sgp,
                            source != LeaguePlayerHistorySource.Lcu).ConfigureAwait(false);
                        var hydrated = ParseMatch(game, target);
                        if (hydrated != null && hydrated.GameId == current.GameId &&
                            hydrated.ParticipantResolved && hydrated.StatisticsAvailable)
                        {
                            hydrated.PreferredSource = source;
                            page.Matches[index] = hydrated;
                        }
                    }
                }
                catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); }
            }
            return page;
        }

        private async Task<LeaguePlayerProfile> LoadTargetProfileAsync(string targetPuuid,
            LeaguePlayerHistorySource source, string serverId, CancellationToken token)
        {
            LeaguePlayerProfile profile = null;
            if (source != LeaguePlayerHistorySource.Sgp && string.IsNullOrEmpty(serverId))
            {
                profile = ParseProfile(await _client.TryGetBytesAsync(
                    "/lol-summoner/v2/summoners/puuid/" + Uri.EscapeDataString(targetPuuid), token).ConfigureAwait(false));
                if (profile != null && string.Equals(profile.PuuId, targetPuuid, StringComparison.Ordinal)) return profile;
            }
            if (_sgpHistory == null || source == LeaguePlayerHistorySource.Lcu) return null;
            return ParseSgpSummoner(await _sgpHistory.TryGetSummonerAsync(targetPuuid, token, serverId)
                .ConfigureAwait(false), targetPuuid);
        }

        internal LeaguePlayerProfile ParseSgpSummoner(byte[] bytes, string targetPuuid)
        {
            if (bytes == null || bytes.Length == 0 || !IsSafePuuid(targetPuuid)) return null;
            try
            {
                var values = _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as object[];
                if (values == null || values.Length != 1) return null;
                var value = values[0] as Dictionary<string, object>;
                if (!string.Equals(ReadString(value, "puuid"), targetPuuid, StringComparison.Ordinal)) return null;
                return new LeaguePlayerProfile { PuuId = targetPuuid,
                    SummonerId = ReadLong(value, "id"), AccountId = ReadLong(value, "accountId"),
                    DisplayName = SafeDisplayName(ReadString(value, "name")),
                    SummonerLevel = ReadInt(value, "level"), ProfileIconId = ReadInt(value, "profileIconId") };
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private async Task<LeaguePlayerMatchPage> TryLoadSgpPageAsync(
            LeaguePlayerProfile target, int startIndex, int count, CancellationToken token, string serverId = null)
        {
            if (_sgpHistory == null || target == null || !IsSafePuuid(target.PuuId)) return null;
            var bytes = await _sgpHistory.TryGetSummaryAsync(target.PuuId, startIndex, count, token, serverId)
                .ConfigureAwait(false);
            return ParseSgpMatchPage(bytes, target, startIndex, count, serverId);
        }

        internal LeaguePlayerMatchPage ParseSgpMatchPage(byte[] bytes,
            LeaguePlayerProfile target, int startIndex, int count, string serverId = null)
        {
            if (target == null || !IsSafePuuid(target.PuuId) || startIndex < 0 ||
                count < 1 || count > MaximumRemoteMatchPageCount) return null;
            var root = ParseObject(bytes);
            if (root == null || !root.ContainsKey("games") || root["games"] == null ||
                root["games"] is string || root["games"] is IDictionary ||
                !(root["games"] is IEnumerable)) return null;
            var page = new LeaguePlayerMatchPage
            {
                StartIndex = startIndex,
                RequestedCount = count
            };
            var seenGames = new HashSet<long>();
            foreach (var item in ReadObjects(root, "games").Take(count))
            {
                var normalized = NormalizeSgpGame(item);
                if (normalized == null) continue;
                MarkSgpSource(normalized, serverId);
                var summary = ParseMatch(normalized, target);
                if (summary != null && summary.ParticipantResolved && summary.GameId > 0 &&
                    seenGames.Add(summary.GameId))
                    page.Matches.Add(summary);
            }
            return page;
        }

        internal static Dictionary<string, object> NormalizeSgpGame(Dictionary<string, object> item)
        {
            var source = ReadObject(item, "json");
            if (source == null || ReadLong(source, "gameId") <= 0) return null;
            var participants = ReadObjects(source, "participants").Take(21).ToList();
            if (participants.Count == 0 || participants.Count > 20) return null;
            var normalized = new Dictionary<string, object>(source);
            var rows = new List<Dictionary<string, object>>();
            var identities = new List<Dictionary<string, object>>();
            var ids = new HashSet<int>();
            var puuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var participant in participants)
            {
                var id = ReadInt(participant, "participantId");
                var puuid = ReadString(participant, "puuid");
                if (id <= 0 || !ids.Add(id) || (!string.IsNullOrEmpty(puuid) && !puuids.Add(puuid))) return null;
                var row = new Dictionary<string, object>(participant);
                var stats = new Dictionary<string, object>(participant);
                var perks = ReadObject(participant, "perks");
                var statPerks = ReadObject(perks, "statPerks");
                if (statPerks != null)
                {
                    foreach (var key in new[] { "offense", "flex", "defense" })
                    {
                        object statPerk;
                        if (statPerks.TryGetValue(key, out statPerk))
                            stats["perkStat" + char.ToUpperInvariant(key[0]) + key.Substring(1)] = statPerk;
                    }
                }
                var styles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var style in ReadObjects(perks, "styles").Take(2))
                {
                    var description = ReadString(style, "description");
                    if (description == null || !styles.Add(description)) continue;
                    var primary = string.Equals(description, "primaryStyle", StringComparison.OrdinalIgnoreCase);
                    var secondary = string.Equals(description, "subStyle", StringComparison.OrdinalIgnoreCase);
                    if (!primary && !secondary) continue;
                    object styleId;
                    if (style.TryGetValue("style", out styleId))
                        stats[primary ? "perkPrimaryStyle" : "perkSubStyle"] = styleId;
                    var slot = primary ? 0 : 4;
                    foreach (var selection in ReadObjects(style, "selections").Take(primary ? 4 : 2))
                    {
                        object perk;
                        if (selection.TryGetValue("perk", out perk)) stats["perk" + slot] = perk;
                        foreach (var variable in new[] { "var1", "var2", "var3" })
                        {
                            object value;
                            if (selection.TryGetValue(variable, out value))
                                stats["perk" + slot + "Var" + variable[3]] = value;
                        }
                        slot++;
                    }
                }
                row["stats"] = stats;
                rows.Add(row);
                var player = new Dictionary<string, object>
                {
                    { "puuid", puuid },
                    { "gameName", ReadString(participant, "riotIdGameName") },
                    { "tagLine", ReadString(participant, "riotIdTagline") },
                    { "summonerName", ReadString(participant, "summonerName") },
                    { "summonerId", ReadLong(participant, "summonerId") }
                };
                identities.Add(new Dictionary<string, object> { { "participantId", id }, { "player", player } });
            }
            normalized["participants"] = rows;
            normalized["participantIdentities"] = identities;
            var teams = new List<Dictionary<string, object>>();
            foreach (var sourceTeam in ReadObjects(source, "teams").Take(20))
            {
                var team = new Dictionary<string, object>(sourceTeam);
                var objectives = ReadObject(sourceTeam, "objectives");
                foreach (var pair in new[] { new[] { "baron", "Baron" }, new[] { "dragon", "Dragon" },
                    new[] { "tower", "Tower" }, new[] { "inhibitor", "Inhibitor" },
                    new[] { "riftHerald", "RiftHerald" }, new[] { "horde", "Horde" } })
                {
                    var objective = ReadObject(objectives, pair[0]);
                    object value;
                    if (objective != null && objective.TryGetValue("kills", out value)) team[pair[0] + "Kills"] = value;
                    if (objective != null && objective.TryGetValue("first", out value)) team["first" + pair[1]] = value;
                }
                teams.Add(team);
            }
            normalized["teams"] = teams;
            return normalized;
        }

        private async Task<Dictionary<string, object>> LoadGameAsync(LeaguePlayerProfile profile,
            long gameId, CancellationToken token, string serverId = null, bool sgpOnly = false, bool allowSgpFallback = true)
        {
            var game = !sgpOnly && string.IsNullOrEmpty(serverId) ? ParseObject(await _client.TryGetBytesAsync(
                "/lol-match-history/v1/games/" + gameId, token).ConfigureAwait(false)) : null;
            var match = ParseMatch(game, profile);
            if (match != null && match.GameId == gameId && match.ParticipantResolved && match.StatisticsAvailable)
                return game;
            if (_sgpHistory == null || !allowSgpFallback) return null;
            var before = ParseProfile(await _client.TryGetBytesAsync(
                LeagueDashboardDetailsService.SummonerPath, token).ConfigureAwait(false));
            if (before == null || !IsSafePuuid(before.PuuId)) return null;
            game = NormalizeSgpGame(ParseObject(await _sgpHistory.TryGetGameSummaryAsync(
                gameId, token, serverId).ConfigureAwait(false)));
            if (game != null) MarkSgpSource(game, serverId);
            var after = ParseProfile(await _client.TryGetBytesAsync(
                LeagueDashboardDetailsService.SummonerPath, token).ConfigureAwait(false));
            if (after == null || !string.Equals(before.PuuId, after.PuuId, StringComparison.Ordinal) ||
                ReadLong(game, "gameId") != gameId) return null;
            return game;
        }

        private void MarkSgpSource(Dictionary<string, object> game, string serverId)
        {
            game["_sgp"] = true;
            game["_sgpServerId"] = serverId ?? LeagueSgpMatchHistoryApiClient.CurrentServerId(_session?.Invoke());
        }

        internal Dictionary<int, string> ParseChampionSummary(byte[] bytes)
        {
            var result = new Dictionary<int, string>();
            if (bytes == null || bytes.Length == 0) return result;
            try
            {
                var root = _json.DeserializeObject(Encoding.UTF8.GetString(bytes));
                var rows = root as IEnumerable;
                if (rows == null || root is string) return result;
                foreach (var item in rows)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    var id = ReadInt(row, "id");
                    var name = ReadString(row, "name");
                    if (id > 0 && !string.IsNullOrWhiteSpace(name)) result[id] = name.Trim();
                }
            }
            catch
            {
                // Metadata is optional. Invalid/changed shape falls back to numeric champion IDs.
            }
            return result;
        }

        internal List<LeaguePlayerChampionStat> BuildChampionStats(LeaguePlayerMatchPage page)
        {
            var grouped = new Dictionary<int, LeaguePlayerChampionStat>();
            if (page != null)
            {
                foreach (var match in page.Matches)
                {
                    if (match == null || !match.ParticipantResolved || match.ChampionId <= 0) continue;
                    LeaguePlayerChampionStat stat;
                    if (!grouped.TryGetValue(match.ChampionId, out stat))
                    {
                        stat = new LeaguePlayerChampionStat
                        {
                            ChampionId = match.ChampionId,
                            ChampionName = match.ChampionName
                        };
                        grouped.Add(match.ChampionId, stat);
                    }
                    else if (string.IsNullOrWhiteSpace(stat.ChampionName) && !string.IsNullOrWhiteSpace(match.ChampionName))
                    {
                        stat.ChampionName = match.ChampionName;
                    }

                    stat.Games++;
                    if (match.Win) stat.Wins++;
                    stat.Kills += match.Kills;
                    stat.Deaths += match.Deaths;
                    stat.Assists += match.Assists;
                }
            }

            var result = new List<LeaguePlayerChampionStat>(grouped.Values);
            result.Sort(delegate(LeaguePlayerChampionStat left, LeaguePlayerChampionStat right)
            {
                var byGames = right.Games.CompareTo(left.Games);
                if (byGames != 0) return byGames;
                var byWins = right.Wins.CompareTo(left.Wins);
                return byWins != 0 ? byWins : left.ChampionId.CompareTo(right.ChampionId);
            });
            return result;
        }

        private LeaguePlayerMatchSummary ParseMatch(Dictionary<string, object> game, LeaguePlayerProfile profile)
        {
            if (game == null) return null;
            var participantId = ResolveParticipantId(game, profile);
            Dictionary<string, object> participant = null;
            foreach (var item in ReadObjects(game, "participants"))
            {
                if (participantId > 0 && ReadInt(item, "participantId") == participantId)
                {
                    participant = item;
                    break;
                }
            }

            var match = ParseParticipantMatch(game, participant, participantId);
            match.PuuId = participant == null ? null : profile.PuuId;
            match.SameTeamPuuids = SameTeamPuuids(game, participant, participantId, profile);
            match.MatchChampionIds = MatchChampionIds(game);
            match.MatchPlayers = MatchPlayerIdentities(game);
            match.ParticipantRoster = participant == null ? null : BuildParticipantRoster(game);
            match.RosterIdentitiesComplete = match.ParticipantRoster != null &&
                match.ParticipantRoster.All(member => !string.IsNullOrEmpty(member.PuuId));
            return match;
        }

        private static LeaguePlayerMatchSummary ParseParticipantMatch(Dictionary<string, object> game,
            Dictionary<string, object> participant, int participantId, bool calculateBaselines = true)
        {
            var stats = participant == null ? null : ReadObject(participant, "stats");
            return new LeaguePlayerMatchSummary
            {
                DataSource = ReadBool(game, "_sgp") ? LeaguePlayerHistorySource.Sgp : LeaguePlayerHistorySource.Lcu,
                SgpServerId = ReadString(game, "_sgpServerId"),
                GameId = ReadLong(game, "gameId"),
                GameCreationLocal = ResolveCreationLocal(game),
                GameDurationSeconds = ReadInt(game, "gameDuration"),
                GameMode = ReadString(game, "gameMode"),
                GameType = ReadString(game, "gameType"),
                GameCreationEpochMs = ReadLong(game, "gameCreation"),
                QueueId = ReadInt(game, "queueId"),
                ChampionId = participant == null ? 0 : ReadInt(participant, "championId"),
                MapId = ReadInt(game, "mapId"),
                TeamId = participant == null ? 0 : ReadInt(participant, "teamId"),
                PlayerSubteamId = LeagueCherryTeams.ValidSubteamId(ReadOptionalCount(stats, "playerSubteamId")),
                SubteamPlacement = LeagueCherryTeams.ValidPlacement(ReadOptionalCount(stats, "subteamPlacement")),
                ParticipantId = participantId > 0 && participant != null ? participantId : 0,
                HasSmite = participant != null && (ReadInt(participant, "spell1Id") == 11 || ReadInt(participant, "spell2Id") == 11),
                Kills = stats == null ? 0 : ReadInt(stats, "kills"),
                Deaths = stats == null ? 0 : ReadInt(stats, "deaths"),
                Assists = stats == null ? 0 : ReadInt(stats, "assists"),
                CreepScore = stats == null ? 0 : ReadInt(stats, "totalMinionsKilled") + ReadInt(stats, "neutralMinionsKilled"),
                Win = stats != null && ReadBool(stats, "win"),
                WinResult = ResolveWinResult(game, participant, stats),
                IsSurrender = ResolveSurrender(game, participant, stats),
                StatisticsAvailable = stats != null && stats.ContainsKey("win") && stats.ContainsKey("kills") && stats.ContainsKey("deaths") && stats.ContainsKey("assists"),
                Position = MatchPosition(participant),
                SoloKills = Metric(ReadObject(stats, "challenges"), "soloKills"),
                VisionScore = Metric(stats, "visionScore"),
                ChampionDamageShare = TeamShare(game, participant, "totalDamageDealtToChampions"),
                DamageTakenShare = TeamShare(game, participant, "totalDamageTaken"),
                GoldShare = TeamShare(game, participant, "goldEarned"),
                KillParticipation = TeamShare(game, participant, "kills", true),
                DamageGoldEfficiency = Ratio(Metric(stats, "totalDamageDealtToChampions"), Metric(stats, "goldEarned")),
                Level = Metric(stats, "champLevel"),
                GoldEarned = Metric(stats, "goldEarned"),
                GoldSpent = Metric(stats, "goldSpent"),
                DamageDealtToChampions = Metric(stats, "totalDamageDealtToChampions"),
                PhysicalDamageToChampions = Metric(stats, "physicalDamageDealtToChampions"),
                MagicDamageToChampions = Metric(stats, "magicDamageDealtToChampions"),
                TrueDamageToChampions = Metric(stats, "trueDamageDealtToChampions"),
                DamageTaken = Metric(stats, "totalDamageTaken"),
                PhysicalDamageTaken = Metric(stats, "physicalDamageTaken"),
                MagicDamageTaken = Metric(stats, "magicDamageTaken"),
                TrueDamageTaken = Metric(stats, "trueDamageTaken"),
                DamageToTowers = Metric(stats, "damageDealtToTurrets"),
                TotalHeal = Metric(stats, "totalHeal"),
                TimeCCingOthers = Metric(stats, "timeCCingOthers"),
                DoubleKills = Metric(stats, "doubleKills"),
                TripleKills = Metric(stats, "tripleKills"),
                QuadraKills = Metric(stats, "quadraKills"),
                PentaKills = Metric(stats, "pentaKills"),
                MetricBaselines = calculateBaselines ? BuildMetricBaselines(game, participant) : null,
                ItemIds = ReadOptionalSlots(stats, "item", 0, 7),
                RoleBoundItemId = ReadOptionalNonNegative(stats, "roleBoundItem"),
                SpellIds = ReadOptionalSlots(participant, "spell", 1, 2, "Id"),
                PerkIds = ReadOptionalSlots(stats, "perk", 0, 6),
                PerkStyleIds = ReadOptionalNamedSlots(stats, "perkPrimaryStyle", "perkSubStyle"),
                AugmentIds = ReadOptionalSlots(stats, "playerAugment", 1, 6),
                ParticipantResolved = participant != null
            };
        }

        private static string ResolveWinResult(Dictionary<string, object> game,
            Dictionary<string, object> participant, Dictionary<string, object> stats)
        {
            var end = ReadString(game, "endOfGameResult");
            if (!string.IsNullOrEmpty(end) && end.StartsWith("Abort_", StringComparison.Ordinal))
                return "abort";
            if (ReadParticipantBool(participant, stats, "gameEndedInEarlySurrender") == true)
                return "remake";
            if (ReadParticipantBool(participant, stats, "teamEarlySurrendered") == true)
                return "loss";
            var won = ReadParticipantBool(participant, stats, "win");
            return won.HasValue ? (won.Value ? "win" : "loss") : null;
        }

        private static bool? ResolveSurrender(Dictionary<string, object> game,
            Dictionary<string, object> participant, Dictionary<string, object> stats)
        {
            var end = ReadString(game, "endOfGameResult");
            if (!string.IsNullOrEmpty(end) && end.StartsWith("Abort_", StringComparison.Ordinal))
                return false;
            if (ReadParticipantBool(participant, stats, "gameEndedInEarlySurrender") == true ||
                ReadParticipantBool(participant, stats, "teamEarlySurrendered") == true)
                return true;
            if (ReadParticipantBool(participant, stats, "win") == true) return false;
            return ReadParticipantBool(participant, stats, "gameEndedInSurrender");
        }

        private static bool? ReadParticipantBool(Dictionary<string, object> participant,
            Dictionary<string, object> stats, string key)
        {
            object value;
            bool result;
            if (participant != null && participant.TryGetValue(key, out value))
                return value != null && bool.TryParse(Convert.ToString(value), out result) ? (bool?)result : null;
            if (stats != null && stats.TryGetValue(key, out value))
                return value != null && bool.TryParse(Convert.ToString(value), out result) ? (bool?)result : null;
            return null;
        }

        private static LeaguePlayerMatchSummary[] BuildParticipantRoster(Dictionary<string, object> game)
        {
            var participants = ReadObjects(game, "participants").Take(21).ToList();
            if (participants.Count == 0 || participants.Count > 20) return null;
            var ids = participants.Select(p => ReadInt(p, "participantId")).ToArray();
            if (ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length) return null;
            var idSet = new HashSet<int>(ids);
            var seen = new HashSet<int>();
            var puuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var identities = new Dictionary<int, string>();
            var identityRows = ReadObjects(game, "participantIdentities").Take(21).ToList();
            if (identityRows.Count > 20) return null;
            foreach (var identity in identityRows)
            {
                var id = ReadInt(identity, "participantId");
                if (id <= 0 || !seen.Add(id)) return null;
                if (!idSet.Contains(id)) continue;
                var puuid = ReadString(ReadObject(identity, "player"), "puuid");
                if (string.IsNullOrWhiteSpace(puuid) || puuid.Length > 128 ||
                    puuid.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_')) continue;
                if (!puuids.Add(puuid)) return null;
                identities[id] = puuid;
            }
            var roster = new LeaguePlayerMatchSummary[participants.Count];
            for (var index = 0; index < participants.Count; index++)
            {
                var id = ids[index];
                var member = ParseParticipantMatch(game, participants[index], id, false);
                string puuid;
                member.PuuId = identities.TryGetValue(id, out puuid) ? puuid : null;
                roster[index] = member;
            }
            AssignRosterMetricBaselines(participants, roster);
            return roster;
        }

        private static void AssignRosterMetricBaselines(List<Dictionary<string, object>> participants,
            LeaguePlayerMatchSummary[] roster)
        {
            foreach (var key in RelativeMetricKeys)
            {
                var values = participants.Select(participant => ParticipantMetric(participant, key)).ToArray();
                var gameSum = 0d;
                var gameMax = 0d;
                var teamSums = new Dictionary<int, double>();
                var teamMaxima = new Dictionary<int, double>();
                for (var index = 0; index < values.Length; index++)
                {
                    if (!values[index].HasValue) continue;
                    var value = values[index].Value;
                    gameSum += value;
                    gameMax = Math.Max(gameMax, value);
                    var teamId = roster[index].TeamId;
                    if (teamId <= 0) continue;
                    double sum;
                    teamSums.TryGetValue(teamId, out sum);
                    teamSums[teamId] = sum + value;
                    double maximum;
                    teamMaxima.TryGetValue(teamId, out maximum);
                    teamMaxima[teamId] = Math.Max(maximum, value);
                }
                if (double.IsInfinity(gameSum) || double.IsNaN(gameSum)) continue;
                for (var index = 0; index < values.Length; index++)
                {
                    if (!values[index].HasValue) continue;
                    var member = roster[index];
                    double teamSum = 0;
                    double teamMax = 0;
                    var hasTeam = member.TeamId > 0 &&
                        teamSums.TryGetValue(member.TeamId, out teamSum) &&
                        teamMaxima.TryGetValue(member.TeamId, out teamMax);
                    if (hasTeam && (double.IsInfinity(teamSum) || double.IsNaN(teamSum))) continue;
                    if (member.MetricBaselines == null)
                        member.MetricBaselines = new Dictionary<string, double[]>(StringComparer.Ordinal);
                    member.MetricBaselines[key] = new[]
                    {
                        hasTeam ? teamSum : double.NaN, hasTeam ? teamMax : double.NaN,
                        gameSum, gameMax
                    };
                }
            }
        }

        private static int? ReadOptionalNonNegative(Dictionary<string, object> source, string key)
        {
            object raw;
            long value;
            return source != null && source.TryGetValue(key, out raw) && raw != null &&
                long.TryParse(Convert.ToString(raw), out value) && value >= 0 && value <= int.MaxValue ?
                (int?)value : null;
        }

        private static int[] ReadOptionalSlots(Dictionary<string, object> source, string prefix,
            int first, int count, string suffix = "")
        {
            var result = new int[count];
            var known = false;
            for (var index = 0; index < count; index++)
            {
                var value = ReadOptionalNonNegative(source, prefix + (first + index) + suffix);
                result[index] = value ?? -1;
                if (value.HasValue) known = true;
            }
            return known ? result : null;
        }

        private static int[] ReadOptionalNamedSlots(Dictionary<string, object> source,
            string first, string second)
        {
            var primary = ReadOptionalNonNegative(source, first);
            var secondary = ReadOptionalNonNegative(source, second);
            return primary.HasValue || secondary.HasValue ? new[] { primary ?? -1, secondary ?? -1 } : null;
        }

        private static int[] MatchChampionIds(Dictionary<string, object> game)
        {
            var ids = ReadObjects(game, "participants").Select(p => ReadInt(p, "championId"))
                .Where(id => id > 0).Distinct().Take(21).ToArray();
            return ids.Length > 0 && ids.Length <= 20 ? ids : null;
        }

        private static LeaguePlayerMatchIdentity[] MatchPlayerIdentities(Dictionary<string, object> game)
        {
            var participantIds = ReadObjects(game, "participants").Select(p => ReadInt(p, "participantId")).ToArray();
            if (participantIds.Length == 0 || participantIds.Length > 20 ||
                participantIds.Any(id => id <= 0) || participantIds.Distinct().Count() != participantIds.Length)
                return null;

            var participants = new HashSet<int>(participantIds);
            var seenIds = new HashSet<int>();
            var seenPuuids = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<LeaguePlayerMatchIdentity>();
            var identities = ReadObjects(game, "participantIdentities").ToList();
            if (identities.Count > 20) return null;
            foreach (var identity in identities)
            {
                var id = ReadInt(identity, "participantId");
                if (id <= 0 || !seenIds.Add(id)) return null;
                if (!participants.Contains(id)) continue;
                var player = ReadObject(identity, "player");
                var puuid = ReadString(player, "puuid");
                if (string.IsNullOrWhiteSpace(puuid) || puuid.Length > 128 ||
                    puuid.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_')) continue;
                if (!seenPuuids.Add(puuid)) return null;
                var name = ReadString(player, "gameName");
                var tag = ReadString(player, "tagLine");
                if (string.IsNullOrWhiteSpace(name)) name = ReadString(player, "summonerName");
                else if (!string.IsNullOrWhiteSpace(tag)) name += "#" + tag;
                result.Add(new LeaguePlayerMatchIdentity { PuuId = puuid, PlayerName = SafeDisplayName(name) });
            }
            return result.Count == 0 ? null : result.ToArray();
        }

        private static string[] SameTeamPuuids(Dictionary<string, object> game, Dictionary<string, object> participant,
            int participantId, LeaguePlayerProfile profile)
        {
            if (participant == null || participantId <= 0 || profile == null || string.IsNullOrEmpty(profile.PuuId)) return null;
            var teamId = ReadInt(participant, "teamId");
            if (teamId <= 0) return null;
            var teamMembers = ReadObjects(game, "participants").Where(row => ReadInt(row, "teamId") == teamId)
                .Select(row => ReadInt(row, "participantId")).Where(id => id > 0).Distinct().Take(11).ToArray();
            if (teamMembers.Length < 2 || teamMembers.Length > 10 || !teamMembers.Contains(participantId)) return null;
            var identities = new Dictionary<int, string>();
            foreach (var identity in ReadObjects(game, "participantIdentities"))
            {
                var id = ReadInt(identity, "participantId");
                var puuid = ReadString(ReadObject(identity, "player"), "puuid");
                if (!teamMembers.Contains(id)) continue;
                if (string.IsNullOrEmpty(puuid) || puuid.Length > 128 ||
                    puuid.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_') || identities.ContainsKey(id)) return null;
                identities[id] = puuid;
            }
            string own;
            if (!identities.TryGetValue(participantId, out own) || !string.Equals(own, profile.PuuId, StringComparison.OrdinalIgnoreCase)) return null;
            var members = teamMembers.Where(identities.ContainsKey).Select(id => identities[id])
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            return members.Length >= 2 ? members : null;
        }

        private static double? Metric(Dictionary<string, object> source, string key)
        {
            object value;
            double number;
            return source != null && source.TryGetValue(key, out value) && value != null &&
                double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
                !double.IsNaN(number) && !double.IsInfinity(number) && number >= 0 ? (double?)number : null;
        }

        private static readonly string[] RelativeMetricKeys =
        {
            "kills", "deaths", "assists", "cs", "champLevel", "goldEarned", "goldSpent",
            "totalDamageDealtToChampions", "physicalDamageDealtToChampions",
            "magicDamageDealtToChampions", "trueDamageDealtToChampions",
            "totalDamageTaken", "physicalDamageTaken", "magicDamageTaken", "trueDamageTaken",
            "damageDealtToTurrets", "totalHeal", "visionScore", "timeCCingOthers", "soloKills",
            "doubleKills", "tripleKills", "quadraKills", "pentaKills"
        };

        private static double? ParticipantMetric(Dictionary<string, object> participant, string key)
        {
            var stats = ReadObject(participant, "stats");
            if (key == "soloKills") return Metric(ReadObject(stats, "challenges"), key);
            if (key != "cs") return Metric(stats, key);
            var lane = Metric(stats, "totalMinionsKilled");
            var jungle = Metric(stats, "neutralMinionsKilled");
            return lane.HasValue && jungle.HasValue ? (double?)(lane.Value + jungle.Value) : null;
        }

        private static Dictionary<string, double[]> BuildMetricBaselines(
            Dictionary<string, object> game, Dictionary<string, object> self)
        {
            if (self == null) return null;
            var participants = ReadObjects(game, "participants").Take(33).ToList();
            if (participants.Count == 0 || participants.Count > 32) return null;
            var teamId = ReadInt(self, "teamId");
            var baselines = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var key in RelativeMetricKeys)
            {
                if (!ParticipantMetric(self, key).HasValue) continue;
                var teamSum = 0d;
                var teamMax = 0d;
                var gameSum = 0d;
                var gameMax = 0d;
                var hasTeam = false;
                foreach (var member in participants)
                {
                    var value = ParticipantMetric(member, key);
                    if (!value.HasValue) continue;
                    gameSum += value.Value;
                    gameMax = Math.Max(gameMax, value.Value);
                    if (teamId <= 0 || ReadInt(member, "teamId") != teamId) continue;
                    hasTeam = true;
                    teamSum += value.Value;
                    teamMax = Math.Max(teamMax, value.Value);
                }
                if (double.IsNaN(gameSum) || double.IsInfinity(gameSum) ||
                    double.IsNaN(teamSum) || double.IsInfinity(teamSum)) continue;
                baselines[key] = new[]
                {
                    hasTeam ? teamSum : double.NaN,
                    hasTeam ? teamMax : double.NaN,
                    gameSum, gameMax
                };
            }
            return baselines.Count > 0 ? baselines : null;
        }

        private static double? Ratio(double? numerator, double? denominator)
        {
            return numerator.HasValue && denominator.HasValue ? numerator.Value / Math.Max(1, denominator.Value) : (double?)null;
        }

        private static double? TeamShare(Dictionary<string, object> game, Dictionary<string, object> participant, string key, bool participation = false)
        {
            if (participant == null || !participant.ContainsKey("teamId")) return null;
            var members = ReadObjects(game, "participants").Where(p => ReadInt(p, "teamId") == ReadInt(participant, "teamId")).ToList();
            var values = members.Select(p => Metric(ReadObject(p, "stats"), key)).ToList();
            if (values.Count == 0 || values.Any(v => !v.HasValue)) return null;
            var stats = ReadObject(participant, "stats");
            var value = Metric(stats, key);
            if (participation) value += Metric(stats, "assists");
            return Ratio(value, values.Sum(v => v.Value));
        }

        private static string MatchPosition(Dictionary<string, object> participant)
        {
            var stats = ReadObject(participant, "stats");
            var explicitPosition = ReadString(stats, "teamPosition");
            if (!string.IsNullOrEmpty(explicitPosition)) return explicitPosition;
            var timeline = ReadObject(participant, "timeline");
            var lane = ReadString(timeline, "lane");
            if (lane == "BOTTOM" && ReadString(timeline, "role") == "DUO_SUPPORT") return "UTILITY";
            return lane;
        }

        private bool CacheSessionMatches(LeagueClientSession cached, LeagueClientSession current)
        {
            return _session == null || LeagueEncounterReplayService.SessionMatches(cached, current);
        }

        private bool SessionStillCurrent(LeagueClientSession expected)
        {
            return _session == null || LeagueEncounterReplayService.SessionMatches(expected, _session());
        }

        private void CachePage(string puuId, LeaguePlayerMatchPage page, LeagueClientSession session)
        {
            if (string.IsNullOrWhiteSpace(puuId) || page == null || !SessionStillCurrent(session)) return;
            lock (_cacheSync)
            {
                _cachedPage = ClonePage(page);
                _cachedPageSession = session;
                _cachedPagePuuId = puuId;
                _pageCachedUtc = DateTime.UtcNow;
            }
        }

        private static int ResolveParticipantId(Dictionary<string, object> game, LeaguePlayerProfile profile)
        {
            if (game == null || profile == null) return 0;
            foreach (var identity in ReadObjects(game, "participantIdentities"))
            {
                var player = ReadObject(identity, "player");
                if (player == null) continue;
                var puuid = ReadString(player, "puuid");
                if (!string.IsNullOrWhiteSpace(profile.PuuId) && string.Equals(puuid, profile.PuuId, StringComparison.OrdinalIgnoreCase))
                    return ReadInt(identity, "participantId");
                var summonerId = ReadLong(player, "summonerId");
                if (profile.SummonerId > 0 && summonerId == profile.SummonerId)
                    return ReadInt(identity, "participantId");
            }
            return 0;
        }

        private static DateTime ResolveCreationLocal(Dictionary<string, object> game)
        {
            var milliseconds = ReadLong(game, "gameCreation");
            if (milliseconds > 0)
            {
                try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(milliseconds).ToLocalTime(); }
                catch { }
            }
            DateTime parsed;
            var text = ReadString(game, "gameCreationDate");
            return DateTime.TryParse(text, out parsed) ? parsed.ToLocalTime() : DateTime.MinValue;
        }

        private Dictionary<string, object> ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static Dictionary<string, object> ReadObject(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static IEnumerable<Dictionary<string, object>> ReadObjects(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null) yield break;
            var enumerable = value as IEnumerable;
            if (enumerable == null || value is string) yield break;
            foreach (var item in enumerable)
            {
                var dictionary = item as Dictionary<string, object>;
                if (dictionary != null) yield return dictionary;
            }
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
        }

        private static int ReadInt(Dictionary<string, object> source, string key)
        {
            var value = ReadLong(source, key);
            return value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;
        }

        private static long ReadLong(Dictionary<string, object> source, string key)
        {
            object value;
            long result;
            return source != null && source.TryGetValue(key, out value) && value != null && long.TryParse(Convert.ToString(value), out result) ? result : 0L;
        }

        private static bool ReadBool(Dictionary<string, object> source, string key)
        {
            object value;
            bool result;
            return source != null && source.TryGetValue(key, out value) && value != null && bool.TryParse(Convert.ToString(value), out result) && result;
        }

        private static LeaguePlayerProfile CloneProfile(LeaguePlayerProfile source)
        {
            return source == null ? null : new LeaguePlayerProfile
            {
                PuuId = source.PuuId,
                SummonerId = source.SummonerId,
                AccountId = source.AccountId,
                GameName = source.GameName,
                TagLine = source.TagLine,
                DisplayName = source.DisplayName,
                SummonerLevel = source.SummonerLevel,
                ProfileIconId = source.ProfileIconId
            };
        }

        private static Dictionary<int, string> CloneChampionNames(Dictionary<int, string> source)
        {
            return source == null ? null : new Dictionary<int, string>(source);
        }

        private static LeaguePlayerMatchPage ClonePage(LeaguePlayerMatchPage source)
        {
            if (source == null) return null;
            var result = new LeaguePlayerMatchPage
            {
                ClientSession = source.ClientSession,
                StartIndex = source.StartIndex,
                RequestedCount = source.RequestedCount,
                ReportedGameCount = source.ReportedGameCount
            };
            foreach (var match in source.Matches)
                result.Matches.Add(CloneMatch(match));
            return result;
        }

        private static LeaguePlayerMatchSummary CloneMatch(LeaguePlayerMatchSummary match)
        {
            if (match == null) return null;
            return new LeaguePlayerMatchSummary
                {
                    PreferredSource = match.PreferredSource,
                    DataSource = match.DataSource,
                    SgpServerId = match.SgpServerId,
                    GameId = match.GameId,
                    GameCreationLocal = match.GameCreationLocal,
                    GameDurationSeconds = match.GameDurationSeconds,
                    GameMode = match.GameMode,
                    GameType = match.GameType,
                    GameCreationEpochMs = match.GameCreationEpochMs,
                    QueueId = match.QueueId,
                    ChampionId = match.ChampionId,
                    MapId = match.MapId,
                    TeamId = match.TeamId,
                    ParticipantId = match.ParticipantId,
                    PlayerSubteamId = match.PlayerSubteamId,
                    SubteamPlacement = match.SubteamPlacement,
                    PuuId = match.PuuId,
                    HasSmite = match.HasSmite,
                    ChampionName = match.ChampionName,
                    Kills = match.Kills,
                    Deaths = match.Deaths,
                    Assists = match.Assists,
                    CreepScore = match.CreepScore,
                    Win = match.Win,
                    WinResult = match.WinResult,
                    IsSurrender = match.IsSurrender,
                    StatisticsAvailable = match.StatisticsAvailable,
                    Position = match.Position,
                    SoloKills = match.SoloKills,
                    VisionScore = match.VisionScore,
                    ChampionDamageShare = match.ChampionDamageShare,
                    DamageTakenShare = match.DamageTakenShare,
                    GoldShare = match.GoldShare,
                    KillParticipation = match.KillParticipation,
                    DamageGoldEfficiency = match.DamageGoldEfficiency,
                    Level = match.Level,
                    GoldEarned = match.GoldEarned,
                    GoldSpent = match.GoldSpent,
                    DamageDealtToChampions = match.DamageDealtToChampions,
                    PhysicalDamageToChampions = match.PhysicalDamageToChampions,
                    MagicDamageToChampions = match.MagicDamageToChampions,
                    TrueDamageToChampions = match.TrueDamageToChampions,
                    DamageTaken = match.DamageTaken,
                    PhysicalDamageTaken = match.PhysicalDamageTaken,
                    MagicDamageTaken = match.MagicDamageTaken,
                    TrueDamageTaken = match.TrueDamageTaken,
                    DamageToTowers = match.DamageToTowers,
                    TotalHeal = match.TotalHeal,
                    TimeCCingOthers = match.TimeCCingOthers,
                    DoubleKills = match.DoubleKills,
                    TripleKills = match.TripleKills,
                    QuadraKills = match.QuadraKills,
                    PentaKills = match.PentaKills,
                    MetricBaselines = match.MetricBaselines == null ? null :
                        match.MetricBaselines.ToDictionary(pair => pair.Key,
                            pair => pair.Value == null ? null : (double[])pair.Value.Clone(), StringComparer.Ordinal),
                    ParticipantRoster = match.ParticipantRoster == null ? null :
                        match.ParticipantRoster.Select(CloneMatch).ToArray(),
                    RosterIdentitiesComplete = match.RosterIdentitiesComplete,
                    SameTeamPuuids = match.SameTeamPuuids == null ? null : (string[])match.SameTeamPuuids.Clone(),
                    MatchChampionIds = match.MatchChampionIds == null ? null : (int[])match.MatchChampionIds.Clone(),
                    MatchPlayers = match.MatchPlayers == null ? null : match.MatchPlayers.Select(player =>
                        player == null ? null : new LeaguePlayerMatchIdentity
                        {
                            PuuId = player.PuuId, PlayerName = player.PlayerName
                        }).ToArray(),
                    ItemIds = match.ItemIds == null ? null : (int[])match.ItemIds.Clone(),
                    RoleBoundItemId = match.RoleBoundItemId,
                    SpellIds = match.SpellIds == null ? null : (int[])match.SpellIds.Clone(),
                    PerkIds = match.PerkIds == null ? null : (int[])match.PerkIds.Clone(),
                    PerkStyleIds = match.PerkStyleIds == null ? null : (int[])match.PerkStyleIds.Clone(),
                    AugmentIds = match.AugmentIds == null ? null : (int[])match.AugmentIds.Clone(),
                    ParticipantResolved = match.ParticipantResolved
                };
        }
    }
}
