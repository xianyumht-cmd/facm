using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    // One chat-ready automation task per connected episode owns both presence-field restores.
    internal sealed class LeagueLoginStatusRestoreController : IDisposable
    {
        private static readonly long RetryTicks = Math.Max(1, Stopwatch.Frequency * 3);
        private readonly LeaguePresenceService _presence;
        private readonly object _sync = new object();
        private readonly TimeSpan _settleDelay;
        private CancellationTokenSource _active;
        private Task _running;
        private long _lastAttempt;
        private int _generation;
        private bool _connected;
        private bool _enabled;
        private bool _signatureEnabled;
        private bool _rankedEnabled;
        private bool _completed;
        private bool _disposed;
        private string _message = string.Empty;
        private string _queue = "RANKED_SOLO_5x5";
        private string _tier = "CHALLENGER";
        private string _division = string.Empty;

        internal LeagueLoginStatusRestoreController(LeaguePresenceService presence)
            : this(presence, TimeSpan.FromSeconds(2)) { }

        internal LeagueLoginStatusRestoreController(LeaguePresenceService presence, TimeSpan settleDelay)
        {
            _presence = presence ?? throw new ArgumentNullException(nameof(presence));
            _settleDelay = settleDelay >= TimeSpan.Zero ? settleDelay : TimeSpan.Zero;
        }

        internal static string NormalizeSavedMessage(string message)
        {
            var safe = LeaguePresenceService.NormalizeStatusMessageForSmokeTest(message);
            return safe.Replace('\r', ' ').Replace('\n', ' ').Replace("\0", string.Empty);
        }

        internal void Configure(bool enabled, string message)
        {
            Configure(enabled, message, false, "RANKED_SOLO_5x5", "CHALLENGER", "I");
        }

        internal void Configure(bool signatureEnabled, string message, bool rankedEnabled,
            string queue, string tier, string division)
        {
            var normalized = NormalizeSavedMessage(message);
            string safeQueue;
            string safeTier;
            string safeDivision;
            var validRank = LeaguePresenceService.TryNormalizeRankedStatusForSmokeTest(
                queue, tier, division, out safeQueue, out safeTier, out safeDivision);
            if (!validRank) rankedEnabled = false;
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed || (_signatureEnabled == signatureEnabled && _message == normalized &&
                    _rankedEnabled == rankedEnabled && _queue == safeQueue &&
                    _tier == safeTier && _division == safeDivision)) return;
                _enabled = signatureEnabled || rankedEnabled;
                _signatureEnabled = signatureEnabled;
                _rankedEnabled = rankedEnabled;
                _message = normalized;
                _queue = safeQueue;
                _tier = safeTier;
                _division = safeDivision;
                _generation++;
                _completed = false;
                _lastAttempt = 0;
                previous = _active;
                _active = null;
            }
            Cancel(previous);
            StartIfEligible();
        }

        internal void Observe(LeagueDashboardPhaseState state)
        {
            var connected = state != null && state.Connected;
            CancellationTokenSource previous = null;
            lock (_sync)
            {
                if (_disposed) return;
                if (_connected && !connected)
                {
                    _generation++;
                    _completed = false;
                    _lastAttempt = 0;
                    previous = _active;
                    _active = null;
                }
                _connected = connected;
            }
            Cancel(previous);
            if (connected) StartIfEligible();
        }

        private void StartIfEligible()
        {
            lock (_sync)
            {
                if (_disposed || !_enabled || !_connected || _completed || _active != null) return;
                var now = Stopwatch.GetTimestamp();
                if (_lastAttempt != 0 && now - _lastAttempt < RetryTicks) return;
                _lastAttempt = now;
                var active = new CancellationTokenSource();
                _active = active;
                var generation = _generation;
                var message = _message;
                var signatureEnabled = _signatureEnabled;
                var rankedEnabled = _rankedEnabled;
                var queue = _queue;
                var tier = _tier;
                var division = _division;
                _running = Task.Run(() => RunAsync(active, generation, signatureEnabled, message,
                    rankedEnabled, queue, tier, division));
            }
        }

        private async Task RunAsync(CancellationTokenSource active, int generation,
            bool signatureEnabled, string message, bool rankedEnabled,
            string queue, string tier, string division)
        {
            try
            {
                var first = await _presence.ReadAsync(active.Token).ConfigureAwait(false);
                if (!first.Connected) return;
                if (_settleDelay > TimeSpan.Zero)
                    await Task.Delay(_settleDelay, active.Token).ConfigureAwait(false);
                var settled = await _presence.ReadAsync(active.Token).ConfigureAwait(false);
                if (!settled.Connected) return;

                var before = first.StatusMessage ?? string.Empty;
                var current = settled.StatusMessage ?? string.Empty;
                lock (_sync)
                {
                    if (!IsCurrent(generation)) return;
                    // Never fight a manual or client-side change made during chat readiness.
                    _completed = true;
                }
                if (signatureEnabled && string.Equals(before, current, StringComparison.Ordinal) &&
                    !string.Equals(current, message, StringComparison.Ordinal))
                {
                    active.Token.ThrowIfCancellationRequested();
                    var result = await _presence.ApplyStatusMessageIfUnchangedAsync(message, current, active.Token)
                        .ConfigureAwait(false);
                    AppLog.Info("League login signature restore: " + (result == null ? "unverified" : result.Status));
                }

                if (rankedEnabled && LeaguePresenceService.SameRankedStatus(first, settled) &&
                    !IsDesiredRank(settled, queue, tier, division))
                {
                    active.Token.ThrowIfCancellationRequested();
                    var result = await _presence.ApplyRankedStatusIfUnchangedAsync(
                        queue, tier, division, settled, active.Token).ConfigureAwait(false);
                    AppLog.Info("League login displayed-rank restore: " +
                        (result == null ? "unverified" : result.Status));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("League login signature restore skipped: " + exception.GetType().Name);
            }
            finally
            {
                lock (_sync)
                    if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        private static bool IsDesiredRank(LeaguePresenceSnapshot current, string queue, string tier, string division)
        {
            if (current == null || !current.Connected) return false;
            if (!string.Equals(current.RankedLeagueQueue ?? string.Empty, queue, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.RankedLeagueTier ?? string.Empty, tier, StringComparison.OrdinalIgnoreCase))
                return false;
            return tier == "MASTER" || tier == "GRANDMASTER" || tier == "CHALLENGER" ||
                string.Equals(current.RankedLeagueDivision ?? string.Empty, division, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCurrent(int generation)
        {
            return !_disposed && _enabled && _connected && !_completed &&
                _generation == generation;
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
