using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Performance;

namespace FACM.League
{
    internal sealed class LeaguePlayerDataService
    {
        internal const int InitialMatchCount = 10;
        internal const int MaximumMatchCount = 20;
        internal const string ChampionSummaryPath = "/lol-game-data/assets/v1/champion-summary.json";
        private static readonly TimeSpan ChampionMetadataCacheDuration = TimeSpan.FromMinutes(30);
        private readonly object _cacheSync = new object();
        private readonly ILeagueClientApi _client;
        private readonly PerformanceBudgetProvider _budgets;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
        private LeaguePlayerProfile _cachedProfile;
        private DateTime _profileCachedUtc = DateTime.MinValue;
        private LeaguePlayerMatchPage _cachedPage;
        private string _cachedPagePuuId;
        private DateTime _pageCachedUtc = DateTime.MinValue;
        private Dictionary<int, string> _cachedChampionNames;
        private DateTime _championNamesCachedUtc = DateTime.MinValue;

        public LeaguePlayerDataService(ILeagueClientApi client, PerformanceBudgetProvider budgets)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
        }

        public LeaguePlayerProfile TryGetCachedProfile()
        {
            lock (_cacheSync) return CloneProfile(_cachedProfile);
        }

        public LeaguePlayerMatchPage TryGetCachedPage()
        {
            lock (_cacheSync) return ClonePage(_cachedPage);
        }

        public async Task<LeaguePlayerProfile> LoadProfileAsync(bool force, CancellationToken cancellationToken)
        {
            lock (_cacheSync)
            {
                if (!force && _cachedProfile != null && DateTime.UtcNow - _profileCachedUtc < TimeSpan.FromSeconds(15))
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
            LeaguePlayerProfile profile,
            int startIndex,
            int count,
            bool force,
            CancellationToken cancellationToken)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.PuuId)) return null;
            startIndex = Math.Max(0, startIndex);
            count = Math.Max(1, Math.Min(MaximumMatchCount, count));

            lock (_cacheSync)
            {
                if (!force && _cachedPage != null &&
                    string.Equals(_cachedPagePuuId, profile.PuuId, StringComparison.OrdinalIgnoreCase) &&
                    _cachedPage.StartIndex == startIndex && _cachedPage.RequestedCount == count &&
                    DateTime.UtcNow - _pageCachedUtc < TimeSpan.FromSeconds(45))
                    return ClonePage(_cachedPage);
            }

            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var budget = _budgets.Current;
                    timeout.CancelAfter(budget.NetworkConcurrency <= 1 ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(4));
                    var endIndex = startIndex + count - 1;
                    var path = "/lol-match-history/v1/products/lol/" + Uri.EscapeDataString(profile.PuuId) +
                               "/matches?begIndex=" + startIndex + "&endIndex=" + endIndex;
                    var bytes = await _client.TryGetBytesAsync(path, timeout.Token).ConfigureAwait(false);
                    var page = ParseMatchPage(bytes, profile, startIndex, count);
                    if (page != null) CachePage(profile.PuuId, page);
                    return page;
                }
            }
            finally
            {
                _requestGate.Release();
            }
        }

        public async Task<LeaguePlayerMatchPage> EnrichIncompleteMatchesAsync(
            LeaguePlayerProfile profile,
            LeaguePlayerMatchPage page,
            CancellationToken cancellationToken)
        {
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
                    if (current == null || current.ParticipantResolved || current.GameId <= 0) continue;

                    byte[] bytes;
                    try
                    {
                        await _requestGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                        try
                        {
                            bytes = await _client.TryGetBytesAsync(
                                "/lol-match-history/v1/games/" + current.GameId,
                                timeout.Token).ConfigureAwait(false);
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

                    var fullGame = ParseObject(bytes);
                    var enriched = ParseMatch(fullGame, profile);
                    if (enriched == null || !enriched.ParticipantResolved) continue;
                    result.Matches[index] = enriched;
                    changed = true;
                }
            }

            if (changed) CachePage(profile.PuuId, result);
            return result;
        }

        public async Task<LeaguePlayerMatchPage> EnrichChampionNamesAsync(
            LeaguePlayerProfile profile,
            LeaguePlayerMatchPage page,
            CancellationToken cancellationToken)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.PuuId) || page == null || page.Matches.Count == 0)
                return page;

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

            if (changed) CachePage(profile.PuuId, result);
            return result;
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

            var stats = participant == null ? null : ReadObject(participant, "stats");
            return new LeaguePlayerMatchSummary
            {
                GameId = ReadLong(game, "gameId"),
                GameCreationLocal = ResolveCreationLocal(game),
                GameDurationSeconds = ReadInt(game, "gameDuration"),
                GameMode = ReadString(game, "gameMode"),
                QueueId = ReadInt(game, "queueId"),
                ChampionId = participant == null ? 0 : ReadInt(participant, "championId"),
                Kills = stats == null ? 0 : ReadInt(stats, "kills"),
                Deaths = stats == null ? 0 : ReadInt(stats, "deaths"),
                Assists = stats == null ? 0 : ReadInt(stats, "assists"),
                CreepScore = stats == null ? 0 : ReadInt(stats, "totalMinionsKilled") + ReadInt(stats, "neutralMinionsKilled"),
                Win = stats != null && ReadBool(stats, "win"),
                ParticipantResolved = participant != null
            };
        }

        private void CachePage(string puuId, LeaguePlayerMatchPage page)
        {
            if (string.IsNullOrWhiteSpace(puuId) || page == null) return;
            lock (_cacheSync)
            {
                _cachedPage = ClonePage(page);
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
                StartIndex = source.StartIndex,
                RequestedCount = source.RequestedCount,
                ReportedGameCount = source.ReportedGameCount
            };
            foreach (var match in source.Matches)
            {
                result.Matches.Add(match == null ? null : new LeaguePlayerMatchSummary
                {
                    GameId = match.GameId,
                    GameCreationLocal = match.GameCreationLocal,
                    GameDurationSeconds = match.GameDurationSeconds,
                    GameMode = match.GameMode,
                    QueueId = match.QueueId,
                    ChampionId = match.ChampionId,
                    ChampionName = match.ChampionName,
                    Kills = match.Kills,
                    Deaths = match.Deaths,
                    Assists = match.Assists,
                    CreepScore = match.CreepScore,
                    Win = match.Win,
                    ParticipantResolved = match.ParticipantResolved
                });
            }
            return result;
        }
    }
}
