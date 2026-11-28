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
        private readonly Func<LeagueClientSession> _sessionProvider;
        private LeagueClientSession _owner;
        private bool _initialPending;
        private int _initialAttempts;

        internal LeagueOfflineStatusLockController(LeaguePresenceService presence, Func<DateTime> utcNow = null,
            Func<LeagueClientSession> sessionProvider = null)
        {
            _presence = presence ?? throw new ArgumentNullException(nameof(presence));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _sessionProvider = sessionProvider;
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
            ObserveCore(state, false);
        }

        internal void ObserveInitial(LeagueDashboardPhaseState state)
        {
            ObserveCore(state, true);
        }

        private void ObserveCore(LeagueDashboardPhaseState state, bool initialOnly)
        {
            ObserveConnection(state);
            if (state == null || !state.Connected) return;
            lock (_sync)
            {
                if (_disposed || !_enabled || !_connected || _active != null || _rejected ||
                    (initialOnly && (!_initialPending || _initialAttempts >= 3))) return;
                if (initialOnly) _initialAttempts++;
                var active = new CancellationTokenSource();
                _active = active;
                var generation = _generation;
                var owner = _owner;
                _running = Task.Run(() => RunAsync(active, generation, null, owner));
            }
        }

        internal void ObserveConnection(LeagueDashboardPhaseState state)
        {
            var connected = state != null && state.Connected;
            CancellationTokenSource previous = null;
            lock (_sync)
            {
                if (_disposed) return;
                var owner = connected && _enabled && _sessionProvider != null ? _sessionProvider() : null;
                if (_enabled && _sessionProvider != null && owner == null) connected = false;
                if (_connected != connected || !SameOwner(_owner, owner))
                {
                    _connected = connected;
                    _owner = owner;
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
            _initialPending = true;
            _initialAttempts = 0;
        }

        private static bool SameOwner(LeagueClientSession first, LeagueClientSession second)
        {
            return first == null ? second == null : LeagueEncounterReplayService.SessionMatches(first, second);
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

        internal Task ObserveEventAsync(string availability, CancellationToken token, LeagueClientSession source = null)
        {
            if (!NeedsOffline(availability)) return Task.CompletedTask;
            token.ThrowIfCancellationRequested();
            CancellationTokenSource active;
            int generation;
            LeagueClientSession owner;
            lock (_sync)
            {
                if (_disposed || !_enabled || !_connected || _active != null || _rejected ||
                    (_sessionProvider != null && !SameOwner(_owner, source))) return Task.CompletedTask;
                owner = _owner;
                active = CancellationTokenSource.CreateLinkedTokenSource(token);
                _active = active;
                generation = _generation;
            }
            return RunAsync(active, generation, availability, owner);
        }

        private async Task RunAsync(CancellationTokenSource active, int generation, string expected, LeagueClientSession owner)
        {
            try
            {
                lock (_sync) if (!IsCurrent(generation)) return;
                var current = await _presence.ReadAsync(active.Token).ConfigureAwait(false);
                if (!current.Connected) return;
                var offline = string.Equals(current.Availability, "offline", StringComparison.OrdinalIgnoreCase);
                if (!offline && !NeedsOffline(current.Availability)) return;

                active.Token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (!IsCurrent(generation) || _rejected) return;
                    if (offline)
                    {
                        _initialPending = false;
                        return;
                    }
                    if (expected != null && !string.Equals(current.Availability, expected, StringComparison.OrdinalIgnoreCase)) return;
                    var now = _utcNow();
                    if (_attempts == 0 || now - _windowStart >= TimeSpan.FromSeconds(30))
                    {
                        _windowStart = now;
                        _attempts = 0;
                    }
                    if (_attempts >= 3) return;
                    _attempts++;
                    _initialPending = false;
                }

                var result = await _presence.ApplyIfAvailabilityUnchangedAsync(
                    LeaguePresenceMode.Offline, current.Availability, active.Token, owner,
                    () => { lock (_sync) return IsCurrent(generation); }).ConfigureAwait(false);
                var status = result == null ? "unverified" : result.Status;
                lock (_sync)
                {
                    if (!IsCurrent(generation)) return;
                    AppLog.Info("League offline lock: " + status);
                    // A refusal or unverifiable result is not an invitation to keep rewriting.
                    if (status != "success") _rejected = true;
                }
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
            return !_disposed && _enabled && _connected && _generation == generation &&
                (_sessionProvider == null || (_owner != null && SameOwner(_owner, _sessionProvider())));
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
