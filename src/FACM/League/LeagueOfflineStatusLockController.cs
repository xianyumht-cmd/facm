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
        private bool _rejected;
        private DateTime _windowStart;
        private int _attempts;
        private readonly Func<DateTime> _utcNow;

        internal LeagueOfflineStatusLockController(LeaguePresenceService presence, Func<DateTime> utcNow = null)
        {
            _presence = presence ?? throw new ArgumentNullException(nameof(presence));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        internal void Configure(bool enabled)
        {
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed || _enabled == enabled) return;
                _enabled = enabled;
                ResetAttempts();
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
                if (_disposed || !_enabled || !_connected || _active != null || _rejected) return;
                var active = new CancellationTokenSource();
                _active = active;
                var generation = _generation;
                _running = Task.Run(() => RunAsync(active, generation, null));
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
                    ResetAttempts();
                    _generation++;
                    previous = _active;
                    _active = null;
                }
            }
            Cancel(previous);
        }

        private void ResetAttempts()
        {
            _rejected = false;
            _attempts = 0;
            _windowStart = DateTime.MinValue;
        }

        // Stream reconnection does not reset the correction budget or override a rejection.
        internal void ResetEventBaseline() { }

        private static bool NeedsOffline(string availability)
        {
            return string.Equals(availability, "chat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(availability, "online", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(availability, "away", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(availability, "dnd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(availability, "mobile", StringComparison.OrdinalIgnoreCase);
        }

        internal Task ObserveEventAsync(string availability, CancellationToken token)
        {
            if (!NeedsOffline(availability)) return Task.CompletedTask;
            token.ThrowIfCancellationRequested();
            CancellationTokenSource active;
            int generation;
            lock (_sync)
            {
                if (_disposed || !_enabled || !_connected || _active != null || _rejected) return Task.CompletedTask;
                active = CancellationTokenSource.CreateLinkedTokenSource(token);
                _active = active;
                generation = _generation;
            }
            return RunAsync(active, generation, availability);
        }

        private async Task RunAsync(CancellationTokenSource active, int generation, string expected)
        {
            try
            {
                var current = await _presence.ReadAsync(active.Token).ConfigureAwait(false);
                if (!current.Connected || !NeedsOffline(current.Availability) ||
                    (expected != null && !string.Equals(current.Availability, expected, StringComparison.OrdinalIgnoreCase))) return;

                active.Token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (!IsCurrent(generation) || _rejected) return;
                    var now = _utcNow();
                    if (_attempts == 0 || now - _windowStart >= TimeSpan.FromSeconds(30))
                    {
                        _windowStart = now;
                        _attempts = 0;
                    }
                    if (_attempts >= 3) return;
                    _attempts++;
                }

                var result = await _presence.ApplyIfAvailabilityUnchangedAsync(
                    LeaguePresenceMode.Offline, current.Availability, active.Token).ConfigureAwait(false);
                var status = result == null ? "unverified" : result.Status;
                AppLog.Info("League offline lock: " + status);
                // A refusal or unverifiable result is not an invitation to keep rewriting.
                lock (_sync)
                    if (IsCurrent(generation) && status != "success") _rejected = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                lock (_sync) if (IsCurrent(generation)) _rejected = true;
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
