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
        private readonly SemaphoreSlim _swapGate = new SemaphoreSlim(1, 1);

        public LeagueBenchQuickPickService(LeagueLiveDataService live, ILeagueBenchSwapWriteApi writer)
        {
            _live = live ?? throw new ArgumentNullException(nameof(live));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public Task<LeagueBenchQuickPickState> RefreshAsync(CancellationToken cancellationToken)
        {
            return _live.RefreshBenchAsync(cancellationToken);
        }

        public Task<byte[]> LoadChampionIconAsync(int championId, CancellationToken cancellationToken)
        {
            return _live.LoadChampionIconAsync(championId, cancellationToken);
        }

        public async Task<LeagueBenchSwapResult> TrySwapAsync(
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
                var settled = await VerifyChampionAsync(championId, TimeSpan.FromMilliseconds(35), cancellationToken).ConfigureAwait(false);
                if (!settled)
                    settled = await VerifyChampionAsync(championId, TimeSpan.FromMilliseconds(70), cancellationToken).ConfigureAwait(false);
                if (!settled)
                    settled = await VerifyChampionAsync(championId, TimeSpan.FromMilliseconds(140), cancellationToken).ConfigureAwait(false);

                return Result(
                    settled ? LeagueBenchSwapStatus.Success : LeagueBenchSwapStatus.VerificationFailed,
                    championId,
                    response.StatusCode,
                    stopwatch);
            }
            finally
            {
                stopwatch.Stop();
                _swapGate.Release();
            }
        }

        private async Task<bool> VerifyChampionAsync(int championId, TimeSpan delay, CancellationToken cancellationToken)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            var state = await _live.RefreshBenchAsync(cancellationToken).ConfigureAwait(false);
            return state != null && state.SessionAvailable && state.LocalChampionId == championId;
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
