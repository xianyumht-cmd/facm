using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;

namespace FACM.League
{
    internal enum LeagueBenchSwapStatus
    {
        Success,
        SessionUnavailable,
        BenchDisabled,
        TargetUnavailable,
        WriteRejected,
        VerificationFailed
    }

    internal sealed class LeagueBenchSwapResult
    {
        public LeagueBenchSwapStatus Status { get; set; }
        public int ChampionId { get; set; }
        public int StatusCode { get; set; }
        public long ElapsedMilliseconds { get; set; }

        public bool Success
        {
            get { return Status == LeagueBenchSwapStatus.Success; }
        }
    }

    internal static class LeagueBenchQuickPickPolling
    {
        public static TimeSpan ResolveDelay(bool benchActive, LeagueActivityLevel activity, bool minimized)
        {
            if (minimized) return TimeSpan.FromSeconds(1);
            if (activity == LeagueActivityLevel.InGame) return TimeSpan.FromSeconds(5);
            return benchActive ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(750);
        }
    }

    /// <summary>
    /// Manual-only quick bench swap transaction. The button itself represents the latest observed
    /// bench state, so a click goes straight to the one allowed POST instead of spending a race-
    /// sensitive round trip on a pre-read. Success is still proven by bounded read-back and the POST
    /// is never retried automatically.
    /// </summary>
    internal sealed class LeagueBenchQuickPickService
    {
        private readonly LeagueLiveDataService _live;
        private readonly ILeagueBenchSwapWriteApi _writer;
        private readonly ILeagueChampSelectActionWriteApi _actionWriter;
        private LeagueBenchQuickPickState _latest;
        private LeagueBenchQuickPickState _lastReroll;
        internal bool SupportsReroll { get { return _writer is ILeagueAramRerollWriteApi; } }
        private readonly SemaphoreSlim _swapGate = new SemaphoreSlim(1, 1);

        public LeagueBenchQuickPickService(LeagueLiveDataService live, ILeagueBenchSwapWriteApi writer,
            ILeagueChampSelectActionWriteApi actionWriter = null)
        {
            _live = live ?? throw new ArgumentNullException(nameof(live));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _actionWriter = actionWriter;
        }

        public async Task<LeagueBenchQuickPickState> RefreshAsync(CancellationToken cancellationToken)
        {
            try
            {
                var state = await _live.RefreshBenchAsync(cancellationToken, true).ConfigureAwait(false);
                Volatile.Write(ref _latest, state);
                return state;
            }
            catch
            {
                Volatile.Write(ref _latest, null);
                throw;
            }
        }

        // The UI supplies the observed draft and operation kind, never an arbitrary LCU action ID.
        // Initial card selection must re-read the active action and allowed choices. The fast bench
        // path keeps its single POST when the latest observation is still current.
        internal async Task<LeagueBenchSwapResult> TryChooseAsync(int championId, LeagueBenchQuickPickState expected,
            CancellationToken cancellationToken)
        {
            await _swapGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var state = Volatile.Read(ref _latest);
                if (expected == null || !expected.CanChoose(championId) || !expected.SameDraft(state))
                    return Result(LeagueBenchSwapStatus.TargetUnavailable, championId, 0, stopwatch);
                if (expected.IsInitialCardSelection || DateTime.UtcNow - state.ObservedAtUtc > TimeSpan.FromMilliseconds(750))
                {
                    state = await _live.RefreshBenchAsync(cancellationToken, true, true).ConfigureAwait(false);
                    Volatile.Write(ref _latest, state);
                }
                if (!expected.SameDraft(state) || !_live.IsQuickPickClientCurrent(state) ||
                    expected.IsInitialCardSelection != state.IsInitialCardSelection ||
                    !string.Equals(expected.TimerPhase, state.TimerPhase, StringComparison.Ordinal) ||
                    expected.LocalChampionId != state.LocalChampionId ||
                    expected.SwapRoute != state.SwapRoute ||
                    expected.LocalPickActionId != state.LocalPickActionId || !state.CanChoose(championId))
                    return Result(LeagueBenchSwapStatus.TargetUnavailable, championId, 0, stopwatch);

                LeagueClientWriteResponse response;
                if (state.IsInitialCardSelection)
                {
                    if (_actionWriter == null || !state.LocalPickActionId.HasValue)
                        return Result(LeagueBenchSwapStatus.BenchDisabled, championId, 0, stopwatch);
                    response = await _actionWriter.TryApplyAsync("pick", state.LocalPickActionId.Value,
                        championId, true, cancellationToken).ConfigureAwait(false);
                }
                else response = await _writer.TrySwapAsync(championId, state.SwapRoute, cancellationToken).ConfigureAwait(false);
                try
                {
                    return await VerifyResponseAsync(response, championId, stopwatch, cancellationToken, state).ConfigureAwait(false);
                }
                finally { Volatile.Write(ref _latest, null); }
            }
            finally { stopwatch.Stop(); _swapGate.Release(); }
        }

        internal async Task<LeagueBenchSwapResult> TryRerollAsync(LeagueBenchQuickPickState expected, CancellationToken token)
        {
            await _swapGate.WaitAsync(token).ConfigureAwait(false);
            var watch = Stopwatch.StartNew();
            try
            {
                var reroll = _writer as ILeagueAramRerollWriteApi;
                if (reroll == null || expected == null || !expected.CanReroll || expected.ClientSession == null)
                    return Result(LeagueBenchSwapStatus.TargetUnavailable, 0, 0, watch);
                var current = await _live.RefreshBenchAsync(token).ConfigureAwait(false);
                if (!expected.SameDraft(current) || !_live.IsQuickPickClientCurrent(current) || !current.CanReroll ||
                    current.LocalChampionId != expected.LocalChampionId ||
                    current.RerollsRemaining != expected.RerollsRemaining)
                    return Result(LeagueBenchSwapStatus.TargetUnavailable, 0, 0, watch);
                if (_lastReroll != null && _lastReroll.SameDraft(current) &&
                    _lastReroll.RerollsRemaining == current.RerollsRemaining)
                    return Result(LeagueBenchSwapStatus.VerificationFailed, 0, 0, watch);

                // One explicit click, one scoped bodyless POST; a lost response is not safe to repeat.
                _lastReroll = current;
                Volatile.Write(ref _latest, null);
                var response = await reroll.TryRerollAsync(current.ClientSession, token).ConfigureAwait(false);
                if (response != null && !response.IsSuccessStatusCode && response.StatusCode < 500)
                {
                    _lastReroll = null;
                    return Result(LeagueBenchSwapStatus.WriteRejected, 0, response.StatusCode, watch);
                }
                for (var i = 0; i < 3; i++)
                {
                    await Task.Delay(60 * (i + 1), token).ConfigureAwait(false);
                    var after = await _live.RefreshBenchAsync(token).ConfigureAwait(false);
                    if (!current.SameDraft(after) || !_live.IsQuickPickClientCurrent(after)) break;
                    if (after.RerollsRemaining.HasValue && after.RerollsRemaining.Value == current.RerollsRemaining.Value - 1 &&
                        after.LocalChampionId > 0 && after.LocalChampionId != current.LocalChampionId)
                        return Result(LeagueBenchSwapStatus.Success, after.LocalChampionId, response == null ? 0 : response.StatusCode, watch);
                }
                return Result(LeagueBenchSwapStatus.VerificationFailed, 0, response == null ? 0 : response.StatusCode, watch);
            }
            finally { watch.Stop(); _swapGate.Release(); }
        }

        public Task<byte[]> LoadChampionIconAsync(int championId, CancellationToken cancellationToken)
        {
            return _live.LoadChampionIconAsync(championId, cancellationToken);
        }

        public Task<LeagueBenchSwapResult> TrySwapAsync(int championId, CancellationToken cancellationToken)
        {
            return TrySwapAsync(championId, _live.LastBenchSwapRouteForQuickPick, cancellationToken);
        }

        internal async Task<LeagueBenchSwapResult> TrySwapAsync(
            int championId,
            LeagueBenchSwapRoute route,
            CancellationToken cancellationToken)
        {
            if (championId <= 0) throw new ArgumentOutOfRangeException(nameof(championId));

            await _swapGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await _writer.TrySwapAsync(championId, route, cancellationToken).ConfigureAwait(false);
                return await VerifyResponseAsync(response, championId, stopwatch, cancellationToken, null).ConfigureAwait(false);
            }
            finally
            {
                stopwatch.Stop();
                _swapGate.Release();
            }
        }

        private async Task<LeagueBenchSwapResult> VerifyResponseAsync(LeagueClientWriteResponse response,
            int championId, Stopwatch stopwatch, CancellationToken cancellationToken, LeagueBenchQuickPickState expected)
        {
            if (response == null)
                return Result(LeagueBenchSwapStatus.SessionUnavailable, championId, 0, stopwatch);
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode == 404 || response.StatusCode == 409
                    ? LeagueBenchSwapStatus.TargetUnavailable
                    : LeagueBenchSwapStatus.WriteRejected;
                return Result(status, championId, response.StatusCode, stopwatch);
            }

            // LCU can acknowledge before the Champ Select model settles. Verification is read-
            // only and bounded; a single click still produces exactly one POST.
            var settled = await VerifyChampionAsync(championId, TimeSpan.FromMilliseconds(35), cancellationToken, expected).ConfigureAwait(false);
            if (!settled)
                settled = await VerifyChampionAsync(championId, TimeSpan.FromMilliseconds(70), cancellationToken, expected).ConfigureAwait(false);
            if (!settled)
                settled = await VerifyChampionAsync(championId, TimeSpan.FromMilliseconds(140), cancellationToken, expected).ConfigureAwait(false);

            return Result(
                settled ? LeagueBenchSwapStatus.Success : LeagueBenchSwapStatus.VerificationFailed,
                championId,
                response.StatusCode,
                stopwatch);
        }

        private async Task<bool> VerifyChampionAsync(int championId, TimeSpan delay, CancellationToken cancellationToken,
            LeagueBenchQuickPickState expected)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            var state = await _live.RefreshBenchAsync(cancellationToken).ConfigureAwait(false);
            return state != null && state.SessionAvailable && state.LocalChampionId == championId &&
                (expected == null || expected.SameDraft(state));
        }

        private static LeagueBenchSwapResult Result(
            LeagueBenchSwapStatus status,
            int championId,
            int statusCode,
            Stopwatch stopwatch)
        {
            return new LeagueBenchSwapResult
            {
                Status = status,
                ChampionId = championId,
                StatusCode = statusCode,
                ElapsedMilliseconds = Math.Max(0L, stopwatch == null ? 0L : stopwatch.ElapsedMilliseconds)
            };
        }
    }
}
