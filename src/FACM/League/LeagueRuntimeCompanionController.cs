using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FACM.Mayhem;
using FACM.Services;

namespace FACM.League
{
    /// <summary>
    /// Runtime Companion orchestration boundary.
    ///
    /// It deliberately does not own Gameflow. The existing popup owner decides when the companion
    /// exists; this controller only refreshes the already-established Bench/live context and the
    /// existing Mayhem guide path. Bench writes remain delegated to LeagueBenchQuickPickService.
    /// </summary>
    internal sealed class LeagueRuntimeCompanionController : IDisposable
    {
        private readonly LeagueBenchQuickPickService _bench;
        private readonly ILeagueClientApi _leagueClient;
        private readonly MayhemAutomaticGuideService _guide;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
        private readonly object _stateGate = new object();

        private LeagueRuntimeCompanionSnapshot _snapshot = new LeagueRuntimeCompanionSnapshot();
        private CancellationTokenSource _guideRequest;
        private int _guideChampionId;
        private int _guideGeneration;
        private bool _disposed;

        public LeagueRuntimeCompanionController(
            LeagueBenchQuickPickService bench,
            ILeagueClientApi leagueClient)
        {
            _bench = bench ?? throw new ArgumentNullException(nameof(bench));
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _guide = new MayhemAutomaticGuideService(_leagueClient);
        }

        public event EventHandler<LeagueRuntimeCompanionUpdateEventArgs> SnapshotChanged;

        public LeagueRuntimeCompanionSnapshot CurrentSnapshot
        {
            get
            {
                lock (_stateGate) return _snapshot.Clone();
            }
        }

        public async Task<LeagueRuntimeCompanionSnapshot> RefreshAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var state = await _bench.RefreshAsync(cancellationToken).ConfigureAwait(false);
                if (state == null || !state.SessionAvailable)
                {
                    CancelGuideForContextLoss();
                    lock (_stateGate)
                    {
                        _snapshot = new LeagueRuntimeCompanionSnapshot
                        {
                            SessionAvailable = false,
                            UpdatedAtUtc = DateTime.UtcNow
                        };
                    }
                    PublishSnapshot();
                    return CurrentSnapshot;
                }

                var championIds = new List<int>();
                foreach (var championId in state.ChampionIds)
                {
                    if (championId > 0 && !championIds.Contains(championId)) championIds.Add(championId);
                }

                lock (_stateGate)
                {
                    _snapshot.SessionAvailable = true;
                    _snapshot.BenchEnabled = state.BenchEnabled;
                    _snapshot.LocalChampionId = state.LocalChampionId;
                    _snapshot.SwapRoute = state.SwapRoute;
                    _snapshot.BenchChampionIds = championIds.AsReadOnly();
                    _snapshot.UpdatedAtUtc = DateTime.UtcNow;
                }

                if (!state.BenchEnabled)
                {
                    CancelGuideForContextLoss();
                    lock (_stateGate)
                    {
                        _snapshot.GuideLoading = false;
                        _snapshot.GuideChampionId = 0;
                        _snapshot.Guide = null;
                        _snapshot.GuideError = null;
                    }
                    PublishSnapshot();
                    return CurrentSnapshot;
                }

                if (state.LocalChampionId > 0 && state.LocalChampionId != _guideChampionId)
                    StartGuideRequest(state.LocalChampionId);

                PublishSnapshot();
                return CurrentSnapshot;
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        public Task<LeagueBenchSwapResult> TrySwapAsync(
            int championId,
            LeagueBenchSwapRoute route,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return _bench.TrySwapAsync(championId, route, cancellationToken);
        }

        public Task<byte[]> LoadBenchChampionIconAsync(int championId, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return _bench.LoadChampionIconAsync(championId, cancellationToken);
        }

        public Task<byte[]> LoadGuideChampionIconAsync(string reference, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return RiotGameDataService.DownloadImageAsync(reference, _leagueClient, cancellationToken);
        }

        private void StartGuideRequest(int championId)
        {
            CancellationTokenSource previous;
            CancellationTokenSource request;
            int generation;

            lock (_stateGate)
            {
                previous = _guideRequest;
                _guideRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _guideRequest.CancelAfter(TimeSpan.FromSeconds(15));
                request = _guideRequest;
                _guideChampionId = championId;
                generation = ++_guideGeneration;
                _snapshot.GuideLoading = true;
                _snapshot.GuideChampionId = championId;
                _snapshot.Guide = null;
                _snapshot.GuideError = null;
                _snapshot.UpdatedAtUtc = DateTime.UtcNow;
            }

            CancelAndDispose(previous);
            PublishSnapshot();
            _ = LoadGuideAsync(generation, championId, request);
        }

        private async Task LoadGuideAsync(int generation, int championId, CancellationTokenSource request)
        {
            MayhemChampionResult result = null;
            string error = null;
            try
            {
                result = await _guide.QueryForChampionIdAsync(championId, request.Token).ConfigureAwait(false);
                if (result == null)
                    error = MayhemUiCopy.NoData;
                else if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                    error = result.ErrorMessage;
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested && !request.IsCancellationRequested)
                    error = MayhemUiCopy.TimeoutShort;
                else if (!_lifetime.IsCancellationRequested)
                    error = MayhemUiCopy.TimeoutShort;
            }
            catch (Exception exception)
            {
                AppLog.Info("Runtime Companion automatic guide failed: " + exception.Message);
                error = MayhemUiCopy.Failed;
            }

            var publish = false;
            lock (_stateGate)
            {
                if (!_disposed && generation == _guideGeneration && championId == _guideChampionId && ReferenceEquals(_guideRequest, request))
                {
                    _snapshot.GuideLoading = false;
                    _snapshot.GuideChampionId = championId;
                    _snapshot.Guide = string.IsNullOrWhiteSpace(error) ? result : null;
                    _snapshot.GuideError = error;
                    _snapshot.UpdatedAtUtc = DateTime.UtcNow;
                    _guideRequest = null;
                    publish = true;
                }
            }

            request.Dispose();
            if (publish) PublishSnapshot();
        }

        private void CancelGuideForContextLoss()
        {
            CancellationTokenSource request;
            lock (_stateGate)
            {
                request = _guideRequest;
                _guideRequest = null;
                _guideChampionId = 0;
                _guideGeneration++;
            }
            CancelAndDispose(request);
        }

        private void PublishSnapshot()
        {
            var handler = SnapshotChanged;
            if (handler == null || _disposed) return;
            var snapshot = CurrentSnapshot;
            try
            {
                handler(this, new LeagueRuntimeCompanionUpdateEventArgs(snapshot));
            }
            catch (Exception exception)
            {
                // A presentation subscriber must not break the refresh/data owner.
                AppLog.Info("Runtime Companion snapshot subscriber failed: " + exception.Message);
            }
        }

        private static void CancelAndDispose(CancellationTokenSource request)
        {
            if (request == null) return;
            try { request.Cancel(); }
            catch { }
            request.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LeagueRuntimeCompanionController));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SnapshotChanged = null;
            try { _lifetime.Cancel(); }
            catch { }

            CancellationTokenSource request;
            lock (_stateGate)
            {
                request = _guideRequest;
                _guideRequest = null;
                _guideGeneration++;
            }
            CancelAndDispose(request);
            _refreshGate.Dispose();
            _lifetime.Dispose();
        }

        internal static void ValidateForSmokeTest()
        {
            var source = new LeagueRuntimeCompanionSnapshot
            {
                SessionAvailable = true,
                BenchEnabled = true,
                LocalChampionId = 58,
                SwapRoute = LeagueBenchSwapRoute.TeamBuilder,
                BenchChampionIds = new List<int> { 266, 55 }.AsReadOnly(),
                GuideLoading = true,
                GuideChampionId = 58,
                UpdatedAtUtc = DateTime.UtcNow
            };
            var clone = source.Clone();
            if (!clone.SessionAvailable || !clone.BenchEnabled || clone.LocalChampionId != 58)
                throw new InvalidOperationException("Runtime Companion snapshot clone lost context fields.");
            if (clone.BenchChampionIds == null || clone.BenchChampionIds.Count != 2 || clone.BenchChampionIds[0] != 266)
                throw new InvalidOperationException("Runtime Companion snapshot clone lost Bench order.");
            if (clone.SwapRoute != LeagueBenchSwapRoute.TeamBuilder || !clone.GuideLoading || clone.GuideChampionId != 58)
                throw new InvalidOperationException("Runtime Companion snapshot clone lost route/guide state.");
        }
    }
}
