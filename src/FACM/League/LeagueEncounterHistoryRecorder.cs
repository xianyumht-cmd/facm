using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueEncounterCapture
    {
        internal LeagueClientSession Session;
        internal long GameId;
        internal string SelfPuuid;
        internal string Region;
        internal string PlatformId;
        internal string QueueType;
        internal string[] Participants;

        internal static LeagueEncounterCapture Project(LeagueClientSession session,
            LeagueLiveSnapshot game, LeaguePlayerProfile account)
        {
            if (session == null || game == null || account == null || game.GameId <= 0 ||
                (!string.IsNullOrEmpty(game.Phase) && game.Phase != "InProgress") ||
                !LeagueDynamicMessageService.SafePuuid(account.PuuId)) return null;
            var participants = game.Players.Select(p => p == null ? null : p.PuuId).ToArray();
            if (participants.Length < 2 || participants.Length > 10 ||
                participants.Any(p => !LeagueDynamicMessageService.SafePuuid(p)) ||
                participants.Distinct(StringComparer.Ordinal).Count() != participants.Length ||
                !participants.Contains(account.PuuId, StringComparer.Ordinal)) return null;
            var queue = game.QueueId > 0 ? "queue-" + game.QueueId : game.GameMode;
            if (string.IsNullOrWhiteSpace(queue) || queue.Length > 64 || queue.Any(char.IsControl)) return null;
            return new LeagueEncounterCapture { Session = session, GameId = game.GameId,
                SelfPuuid = account.PuuId, Region = session.Region, PlatformId = session.PlatformId,
                QueueType = queue, Participants = participants };
        }
    }

    // One bounded read during InProgress and one local write on the existing end-of-game observation.
    internal sealed class LeagueEncounterHistoryRecorder : IDisposable
    {
        private readonly ILeagueClientApi _client;
        private readonly Func<LeagueClientSession> _session;
        private readonly LeaguePlayerDataService _players;
        private readonly LeagueLiveDataService _live;
        private readonly LeagueEncounterHistoryStore _store;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly object _sync = new object();
        private LeagueClientSession _episodeSession;
        private LeagueEncounterCapture _capture;
        private string _observedPhase;
        private int _episode;
        private int _captureAttempts;
        private int _saveAttempts;
        private bool _capturing;
        private bool _saving;
        private bool _saved;
        private bool _disposed;

        internal LeagueEncounterHistoryRecorder(ILeagueClientApi client, PerformanceBudgetProvider budgets,
            Func<LeagueClientSession> session, LeagueEncounterHistoryStore store)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _players = new LeaguePlayerDataService(client, budgets);
            _live = new LeagueLiveDataService(client, budgets);
        }

        internal void Observe(LeagueDashboardPhaseState state)
        {
            var session = state != null && state.Connected ? _session() : null;
            var phase = state != null && state.Connected ? state.Phase : null;
            var game = phase == "InProgress";
            var ending = phase == "PreEndOfGame" || phase == "EndOfGame";
            bool capture = false, save = false;
            LeagueEncounterCapture completedGame = null;
            int version;
            lock (_sync)
            {
                if (_disposed) return;
                var sameSession = _episodeSession != null && _episodeSession.Matches(session);
                if (session == null || (!game && !ending && phase != "Reconnect") ||
                    (_episodeSession != null && !sameSession))
                {
                    _episode++;
                    _episodeSession = null; _capture = null; _capturing = false; _saving = false;
                    _captureAttempts = 0; _saveAttempts = 0; _saved = false;
                }
                _observedPhase = phase;
                if (game && session != null)
                {
                    if (_episodeSession == null) _episodeSession = session;
                    if (_capture == null && !_capturing && _captureAttempts < 3)
                    {
                        _capturing = true; _captureAttempts++; capture = true;
                    }
                }
                if (ending && _capture != null && !_saving && !_saved && _saveAttempts < 3)
                {
                    _saving = true; _saveAttempts++; save = true; completedGame = _capture;
                }
                version = _episode;
            }
            if (capture) Task.Run(() => CaptureAsync(version, session)).Forget("League encountered-game capture");
            if (save) Task.Run(() => SaveAsync(version, completedGame)).Forget("League encountered-game persist");
        }

        private async Task CaptureAsync(int version, LeagueClientSession session)
        {
            LeagueEncounterCapture capture = null;
            try
            {
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(7));
                    var bytes = await _client.TryGetBytesAsync(LeagueLiveDataService.GameflowSessionPath,
                        deadline.Token).ConfigureAwait(false);
                    var snapshot = new LeagueLiveSnapshot();
                    _live.ApplyCurrentGame(snapshot, bytes);
                    var me = await _players.LoadProfileAsync(true, deadline.Token).ConfigureAwait(false);
                    capture = LeagueEncounterCapture.Project(session, snapshot, me);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { AppLog.Info("League encounter capture skipped: " + error.GetType().Name); }
            bool save = false;
            LeagueEncounterCapture completedGame = null;
            lock (_sync)
            {
                if (_disposed || version != _episode || _episodeSession == null ||
                    !_episodeSession.Matches(_session())) return;
                _capturing = false;
                if (capture != null) _capture = capture;
                if (_capture != null && (_observedPhase == "PreEndOfGame" || _observedPhase == "EndOfGame") &&
                    !_saving && !_saved && _saveAttempts < 3)
                {
                    _saving = true; _saveAttempts++; save = true; completedGame = _capture;
                }
            }
            if (save) Task.Run(() => SaveAsync(version, completedGame)).Forget("League encountered-game persist");
        }

        private async Task SaveAsync(int version, LeagueEncounterCapture capture)
        {
            var success = false;
            try
            {
                if (capture != null && capture.Session.Matches(_session()))
                {
                    using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                    {
                        deadline.CancelAfter(TimeSpan.FromSeconds(5));
                        var account = await _players.LoadProfileAsync(true, deadline.Token).ConfigureAwait(false);
                        if (account != null && account.PuuId == capture.SelfPuuid &&
                            capture.Session.Matches(_session()))
                        {
                            _store.SaveGame(capture.GameId, capture.SelfPuuid, capture.Region,
                                capture.PlatformId, capture.QueueType, capture.Participants);
                            success = true;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { AppLog.Info("League encounter persist skipped: " + error.GetType().Name); }
            lock (_sync)
            {
                if (!_disposed && version == _episode)
                {
                    _saving = false;
                    if (success) _saved = true;
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true; _episode++;
            }
            _lifetime.Cancel(); _lifetime.Dispose();
        }
    }
}
