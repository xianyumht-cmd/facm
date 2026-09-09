using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FACM.Mayhem;
using FACM.Services;

namespace FACM.League
{
    /// <summary>
    /// Runtime Companion orchestration boundary.
    ///
    /// It deliberately does not own Gameflow. LeagueHubModule decides when the companion exists.
    /// This controller reuses the existing Bench owner, existing Build Advisor owner and existing
    /// Mayhem guide path. Bench writes remain delegated to LeagueBenchQuickPickService.
    /// </summary>
    internal sealed class LeagueRuntimeCompanionController : IDisposable
    {
        internal static readonly TimeSpan BuildRefreshInterval = TimeSpan.FromSeconds(3);

        private readonly LeagueBenchQuickPickService _bench;
        private readonly ILeagueClientApi _leagueClient;
        private readonly LeagueBuildAdvisorDataService _advisor;
        private readonly MayhemAutomaticGuideService _guide;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
        private readonly object _stateGate = new object();

        private LeagueRuntimeCompanionSnapshot _snapshot = new LeagueRuntimeCompanionSnapshot();
        private CancellationTokenSource _guideRequest;
        private CancellationTokenSource _buildRequest;
        private int _guideChampionId;
        private int _guideGeneration;
        private int _buildGeneration;
        private int _buildChampionHint;
        private DateTime _lastBuildStartedUtc = DateTime.MinValue;
        private bool _disposed;

        public LeagueRuntimeCompanionController(
            LeagueBenchQuickPickService bench,
            ILeagueClientApi leagueClient,
            LeagueBuildAdvisorDataService advisor = null)
        {
            _bench = bench ?? throw new ArgumentNullException(nameof(bench));
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _advisor = advisor;
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
                LeagueBenchQuickPickState state = null;
                try
                {
                    state = await _bench.RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Build context may still be recoverable through the existing advisor owner.
                    AppLog.Info("Runtime Companion Bench refresh unavailable: " + exception.Message);
                }

                var sessionAvailable = state != null && state.SessionAvailable;
                var championHint = sessionAvailable ? state.LocalChampionId : 0;
                var championIds = new List<int>();
                if (state != null)
                {
                    foreach (var championId in state.ChampionIds)
                    {
                        if (championId > 0 && !championIds.Contains(championId)) championIds.Add(championId);
                    }
                }

                lock (_stateGate)
                {
                    _snapshot.SessionAvailable = sessionAvailable;
                    _snapshot.BenchEnabled = sessionAvailable && state.BenchEnabled;
                    _snapshot.LocalChampionId = championHint;
                    _snapshot.SwapRoute = state == null ? LeagueBenchSwapRoute.Legacy : state.SwapRoute;
                    _snapshot.BenchChampionIds = championIds.AsReadOnly();
                    _snapshot.UpdatedAtUtc = DateTime.UtcNow;
                }

                if (state != null && state.BenchEnabled)
                {
                    if (state.LocalChampionId > 0 && state.LocalChampionId != _guideChampionId)
                        StartGuideRequest(state.LocalChampionId);
                }
                else
                {
                    CancelGuideForContextLoss();
                    lock (_stateGate)
                    {
                        _snapshot.GuideLoading = false;
                        _snapshot.GuideChampionId = 0;
                        _snapshot.Guide = null;
                        _snapshot.GuideError = null;
                    }
                }

                MaybeStartBuildRequest(championHint);
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

        private void MaybeStartBuildRequest(int championHint)
        {
            if (_advisor == null || _disposed) return;

            CancellationTokenSource previous = null;
            CancellationTokenSource request = null;
            int generation = 0;
            var now = DateTime.UtcNow;

            lock (_stateGate)
            {
                var championChanged = championHint > 0 && championHint != _buildChampionHint;
                if (_buildRequest != null && !championChanged) return;
                if (!championChanged && now - _lastBuildStartedUtc < BuildRefreshInterval) return;

                previous = _buildRequest;
                _buildRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _buildRequest.CancelAfter(TimeSpan.FromSeconds(12));
                request = _buildRequest;
                generation = ++_buildGeneration;
                _buildChampionHint = championHint;
                _lastBuildStartedUtc = now;
                _snapshot.BuildLoading = true;
                _snapshot.BuildError = null;
                _snapshot.UpdatedAtUtc = now;
            }

            CancelAndDispose(previous);
            PublishSnapshot();
            _ = LoadBuildAsync(generation, request);
        }

        private async Task LoadBuildAsync(int generation, CancellationTokenSource request)
        {
            LeagueBuildAdvisorSnapshot result = null;
            string error = null;
            try
            {
                result = await _advisor.RefreshAsync(false, request.Token).ConfigureAwait(false);
                if (result == null)
                    error = "unavailable";
                else if (string.Equals(result.Status, "timeout", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(result.Status, "opgg-unavailable", StringComparison.OrdinalIgnoreCase))
                    error = result.Status;
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested && generation == Volatile.Read(ref _buildGeneration))
                    error = "timeout";
            }
            catch (Exception exception)
            {
                AppLog.Info("Runtime Companion build refresh failed: " + exception.Message);
                error = "failed";
            }

            var publish = false;
            lock (_stateGate)
            {
                if (!_disposed && generation == _buildGeneration && ReferenceEquals(_buildRequest, request))
                {
                    _snapshot.BuildLoading = false;
                    _snapshot.Build = result;
                    _snapshot.BuildError = error;
                    _snapshot.UpdatedAtUtc = DateTime.UtcNow;
                    if (result != null && result.ChampionId > 0) _buildChampionHint = result.ChampionId;
                    _buildRequest = null;
                    publish = true;
                }
            }

            request.Dispose();
            if (publish) PublishSnapshot();
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
                if (!_lifetime.IsCancellationRequested && generation == Volatile.Read(ref _guideGeneration))
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

            CancellationTokenSource guideRequest;
            CancellationTokenSource buildRequest;
            lock (_stateGate)
            {
                guideRequest = _guideRequest;
                buildRequest = _buildRequest;
                _guideRequest = null;
                _buildRequest = null;
                _guideGeneration++;
                _buildGeneration++;
            }
            CancelAndDispose(guideRequest);
            CancelAndDispose(buildRequest);

            // Do not dispose _refreshGate here. A canceled in-flight RefreshAsync still executes its
            // finally block and must be able to Release() safely. The semaphore is a tiny managed
            // lifetime object and becomes collectible with this controller after pending work exits.
            _lifetime.Dispose();
        }

        internal static void ValidateForSmokeTest()
        {
            if (BuildRefreshInterval < TimeSpan.FromSeconds(2))
                throw new InvalidOperationException("Runtime Companion build refresh became too aggressive.");

            var build = new LeagueBuildAdvisorSnapshot
            {
                Connected = true,
                Activity = Performance.LeagueActivityLevel.ChampSelect,
                ChampionId = 58,
                ChampionName = "Renekton",
                Mode = "ranked",
                Position = "top",
                Version = "16.18",
                Status = "ready",
                Recommendation = new LeagueBuildRecommendation()
            };
            build.Recommendation.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "runes",
                Recommendation = "Conqueror",
                Evidence = "pick 60.0%"
            });

            var source = new LeagueRuntimeCompanionSnapshot
            {
                SessionAvailable = true,
                BenchEnabled = false,
                LocalChampionId = 58,
                SwapRoute = LeagueBenchSwapRoute.TeamBuilder,
                BenchChampionIds = new List<int> { 266, 55 }.AsReadOnly(),
                Build = build,
                GuideLoading = false,
                GuideChampionId = 0,
                UpdatedAtUtc = DateTime.UtcNow
            };
            var clone = source.Clone();
            if (!clone.SessionAvailable || clone.LocalChampionId != 58 || !clone.HasBuild)
                throw new InvalidOperationException("Runtime Companion snapshot clone lost ranked context fields.");
            if (clone.BenchChampionIds == null || clone.BenchChampionIds.Count != 2 || clone.BenchChampionIds[0] != 266)
                throw new InvalidOperationException("Runtime Companion snapshot clone lost Bench order.");
            if (clone.Build == source.Build || clone.Build.Recommendation == source.Build.Recommendation)
                throw new InvalidOperationException("Runtime Companion build snapshot was not defensively cloned.");
            if (clone.Build.Recommendation.Rows.Count != 1 || clone.Build.Recommendation.Rows[0].Category != "runes")
                throw new InvalidOperationException("Runtime Companion build rows were not cloned.");
        }
    }
}
