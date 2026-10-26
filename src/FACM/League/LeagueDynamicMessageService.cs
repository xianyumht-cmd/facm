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
            return options != null && new[] { "friendly", "enemy", "all" }.Contains(options.Target) &&
                new[] { "preferName", "preferChampionName", "championNameWithName" }.Contains(options.NameStrategy);
        }
    }

    internal sealed class LeagueDynamicMessagePlayer
    {
        internal LeagueLivePlayerRow Player;
        internal LeaguePlayerMatchPage History;
        internal bool Friendly;
        internal string ChampionName;
    }

    internal sealed class LeagueDynamicMessageSnapshot
    {
        internal readonly List<LeagueDynamicMessagePlayer> Players = new List<LeagueDynamicMessagePlayer>();
        internal string Phase;
        internal string RosterKey;
        internal string SelfPuuid;
        internal LeagueClientSession Session;
        internal DateTime CreatedUtc;
    }

    internal sealed class LeagueDynamicMessageService : IDisposable
    {
        private readonly ILeagueClientApi _client;
        private readonly LeaguePlayerDataService _players;
        private readonly LeagueLiveDataService _live;
        private readonly Func<LeagueClientSession> _session;
        private readonly Func<LeagueDashboardPhaseState> _phase;
        private readonly object _sync = new object();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _active;
        private bool _disposed;

        internal LeagueDynamicMessageService(ILeagueClientApi client, PerformanceBudgetProvider budgets,
            Func<LeagueClientSession> session, Func<LeagueDashboardPhaseState> phase)
        {
            _client = client;
            _players = new LeaguePlayerDataService(client, budgets);
            _live = new LeagueLiveDataService(client, budgets);
            _session = session;
            _phase = phase;
        }

        internal async Task<LeagueDynamicMessageSnapshot> LoadAsync(CancellationToken token)
        {
            var phase = _phase();
            var session = _session();
            if (phase == null || !phase.Connected || (phase.Phase != "ChampSelect" && phase.Phase != "InProgress") || session == null) return null;
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
                active.CancelAfter(TimeSpan.FromSeconds(15));
                await _gate.WaitAsync(active.Token).ConfigureAwait(false);
                entered = true;
                var me = await _players.LoadProfileAsync(true, active.Token).ConfigureAwait(false);
                if (me == null || string.IsNullOrEmpty(me.PuuId) || !Current(session, phase.Phase)) return null;
                var roster = await ReadRosterAsync(phase.Phase, active.Token).ConfigureAwait(false);
                var own = roster.Players.FirstOrDefault(p => p.PuuId == me.PuuId);
                if (own == null || roster.Players.Count > 20 || roster.Players.Where(p => SafePuuid(p.PuuId)).GroupBy(p => p.PuuId).Any(g => g.Count() != 1)) return null;
                var snapshot = new LeagueDynamicMessageSnapshot { Phase = phase.Phase, Session = session,
                    SelfPuuid = me.PuuId, RosterKey = RosterKey(roster), CreatedUtc = DateTime.UtcNow };
                foreach (var row in roster.Players.Where(p => SafePuuid(p.PuuId)).GroupBy(p => p.PuuId).Select(g => g.First()).Take(20))
                {
                    var player = new LeagueDynamicMessagePlayer { Player = row, Friendly = row.Side == own.Side };
                    snapshot.Players.Add(player);
                    if (!Current(session, phase.Phase)) return null;
                    try
                    {
                        player.History = await _players.LoadRecentMatchesAsync(new LeaguePlayerProfile { PuuId = row.PuuId,
                            SummonerId = row.SummonerId }, 0, LeaguePlayerDataService.MaximumMatchCount, false, active.Token).ConfigureAwait(false);
                        if (player.History != null && player.History.Matches.Count > LeaguePlayerDataService.MaximumMatchCount)
                            player.History.Matches.RemoveRange(LeaguePlayerDataService.MaximumMatchCount,
                                player.History.Matches.Count - LeaguePlayerDataService.MaximumMatchCount);
                        if (player.History != null)
                            player.History = await _players.EnrichChampionNamesAsync(new LeaguePlayerProfile { PuuId = row.PuuId },
                                player.History, active.Token).ConfigureAwait(false);
                        player.ChampionName = player.History?.Matches.FirstOrDefault(m => m.ChampionId == row.ChampionId)?.ChampionName;
                    }
                    catch (OperationCanceledException) when (!active.IsCancellationRequested) { }
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

        private bool Current(LeagueClientSession session, string phase)
        {
            lock (_sync) if (_disposed) return false;
            var current = _phase();
            return current != null && current.Connected && current.Phase == phase && session.Matches(_session());
        }

        private async Task<LeagueLiveSnapshot> ReadRosterAsync(string phase, CancellationToken token)
        {
            var roster = new LeagueLiveSnapshot();
            var bytes = await _client.TryGetBytesAsync(phase == "ChampSelect" ? LeagueLiveDataService.ChampSelectSessionPath :
                LeagueLiveDataService.GameflowSessionPath, token).ConfigureAwait(false);
            if (phase == "ChampSelect") _live.ApplyChampSelect(roster, bytes);
            else _live.ApplyCurrentGame(roster, bytes);
            return roster;
        }

        internal static string RosterKey(LeagueLiveSnapshot roster)
        {
            return string.Join("|", roster.Players.OrderBy(p => p.PuuId, StringComparer.Ordinal)
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

        public void Dispose()
        {
            lock (_sync) { _disposed = true; _active?.Cancel(); }
        }
    }
}
