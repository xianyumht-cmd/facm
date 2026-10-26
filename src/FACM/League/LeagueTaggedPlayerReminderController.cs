using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    // Reuses the shared Gameflow event owner; no message or phase polling is started independently.
    internal sealed class LeagueTaggedPlayerReminderController : IDisposable
    {
        private const string ConversationsPath = "/lol-chat/v1/conversations";
        private const string ChampSelectPath = LeagueLiveDataService.ChampSelectSessionPath;
        private static readonly long RetryTicks = Math.Max(1, Stopwatch.Frequency * 2);
        private readonly ILeagueClientApi _read;
        private readonly ILeagueTaggedPlayerNoticeWriteApi _write;
        private readonly Func<LeagueClientSession> _session;
        private readonly LeaguePlayerTagStore _tags;
        private readonly LeaguePlayerDataService _players;
        private readonly LeagueLiveDataService _live;
        private readonly object _sync = new object();
        private readonly HashSet<string> _attempted = new HashSet<string>(StringComparer.Ordinal);
        private CancellationTokenSource _active;
        private Task _running;
        private LeagueClientSession _episodeSession;
        private Dictionary<string, string> _scopedTags;
        private string _selfPuuid;
        private string _roomId;
        private int _generation;
        private long _lastAttempt;
        private bool _inChampSelect;
        private bool _noTags;
        private bool _disposed;

        internal LeagueTaggedPlayerReminderController(ILeagueClientApi read,
            ILeagueTaggedPlayerNoticeWriteApi write, Func<LeagueClientSession> session,
            LeaguePlayerTagStore tags, PerformanceBudgetProvider budgets)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _tags = tags ?? throw new ArgumentNullException(nameof(tags));
            _players = new LeaguePlayerDataService(read, budgets);
            _live = new LeagueLiveDataService(read, budgets);
        }

        internal void Observe(LeagueDashboardPhaseState state)
        {
            var eligible = state != null && state.Connected && state.Phase == "ChampSelect";
            var session = eligible ? _session() : null;
            CancellationTokenSource previous = null;
            lock (_sync)
            {
                if (_disposed) return;
                if (!eligible || session == null ||
                    (_episodeSession != null && !_episodeSession.Matches(session)))
                {
                    _generation++;
                    _episodeSession = null; _scopedTags = null; _selfPuuid = null; _roomId = null;
                    _noTags = false; _attempted.Clear(); _lastAttempt = 0;
                    previous = _active; _active = null;
                }
                _inChampSelect = eligible && session != null;
                if (_inChampSelect && _episodeSession == null) _episodeSession = session;
            }
            Cancel(previous);
            StartIfEligible();
        }

        internal void TagsChanged()
        {
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed) return;
                _generation++;
                previous = _active; _active = null;
                _scopedTags = null;
                _noTags = false;
                _lastAttempt = 0;
            }
            Cancel(previous);
            StartIfEligible();
        }

        private void StartIfEligible()
        {
            lock (_sync)
            {
                if (_disposed || !_inChampSelect || _active != null || _noTags) return;
                var now = Stopwatch.GetTimestamp();
                if (_lastAttempt != 0 && now - _lastAttempt < RetryTicks) return;
                _lastAttempt = now;
                var active = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                _active = active;
                var version = _generation;
                var episode = _episodeSession;
                _running = Task.Run(() => RunAsync(active, version, episode));
            }
        }

        private async Task RunAsync(CancellationTokenSource active, int version, LeagueClientSession session)
        {
            try { await AttemptAsync(version, session, active.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                AppLog.Info("League tagged-player reminder skipped: " + error.GetType().Name);
            }
            finally
            {
                lock (_sync)
                    if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        private async Task AttemptAsync(int version, LeagueClientSession session, CancellationToken token)
        {
            if (!Current(version, session)) return;
            Dictionary<string, string> scoped;
            string self;
            lock (_sync) { scoped = _scopedTags; self = _selfPuuid; }
            if (scoped == null)
            {
                if (_tags.ReadAllTags().Count == 0)
                {
                    SetNoTags(version, session);
                    return;
                }
                var account = await _players.LoadProfileAsync(true, token).ConfigureAwait(false);
                if (account == null || !LeagueDynamicMessageService.SafePuuid(account.PuuId)) return;
                self = account.PuuId;
                scoped = _tags.ReadTags(self, session.Region, session.PlatformId)
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                lock (_sync)
                {
                    if (!CurrentLocked(version, session)) return;
                    _selfPuuid = self; _scopedTags = scoped;
                    if (scoped.Count == 0) _noTags = true;
                }
            }
            if (scoped.Count == 0) return;

            var room = LeagueAramTeamSideController.ParseConversation(
                await _read.TryGetBytesAsync(ConversationsPath, token).ConfigureAwait(false));
            if (room == null || !Current(version, session)) return;
            lock (_sync)
            {
                if (!CurrentLocked(version, session)) return;
                if (_roomId != room) { _roomId = room; _attempted.Clear(); }
            }
            var snapshot = ReadRoster(await _read.TryGetBytesAsync(ChampSelectPath, token).ConfigureAwait(false));
            if (!ValidRoster(snapshot, self)) return;
            foreach (var player in snapshot.Players.Take(10))
            {
                token.ThrowIfCancellationRequested();
                if (player == null || player.PuuId == self || !scoped.TryGetValue(player.PuuId ?? "", out var tag)) continue;
                var message = FormatReminder(player, tag);
                if (message == null) continue;
                if (!Current(version, session)) return;
                lock (_sync)
                    if (_attempted.Contains(room + "|" + player.PuuId)) continue;

                var latestRoom = LeagueAramTeamSideController.ParseConversation(
                    await _read.TryGetBytesAsync(ConversationsPath, token).ConfigureAwait(false));
                if (latestRoom != room) return;
                var latestRoster = ReadRoster(
                    await _read.TryGetBytesAsync(ChampSelectPath, token).ConfigureAwait(false));
                if (!ValidRoster(latestRoster, self) ||
                    !latestRoster.Players.Any(p => p.PuuId == player.PuuId &&
                        p.GameName == player.GameName && p.TagLine == player.TagLine)) return;
                lock (_sync)
                {
                    if (!CurrentLocked(version, session) || _roomId != room ||
                        !_attempted.Add(room + "|" + player.PuuId)) return;
                }
                token.ThrowIfCancellationRequested();
                var response = await _write.TrySendTaggedNoticeAsync(session, room, message, token)
                    .ConfigureAwait(false);
                AppLog.Info("League tagged-player reminder: " +
                    (response != null && response.IsSuccessStatusCode ? "submitted" : "unverified"));
            }
        }

        private LeagueLiveSnapshot ReadRoster(byte[] bytes)
        {
            var snapshot = new LeagueLiveSnapshot();
            _live.ApplyChampSelect(snapshot, bytes);
            return snapshot;
        }

        private static bool ValidRoster(LeagueLiveSnapshot roster, string self)
        {
            return roster != null && roster.Players.Count > 1 && roster.Players.Count <= 20 &&
                roster.Players.Where(p => p != null && LeagueDynamicMessageService.SafePuuid(p.PuuId))
                    .GroupBy(p => p.PuuId, StringComparer.Ordinal).All(g => g.Count() == 1) &&
                roster.Players.Any(p => p != null && p.PuuId == self && p.IsLocalPlayer);
        }

        internal static string FormatReminder(LeagueLivePlayerRow player, string tag)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.GameName) ||
                string.IsNullOrWhiteSpace(player.TagLine) || string.IsNullOrWhiteSpace(tag) ||
                player.GameName.Length > 80 || player.TagLine.Length > 32 || tag.Length > 120 ||
                player.GameName.Any(char.IsControl) || player.TagLine.Any(char.IsControl) || tag.Any(char.IsControl))
                return null;
            var result = "[已标记的玩家: " + player.GameName + "#" + player.TagLine + "]: \n" + tag.Trim();
            return SafeReminder(result) ? result : null;
        }

        internal static bool SafeReminder(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && text.Length <= 300 &&
                text.StartsWith("[已标记的玩家: ", StringComparison.Ordinal) &&
                text.IndexOf("]: \n", StringComparison.Ordinal) > 0 &&
                text.Count(c => c == '\n') == 1 &&
                !text.Any(c => char.IsControl(c) && c != '\n');
        }

        private void SetNoTags(int version, LeagueClientSession session)
        {
            lock (_sync) if (CurrentLocked(version, session)) _noTags = true;
        }

        private bool Current(int version, LeagueClientSession session)
        {
            lock (_sync) return CurrentLocked(version, session);
        }

        private bool CurrentLocked(int version, LeagueClientSession session)
        {
            return !_disposed && _inChampSelect && _generation == version &&
                _episodeSession != null && _episodeSession.Matches(session) &&
                session.Matches(_session());
        }

        internal Task AwaitCurrentForSmokeTestAsync()
        {
            lock (_sync) return _running ?? Task.FromResult(0);
        }

        public void Dispose()
        {
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true; _generation++;
                previous = _active; _active = null; _attempted.Clear(); _scopedTags = null;
            }
            Cancel(previous);
        }

        private static void Cancel(CancellationTokenSource source)
        {
            if (source == null) return;
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }
}
