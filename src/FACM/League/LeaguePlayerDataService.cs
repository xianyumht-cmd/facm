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
        private readonly object _cacheSync = new object();
        private readonly ILeagueClientApi _client;
        private readonly PerformanceBudgetProvider _budgets;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
        private LeaguePlayerProfile _cachedProfile;
        private DateTime _profileCachedUtc = DateTime.MinValue;
        private LeaguePlayerMatchPage _cachedPage;
        private DateTime _pageCachedUtc = DateTime.MinValue;

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
                if (!force && _cachedProfile != null && DateTime.UtcNow - _profileCachedUtc < TimeSpan.FromMinutes(5))
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
                    _cachedPage.StartIndex == startIndex && _cachedPage.RequestedCount == count &&
                    DateTime.UtcNow - _pageCachedUtc < TimeSpan.FromSeconds(45))
                    return ClonePage(_cachedPage);
            }

            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    var endIndex = startIndex + count - 1;
                    var path = "/lol-match-history/v1/products/lol/" + Uri.EscapeDataString(profile.PuuId) +
                               "/matches?begIndex=" + startIndex + "&endIndex=" + endIndex;
                    var bytes = await _client.TryGetBytesAsync(path, timeout.Token).ConfigureAwait(false);
                    var page = ParseMatchPage(bytes, profile, startIndex, count);
                    if (page != null)
                    {
                        lock (_cacheSync)
                        {
                            _cachedPage = ClonePage(page);
                            _pageCachedUtc = DateTime.UtcNow;
                        }
                    }
                    return page;
                }
            }
            finally
            {
                _requestGate.Release();
            }
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
            var creation = ResolveCreationLocal(game);
            return new LeaguePlayerMatchSummary
            {
                GameId = ReadLong(game, "gameId"),
                GameCreationLocal = creation,
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
                result.Matches.Add(new LeaguePlayerMatchSummary
                {
                    GameId = match.GameId,
                    GameCreationLocal = match.GameCreationLocal,
                    GameDurationSeconds = match.GameDurationSeconds,
                    GameMode = match.GameMode,
                    QueueId = match.QueueId,
                    ChampionId = match.ChampionId,
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
