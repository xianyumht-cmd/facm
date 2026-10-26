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
            if (options == null || !new[] { "friendly", "enemy", "all" }.Contains(options.Target) ||
                !new[] { "preferName", "preferChampionName", "championNameWithName" }.Contains(options.NameStrategy) ||
                !new[] { "preferName", "preferChampionName", "championNameWithName" }.Contains(options.PremadeNameStrategy)) return false;
            var bindings = new List<string>();
            foreach (var shortcut in new[] { options.FriendlyShortcut, options.EnemyShortcut, options.AllShortcut })
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
        internal int UnavailableIdentityCount;
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
        private string _observedPhase;
        private string _selectionKey;
        private HashSet<string> _selected;

        internal LeagueDynamicMessageService(ILeagueClientApi client, PerformanceBudgetProvider budgets,
            Func<LeagueClientSession> session, Func<LeagueDashboardPhaseState> phase)
        {
            _client = client;
            _players = new LeaguePlayerDataService(client, budgets);
            _live = new LeagueLiveDataService(client, budgets);
            _session = session;
            _phase = phase;
            _observedPhase = phase()?.Phase;
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
            var deadline = DateTime.UtcNow.AddSeconds(15);
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
                    SelfPuuid = me.PuuId, RosterKey = RosterKey(roster), CreatedUtc = DateTime.UtcNow,
                    UnavailableIdentityCount = roster.Players.Count(p => !SafePuuid(p.PuuId)) };
                foreach (var row in roster.Players.Where(p => SafePuuid(p.PuuId)).GroupBy(p => p.PuuId).Select(g => g.First()).Take(20))
                    snapshot.Players.Add(new LeagueDynamicMessagePlayer { Player = row, Friendly = row.Side == own.Side });
                foreach (var player in snapshot.Players)
                {
                    if (!Current(session, phase.Phase)) return null;
                    var remaining = deadline - DateTime.UtcNow - TimeSpan.FromSeconds(4);
                    if (remaining <= TimeSpan.Zero) break;
                    var row = player.Player;
                    using (var playerTimeout = CancellationTokenSource.CreateLinkedTokenSource(active.Token))
                    {
                        playerTimeout.CancelAfter(remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2));
                        try
                        {
                            player.History = await _players.LoadRecentMatchesAsync(new LeaguePlayerProfile { PuuId = row.PuuId,
                                SummonerId = row.SummonerId }, 0, LeaguePlayerDataService.MaximumMatchCount, false, playerTimeout.Token).ConfigureAwait(false);
                            if (player.History != null && player.History.Matches.Count > LeaguePlayerDataService.MaximumMatchCount)
                                player.History.Matches.RemoveRange(LeaguePlayerDataService.MaximumMatchCount,
                                    player.History.Matches.Count - LeaguePlayerDataService.MaximumMatchCount);
                            if (player.History != null)
                                player.History = await _players.EnrichChampionNamesAsync(new LeaguePlayerProfile { PuuId = row.PuuId },
                                    player.History, playerTimeout.Token).ConfigureAwait(false);
                            player.ChampionName = player.History?.Matches.FirstOrDefault(m => m.ChampionId == row.ChampionId)?.ChampionName;
                        }
                        catch (OperationCanceledException) when (!active.IsCancellationRequested) { }
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

        internal async Task<bool> SendAsync(LeagueDynamicMessageSnapshot snapshot, IReadOnlyList<string> lines, int interval,
            Func<string, CancellationToken, Task<bool>> sendLine, CancellationToken token)
        {
            if (lines == null || lines.Count == 0 || lines.Count > 100 || interval < 0 || interval > 5000 ||
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
            lock (_sync) { _disposed = true; _active?.Cancel(); }
        }
    }
}
