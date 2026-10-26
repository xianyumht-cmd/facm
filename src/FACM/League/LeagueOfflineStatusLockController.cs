using System;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueOfflineStatusLockController : IDisposable
    {
        private readonly LeaguePresenceService _presence;
        private readonly object _sync = new object();
        private CancellationTokenSource _active;
        private Task _running;
        private int _generation;
        private bool _connected;
        private bool _enabled;
        private bool _disposed;
        private string _lastAvailability;

        internal LeagueOfflineStatusLockController(LeaguePresenceService presence)
        {
            _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        }

        internal void Configure(bool enabled)
        {
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed || _enabled == enabled) return;
                _enabled = enabled;
                _lastAvailability = null;
                _generation++;
                previous = _active;
                _active = null;
            }
            Cancel(previous);
        }

        internal void Observe(LeagueDashboardPhaseState state)
        {
            ObserveConnection(state);
            if (state == null || !state.Connected) return;
            lock (_sync)
            {
                if (_disposed || !_enabled || !_connected || _active != null) return;
                var active = new CancellationTokenSource();
                _active = active;
                var generation = _generation;
                _running = Task.Run(() => RunAsync(active, generation));
            }
        }

        internal void ObserveConnection(LeagueDashboardPhaseState state)
        {
            var connected = state != null && state.Connected;
            CancellationTokenSource previous = null;
            lock (_sync)
            {
                if (_disposed) return;
                if (_connected != connected)
                {
                    _connected = connected;
                    _lastAvailability = null;
                    _generation++;
                    previous = _active;
                    _active = null;
                }
            }
            Cancel(previous);
        }

        internal void ResetEventBaseline()
        {
            lock (_sync) _lastAvailability = null;
        }

        internal async Task ObserveEventAsync(string availability, CancellationToken token)
        {
            int generation;
            bool correct;
            lock (_sync)
            {
                if (_disposed || !_enabled || !_connected) return;
                generation = _generation;
                correct = _lastAvailability == "offline" && (availability == "away" || availability == "chat");
                _lastAvailability = availability;
            }
            if (!correct) return;
            token.ThrowIfCancellationRequested();
            var current = await _presence.ReadAsync(token).ConfigureAwait(false);
            if (!current.Connected || current.Availability != availability) return;
            lock (_sync) if (!IsCurrent(generation)) return;
            var result = await _presence.ApplyIfAvailabilityUnchangedAsync(LeaguePresenceMode.Offline, availability, token).ConfigureAwait(false);
            AppLog.Info("League offline event lock: " + (result == null ? "unverified" : result.Status));
        }

        private async Task RunAsync(CancellationTokenSource active, int generation)
        {
            try
            {
                var first = await _presence.ReadAsync(active.Token).ConfigureAwait(false);
                if (!first.Connected) return;
                var availability = first.Availability ?? string.Empty;
                bool shouldLock;
                lock (_sync)
                {
                    if (!IsCurrent(generation)) return;
                    shouldLock = string.Equals(_lastAvailability, "offline", StringComparison.OrdinalIgnoreCase) &&
                        (string.Equals(availability, "chat", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(availability, "away", StringComparison.OrdinalIgnoreCase));
                    _lastAvailability = availability;
                }
                if (!shouldLock) return;

                active.Token.ThrowIfCancellationRequested();
                var current = await _presence.ReadAsync(active.Token).ConfigureAwait(false);
                if (!current.Connected || !string.Equals(current.Availability, availability,
                    StringComparison.OrdinalIgnoreCase)) return;
                lock (_sync) if (!IsCurrent(generation)) return;

                var result = await _presence.ApplyIfAvailabilityUnchangedAsync(
                    LeaguePresenceMode.Offline, availability, active.Token).ConfigureAwait(false);
                AppLog.Info("League offline lock: " + (result == null ? "unverified" : result.Status));
                if (result != null && result.Status == "success")
                {
                    lock (_sync)
                        if (IsCurrent(generation)) _lastAvailability = "offline";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("League offline lock skipped: " + exception.GetType().Name);
            }
            finally
            {
                lock (_sync)
                    if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        private bool IsCurrent(int generation)
        {
            return !_disposed && _enabled && _connected && _generation == generation;
        }

        private static void Cancel(CancellationTokenSource source)
        {
            if (source == null) return;
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        internal Task AwaitCurrentForSmokeTestAsync()
        {
            lock (_sync) return _running ?? Task.CompletedTask;
        }

        public void Dispose()
        {
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _generation++;
                previous = _active;
                _active = null;
            }
            Cancel(previous);
        }
    }
}
