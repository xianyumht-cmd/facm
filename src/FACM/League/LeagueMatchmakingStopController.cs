using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal enum LeagueMatchmakingStopPolicy
    {
        Never,
        FixedTime,
        EstimatedTime
    }

    internal static class LeagueMatchmakingStopPolicyCodec
    {
        public const string Never = "never";
        public const string Fixed = "fixed";
        public const string Estimated = "estimated";

        public static LeagueMatchmakingStopPolicy Parse(string value)
        {
            var normalized = Normalize(value);
            if (normalized == Fixed) return LeagueMatchmakingStopPolicy.FixedTime;
            if (normalized == Estimated) return LeagueMatchmakingStopPolicy.EstimatedTime;
            return LeagueMatchmakingStopPolicy.Never;
        }

        public static string Normalize(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == Fixed || normalized == Estimated) return normalized;
            return Never;
        }

        public static string Serialize(LeagueMatchmakingStopPolicy policy)
        {
            if (policy == LeagueMatchmakingStopPolicy.FixedTime) return Fixed;
            if (policy == LeagueMatchmakingStopPolicy.EstimatedTime) return Estimated;
            return Never;
        }
    }

    internal sealed class LeagueMatchmakingSearchState
    {
        public bool IsCurrentlyInQueue { get; set; }
        public double? TimeInQueueSeconds { get; set; }
        public double? EstimatedQueueTimeSeconds { get; set; }

        public bool HasUsableEstimate
        {
            get
            {
                return IsCurrentlyInQueue &&
                       TimeInQueueSeconds.HasValue && TimeInQueueSeconds.Value >= 0d &&
                       EstimatedQueueTimeSeconds.HasValue && EstimatedQueueTimeSeconds.Value > 0d;
            }
        }
    }

    /// <summary>
    /// Akari-style matchmaking stop policy built on FACM's existing shared Gameflow owner.
    /// The module feeds Observe from LeagueDashboardModule; this class never creates a second
    /// Gameflow monitor. It reads only the existing matchmaking search state while queueing and
    /// delegates the one allowed stop write to ILeagueMatchmakingWriteApi.
    /// </summary>
    internal sealed class LeagueMatchmakingStopController : IDisposable
    {
        internal const string SearchStatePath = "/lol-matchmaking/v1/search";
        internal static readonly TimeSpan EstimatedPollInterval = TimeSpan.FromSeconds(1);
        internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

        private readonly ILeagueClientApi _readApi;
        private readonly ILeagueMatchmakingWriteApi _writeApi;
        private readonly object _sync = new object();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private CancellationTokenSource _episode;
        private LeagueMatchmakingStopPolicy _policy;
        private int _fixedStopAfterMs;
        private string _phase = string.Empty;
        private LeagueActivityLevel _activity = LeagueActivityLevel.Unknown;
        private int _generation;
        private bool _stopConfirmed;
        private bool _disposed;

        public LeagueMatchmakingStopController(ILeagueClientApi readApi, ILeagueMatchmakingWriteApi writeApi)
        {
            _readApi = readApi ?? throw new ArgumentNullException(nameof(readApi));
            _writeApi = writeApi ?? throw new ArgumentNullException(nameof(writeApi));
        }

        public void Configure(string policy, int fixedStopAfterMs)
        {
            Configure(LeagueMatchmakingStopPolicyCodec.Parse(policy), fixedStopAfterMs);
        }

        public void Configure(LeagueMatchmakingStopPolicy policy, int fixedStopAfterMs)
        {
            CancellationTokenSource previous = null;
            var restart = false;
            lock (_sync)
            {
                if (_disposed) return;
                var normalizedDelay = Math.Max(1000, Math.Min(600000, fixedStopAfterMs));
                restart = _policy != policy || _fixedStopAfterMs != normalizedDelay;
                _policy = policy;
                _fixedStopAfterMs = normalizedDelay;
                if (restart)
                {
                    previous = _episode;
                    _episode = null;
                    _generation++;
                    _stopConfirmed = false;
                }
            }
            CancelAndDispose(previous);
            if (restart) EnsureEpisodeForCurrentState();
        }

        public void Observe(LeagueDashboardPhaseState state)
        {
            CancellationTokenSource previous = null;
            var start = false;
            lock (_sync)
            {
                if (_disposed) return;
                var nextPhase = state == null ? string.Empty : state.Phase ?? string.Empty;
                var nextActivity = state == null ? LeagueActivityLevel.Unknown : state.Activity;
                var wasQueueing = IsQueueing(_phase, _activity);
                var isQueueing = IsQueueing(nextPhase, nextActivity);
                _phase = nextPhase;
                _activity = nextActivity;

                if (!isQueueing)
                {
                    previous = _episode;
                    _episode = null;
                    _generation++;
                    _stopConfirmed = false;
                }
                else if (!wasQueueing || _episode == null)
                {
                    start = _policy != LeagueMatchmakingStopPolicy.Never;
                    _stopConfirmed = false;
                }
            }
            CancelAndDispose(previous);
            if (start) StartEpisode();
        }

        private void EnsureEpisodeForCurrentState()
        {
            var start = false;
            lock (_sync)
            {
                if (_disposed || _episode != null) return;
                start = _policy != LeagueMatchmakingStopPolicy.Never && IsQueueing(_phase, _activity);
            }
            if (start) StartEpisode();
        }

        private void StartEpisode()
        {
            CancellationTokenSource request;
            int generation;
            LeagueMatchmakingStopPolicy policy;
            int delayMs;
            lock (_sync)
            {
                if (_disposed || _episode != null || _policy == LeagueMatchmakingStopPolicy.Never || !IsQueueing(_phase, _activity))
                    return;
                _episode = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                request = _episode;
                generation = ++_generation;
                policy = _policy;
                delayMs = _fixedStopAfterMs;
            }

            if (policy == LeagueMatchmakingStopPolicy.FixedTime)
                _ = RunFixedEpisodeAsync(generation, delayMs, request.Token);
            else if (policy == LeagueMatchmakingStopPolicy.EstimatedTime)
                _ = RunEstimatedEpisodeAsync(generation, request.Token);
        }

        private async Task RunFixedEpisodeAsync(int generation, int delayMs, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Math.Max(1000, delayMs), cancellationToken).ConfigureAwait(false);
                if (!IsCurrentQueueEpisode(generation)) return;
                var state = await ReadSearchStateAsync(cancellationToken).ConfigureAwait(false);
                if (state == null || !state.IsCurrentlyInQueue) return;
                await TryStopAsync(generation, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("League matchmaking fixed stop skipped: " + exception.Message);
            }
        }

        private async Task RunEstimatedEpisodeAsync(int generation, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsCurrentQueueEpisode(generation))
                {
                    var state = await ReadSearchStateAsync(cancellationToken).ConfigureAwait(false);
                    if (state == null || !state.IsCurrentlyInQueue) return;
                    if (state.HasUsableEstimate && state.TimeInQueueSeconds.Value >= state.EstimatedQueueTimeSeconds.Value)
                    {
                        var stopped = await TryStopAsync(generation, cancellationToken).ConfigureAwait(false);
                        if (stopped) return;
                        await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Delay(EstimatedPollInterval, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("League matchmaking estimated stop skipped: " + exception.Message);
            }
        }

        private async Task<bool> TryStopAsync(int generation, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!IsCurrentQueueEpisodeLocked(generation) || _stopConfirmed) return _stopConfirmed;
            }

            LeagueClientWriteResponse response = null;
            try
            {
                response = await _writeApi.TrySendAsync(
                    "DELETE",
                    LeagueMatchmakingWriteApiClient.SearchPath,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                AppLog.Info("League matchmaking stop write failed: " + exception.Message);
            }

            if (!IsCurrentQueueEpisode(generation)) return false;
            var after = await ReadSearchStateAsync(cancellationToken).ConfigureAwait(false);
            var confirmed = after != null && !after.IsCurrentlyInQueue;
            if (!confirmed)
            {
                // A 2xx response alone is not success; the queue state must be observed as stopped.
                if (response != null)
                    AppLog.Info("League matchmaking stop not confirmed; http=" + response.StatusCode.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            lock (_sync)
            {
                if (!IsCurrentQueueEpisodeLocked(generation)) return false;
                _stopConfirmed = true;
            }
            AppLog.Info("League matchmaking stop confirmed by search-state reconciliation.");
            return true;
        }

        internal async Task<LeagueMatchmakingSearchState> ReadSearchStateAsync(CancellationToken cancellationToken)
        {
            var bytes = await _readApi.TryGetBytesAsync(SearchStatePath, cancellationToken).ConfigureAwait(false);
            return ParseSearchState(bytes);
        }

        internal static LeagueMatchmakingSearchState ParseSearchState(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes)) as IDictionary<string, object>;
                if (root == null) return null;
                var result = new LeagueMatchmakingSearchState
                {
                    IsCurrentlyInQueue = ReadBool(root, "isCurrentlyInQueue")
                };
                result.TimeInQueueSeconds = ReadNullableDouble(root, "timeInQueue");
                result.EstimatedQueueTimeSeconds = FirstPositive(
                    ReadNullableDouble(root, "estimatedQueueTime"),
                    ReadNullableDouble(root, "estimatedQueueTimeInSeconds"));

                IDictionary<string, object> searchState;
                if (!result.EstimatedQueueTimeSeconds.HasValue && TryDictionary(root, "searchState", out searchState))
                {
                    result.EstimatedQueueTimeSeconds = FirstPositive(
                        ReadNullableDouble(searchState, "estimatedQueueTime"),
                        ReadNullableDouble(searchState, "estimatedQueueTimeInSeconds"));
                }
                return result;
            }
            catch
            {
                return null;
            }
        }

        private static double? FirstPositive(params double?[] values)
        {
            if (values == null) return null;
            foreach (var value in values)
                if (value.HasValue && value.Value > 0d && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
                    return value;
            return null;
        }

        private static bool ReadBool(IDictionary<string, object> root, string key)
        {
            object raw;
            if (root == null || !root.TryGetValue(key, out raw) || raw == null) return false;
            if (raw is bool) return (bool)raw;
            bool value;
            return bool.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out value) && value;
        }

        private static double? ReadNullableDouble(IDictionary<string, object> root, string key)
        {
            object raw;
            if (root == null || !root.TryGetValue(key, out raw) || raw == null) return null;
            try
            {
                var value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                if (double.IsNaN(value) || double.IsInfinity(value)) return null;
                return value;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryDictionary(IDictionary<string, object> root, string key, out IDictionary<string, object> value)
        {
            value = null;
            object raw;
            if (root == null || !root.TryGetValue(key, out raw) || raw == null) return false;
            value = raw as IDictionary<string, object>;
            return value != null;
        }

        private bool IsCurrentQueueEpisode(int generation)
        {
            lock (_sync) return IsCurrentQueueEpisodeLocked(generation);
        }

        private bool IsCurrentQueueEpisodeLocked(int generation)
        {
            return !_disposed && generation == _generation && _episode != null &&
                   _policy != LeagueMatchmakingStopPolicy.Never && IsQueueing(_phase, _activity);
        }

        private static bool IsQueueing(string phase, LeagueActivityLevel activity)
        {
            return activity == LeagueActivityLevel.Queueing ||
                   string.Equals(phase, "Matchmaking", StringComparison.OrdinalIgnoreCase);
        }

        private static void CancelAndDispose(CancellationTokenSource source)
        {
            if (source == null) return;
            try { source.Cancel(); }
            catch { }
            source.Dispose();
        }

        public void Dispose()
        {
            CancellationTokenSource episode;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _generation++;
                episode = _episode;
                _episode = null;
            }
            CancelAndDispose(episode);
            try { _lifetime.Cancel(); }
            catch { }
            _lifetime.Dispose();
        }
    }
}
