using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        internal const string TeamBuilderChampSelectSessionPath = "/lol-lobby-team-builder/champ-select/v1/session";
        internal const string GameflowSessionPath = "/lol-gameflow/v1/session";
        internal const string ChampionIconPathPrefix = "/lol-game-data/assets/v1/champion-icons/";

        private readonly ILeagueClientApi _client;
        private readonly PerformanceBudgetProvider _budgets;
        private readonly LeagueDashboardPhaseService _phaseService;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
        private readonly object _iconCacheSync = new object();
        private readonly Dictionary<int, byte[]> _championIconCache = new Dictionary<int, byte[]>();
        private readonly ILeagueSgpGsmApi _sgpGsm;
        private readonly Func<LeagueClientSession> _session;
        private List<LeagueLivePlayerRow> _sgpRows;
        private LeagueClientSession _sgpSession;
        private string _sgpOwnerPuuid;
        private long _sgpGameId;
        private DateTime _sgpUntilUtc;
        private string _lastLocalPuuid;
        private int _lastBenchSwapRoute;

        public LeagueLiveDataService(ILeagueClientApi client, PerformanceBudgetProvider budgets,
            ILeagueSgpGsmApi sgpGsm = null, Func<LeagueClientSession> session = null)
        {
            _sgpGsm = sgpGsm;
            _session = session;
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
            _phaseService = new LeagueDashboardPhaseService(client, budgets);
            _lastBenchSwapRoute = (int)LeagueBenchSwapRoute.Legacy;
        }

        internal LeagueBenchSwapRoute LastBenchSwapRouteForQuickPick
        {
            get { return (LeagueBenchSwapRoute)Volatile.Read(ref _lastBenchSwapRoute); }
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

                if (phase.Activity == LeagueActivityLevel.InGame)
                    await ApplyCachedSgpAsync(snapshot, cancellationToken).ConfigureAwait(false);
                else
                    ClearSgpCache();

                snapshot.BudgetName = _budgets.Current.Name;
                snapshot.UpdatedAtUtc = DateTime.UtcNow;
                return snapshot;
            }
            finally
            {
                _requestGate.Release();
            }
        }


        // SGP is explicitly requested from the visible Live refresh button. The normal phase
        // observer never performs a remote SGP request.
        internal async Task<bool> TryRefreshSgpTeamsAsync(LeagueLiveSnapshot snapshot, CancellationToken token)
        {
            if (snapshot == null || snapshot.Activity != LeagueActivityLevel.InGame ||
                snapshot.GameId <= 0 || _sgpGsm == null || _session == null) return false;
            await _requestGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ClearSgpCache();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(12));
                    try
                    {
                        var ownerSession = _session();
                        if (ownerSession == null || LeagueSgpMatchHistoryApiClient.ResolveServer(ownerSession) == null)
                            return false;
                        var owner = await ReadLocalPuuidAsync(timeout.Token).ConfigureAwait(false);
                        if (!LeagueSgpMatchHistoryApiClient.SafePuuid(owner) || !SameSgpSession(ownerSession))
                            return false;

                        var payload = await _sgpGsm.TryGetGsmByPuuidAsync(owner, timeout.Token).ConfigureAwait(false);
                        var rows = ParseSgpGame(payload, snapshot.GameId, owner);
                        if (rows == null || !SameSgpSession(ownerSession)) return false;

                        var after = await ReadLocalPuuidAsync(timeout.Token).ConfigureAwait(false);
                        if (!string.Equals(owner, after, StringComparison.Ordinal) || !SameSgpSession(ownerSession))
                            return false;

                        var gameflow = await _client.TryGetBytesAsync(GameflowSessionPath, timeout.Token)
                            .ConfigureAwait(false);
                        var current = new LeagueLiveSnapshot();
                        ApplyCurrentGame(current, gameflow);
                        if (current.GameId != snapshot.GameId ||
                            !string.Equals(current.Phase, "InProgress", StringComparison.OrdinalIgnoreCase) ||
                            !SameSgpSession(ownerSession))
                            return false;
                        if (!MergeSgpRows(snapshot, rows, owner)) return false;

                        _sgpRows = rows;
                        _sgpSession = ownerSession;
                        _sgpOwnerPuuid = owner;
                        _sgpGameId = snapshot.GameId;
                        _sgpUntilUtc = DateTime.UtcNow.AddSeconds(90);
                        snapshot.SgpEnriched = true;
                        return true;
                    }
                    catch (OperationCanceledException)
                    {
                        if (token.IsCancellationRequested) throw;
                        return false;
                    }
                    catch (Exception) { return false; }
                }
            }
            finally { _requestGate.Release(); }
        }

        private async Task ApplyCachedSgpAsync(LeagueLiveSnapshot snapshot, CancellationToken token)
        {
            if (_sgpRows == null) return;
            if (snapshot.GameId <= 0 || snapshot.GameId != _sgpGameId ||
                DateTime.UtcNow >= _sgpUntilUtc || !SameSgpSession(_sgpSession))
            {
                ClearSgpCache();
                return;
            }
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    var owner = await ReadLocalPuuidAsync(deadline.Token).ConfigureAwait(false);
                    if (!string.Equals(owner, _sgpOwnerPuuid, StringComparison.Ordinal) ||
                        !MergeSgpRows(snapshot, _sgpRows, owner))
                        ClearSgpCache();
                    else
                        snapshot.SgpEnriched = true;
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    ClearSgpCache();
                }
                catch (Exception) { ClearSgpCache(); }
            }
        }

        private async Task<string> ReadLocalPuuidAsync(CancellationToken token)
        {
            var bytes = await _client.TryGetBytesAsync(
                LeagueDashboardDetailsService.SummonerPath, token).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0 || bytes.Length > 64 * 1024) return null;
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 }
                    .DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                var puuid = ReadString(root, "puuid");
                return LeagueSgpMatchHistoryApiClient.SafePuuid(puuid) ? puuid : null;
            }
            catch (Exception) { return null; }
        }

        private bool SameSgpSession(LeagueClientSession expected)
        {
            var current = _session == null ? null : _session();
            return expected != null && expected.Matches(current) &&
                string.Equals(LeagueSgpMatchHistoryApiClient.CurrentServerId(expected),
                    LeagueSgpMatchHistoryApiClient.CurrentServerId(current), StringComparison.Ordinal);
        }

        private void ClearSgpCache()
        {
            _sgpRows = null;
            _sgpSession = null;
            _sgpOwnerPuuid = null;
            _sgpGameId = 0;
            _sgpUntilUtc = DateTime.MinValue;
        }

        internal static List<LeagueLivePlayerRow> ParseSgpGame(byte[] bytes, long gameId, string owner)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024 ||
                gameId <= 0 || !LeagueSgpMatchHistoryApiClient.SafePuuid(owner)) return null;
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }
                    .DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                var credentials = ReadDictionary(root, "playerCredentials");
                var game = ReadDictionary(root, "game");
                if (ReadLong(credentials, "gameId") != gameId ||
                    !string.Equals(ReadString(credentials, "puuid"), owner, StringComparison.Ordinal) ||
                    ReadLong(game, "id") != gameId) return null;

                var rows = new List<LeagueLivePlayerRow>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var side in new[] { "teamOne", "teamTwo" })
                {
                    var count = 0;
                    foreach (var member in EnumerateDictionaries(ReadValue(game, side)))
                    {
                        if (++count > 20) return null;
                        var puuid = ReadString(member, "puuid");
                        if (!LeagueSgpMatchHistoryApiClient.SafePuuid(puuid) || !seen.Add(puuid))
                            return null;
                        rows.Add(new LeagueLivePlayerRow
                        {
                            Side = side == "teamOne" ? "team-1" : "team-2",
                            PuuId = puuid,
                            SummonerId = ReadLong(member, "summonerId"),
                            DisplayName = ReadString(member, "summonerName"),
                            Position = ReadString(member, "selectedPosition"),
                            Role = ReadString(member, "selectedRole"),
                            ChampionId = ReadInt(member, "championId"),
                            IsLocalPlayer = string.Equals(puuid, owner, StringComparison.Ordinal)
                        });
                    }
                }
                if (rows.Count == 0 || !seen.Contains(owner)) return null;
                var selected = new HashSet<string>(StringComparer.Ordinal);
                foreach (var choice in EnumerateDictionaries(ReadValue(game, "playerChampionSelections")))
                {
                    var puuid = ReadString(choice, "puuid");
                    if (!LeagueSgpMatchHistoryApiClient.SafePuuid(puuid) || !selected.Add(puuid))
                        return null;
                    var row = rows.Find(item => string.Equals(item.PuuId, puuid, StringComparison.Ordinal));
                    if (row == null) continue;
                    if (row.ChampionId == 0) row.ChampionId = ReadInt(choice, "championId");
                    row.Spell1Id = ReadInt(choice, "spell1Id");
                    row.Spell2Id = ReadInt(choice, "spell2Id");
                }
                return rows;
            }
            catch (Exception) { return null; }
        }

        private static bool MergeSgpRows(LeagueLiveSnapshot snapshot,
            IEnumerable<LeagueLivePlayerRow> rows, string owner)
        {
            var incoming = rows.ToList();
            foreach (var row in incoming)
            {
                var existing = snapshot.Players.Find(item =>
                    string.Equals(item.PuuId, row.PuuId, StringComparison.Ordinal));
                if (existing != null && !string.Equals(existing.Side, row.Side, StringComparison.Ordinal))
                    return false;
            }
            foreach (var item in snapshot.Players)
                item.IsLocalPlayer = !string.IsNullOrEmpty(item.PuuId) &&
                    string.Equals(item.PuuId, owner, StringComparison.Ordinal);
            foreach (var row in incoming)
            {
                var existing = snapshot.Players.Find(item =>
                    string.Equals(item.PuuId, row.PuuId, StringComparison.Ordinal));
                if (existing == null)
                {
                    snapshot.Players.Add(row);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(existing.DisplayName)) existing.DisplayName = row.DisplayName;
                if (string.IsNullOrWhiteSpace(existing.Position)) existing.Position = row.Position;
                if (string.IsNullOrWhiteSpace(existing.Role)) existing.Role = row.Role;
                if (existing.ChampionId <= 0) existing.ChampionId = row.ChampionId;
                if (existing.Spell1Id <= 0) existing.Spell1Id = row.Spell1Id;
                if (existing.Spell2Id <= 0) existing.Spell2Id = row.Spell2Id;
            }
            return true;
        }

        /// <summary>
        /// Lightweight Champ Select bench probe used only by the visible quick-pick row. Current
        /// ARAM/Mayhem sessions expose benchChampions; old clients may expose benchChampionIds.
        /// Team Builder is queried only as a narrow compatibility fallback when the generic session
        /// identifies a Team Builder bench but does not expose the actual bench list.
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
                    var state = ParseBenchState(bytes);

                    var shouldTryTeamBuilder = state == null ||
                                               !state.SessionAvailable ||
                                               (state.BenchEnabled &&
                                                state.SwapRoute == LeagueBenchSwapRoute.TeamBuilder &&
                                                state.ChampionIds.Count == 0);
                    if (!shouldTryTeamBuilder) return state;

                    var teamBuilderBytes = await _client.TryGetBytesAsync(
                        TeamBuilderChampSelectSessionPath,
                        timeout.Token).ConfigureAwait(false);
                    var teamBuilderState = ParseBenchState(teamBuilderBytes);
                    if (teamBuilderState != null && teamBuilderState.SessionAvailable)
                    {
                        // Some Team Builder session shapes expose the bench roster but omit the
                        // queue/mode fields present on the generic ChampSelect session. Preserve
                        // that already-read context so Runtime Companion mode routing does not
                        // silently degrade to an unknown guide kind after the compatibility GET.
                        if (teamBuilderState.QueueId <= 0 && state != null)
                            teamBuilderState.QueueId = state.QueueId;
                        if (string.IsNullOrWhiteSpace(teamBuilderState.GameMode) && state != null)
                            teamBuilderState.GameMode = state.GameMode;
                        if (state != null)
                        {
                            if (string.IsNullOrWhiteSpace(teamBuilderState.TimerPhase))
                                teamBuilderState.TimerPhase = state.TimerPhase;
                            if (teamBuilderState.TimerMillisecondsLeft <= 0)
                                teamBuilderState.TimerMillisecondsLeft = state.TimerMillisecondsLeft;
                            if (teamBuilderState.AllyBans.Count == 0)
                                foreach (var value in state.AllyBans) teamBuilderState.AllyBans.Add(value);
                            if (teamBuilderState.EnemyBans.Count == 0)
                                foreach (var value in state.EnemyBans) teamBuilderState.EnemyBans.Add(value);
                            if (teamBuilderState.Players.Count == 0)
                                foreach (var value in state.Players) teamBuilderState.Players.Add(value);
                        }
                        teamBuilderState.SwapRoute = LeagueBenchSwapRoute.TeamBuilder;
                        RememberBenchSwapRoute(LeagueBenchSwapRoute.TeamBuilder);
                        return teamBuilderState;
                    }

                    return state ?? new LeagueBenchQuickPickState();
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
            snapshot.GameId = ReadLong(data, "gameId");
            snapshot.GameMode = FirstNonEmpty(ReadString(data, "gameMode"), ReadString(data, "gameModeName"));
            snapshot.LocalPlayerCellId = ReadInt(data, "localPlayerCellId");

            var timer = ReadDictionary(data, "timer");
            snapshot.TimerPhase = ReadString(timer, "phase");
            snapshot.TimerMillisecondsLeft = ReadInt(timer, "adjustedTimeLeftInPhase");

            var bans = ReadDictionary(data, "bans");
            AppendInts(snapshot.AllyBans, ReadValue(bans, "myTeamBans"));
            AppendInts(snapshot.EnemyBans, ReadValue(bans, "theirTeamBans"));

            snapshot.BenchEnabled = ReadBool(data, "benchEnabled");
            snapshot.BenchSwapRoute = ResolveBenchSwapRoute(data);
            RememberBenchSwapRoute(snapshot.BenchSwapRoute);
            AppendBenchChampionIds(snapshot.BenchChampionIds, data);

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
            var state = ParseBenchPayload(bytes);
            if (state.SessionAvailable) RememberBenchSwapRoute(state.SwapRoute);
            return state;
        }

        // The phase-owned automatic selector can project an already-read session without
        // introducing a second League Live service, read loop or session owner.
        internal static LeagueBenchQuickPickState ParseBenchPayload(byte[] bytes)
        {
            var state = new LeagueBenchQuickPickState();
            if (bytes == null || bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024) return state;
            Dictionary<string, object> data;
            try
            {
                data = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }
                    .DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
            }
            catch (ArgumentException) { return state; }
            catch (InvalidOperationException) { return state; }
            if (data == null) return state;

            state.SessionAvailable = true;
            state.BenchEnabled = ReadBool(data, "benchEnabled");
            state.AutoSelectGroup = ReadString(data, "autoSelectGroup");
            state.AutoSelectContext = ReadString(data, "autoSelectContext");
            state.QueueId = ReadInt(data, "queueId");
            state.GameMode = FirstNonEmpty(ReadString(data, "gameMode"), ReadString(data, "gameModeName"));
            state.LocalPlayerCellId = ReadInt(data, "localPlayerCellId");
            state.SwapRoute = ResolveBenchSwapRoute(data);
            AppendBenchChampionIds(state.ChampionIds, data);

            var timer = ReadDictionary(data, "timer");
            state.TimerPhase = ReadString(timer, "phase");
            state.TimerMillisecondsLeft = ReadInt(timer, "adjustedTimeLeftInPhase");

            var bans = ReadDictionary(data, "bans");
            AppendInts(state.AllyBans, ReadValue(bans, "myTeamBans"));
            AppendInts(state.EnemyBans, ReadValue(bans, "theirTeamBans"));

            // Reuse the same parser as League Live so the compact quick state can project draft
            // context without a second ChampSelect GET or a second Gameflow owner.
            var projected = new LeagueLiveSnapshot { LocalPlayerCellId = state.LocalPlayerCellId };
            AppendChampSelectTeam(projected, ReadValue(data, "myTeam"), "ally");
            AppendChampSelectTeam(projected, ReadValue(data, "theirTeam"), "enemy");
            foreach (var member in projected.Players) state.Players.Add(member);

            foreach (var member in state.Players)
            {
                if (!member.IsLocalPlayer) continue;
                state.LocalChampionId = member.ChampionId;
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
            snapshot.QueueType = ReadString(queue, "type");
            if (string.IsNullOrWhiteSpace(snapshot.GameMode))
                snapshot.GameMode = FirstNonEmpty(ReadString(queue, "gameMode"), ReadString(gameData, "gameName"));

            AppendCurrentGameTeam(snapshot, ReadValue(gameData, "teamOne"), "team-1");
            AppendCurrentGameTeam(snapshot, ReadValue(gameData, "teamTwo"), "team-2");
        }

        private static void AppendChampSelectTeam(LeagueLiveSnapshot snapshot, object value, string side)
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

        private void RememberBenchSwapRoute(LeagueBenchSwapRoute route)
        {
            Volatile.Write(ref _lastBenchSwapRoute, (int)route);
        }

        private static LeagueBenchSwapRoute ResolveBenchSwapRoute(Dictionary<string, object> source)
        {
            object value;
            if (source != null && source.TryGetValue("isLegacyChampSelect", out value))
            {
                bool isLegacy;
                if (value != null && bool.TryParse(Convert.ToString(value), out isLegacy))
                    return isLegacy ? LeagueBenchSwapRoute.Legacy : LeagueBenchSwapRoute.TeamBuilder;
            }
            return LeagueBenchSwapRoute.Legacy;
        }

        private static void AppendBenchChampionIds(ICollection<int> target, Dictionary<string, object> source)
        {
            if (target == null || source == null) return;
            var seen = new HashSet<int>(target);

            foreach (var benchChampion in EnumerateDictionaries(ReadValue(source, "benchChampions")))
            {
                var championId = ReadInt(benchChampion, "championId");
                if (championId > 0 && seen.Add(championId)) target.Add(championId);
            }

            foreach (var value in EnumerateValues(ReadValue(source, "benchChampionIds")))
            {
                int championId;
                if (value != null && int.TryParse(Convert.ToString(value), out championId) && championId > 0 && seen.Add(championId))
                    target.Add(championId);
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
