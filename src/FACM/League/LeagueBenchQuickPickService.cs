using System;
using System.Diagnostics;
using System.Linq;
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
            return benchActive ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(750);
        }
    }

    /// <summary>
    /// Manual-only quick bench swap transaction. It deliberately re-reads the current bench before
    /// the write and verifies the local champion after the write. One user click can produce at most
    /// one POST; verification never retries the write.
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

        public async Task<LeagueBenchSwapResult> TrySwapAsync(int championId, CancellationToken cancellationToken)
        {
            if (championId <= 0) throw new ArgumentOutOfRangeException(nameof(championId));

            await _swapGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var before = await _live.RefreshBenchAsync(cancellationToken).ConfigureAwait(false);
                if (before == null || !before.SessionAvailable)
                    return Result(LeagueBenchSwapStatus.SessionUnavailable, championId, 0, stopwatch);
                if (!before.BenchEnabled)
                    return Result(LeagueBenchSwapStatus.BenchDisabled, championId, 0, stopwatch);
                if (!before.ChampionIds.Contains(championId))
                    return Result(LeagueBenchSwapStatus.TargetUnavailable, championId, 0, stopwatch);

                var response = await _writer.TrySwapAsync(championId, cancellationToken).ConfigureAwait(false);
                if (response == null || !response.IsSuccessStatusCode)
                    return Result(
                        LeagueBenchSwapStatus.WriteRejected,
                        championId,
                        response == null ? 0 : response.StatusCode,
                        stopwatch);

                // LCU can acknowledge a write before the Champ Select session has settled. Keep the
                // verification bounded and read-only; never repeat the POST from a single click.
                var settled = await VerifyChampionAsync(championId, TimeSpan.FromMilliseconds(70), cancellationToken).ConfigureAwait(false);
                if (!settled)
                    settled = await VerifyChampionAsync(championId, TimeSpan.FromMilliseconds(130), cancellationToken).ConfigureAwait(false);

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
