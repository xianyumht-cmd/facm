using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Performance;

namespace FACM.League
{
    internal sealed class LeagueDynamicMessageOptions
    {
        public string Target { get; set; } = "all";
        public string NameStrategy { get; set; } = "preferChampionName";
        public string PremadeNameStrategy { get; set; } = "preferName";
        public int PremadeMatchThreshold { get; set; } = 5;
        public int MatchHistoryLoadCount { get; set; } = 20;
        public int HistoryConcurrency { get; set; } = 2;
        public int AutoHistoryPages { get; set; }
        public string MatchHistoryTagPreference { get; set; } = "all";
        public bool QueryInLobbyPhase { get; set; }
        public string OrderPlayerBy { get; set; } = "default";
        public int GameDetailsLoadCount { get; set; } = 20;
        public string ChampionUsageMode { get; set; } = "recent";
        public bool ShowSelfTag { get; set; } = true;
        public bool ShowTaggedTag { get; set; } = true;
        public bool ShowMetTag { get; set; } = true;
        public bool ShowPremadeTeamTag { get; set; } = true;
        public bool ShowWinRateTeamTag { get; set; } = true;
        public bool ShowWinningStreakTag { get; set; } = true;
        public bool ShowLosingStreakTag { get; set; } = true;
        public bool ShowSuspiciousFlashPositionTag { get; set; } = true;
        public bool ShowSoloKillsTag { get; set; } = true;
        public bool ShowAverageTeamDamageTag { get; set; }
        public bool ShowAverageTeamDamageTakenTag { get; set; }
        public bool ShowAverageTeamGoldTag { get; set; }
        public bool ShowAverageCsPerMinuteTag { get; set; }
        public bool ShowAverageDamageGoldEfficiencyTag { get; set; }
        public bool ShowAverageKillDamageEfficiencyTag { get; set; }
        public bool ShowEasyGankTag { get; set; } = true;
        public bool ShowPrivacyTag { get; set; } = true;
        public bool ShowAverageEnemyMissingPingsTag { get; set; }
        public bool ShowAverageVisionScoreTag { get; set; }
        public bool ShowGreatPerformanceTag { get; set; } = true;
        public bool ShowAkariScoreTag { get; set; }
        public string JungleNameStrategy { get; set; } = "preferChampionName";
        public bool JungleShowCurrentChampion { get; set; } = true;
        public bool JungleActivityPreference { get; set; } = true;
        public bool JungleFirstClearDistribution { get; set; } = true;
        public bool JungleEarlyGank { get; set; } = true;
        public bool JungleDragonControl { get; set; } = true;
        public bool JungleMonsterControl { get; set; } = true;
        public bool JungleMainChampions { get; set; } = true;
        public bool ShowCurrentChampion { get; set; }
        public bool WinRate { get; set; } = true;
        public bool Kda { get; set; } = true;
        public bool SoloKills { get; set; } = true;
        public bool VisionScore { get; set; }
        public bool ChampionDamageShare { get; set; }
        public bool DamageTakenShare { get; set; }
        public bool GoldShare { get; set; }
        public bool CsPerMinute { get; set; }
        public bool KillParticipation { get; set; }
        public bool DamageGoldEfficiency { get; set; }
        public bool MainChampions { get; set; } = true;
        public bool MainPositions { get; set; } = true;
        public string FriendlyShortcut { get; set; } = "";
        public string EnemyShortcut { get; set; } = "";
        public string AllShortcut { get; set; } = "";
        public string JungleFriendlyShortcut { get; set; } = "";
        public string JungleEnemyShortcut { get; set; } = "";
        public string JungleAllShortcut { get; set; } = "";
        public string PremadeFriendlyShortcut { get; set; } = "";
        public string PremadeEnemyShortcut { get; set; } = "";
        public string PremadeAllShortcut { get; set; } = "";

        internal static LeagueDynamicMessageOptions Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 4096) return new LeagueDynamicMessageOptions();
            try
            {
                var options = new JavaScriptSerializer().Deserialize<LeagueDynamicMessageOptions>(json);
                return Valid(options) ? options : new LeagueDynamicMessageOptions();
            }
            catch (ArgumentException) { return new LeagueDynamicMessageOptions(); }
            catch (InvalidOperationException) { return new LeagueDynamicMessageOptions(); }
        }

        internal static string Save(LeagueDynamicMessageOptions options)
        {
            if (!Valid(options)) throw new ArgumentException("Invalid dynamic message options.");
            return new JavaScriptSerializer().Serialize(options);
        }

        private static bool Valid(LeagueDynamicMessageOptions options)
        {
            if (options == null || options.PremadeMatchThreshold < 2 ||
                options.PremadeMatchThreshold > LeaguePlayerDataService.MaximumMatchCount ||
                options.MatchHistoryLoadCount < 2 ||
                options.MatchHistoryLoadCount > LeaguePlayerDataService.MaximumMatchCount ||
                options.HistoryConcurrency < 1 || options.HistoryConcurrency > 4 ||
                options.AutoHistoryPages < 0 || options.AutoHistoryPages > 10 ||
                options.GameDetailsLoadCount < 0 || options.GameDetailsLoadCount > 20 ||
                !new[] { "all", "current" }.Contains(options.MatchHistoryTagPreference) ||
                !new[] { "default", "win-rate", "kda", "akari-score", "position", "premade-team" }
                    .Contains(options.OrderPlayerBy) ||
                !new[] { "none", "recent", "mastery" }.Contains(options.ChampionUsageMode) ||
                !new[] { "friendly", "enemy", "all" }.Contains(options.Target) ||
                !new[] { "preferName", "preferChampionName", "championNameWithName" }.Contains(options.NameStrategy) ||
                !new[] { "preferName", "preferChampionName", "championNameWithName" }.Contains(options.PremadeNameStrategy) ||
                !new[] { "preferName", "preferChampionName", "championNameWithName" }.Contains(options.JungleNameStrategy)) return false;
            var bindings = new List<string>();
            foreach (var shortcut in new[] { options.FriendlyShortcut, options.EnemyShortcut, options.AllShortcut,
                options.JungleFriendlyShortcut, options.JungleEnemyShortcut, options.JungleAllShortcut,
                options.PremadeFriendlyShortcut, options.PremadeEnemyShortcut, options.PremadeAllShortcut })
            {
                LeagueHotkeyBinding binding;
                string error;
                if (shortcut != null && shortcut.Length > 100 || !LeagueHotkeyBinding.TryParse(shortcut, out binding, out error)) return false;
                if (binding.Enabled) bindings.Add(binding.ToString());
            }
            return bindings.Distinct(StringComparer.OrdinalIgnoreCase).Count() == bindings.Count;
        }
    }

    internal sealed class LeagueDynamicMessagePlayer
    {
        internal LeagueLivePlayerRow Player;
        internal LeaguePlayerMatchPage History;
        internal bool Friendly;
        internal int HistoricalTeamId;
        internal int HistoricalOriginalTeamId;
        internal int HistoricalOriginalChampionId;
        internal string HistoricalOriginalChampionName;
        internal string HistoricalOriginalPosition;
        internal string HistoricalPrimaryPosition = "";
        internal string HistoricalSecondaryPosition = "";
        internal string ChampionName;
        internal IReadOnlyList<LeagueJungleTimelineSample> JungleSamples;
        internal double? EarlyGankAverage;
        internal bool EarlyGankLoaded;
        internal bool? IsPrivate;
        internal bool PrivacyLoaded;
        internal LeaguePlayerMasteryDetails Mastery;
        internal readonly HashSet<string> AnalysisDetailAttempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class LeagueDynamicMessageSnapshot
    {
        internal readonly List<LeagueDynamicMessagePlayer> Players = new List<LeagueDynamicMessagePlayer>();
        internal string Phase;
        internal string HistoricalGameMode;
        internal int HistoryLoadCount = LeaguePlayerDataService.MaximumMatchCount;
        internal string HistoryQueueTag;
        internal string RosterKey;
        internal string SelfPuuid;
        internal LeagueClientSession Session;
        internal DateTime CreatedUtc;
        internal int UnavailableIdentityCount;
    }

    internal sealed class LeagueDynamicMessageService : IDisposable
    {
        private readonly ILeagueClientApi _client;
        private readonly LeaguePlayerDataService _players;
        private readonly LeagueLiveDataService _live;
        private readonly PerformanceBudgetProvider _budgets;
        private readonly Func<LeagueClientSession> _session;
        private readonly Func<LeagueDashboardPhaseState> _phase;
        private readonly object _sync = new object();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private const int MaximumJungleCachePlayers = 20;
        private static readonly TimeSpan JungleCacheLifetime = TimeSpan.FromMinutes(10);
        private readonly Dictionary<string, JungleCacheEntry> _jungleCache =
            new Dictionary<string, JungleCacheEntry>(StringComparer.Ordinal);
        private LeagueClientSession _jungleCacheSession;
        private string _jungleCacheSelfPuuid;
        private CancellationTokenSource _active;
        private bool _disposed;
        private string _observedPhase;
        private string _selectionKey;
        private HashSet<string> _selected;

        private sealed class JungleCacheEntry
        {
            internal string HistoryKey;
            internal DateTime LoadedUtc;
            internal IReadOnlyList<LeagueJungleTimelineSample> Samples;
        }

        internal LeagueDynamicMessageService(ILeagueClientApi client, PerformanceBudgetProvider budgets,
            Func<LeagueClientSession> session, Func<LeagueDashboardPhaseState> phase,
            ILeagueSgpMatchHistoryApi sgpHistory = null)
        {
            _client = client;
            _budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
            _players = new LeaguePlayerDataService(client, budgets, null, sgpHistory, session);
            _live = new LeagueLiveDataService(client, budgets);
            _session = session;
            _phase = phase;
            _observedPhase = phase()?.Phase;
        }

        internal static IReadOnlyList<LeagueDynamicMessagePlayer> OrderPlayers(
            LeagueDynamicMessageSnapshot snapshot, string mode, int premadeThreshold = 5)
        {
            if (snapshot == null) return Array.Empty<LeagueDynamicMessagePlayer>();
            var players = snapshot.Players.Where(p => p?.Player != null).ToArray();
            if (!new[] { "default", "win-rate", "kda", "akari-score", "position", "premade-team" }
                .Contains(mode)) return players;

            var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
            var groups = new Dictionary<string, int>(StringComparer.Ordinal);
            if (mode == "premade-team")
            {
                var inferred = LeaguePremadeInference.Infer(snapshot, premadeThreshold)
                    .OrderByDescending(g => g.Length)
                    .ThenBy(g => string.Join("|", g), StringComparer.Ordinal).ToArray();
                for (var i = 0; i < inferred.Length; i++)
                    foreach (var id in inferred[i])
                    {
                        sizes[id] = inferred[i].Length;
                        groups[id] = i;
                    }
            }
            Func<LeagueDynamicMessagePlayer, double?> metric = player =>
            {
                if (mode != "win-rate" && mode != "kda" && mode != "akari-score") return null;
                var history = LeagueHistoricalMatchDryRun.VerifiedRecentMatches(player);
                if (history.Count == 0) return null;
                var analysis = LeaguePlayerHistoryAnalysis.Calculate(history);
                if (mode == "win-rate")
                    return analysis.Wins + analysis.Losses > 0
                        ? (double?)analysis.Wins / (analysis.Wins + analysis.Losses) : null;
                if (mode == "kda")
                    return history.Any(m => m.Kills < 0 || m.Deaths < 0 || m.Assists < 0)
                        ? (double?)null
                        : history.Sum(m => (long)m.Kills + m.Assists) /
                            (double)Math.Max(1L, history.Sum(m => (long)m.Deaths));
                return analysis.AkariScore?.Total;
            };
            return players.Select((p, i) => new {
                    Player = p,
                    Index = i,
                    Position = Array.IndexOf(new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" },
                        (p.Player.Position ?? "").ToUpperInvariant()),
                    Metric = metric(p),
                    GroupSize = sizes.ContainsKey(p.Player.PuuId ?? "") ? sizes[p.Player.PuuId] : 0,
                    GroupOrder = groups.ContainsKey(p.Player.PuuId ?? "") ? groups[p.Player.PuuId] : int.MaxValue
                })
                .OrderByDescending(x => x.Player.Friendly)
                .ThenBy(x => mode == "position" ? (x.Position < 0 ? 5 : x.Position) : 0)
                .ThenByDescending(x => mode == "premade-team" ? x.GroupSize : 0)
                .ThenBy(x => mode == "premade-team" ? x.GroupOrder : 0)
                .ThenByDescending(x => x.Metric ?? double.NegativeInfinity)
                .ThenBy(x => x.Index)
                .Select(x => x.Player).ToArray();
        }

        internal static string ResolveHistoryQueueTag(string preference, int queueId)
        {
            if (preference == "all") return null;
            if (preference != "current" || queueId <= 0 || queueId > 100000)
                return string.Empty;
            var tag = "q_" + queueId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return LeagueSgpSupportedQueues.IsValidTag(tag) ? tag : string.Empty;
        }

        internal async Task<LeagueDynamicMessageSnapshot> LoadAsync(CancellationToken token,
            int historyLoadCount = LeaguePlayerDataService.MaximumMatchCount,
            string historyTagPreference = "all", bool queryInLobbyPhase = false,
            int historyConcurrency = 2)
        {
            if (historyConcurrency < 1 || historyConcurrency > 4 ||
                historyLoadCount < 2 ||
                historyLoadCount > LeaguePlayerDataService.MaximumMatchCount ||
                (historyTagPreference != "all" && historyTagPreference != "current")) return null;
            var phase = _phase();
            var session = _session();
            if (phase == null || !phase.Connected || session == null ||
                (phase.Phase != "ChampSelect" && phase.Phase != "InProgress" &&
                 !(queryInLobbyPhase && phase.Phase == "Lobby"))) return null;
            CancellationTokenSource active;
            lock (_sync)
            {
                if (_disposed) return null;
                _active?.Cancel();
                active = CancellationTokenSource.CreateLinkedTokenSource(token);
                _active = active;
            }
            var entered = false;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            try
            {
                active.CancelAfter(TimeSpan.FromSeconds(15));
                await _gate.WaitAsync(active.Token).ConfigureAwait(false);
                entered = true;
                var me = await _players.LoadProfileAsync(true, active.Token).ConfigureAwait(false);
                if (me == null || string.IsNullOrEmpty(me.PuuId) || !Current(session, phase.Phase)) return null;
                SetJungleCacheScope(session, me.PuuId);
                var roster = await ReadRosterAsync(phase.Phase, active.Token).ConfigureAwait(false);
                var own = roster.Players.FirstOrDefault(p => p.PuuId == me.PuuId);
                if (own == null || roster.Players.Count > 20 || roster.Players.Where(p => SafePuuid(p.PuuId)).GroupBy(p => p.PuuId).Any(g => g.Count() != 1)) return null;
                var historyQueueTag = ResolveHistoryQueueTag(historyTagPreference, roster.QueueId);
                if (historyQueueTag == string.Empty) return null;
                var snapshot = new LeagueDynamicMessageSnapshot { Phase = phase.Phase, Session = session,
                    HistoryQueueTag = historyQueueTag,
                    SelfPuuid = me.PuuId, RosterKey = RosterKey(roster),
                    HistoryLoadCount = historyLoadCount, CreatedUtc = DateTime.UtcNow,
                    UnavailableIdentityCount = roster.Players.Count(p => !SafePuuid(p.PuuId)) };
                foreach (var row in roster.Players.Where(p => SafePuuid(p.PuuId)).GroupBy(p => p.PuuId).Select(g => g.First()).Take(20))
                    snapshot.Players.Add(new LeagueDynamicMessagePlayer { Player = row, Friendly = row.Side == own.Side });
                var effectiveConcurrency = Math.Min(historyConcurrency, _budgets.Current.NetworkConcurrency);
                Func<LeagueDynamicMessagePlayer, CancellationToken, Task> loadOne = async (player, ct) =>
                {
                    var row = player.Player;
                    var profile = new LeaguePlayerProfile { PuuId = row.PuuId, SummonerId = row.SummonerId };
                    player.History = historyQueueTag != null && effectiveConcurrency > 1
                        ? await _players.LoadTaggedRosterHistoryAsync(profile, 0, historyLoadCount,
                            historyQueueTag, me.PuuId, ct).ConfigureAwait(false)
                        : await _players.LoadRecentMatchesAsync(profile, 0, historyLoadCount,
                            false, ct, historyQueueTag, me.PuuId).ConfigureAwait(false);
                    if (player.History != null && player.History.Matches.Count > historyLoadCount)
                        player.History.Matches.RemoveRange(historyLoadCount,
                            player.History.Matches.Count - historyLoadCount);
                    if (player.History != null)
                        player.History = await _players.EnrichChampionNamesAsync(profile,
                            player.History, ct, historyQueueTag == null).ConfigureAwait(false);
                    player.ChampionName = player.History?.Matches.FirstOrDefault(
                        m => m.ChampionId == row.ChampionId)?.ChampionName;
                    player.JungleSamples = CachedJungle(row.PuuId, player.History);
                };
                if (historyQueueTag != null && effectiveConcurrency > 1)
                {
                    using (var slots = new SemaphoreSlim(effectiveConcurrency, effectiveConcurrency))
                    {
                        var jobs = snapshot.Players.Select(async player =>
                        {
                            await slots.WaitAsync(active.Token).ConfigureAwait(false);
                            try
                            {
                                if (!Current(session, phase.Phase)) return;
                                var remaining = deadline - DateTime.UtcNow - TimeSpan.FromSeconds(4);
                                if (remaining <= TimeSpan.Zero) return;
                                using (var playerTimeout = CancellationTokenSource.CreateLinkedTokenSource(active.Token))
                                {
                                    playerTimeout.CancelAfter(remaining < TimeSpan.FromSeconds(8)
                                        ? remaining : TimeSpan.FromSeconds(8));
                                    try { await loadOne(player, playerTimeout.Token).ConfigureAwait(false); }
                                    catch (OperationCanceledException) when (!active.IsCancellationRequested) { }
                                }
                            }
                            finally { slots.Release(); }
                        }).ToArray();
                        await Task.WhenAll(jobs).ConfigureAwait(false);
                    }
                }
                else
                {
                    foreach (var player in snapshot.Players)
                    {
                        if (!Current(session, phase.Phase)) return null;
                        var remaining = deadline - DateTime.UtcNow - TimeSpan.FromSeconds(4);
                        if (remaining <= TimeSpan.Zero) break;
                        using (var playerTimeout = CancellationTokenSource.CreateLinkedTokenSource(active.Token))
                        {
                            playerTimeout.CancelAfter(remaining < TimeSpan.FromSeconds(2)
                                ? remaining : TimeSpan.FromSeconds(2));
                            try { await loadOne(player, playerTimeout.Token).ConfigureAwait(false); }
                            catch (OperationCanceledException) when (!active.IsCancellationRequested) { }
                        }
                    }
                }
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return null;
                active.Token.ThrowIfCancellationRequested();
                return snapshot;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
            finally
            {
                if (entered) _gate.Release();
                lock (_sync) if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        internal async Task<bool> IsCurrentAsync(LeagueDynamicMessageSnapshot snapshot, CancellationToken token)
        {
            if (snapshot == null || DateTime.UtcNow - snapshot.CreatedUtc > TimeSpan.FromMinutes(2) ||
                !Current(snapshot.Session, snapshot.Phase)) return false;
            var roster = await ReadRosterAsync(snapshot.Phase, token).ConfigureAwait(false);
            var me = await _players.LoadProfileAsync(true, token).ConfigureAwait(false);
            return Current(snapshot.Session, snapshot.Phase) && me != null && me.PuuId == snapshot.SelfPuuid &&
                snapshot.RosterKey == RosterKey(roster);
        }

        // An explicit player-card action can browse at most 200 source matches in 20-row
        // pages. The normal roster preview remains bounded to its configured 2-20 rows.
        internal async Task<IReadOnlyList<LeaguePlayerMatchSummary>> LoadHistoryPageAsync(
            LeagueDynamicMessageSnapshot snapshot, string puuid, int startIndex, CancellationToken token)
        {
            if (snapshot == null || !SafePuuid(puuid) ||
                startIndex < snapshot.HistoryLoadCount || startIndex >= 200 ||
                (startIndex - snapshot.HistoryLoadCount) % 20 != 0) return null;
            var matches = snapshot.Players.Where(p => p?.Player != null &&
                string.Equals(p.Player.PuuId, puuid, StringComparison.Ordinal)).Take(2).ToArray();
            if (matches.Length != 1 || matches[0].History == null) return null;
            CancellationTokenSource active;
            lock (_sync)
            {
                if (_disposed) return null;
                _active?.Cancel();
                active = CancellationTokenSource.CreateLinkedTokenSource(token);
                _active = active;
            }
            var entered = false;
            try
            {
                active.CancelAfter(TimeSpan.FromSeconds(14));
                await _gate.WaitAsync(active.Token).ConfigureAwait(false);
                entered = true;
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return null;
                var count = Math.Min(20, 200 - startIndex);
                var profile = new LeaguePlayerProfile { PuuId = puuid,
                    SummonerId = matches[0].Player.SummonerId };
                var page = await _players.LoadRecentMatchesAsync(profile, startIndex, count,
                    true, active.Token, snapshot.HistoryQueueTag, snapshot.SelfPuuid)
                    .ConfigureAwait(false);
                if (page == null || page.Matches.Count > count ||
                    !await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return null;
                page = await _players.EnrichChampionNamesAsync(profile, page, active.Token,
                    snapshot.HistoryQueueTag == null).ConfigureAwait(false);
                if (page == null || !await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false))
                    return null;
                var rows = LeagueHistoricalMatchDryRun.CardRecentMatches(
                    new LeagueDynamicMessagePlayer { Player = matches[0].Player, History = page });
                var original = matches[0].History.Matches.FirstOrDefault(m =>
                    m != null && m.DataSource != LeaguePlayerHistorySource.Automatic);
                if (original != null && rows.Any(m => m.DataSource != original.DataSource ||
                    !string.Equals(m.SgpServerId ?? "", original.SgpServerId ?? "",
                        StringComparison.OrdinalIgnoreCase))) return null;
                active.Token.ThrowIfCancellationRequested();
                return rows;
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return null; }
            finally
            {
                if (entered) _gate.Release();
                lock (_sync) if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        // Explicitly enrich the currently displayed player's sparse initial history.
        // This reuses the account-fenced detail hydration owner and never prefetches in the background.
        internal async Task<bool> HydrateHistoryAsync(
            LeagueDynamicMessageSnapshot snapshot, string puuid, CancellationToken token,
            int detailLoadCount = 20)
        {
            if (snapshot == null || !SafePuuid(puuid) || detailLoadCount < 1 || detailLoadCount > 20)
                return false;
            var matches = snapshot.Players.Where(p => p?.Player != null &&
                string.Equals(p.Player.PuuId, puuid, StringComparison.Ordinal)).Take(2).ToArray();
            if (matches.Length != 1 || matches[0].History == null ||
                matches[0].History.Matches.Count == 0 ||
                matches[0].History.Matches.Count > LeaguePlayerDataService.MaximumMatchCount)
                return false;
            CancellationTokenSource active;
            lock (_sync)
            {
                if (_disposed) return false;
                _active?.Cancel();
                active = CancellationTokenSource.CreateLinkedTokenSource(token);
                _active = active;
            }
            var entered = false;
            try
            {
                active.CancelAfter(TimeSpan.FromSeconds(29));
                await _gate.WaitAsync(active.Token).ConfigureAwait(false);
                entered = true;
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return false;
                var player = matches[0];
                var result = await _players.HydrateAnalysisAsync(snapshot.SelfPuuid, puuid,
                    snapshot.Session, player.History.Matches, player.AnalysisDetailAttempted,
                    active.Token, detailLoadCount).ConfigureAwait(false);
                if (result == null || result.AccountChanged ||
                    result.Matches.Count != player.History.Matches.Count ||
                    !await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return false;
                active.Token.ThrowIfCancellationRequested();
                foreach (var key in result.Attempted) player.AnalysisDetailAttempted.Add(key);
                if (result.Updated <= 0) return false;
                player.History.Matches.Clear();
                player.History.Matches.AddRange(result.Matches);
                player.JungleSamples = null;
                return true;
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return false; }
            finally
            {
                if (entered) _gate.Release();
                lock (_sync) if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        // Mastery is read only on a player's explicit request through the existing LCU owner.
        internal async Task<bool> LoadMasteryAsync(LeagueDynamicMessageSnapshot snapshot,
            string puuid, CancellationToken token)
        {
            if (snapshot == null || !SafePuuid(puuid)) return false;
            var matching = snapshot.Players.Where(p => p?.Player != null &&
                string.Equals(p.Player.PuuId, puuid, StringComparison.Ordinal)).Take(2).ToArray();
            if (matching.Length != 1) return false;
            var player = matching[0];
            if (player.Mastery != null)
                return await IsCurrentAsync(snapshot, token).ConfigureAwait(false);
            CancellationTokenSource active;
            lock (_sync)
            {
                if (_disposed) return false;
                _active?.Cancel();
                active = CancellationTokenSource.CreateLinkedTokenSource(token);
                _active = active;
            }
            var entered = false;
            try
            {
                active.CancelAfter(TimeSpan.FromSeconds(16));
                await _gate.WaitAsync(active.Token).ConfigureAwait(false);
                entered = true;
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return false;
                var result = await _players.LoadPlayerMasteriesAsync(snapshot.SelfPuuid,
                    puuid, snapshot.Session, null, active.Token, true).ConfigureAwait(false);
                if (result.Status != LeaguePlayerDetailsStatus.Ready ||
                    result.Masteries == null || result.Masteries.Count > 8 ||
                    !await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false))
                    return false;
                active.Token.ThrowIfCancellationRequested();
                player.Mastery = result;
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception) { return false; }
            finally
            {
                if (entered) _gate.Release();
                lock (_sync) if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        // Explicit one-player, phase-bound analysis. No background or periodic timeline requests.
        // A single explicit LCU summoner lookup; never infer privacy from a missing profile.
        internal async Task<bool> LoadPrivacyAsync(LeagueDynamicMessageSnapshot snapshot,
            string puuid, CancellationToken token)
        {
            if (snapshot == null || !SafePuuid(puuid)) return false;
            var player = snapshot.Players.SingleOrDefault(p => p.Player != null &&
                string.Equals(p.Player.PuuId, puuid, StringComparison.Ordinal));
            if (player == null) return false;
            if (player.PrivacyLoaded && player.IsPrivate.HasValue)
                return await IsCurrentAsync(snapshot, token).ConfigureAwait(false);
            CancellationTokenSource active;
            lock (_sync)
            {
                if (_disposed) return false;
                _active?.Cancel();
                active = CancellationTokenSource.CreateLinkedTokenSource(token);
                _active = active;
            }
            var entered = false;
            try
            {
                active.CancelAfter(TimeSpan.FromSeconds(8));
                await _gate.WaitAsync(active.Token).ConfigureAwait(false);
                entered = true;
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return false;
                var bytes = await _client.TryGetBytesAsync(
                    "/lol-summoner/v2/summoners/puuid/" + Uri.EscapeDataString(puuid),
                    active.Token).ConfigureAwait(false);
                var privacy = ParsePrivacy(bytes, puuid);
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return false;
                active.Token.ThrowIfCancellationRequested();
                player.IsPrivate = privacy;
                player.PrivacyLoaded = true;
                return privacy.HasValue;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception) { return false; }
            finally
            {
                if (entered) _gate.Release();
                lock (_sync) if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        internal static bool? ParsePrivacy(byte[] bytes, string puuid)
        {
            if (!SafePuuid(puuid) || bytes == null || bytes.Length == 0 || bytes.Length > 65536)
                return null;
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 65536 }
                    .DeserializeObject(System.Text.Encoding.UTF8.GetString(bytes))
                    as Dictionary<string, object>;
                object identity, value;
                if (root == null || !root.TryGetValue("puuid", out identity) ||
                    !string.Equals(identity as string, puuid, StringComparison.Ordinal) ||
                    !root.TryGetValue("privacy", out value)) return null;
                var privacy = value as string;
                return privacy == "PRIVATE" ? true : privacy == "PUBLIC" ? (bool?)false : null;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        internal async Task<bool> LoadEarlyGankAsync(LeagueDynamicMessageSnapshot snapshot,
            string puuid, CancellationToken token)
        {
            if (snapshot == null || !SafePuuid(puuid)) return false;
            var player = snapshot.Players.SingleOrDefault(p => p.Player != null &&
                string.Equals(p.Player.PuuId, puuid, StringComparison.Ordinal));
            if (player == null) return false;
            if (player.EarlyGankLoaded) return await IsCurrentAsync(snapshot, token).ConfigureAwait(false);
            CancellationTokenSource active;
            lock (_sync)
            {
                if (_disposed) return false;
                _active?.Cancel();
                active = CancellationTokenSource.CreateLinkedTokenSource(token);
                _active = active;
            }
            var entered = false;
            try
            {
                active.CancelAfter(TimeSpan.FromSeconds(55));
                await _gate.WaitAsync(active.Token).ConfigureAwait(false);
                entered = true;
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false) ||
                    !await EnsureHistoryAsync(snapshot, player, active.Token).ConfigureAwait(false)) return false;
                var row = player.Player;
                if (string.Equals(row.Position, "JUNGLE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(row.Role, "JUNGLE", StringComparison.OrdinalIgnoreCase) ||
                    row.Spell1Id == 11 || row.Spell2Id == 11)
                {
                    player.EarlyGankLoaded = true;
                    return await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false);
                }
                var samples = new List<int>();
                var profile = new LeaguePlayerProfile { PuuId = puuid, SummonerId = row.SummonerId };
                foreach (var item in player.History.Matches.Where(m => m != null && m.GameId > 0)
                    .GroupBy(m => m.GameId).Select(g => g.First()).Take(6))
                {
                    active.Token.ThrowIfCancellationRequested();
                    if (!Current(snapshot.Session, snapshot.Phase)) return false;
                    LeaguePlayerMatchSummary match;
                    using (var detailDeadline = CancellationTokenSource.CreateLinkedTokenSource(active.Token))
                    {
                        detailDeadline.CancelAfter(TimeSpan.FromSeconds(4));
                        try { match = await _players.LoadMatchDetailAsync(profile, item.GameId,
                            detailDeadline.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!active.IsCancellationRequested) { continue; }
                    }
                    if (match == null || match.GameId != item.GameId ||
                        !string.Equals(match.PuuId, puuid, StringComparison.Ordinal) ||
                        LeagueEarlyJungleDeathAnalysis.Candidates(new[] { match }, puuid).Count == 0)
                        continue;
                    using (var timelineDeadline = CancellationTokenSource.CreateLinkedTokenSource(active.Token))
                    {
                        timelineDeadline.CancelAfter(TimeSpan.FromSeconds(4));
                        try
                        {
                            var timeline = await _players.LoadMatchTimelineAsync(snapshot.SelfPuuid,
                                match, timelineDeadline.Token, snapshot.Session).ConfigureAwait(false);
                            var deaths = LeagueEarlyJungleDeathAnalysis.Calculate(timeline, match);
                            if (deaths.HasValue) samples.Add(deaths.Value);
                        }
                        catch (OperationCanceledException) when (!active.IsCancellationRequested) { }
                    }
                }
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return false;
                active.Token.ThrowIfCancellationRequested();
                player.EarlyGankAverage = samples.Count == 0 ? (double?)null : samples.Average();
                player.EarlyGankLoaded = true;
                return true;
            }
            catch (OperationCanceledException) { return false; }
            finally
            {
                if (entered) _gate.Release();
                lock (_sync) if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        internal async Task<bool> LoadJungleAsync(LeagueDynamicMessageSnapshot snapshot, string puuid, CancellationToken token)
        {
            if (snapshot == null || !SafePuuid(puuid)) return false;
            var player = snapshot.Players.SingleOrDefault(p => p.Player != null && p.Player.PuuId == puuid);
            if (player == null) return false;
            if (player.JungleSamples != null) return await IsCurrentAsync(snapshot, token).ConfigureAwait(false);
            CancellationTokenSource active;
            lock (_sync)
            {
                if (_disposed) return false;
                _active?.Cancel();
                active = CancellationTokenSource.CreateLinkedTokenSource(token);
                _active = active;
            }
            var entered = false;
            try
            {
                active.CancelAfter(TimeSpan.FromSeconds(65));
                await _gate.WaitAsync(active.Token).ConfigureAwait(false);
                entered = true;
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return false;
                if (!await EnsureHistoryAsync(snapshot, player, active.Token).ConfigureAwait(false)) return false;
                if (player.JungleSamples != null)
                    return await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false);
                if (player.History.Matches.Count == 0)
                {
                    player.JungleSamples = Array.Empty<LeagueJungleTimelineSample>();
                    return await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false);
                }
                player.JungleSamples = CachedJungle(puuid, player.History);
                if (player.JungleSamples != null) return true;
                var samples = new List<LeagueJungleTimelineSample>();
                var historyProfile = new LeaguePlayerProfile { PuuId = puuid, SummonerId = player.Player.SummonerId };
                foreach (var item in player.History.Matches.Where(m => m != null && m.GameId > 0)
                    .GroupBy(m => m.GameId).Select(g => g.First()).Take(LeaguePlayerDataService.MaximumMatchCount))
                {
                    active.Token.ThrowIfCancellationRequested();
                    if (!Current(snapshot.Session, snapshot.Phase)) return false;
                    if (item.MapId != 0 && item.MapId != 11) continue;
                    var match = item;
                    if (!match.ParticipantResolved || match.ParticipantId < 1 || match.TeamId == 0 || match.MapId == 0)
                    {
                        using (var detailDeadline = CancellationTokenSource.CreateLinkedTokenSource(active.Token))
                        {
                            detailDeadline.CancelAfter(TimeSpan.FromSeconds(3));
                            try { match = await _players.LoadMatchDetailAsync(historyProfile, item.GameId, detailDeadline.Token).ConfigureAwait(false); }
                            catch (OperationCanceledException) when (!active.IsCancellationRequested) { continue; }
                        }
                    }
                    if (match == null || match.MapId != 11 || match.ParticipantId < 1 ||
                        !(match.HasSmite || string.Equals(match.Position, "JUNGLE", StringComparison.OrdinalIgnoreCase))) continue;
                    using (var timelineDeadline = CancellationTokenSource.CreateLinkedTokenSource(active.Token))
                    {
                        timelineDeadline.CancelAfter(TimeSpan.FromSeconds(3));
                        try
                        {
                            var data = await _client.TryGetBytesAsync(
                                "/lol-match-history/v1/game-timelines/" + match.GameId, timelineDeadline.Token).ConfigureAwait(false);
                            var parsed = LeagueJungleTimelineAnalysis.Parse(data, match.GameId, match.ChampionId,
                                match.ParticipantId, match.TeamId, match.MapId, true);
                            if (parsed != null) samples.Add(parsed);
                        }
                        catch (OperationCanceledException) when (!active.IsCancellationRequested) { }
                    }
                }
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return false;
                active.Token.ThrowIfCancellationRequested();
                player.JungleSamples = samples.ToArray();
                CacheJungle(puuid, player.History, player.JungleSamples);
                return true;
            }
            catch (OperationCanceledException) { return false; }
            finally
            {
                if (entered) _gate.Release();
                lock (_sync) if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        // The initial snapshot has a short total deadline. A user-triggered batch may
        // complete history for the remaining roster members without a second observer.
        internal async Task<bool> EnsureHistoriesAsync(LeagueDynamicMessageSnapshot snapshot,
            ISet<string> selected, CancellationToken token)
        {
            if (snapshot == null || selected == null || selected.Count == 0) return false;
            CancellationTokenSource active;
            lock (_sync)
            {
                if (_disposed) return false;
                _active?.Cancel();
                active = CancellationTokenSource.CreateLinkedTokenSource(token);
                _active = active;
            }
            var entered = false;
            try
            {
                await _gate.WaitAsync(active.Token).ConfigureAwait(false);
                entered = true;
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return false;
                foreach (var player in snapshot.Players.Where(p => p.Player != null && selected.Contains(p.Player.PuuId)))
                {
                    active.Token.ThrowIfCancellationRequested();
                    if (!await EnsureHistoryAsync(snapshot, player, active.Token).ConfigureAwait(false)) return false;
                }
                return await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return false; }
            finally
            {
                if (entered) _gate.Release();
                lock (_sync) if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        private async Task<bool> EnsureHistoryAsync(LeagueDynamicMessageSnapshot snapshot,
            LeagueDynamicMessagePlayer player, CancellationToken token)
        {
            if (player.History != null) return true;
            if (!Current(snapshot.Session, snapshot.Phase)) return false;
            var profile = new LeaguePlayerProfile { PuuId = player.Player.PuuId,
                SummonerId = player.Player.SummonerId };
            var count = snapshot.HistoryLoadCount;
            if (count < 2 || count > LeaguePlayerDataService.MaximumMatchCount) return false;
            var history = await _players.LoadRecentMatchesAsync(profile, 0,
                count, false, token, snapshot.HistoryQueueTag, snapshot.SelfPuuid).ConfigureAwait(false);
            if (history == null || !Current(snapshot.Session, snapshot.Phase)) return false;
            if (history.Matches.Count > count)
                history.Matches.RemoveRange(count, history.Matches.Count - count);
            history = await _players.EnrichChampionNamesAsync(profile, history, token,
                snapshot.HistoryQueueTag == null).ConfigureAwait(false);
            if (!Current(snapshot.Session, snapshot.Phase)) return false;
            player.History = history;
            player.ChampionName = history.Matches.FirstOrDefault(m => m.ChampionId == player.Player.ChampionId)?.ChampionName;
            player.JungleSamples = CachedJungle(player.Player.PuuId, history);
            return true;
        }

        private bool Current(LeagueClientSession session, string phase)
        {
            lock (_sync) if (_disposed) return false;
            var current = _phase();
            return current != null && current.Connected && current.Phase == phase && session.Matches(_session());
        }

        private void SetJungleCacheScope(LeagueClientSession session, string selfPuuid)
        {
            lock (_sync)
            {
                if (_jungleCacheSession != null && _jungleCacheSession.Matches(session) &&
                    string.Equals(_jungleCacheSelfPuuid, selfPuuid, StringComparison.Ordinal)) return;
                _jungleCache.Clear();
                _jungleCacheSession = session;
                _jungleCacheSelfPuuid = selfPuuid;
            }
        }

        private static string JungleHistoryKey(LeaguePlayerMatchPage history)
        {
            if (history == null) return null;
            return string.Join("|", history.Matches.Where(m => m != null && m.GameId > 0)
                .Select(m => m.GameId.ToString() + ":" + m.ChampionId.ToString())
                .Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal)
                .Take(LeaguePlayerDataService.MaximumMatchCount));
        }

        private IReadOnlyList<LeagueJungleTimelineSample> CachedJungle(string puuid, LeaguePlayerMatchPage history)
        {
            var historyKey = JungleHistoryKey(history);
            if (historyKey == null) return null;
            lock (_sync)
            {
                JungleCacheEntry entry;
                if (!_jungleCache.TryGetValue(puuid, out entry)) return null;
                if (DateTime.UtcNow - entry.LoadedUtc > JungleCacheLifetime ||
                    !string.Equals(entry.HistoryKey, historyKey, StringComparison.Ordinal))
                {
                    _jungleCache.Remove(puuid);
                    return null;
                }
                return entry.Samples;
            }
        }

        private void CacheJungle(string puuid, LeaguePlayerMatchPage history, IReadOnlyList<LeagueJungleTimelineSample> samples)
        {
            if (samples == null || samples.Count == 0 || samples.Count > LeaguePlayerDataService.MaximumMatchCount) return;
            var historyKey = JungleHistoryKey(history);
            if (historyKey == null) return;
            lock (_sync)
            {
                if (_disposed) return;
                if (_jungleCache.Count >= MaximumJungleCachePlayers && !_jungleCache.ContainsKey(puuid))
                {
                    var oldest = _jungleCache.OrderBy(entry => entry.Value.LoadedUtc).First().Key;
                    _jungleCache.Remove(oldest);
                }
                _jungleCache[puuid] = new JungleCacheEntry
                {
                    HistoryKey = historyKey,
                    LoadedUtc = DateTime.UtcNow,
                    Samples = samples.ToArray()
                };
            }
        }

        private async Task<LeagueLiveSnapshot> ReadRosterAsync(string phase, CancellationToken token)
        {
            var roster = new LeagueLiveSnapshot();
            var bytes = await _client.TryGetBytesAsync(phase == "ChampSelect" ? LeagueLiveDataService.ChampSelectSessionPath :
                phase == "Lobby" ? LeagueLiveDataService.LobbyPath : LeagueLiveDataService.GameflowSessionPath,
                token).ConfigureAwait(false);
            if (phase == "ChampSelect") _live.ApplyChampSelect(roster, bytes);
            else if (phase == "Lobby") _live.ApplyLobby(roster, bytes);
            else _live.ApplyCurrentGame(roster, bytes);
            return roster;
        }

        internal static string RosterKey(LeagueLiveSnapshot roster)
        {
            return roster.GameId + ":" + roster.QueueId + "|" + string.Join("|", roster.Players.OrderBy(p => p.PuuId, StringComparer.Ordinal)
                .Select(p => p.PuuId + ":" + p.Side + ":" + p.ChampionId));
        }

        internal static bool SafePuuid(string puuid)
        {
            return !string.IsNullOrEmpty(puuid) && puuid.Length <= 128 &&
                puuid.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
        }

        internal void Cancel()
        {
            lock (_sync) _active?.Cancel();
        }

        internal void Observe(LeagueDashboardPhaseState state)
        {
            var phase = state != null && state.Connected ? state.Phase : null;
            lock (_sync)
            {
                if (_observedPhase == phase) return;
                _observedPhase = phase;
                _selectionKey = null;
                _selected = null;
                _active?.Cancel();
            }
        }

        internal void Select(LeagueDynamicMessageSnapshot snapshot, IEnumerable<string> selected)
        {
            if (snapshot == null) return;
            var allowed = new HashSet<string>(snapshot.Players.Select(p => p.Player.PuuId), StringComparer.Ordinal);
            lock (_sync)
            {
                _selectionKey = snapshot.Phase + ":" + snapshot.RosterKey;
                _selected = new HashSet<string>((selected ?? Array.Empty<string>()).Where(allowed.Contains).Take(20), StringComparer.Ordinal);
            }
        }

        internal ISet<string> Selection(LeagueDynamicMessageSnapshot snapshot)
        {
            lock (_sync)
                return snapshot != null && _selectionKey == snapshot.Phase + ":" + snapshot.RosterKey && _selected != null
                    ? new HashSet<string>(_selected, StringComparer.Ordinal) : null;
        }

        internal static ISet<string> TargetPlayers(LeagueDynamicMessageSnapshot snapshot, string target)
        {
            if (snapshot == null || (target != "friendly" && target != "enemy" && target != "all"))
                return new HashSet<string>(StringComparer.Ordinal);
            return new HashSet<string>(snapshot.Players.Where(p => p.Player != null &&
                (target == "all" || target == "friendly" && p.Friendly || target == "enemy" && !p.Friendly))
                .Select(p => p.Player.PuuId).Where(SafePuuid), StringComparer.Ordinal);
        }

        internal static bool TargetNeedsHistory(LeagueDynamicMessageSnapshot snapshot, string target)
        {
            return SelectedNeedsHistory(snapshot, TargetPlayers(snapshot, target));
        }

        internal static bool SelectedNeedsHistory(LeagueDynamicMessageSnapshot snapshot, ISet<string> selected)
        {
            return snapshot != null && selected != null && snapshot.Players.Any(p => p.Player != null &&
                selected.Contains(p.Player.PuuId) && p.History == null);
        }

        internal static bool JunglePrefetchEnabled(LeagueDynamicMessageOptions options)
        {
            return options != null && (!string.IsNullOrEmpty(options.JungleFriendlyShortcut) ||
                !string.IsNullOrEmpty(options.JungleEnemyShortcut) || !string.IsNullOrEmpty(options.JungleAllShortcut));
        }

        internal static IReadOnlyList<LeagueDynamicMessagePlayer> JunglePrefetchOrder(LeagueDynamicMessageSnapshot snapshot)
        {
            return snapshot == null ? Array.Empty<LeagueDynamicMessagePlayer>() : snapshot.Players
                .Where(p => p != null && p.Player != null && SafePuuid(p.Player.PuuId))
                .OrderByDescending(p => string.Equals(p.Player.Position, "JUNGLE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Player.Role, "JUNGLE", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => p.History != null)
                .ThenByDescending(p => p.Friendly).ToArray();
        }

        internal async Task<bool> SendAsync(LeagueDynamicMessageSnapshot snapshot, IReadOnlyList<string> lines, int interval,
            Func<string, CancellationToken, Task<bool>> sendLine, CancellationToken token)
        {
            if (snapshot == null || snapshot.Phase == "Lobby" ||
                lines == null || lines.Count == 0 || lines.Count > 100 || interval < 0 || interval > 5000 ||
                lines.Any(line => string.IsNullOrEmpty(line) || line.Length > LeagueQuickMessagePresets.MaximumMessageLength || line.Any(char.IsControl))) return false;
            CancellationTokenSource active;
            lock (_sync)
            {
                if (_disposed) return false;
                _active?.Cancel();
                active = CancellationTokenSource.CreateLinkedTokenSource(token);
                _active = active;
            }
            var entered = false;
            try
            {
                active.CancelAfter(TimeSpan.FromMilliseconds(Math.Min(600000, lines.Count * (interval + 1000L) + 5000)));
                await _gate.WaitAsync(active.Token).ConfigureAwait(false);
                entered = true;
                if (!await IsCurrentAsync(snapshot, active.Token).ConfigureAwait(false)) return false;
                for (var index = 0; index < lines.Count; index++)
                {
                    active.Token.ThrowIfCancellationRequested();
                    if (!Current(snapshot.Session, snapshot.Phase) || !await sendLine(lines[index], active.Token).ConfigureAwait(false)) return false;
                    if (index + 1 < lines.Count) await Task.Delay(interval, active.Token).ConfigureAwait(false);
                }
                return true;
            }
            catch (OperationCanceledException) { return false; }
            finally
            {
                if (entered) _gate.Release();
                lock (_sync) if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                _active?.Cancel();
                _jungleCache.Clear();
                _jungleCacheSession = null;
                _jungleCacheSelfPuuid = null;
            }
        }
    }
}
