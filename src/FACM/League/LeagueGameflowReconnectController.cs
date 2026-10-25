using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal interface ILeagueGameflowReconnectWriteApi
    {
        Task<LeagueClientWriteResponse> TryReconnectAsync(CancellationToken cancellationToken);
    }

    internal sealed class LeagueGameflowReconnectController : IDisposable
    {
        internal const string PhasePath = "/lol-gameflow/v1/gameflow-phase";
        internal static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);

        private readonly ILeagueClientApi _read;
        private readonly ILeagueGameflowReconnectWriteApi _write;
        private readonly ILeagueMatchmakingClock _clock;
        private readonly object _sync = new object();
        private CancellationTokenSource _episode;
        private Task _pending;
        private bool _enabled;
        private bool _inReconnect;
        private bool _writeAttemptedThisPhase;
        private bool _disposed;
        private string _trace;

        public LeagueGameflowReconnectController(ILeagueClientApi read, ILeagueGameflowReconnectWriteApi write)
            : this(read, write, new LeagueMatchmakingSystemClock())
        {
        }

        internal LeagueGameflowReconnectController(
            ILeagueClientApi read,
            ILeagueGameflowReconnectWriteApi write,
            ILeagueMatchmakingClock clock)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public void Configure(bool enabled)
        {
            CancellationTokenSource previous = null;
            lock (_sync)
            {
                if (_disposed || _enabled == enabled) return;
                _enabled = enabled;
                if (!enabled)
                {
                    previous = _episode;
                    _episode = null;
                }
            }
            Cancel(previous);
            if (enabled) StartIfEligible();
        }

        public void Observe(LeagueDashboardPhaseState state)
        {
            var reconnect = state != null && state.Connected &&
                string.Equals(state.Phase, "Reconnect", StringComparison.OrdinalIgnoreCase);
            CancellationTokenSource previous = null;
            string previousTrace = null;
            lock (_sync)
            {
                if (_disposed) return;
                if (!reconnect)
                {
                    previous = _episode;
                    _episode = null;
                    if (_inReconnect && _writeAttemptedThisPhase) previousTrace = _trace;
                    _writeAttemptedThisPhase = false;
                }
                else if (!_inReconnect)
                {
                    _writeAttemptedThisPhase = false;
                }
                _inReconnect = reconnect;
            }
            Cancel(previous);
            if (previousTrace != null)
                CompactTraceLog.Record(previousTrace, "reconnect", "phase", "left_reconnect", 0);
            if (reconnect) StartIfEligible();
        }

        private void StartIfEligible()
        {
            CancellationTokenSource episode;
            string trace;
            lock (_sync)
            {
                if (_disposed || !_enabled || !_inReconnect || _episode != null || _writeAttemptedThisPhase)
                    return;
                _episode = new CancellationTokenSource();
                _trace = CompactTraceLog.NewId();
                episode = _episode;
                trace = _trace;
            }
            CompactTraceLog.Record(trace, "reconnect", "schedule", "armed", 0);
            var operation = RunAsync(episode, trace);
            lock (_sync) _pending = operation;
        }

        private async Task RunAsync(CancellationTokenSource episode, string trace)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                await _clock.Delay(ReconnectDelay, episode.Token).ConfigureAwait(false);
                if (!IsCurrent(episode)) return;

                // The shared Gameflow observer may be on an in-game power-saving cadence.
                // Check the current LCU phase before issuing an irreversible write.
                var phase = await _read.TryGetBytesAsync(PhasePath, episode.Token).ConfigureAwait(false);
                if (!IsCurrent(episode)) return;
                if (!IsReconnectPhase(phase))
                {
                    CompactTraceLog.Record(trace, "reconnect", "preflight",
                        phase == null ? "failed_unavailable" : "phase_mismatch", watch.ElapsedMilliseconds);
                    return;
                }
                CompactTraceLog.Record(trace, "reconnect", "preflight", "ok", watch.ElapsedMilliseconds);

                lock (_sync)
                {
                    if (_disposed || !_enabled || !_inReconnect || !ReferenceEquals(_episode, episode)) return;
                    if (_writeAttemptedThisPhase) return;
                    _writeAttemptedThisPhase = true;
                }

                var response = await _write.TryReconnectAsync(episode.Token).ConfigureAwait(false);
                CompactTraceLog.Record(trace, "reconnect", "write",
                    response == null ? "failed_unavailable" :
                    response.IsSuccessStatusCode ? "submitted" : "failed_http_" + response.StatusCode,
                    watch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (episode.IsCancellationRequested)
            {
                CompactTraceLog.Record(trace, "reconnect", "task", "cancelled", watch.ElapsedMilliseconds);
            }
            catch (Exception exception)
            {
                CompactTraceLog.Record(trace, "reconnect", "task", "failed_exception", watch.ElapsedMilliseconds);
                AppLog.Info("League reconnect skipped: " + exception.GetType().Name);
            }
            finally
            {
                episode.Dispose();
            }
        }

        private bool IsCurrent(CancellationTokenSource episode)
        {
            lock (_sync)
                return !_disposed && _enabled && _inReconnect && ReferenceEquals(_episode, episode) &&
                    !episode.IsCancellationRequested;
        }

        internal static bool IsReconnectPhase(byte[] payload)
        {
            if (payload == null || payload.Length < 2 || payload.Length > 64) return false;
            var phase = Encoding.UTF8.GetString(payload).Trim();
            return string.Equals(phase, "\"Reconnect\"", StringComparison.OrdinalIgnoreCase);
        }

        internal Task PendingForSmokeTest
        {
            get { lock (_sync) return _pending ?? Task.FromResult(0); }
        }

        private static void Cancel(CancellationTokenSource source)
        {
            if (source == null) return;
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
            // Dispose after any in-flight transport has finished using its token.
        }

        public void Dispose()
        {
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                previous = _episode;
                _episode = null;
            }
            Cancel(previous);
        }
    }
}
