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
    internal sealed class LeagueLiveDataService
    {
        internal const string ChampSelectSessionPath = "/lol-champ-select/v1/session";
        internal const string GameflowSessionPath = "/lol-gameflow/v1/session";
        internal const string ChampionIconPathPrefix = "/lol-game-data/assets/v1/champion-icons/";

        private readonly ILeagueClientApi _client;
        private readonly PerformanceBudgetProvider _budgets;
        private readonly LeagueDashboardPhaseService _phaseService;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
        private readonly object _iconCacheSync = new object();
        private readonly Dictionary<int, byte[]> _championIconCache = new Dictionary<int, byte[]>();
        private string _lastLocalPuuid;

        public LeagueLiveDataService(ILeagueClientApi client, PerformanceBudgetProvider budgets)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
            _phaseService = new LeagueDashboardPhaseService(client, budgets);
        }

        public async Task<LeagueLiveSnapshot> RefreshAsync(CancellationToken cancellationToken)
        {
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var phase = await _phaseService.RefreshAsync(cancellationToken).ConfigureAwait(false);
                var snapshot = new LeagueLiveSnapshot
                {
                    Connected = phase.Connected,
                    Phase = phase.Phase,
                    Activity = phase.Activity,
                    BudgetName = phase.BudgetName,
                    UpdatedAtUtc = phase.UpdatedAtUtc
                };

                if (!phase.Connected) return snapshot;

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    if (phase.Activity == LeagueActivityLevel.ChampSelect)
                    {
                        var bytes = await _client.TryGetBytesAsync(ChampSelectSessionPath, timeout.Token).ConfigureAwait(false);
                        ApplyChampSelect(snapshot, bytes);
                    }
                    else if (phase.Activity == LeagueActivityLevel.InGame)
                    {
                        var bytes = await _client.TryGetBytesAsync(GameflowSessionPath, timeout.Token).ConfigureAwait(false);
                        ApplyCurrentGame(snapshot, bytes);
                    }
                }

                snapshot.BudgetName = _budgets.Current.Name;
                snapshot.UpdatedAtUtc = DateTime.UtcNow;
                return snapshot;
            }
            finally
            {
                _requestGate.Release();
            }
        }

        /// <summary>
        /// Lightweight Champ Select bench probe used only by the visible quick-pick row. It shares
        /// the exact same request gate as the normal live refresh so the feature cannot create
        /// parallel LCU reads inside this module.
        /// </summary>
        public async Task<LeagueBenchQuickPickState> RefreshBenchAsync(CancellationToken cancellationToken)
        {
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    var bytes = await _client.TryGetBytesAsync(ChampSelectSessionPath, timeout.Token).ConfigureAwait(false);
                    return ParseBenchState(bytes);
                }
            }
            finally
            {
                _requestGate.Release();
            }
        }

        /// <summary>
        /// Loads a local LCU champion portrait only after that champion is actually visible on the
        /// bench. Bytes are cached for the lifetime of the Live service; there is no external image
        /// request or background prefetch.
        /// </summary>
        public async Task<byte[]> LoadChampionIconAsync(int championId, CancellationToken cancellationToken)
        {
            if (championId <= 0) return null;

            byte[] cached;
            lock (_iconCacheSync)
            {
                if (_championIconCache.TryGetValue(championId, out cached)) return cached;
            }

            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_iconCacheSync)
                {
                    if (_championIconCache.TryGetValue(championId, out cached)) return cached;
                }

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    var bytes = await _client.TryGetBytesAsync(
                        ChampionIconPathPrefix + championId + ".png",
                        timeout.Token).ConfigureAwait(false);
                    if (bytes == null || bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024) return null;

                    lock (_iconCacheSync) _championIconCache[championId] = bytes;
                    return bytes;
                }
            }
            finally
            {
                _requestGate.Release();
            }
        }

        internal void ApplyChampSelect(LeagueLiveSnapshot snapshot, byte[] bytes)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var data = ParseObject(bytes);
            if (data == null) return;

            snapshot.GameId = ReadLong(data, "gameId");
            snapshot.QueueId = ReadInt(data, "queueId");
            snapshot.LocalPlayerCellId = ReadInt(data, "localPlayerCellId");

            var timer = ReadDictionary(data, "timer");
            snapshot.TimerPhase = ReadString(timer, "phase");
            snapshot.TimerMillisecondsLeft = ReadInt(timer, "adjustedTimeLeftInPhase");

            var bans = ReadDictionary(data, "bans");
            AppendInts(snapshot.AllyBans, ReadValue(bans, "myTeamBans"));
            AppendInts(snapshot.EnemyBans, ReadValue(bans, "theirTeamBans"));

            snapshot.BenchEnabled = ReadBool(data, "benchEnabled");
            AppendInts(snapshot.BenchChampionIds, ReadValue(data, "benchChampionIds"));

            AppendChampSelectTeam(snapshot, ReadValue(data, "myTeam"), "ally");
            AppendChampSelectTeam(snapshot, ReadValue(data, "theirTeam"), "enemy");
            ApplyLocalAction(snapshot, ReadValue(data, "actions"));

            foreach (var row in snapshot.Players)
            {
                if (row.IsLocalPlayer && !string.IsNullOrWhiteSpace(row.PuuId))
                {
                    _lastLocalPuuid = row.PuuId;
                    break;
                }
            }
        }

        internal LeagueBenchQuickPickState ParseBenchState(byte[] bytes)
        {
            var state = new LeagueBenchQuickPickState();
            var data = ParseObject(bytes);
            if (data == null) return state;

            state.SessionAvailable = true;
            state.BenchEnabled = ReadBool(data, "benchEnabled");
            state.LocalPlayerCellId = ReadInt(data, "localPlayerCellId");
            AppendInts(state.ChampionIds, ReadValue(data, "benchChampionIds"));

            foreach (var member in EnumerateDictionaries(ReadValue(data, "myTeam")))
            {
                if (ReadInt(member, "cellId") != state.LocalPlayerCellId) continue;
                state.LocalChampionId = ReadInt(member, "championId");
                break;
            }
            return state;
        }

        internal void ApplyCurrentGame(LeagueLiveSnapshot snapshot, byte[] bytes)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var data = ParseObject(bytes);
            if (data == null) return;

            var phase = ReadString(data, "phase");
            if (!string.IsNullOrWhiteSpace(phase)) snapshot.Phase = phase;

            var map = ReadDictionary(data, "map");
            snapshot.MapId = ReadInt(map, "id");
            snapshot.MapName = FirstNonEmpty(ReadString(map, "name"), ReadString(map, "mapStringId"));
            snapshot.GameMode = FirstNonEmpty(ReadString(map, "gameModeName"), ReadString(map, "gameMode"));

            var gameData = ReadDictionary(data, "gameData");
            snapshot.GameId = ReadLong(gameData, "gameId");
            var queue = ReadDictionary(gameData, "queue");
            snapshot.QueueId = ReadInt(queue, "id");
            snapshot.QueueName = FirstNonEmpty(ReadString(queue, "name"), ReadString(queue, "shortName"));
            if (string.IsNullOrWhiteSpace(snapshot.GameMode))
                snapshot.GameMode = FirstNonEmpty(ReadString(queue, "gameMode"), ReadString(gameData, "gameName"));

            AppendCurrentGameTeam(snapshot, ReadValue(gameData, "teamOne"), "team-1");
            AppendCurrentGameTeam(snapshot, ReadValue(gameData, "teamTwo"), "team-2");
        }

        private void AppendChampSelectTeam(LeagueLiveSnapshot snapshot, object value, string side)
        {
            foreach (var member in EnumerateDictionaries(value))
            {
                var cellId = ReadInt(member, "cellId");
                snapshot.Players.Add(new LeagueLivePlayerRow
                {
                    Side = side,
                    CellId = cellId,
                    IsLocalPlayer = cellId == snapshot.LocalPlayerCellId,
                    GameName = ReadString(member, "gameName"),
                    TagLine = ReadString(member, "tagLine"),
                    DisplayName = FirstNonEmpty(ReadString(member, "playerAlias"), ReadString(member, "internalName")),
                    PuuId = ReadString(member, "puuid"),
                    SummonerId = ReadLong(member, "summonerId"),
                    Position = ReadString(member, "assignedPosition"),
                    ChampionId = ReadInt(member, "championId"),
                    ChampionPickIntent = ReadInt(member, "championPickIntent"),
                    Spell1Id = ReadInt(member, "spell1Id"),
                    Spell2Id = ReadInt(member, "spell2Id")
                });
            }
        }

        private void AppendCurrentGameTeam(LeagueLiveSnapshot snapshot, object value, string side)
        {
            foreach (var member in EnumerateDictionaries(value))
            {
                var puuid = ReadString(member, "puuid");
                snapshot.Players.Add(new LeagueLivePlayerRow
                {
                    Side = side,
                    PuuId = puuid,
                    SummonerId = ReadLong(member, "summonerId"),
                    DisplayName = FirstNonEmpty(ReadString(member, "summonerName"), ReadString(member, "summonerInternalName")),
                    Position = ReadString(member, "selectedPosition"),
                    Role = ReadString(member, "selectedRole"),
                    ChampionId = ReadInt(member, "championId"),
                    IsLocalPlayer = !string.IsNullOrWhiteSpace(_lastLocalPuuid) && string.Equals(_lastLocalPuuid, puuid, StringComparison.Ordinal)
                });
            }
        }

        private static void ApplyLocalAction(LeagueLiveSnapshot snapshot, object actionsValue)
        {
            foreach (var group in EnumerateValues(actionsValue))
            {
                foreach (var action in EnumerateDictionaries(group))
                {
                    if (ReadInt(action, "actorCellId") != snapshot.LocalPlayerCellId) continue;
                    if (!ReadBool(action, "isInProgress")) continue;
                    snapshot.LocalActionType = ReadString(action, "type");
                    snapshot.LocalActionChampionId = ReadInt(action, "championId");
                    return;
                }
            }
        }

        private Dictionary<string, object> ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static object ReadValue(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            return ReadValue(source, key) as Dictionary<string, object>;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            return value == null ? null : Convert.ToString(value);
        }

        private static int ReadInt(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            int result;
            return value != null && int.TryParse(Convert.ToString(value), out result) ? result : 0;
        }

        private static long ReadLong(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            long result;
            return value != null && long.TryParse(Convert.ToString(value), out result) ? result : 0L;
        }

        private static bool ReadBool(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            bool result;
            return value != null && bool.TryParse(Convert.ToString(value), out result) && result;
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
            }
        }

        private static IEnumerable<Dictionary<string, object>> EnumerateDictionaries(object value)
        {
            foreach (var item in EnumerateValues(value))
            {
                var dictionary = item as Dictionary<string, object>;
                if (dictionary != null) yield return dictionary;
            }
        }

        private static void AppendInts(ICollection<int> target, object value)
        {
            foreach (var item in EnumerateValues(value))
            {
                int parsed;
                if (item != null && int.TryParse(Convert.ToString(item), out parsed) && parsed > 0) target.Add(parsed);
            }
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return string.IsNullOrWhiteSpace(first) ? second : first;
        }
    }
}
