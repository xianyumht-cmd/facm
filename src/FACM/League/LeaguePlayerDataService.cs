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
        private LeagueBuildAdvisorCatalog _cachedMatchBuildCatalog;
        private DateTime _matchBuildCatalogCachedUtc = DateTime.MinValue;

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

        internal async Task<LeaguePlayerMatchSummary> LoadMatchDetailAsync(
            LeaguePlayerProfile profile, long gameId, CancellationToken cancellationToken)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.PuuId) || gameId <= 0) return null;
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var bytes = await _client.TryGetBytesAsync(
                    "/lol-match-history/v1/games/" + gameId, cancellationToken).ConfigureAwait(false);
                var match = ParseMatch(ParseObject(bytes), profile);
                return match != null && match.GameId == gameId && match.ParticipantResolved ? match : null;
            }
            finally { _requestGate.Release(); }
        }

        internal async Task<LeaguePlayerMatchBreakdown> LoadMatchBreakdownAsync(
            LeaguePlayerProfile profile, long gameId, CancellationToken cancellationToken)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.PuuId) || gameId <= 0) return null;
            LeaguePlayerMatchBreakdown breakdown;
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var bytes = await _client.TryGetBytesAsync(
                    "/lol-match-history/v1/games/" + gameId, cancellationToken).ConfigureAwait(false);
                var game = ParseObject(bytes);
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
                foreach (var participant in breakdown.Participants)
                {
                    string name;
                    if (names.TryGetValue(participant.ChampionId, out name)) participant.ChampionName = name;
                    if (participant.IsTarget) breakdown.Match.ChampionName = participant.ChampionName;
                }
            }
            breakdown.BuildCatalog = await LoadMatchBuildCatalogAsync(cancellationToken).ConfigureAwait(false);
            return breakdown;
        }

        private async Task<LeagueBuildAdvisorCatalog> LoadMatchBuildCatalogAsync(CancellationToken cancellationToken)
        {
            lock (_cacheSync)
            {
                if (_cachedMatchBuildCatalog != null &&
                    DateTime.UtcNow - _matchBuildCatalogCachedUtc < ChampionMetadataCacheDuration)
                    return _cachedMatchBuildCatalog;
            }
            if (!_budgets.Current.AllowMaintenanceWork) return null;

            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_cacheSync)
                {
                    if (_cachedMatchBuildCatalog != null &&
                        DateTime.UtcNow - _matchBuildCatalogCachedUtc < ChampionMetadataCacheDuration)
                        return _cachedMatchBuildCatalog;
                }
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var items = await _client.TryGetBytesAsync(LeagueBuildAdvisorDataService.ItemsPath, timeout.Token)
                        .ConfigureAwait(false);
                    var spells = await _client.TryGetBytesAsync(LeagueBuildAdvisorDataService.SummonerSpellsPath, timeout.Token)
                        .ConfigureAwait(false);
                    var perks = await _client.TryGetBytesAsync(LeagueBuildAdvisorDataService.PerksPath, timeout.Token)
                        .ConfigureAwait(false);
                    var catalog = LeagueBuildAdvisorDataService.ParseLocalCatalog(null, items, spells, perks);
                    if (catalog.Items.Count == 0 && catalog.Spells.Count == 0 && catalog.Perks.Count == 0)
                        return null;
                    lock (_cacheSync)
                    {
                        _cachedMatchBuildCatalog = catalog;
                        _matchBuildCatalogCachedUtc = DateTime.UtcNow;
                    }
                    return catalog;
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                return null;
            }
            catch (Exception)
            {
                return null;
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
                    PuuId = puuid,
                    PlayerName = SafeDisplayName(name),
                    ChampionId = ReadInt(participant, "championId"),
                    ItemIds = Enumerable.Range(0, 7).Select(slot => ReadInt(stats, "item" + slot)).ToArray(),
                    SpellIds = new[] { ReadInt(participant, "spell1Id"), ReadInt(participant, "spell2Id") },
                    PerkIds = Enumerable.Range(0, 6).Select(slot => ReadInt(stats, "perk" + slot)).ToArray(),
                    PrimaryPerkStyleId = ReadInt(stats, "perkPrimaryStyle"),
                    SecondaryPerkStyleId = ReadInt(stats, "perkSubStyle"),
                    AugmentIds = Enumerable.Range(1, 6).Select(slot => ReadInt(stats, "playerAugment" + slot)).ToArray(),
                    Kills = ReadInt(stats, "kills"), Deaths = ReadInt(stats, "deaths"),
                    Assists = ReadInt(stats, "assists"),
                    CreepScore = (int)Math.Min(int.MaxValue, Math.Max(0L,
                        (long)ReadInt(stats, "totalMinionsKilled") + ReadInt(stats, "neutralMinionsKilled"))),
                    DamageToChampions = ReadInt(stats, "totalDamageDealtToChampions"),
                    DamageAvailable = stats.ContainsKey("totalDamageDealtToChampions"),
                    GoldEarned = ReadInt(stats, "goldEarned"),
                    GoldAvailable = stats.ContainsKey("goldEarned"),
                    VisionScore = ReadInt(stats, "visionScore"),
                    Win = ReadBool(stats, "win"), IsTarget = id == targetId
                });
            }
            return result.Count == 0 ? null : result.OrderBy(p => p.TeamId)
                .ThenBy(p => p.ParticipantId).ToList();
        }

        private static List<LeagueMatchTeamScore> ParseTeamScores(Dictionary<string, object> game,
            List<LeaguePlayerMatchParticipant> participants)
        {
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
            foreach (var group in participants.Where(p => p.TeamId > 0).GroupBy(p => p.TeamId).OrderBy(g => g.Key))
            {
                var players = group.ToList();
                var consistentWin = players.All(p => p.Win == players[0].Win);
                Dictionary<string, object> team;
                metadata.TryGetValue(group.Key, out team);
                scores.Add(new LeagueMatchTeamScore
                {
                    TeamId = group.Key, PlayerCount = players.Count,
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
                    HordeKills = ReadOptionalCount(team, "hordeKills")
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

            if (changed) CachePage(profile.PuuId, result);
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
                MapId = ReadInt(game, "mapId"),
                TeamId = participant == null ? 0 : ReadInt(participant, "teamId"),
                ParticipantId = participantId > 0 && participant != null ? participantId : 0,
                HasSmite = participant != null && (ReadInt(participant, "spell1Id") == 11 || ReadInt(participant, "spell2Id") == 11),
                Kills = stats == null ? 0 : ReadInt(stats, "kills"),
                Deaths = stats == null ? 0 : ReadInt(stats, "deaths"),
                Assists = stats == null ? 0 : ReadInt(stats, "assists"),
                CreepScore = stats == null ? 0 : ReadInt(stats, "totalMinionsKilled") + ReadInt(stats, "neutralMinionsKilled"),
                Win = stats != null && ReadBool(stats, "win"),
                StatisticsAvailable = stats != null && stats.ContainsKey("win") && stats.ContainsKey("kills") && stats.ContainsKey("deaths") && stats.ContainsKey("assists"),
                Position = MatchPosition(participant),
                SoloKills = Metric(ReadObject(stats, "challenges"), "soloKills"),
                VisionScore = Metric(stats, "visionScore"),
                ChampionDamageShare = TeamShare(game, participant, "totalDamageDealtToChampions"),
                DamageTakenShare = TeamShare(game, participant, "totalDamageTaken"),
                GoldShare = TeamShare(game, participant, "goldEarned"),
                KillParticipation = TeamShare(game, participant, "kills", true),
                DamageGoldEfficiency = Ratio(Metric(stats, "totalDamageDealtToChampions"), Metric(stats, "goldEarned")),
                SameTeamPuuids = SameTeamPuuids(game, participant, participantId, profile),
                ParticipantResolved = participant != null
            };
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
                    MapId = match.MapId,
                    TeamId = match.TeamId,
                    ParticipantId = match.ParticipantId,
                    HasSmite = match.HasSmite,
                    ChampionName = match.ChampionName,
                    Kills = match.Kills,
                    Deaths = match.Deaths,
                    Assists = match.Assists,
                    CreepScore = match.CreepScore,
                    Win = match.Win,
                    StatisticsAvailable = match.StatisticsAvailable,
                    Position = match.Position,
                    SoloKills = match.SoloKills,
                    VisionScore = match.VisionScore,
                    ChampionDamageShare = match.ChampionDamageShare,
                    DamageTakenShare = match.DamageTakenShare,
                    GoldShare = match.GoldShare,
                    KillParticipation = match.KillParticipation,
                    DamageGoldEfficiency = match.DamageGoldEfficiency,
                    SameTeamPuuids = match.SameTeamPuuids == null ? null : (string[])match.SameTeamPuuids.Clone(),
                    ParticipantResolved = match.ParticipantResolved
                });
            }
            return result;
        }
    }
}
