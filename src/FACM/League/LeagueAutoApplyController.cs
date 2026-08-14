using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueAutoApplyDecision
    {
        public bool ShouldExecute { get; set; }
        public string Fingerprint { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Pure state machine: no HTTP, disk, WinForms or timers. It turns repeated observations into
    /// at-most-once decisions after the same actionable context has stayed stable long enough.
    /// </summary>
    internal sealed class LeagueAutoApplyCoordinator
    {
        internal static readonly TimeSpan DefaultStabilityWindow = TimeSpan.FromMilliseconds(1500);
        private readonly TimeSpan _stabilityWindow;
        private string _pendingFingerprint;
        private DateTime _pendingSinceUtc = DateTime.MinValue;
        private string _attemptedFingerprint;

        public LeagueAutoApplyCoordinator()
            : this(DefaultStabilityWindow)
        {
        }

        internal LeagueAutoApplyCoordinator(TimeSpan stabilityWindow)
        {
            _stabilityWindow = stabilityWindow < TimeSpan.Zero ? TimeSpan.Zero : stabilityWindow;
        }

        public LeagueAutoApplyDecision Observe(
            LeagueBuildAdvisorSnapshot snapshot,
            bool enabled,
            DateTime utcNow)
        {
            if (!enabled)
            {
                ClearPending();
                return No("disabled");
            }

            var fingerprint = BuildFingerprint(snapshot);
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                ClearPending();
                return No("not-actionable");
            }

            if (string.Equals(_attemptedFingerprint, fingerprint, StringComparison.Ordinal))
                return No("already-attempted", fingerprint);

            if (!string.Equals(_pendingFingerprint, fingerprint, StringComparison.Ordinal))
            {
                _pendingFingerprint = fingerprint;
                _pendingSinceUtc = utcNow;
                return No("stabilizing", fingerprint);
            }

            if (utcNow - _pendingSinceUtc < _stabilityWindow)
                return No("stabilizing", fingerprint);

            _attemptedFingerprint = fingerprint;
            ClearPending();
            return new LeagueAutoApplyDecision
            {
                ShouldExecute = true,
                Fingerprint = fingerprint,
                Reason = "stable"
            };
        }

        public void CancelPending()
        {
            ClearPending();
        }

        public void ReleaseAttempt(string fingerprint)
        {
            if (!string.IsNullOrWhiteSpace(fingerprint) &&
                string.Equals(_attemptedFingerprint, fingerprint, StringComparison.Ordinal))
                _attemptedFingerprint = null;
        }

        internal static string BuildFingerprint(LeagueBuildAdvisorSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Connected ||
                snapshot.Activity != Performance.LeagueActivityLevel.ChampSelect ||
                snapshot.ChampionId <= 0 || snapshot.Recommendation == null ||
                !string.Equals(snapshot.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(snapshot.Mode) ||
                string.IsNullOrWhiteSpace(snapshot.Position) ||
                string.IsNullOrWhiteSpace(snapshot.Version))
                return null;

            var recommendation = new StringBuilder();
            foreach (var row in snapshot.Recommendation.Rows
                .Where(item => item != null)
                .OrderBy(item => item.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                recommendation.Append('|');
                recommendation.Append(row.Category ?? string.Empty);
                recommendation.Append('=');
                recommendation.Append(row.Recommendation ?? string.Empty);
            }

            return snapshot.ChampionId.ToString(CultureInfo.InvariantCulture) + ":" +
                   snapshot.QueueId.ToString(CultureInfo.InvariantCulture) + ":" +
                   (snapshot.Mode ?? string.Empty).Trim().ToLowerInvariant() + ":" +
                   (snapshot.Position ?? string.Empty).Trim().ToLowerInvariant() + ":" +
                   (snapshot.Version ?? string.Empty).Trim() + recommendation;
        }

        private static LeagueAutoApplyDecision No(string reason, string fingerprint = null)
        {
            return new LeagueAutoApplyDecision
            {
                ShouldExecute = false,
                Fingerprint = fingerprint,
                Reason = reason
            };
        }

        private void ClearPending()
        {
            _pendingFingerprint = null;
            _pendingSinceUtc = DateTime.MinValue;
        }
    }

    internal sealed class LeagueAutoApplyAttemptResult
    {
        public string Status { get; set; }
        public string BuildStatus { get; set; }
        public string ItemSetStatus { get; set; }

        internal static LeagueAutoApplyAttemptResult Aggregate(
            bool buildExpected,
            LeagueBuildApplyResult build,
            bool itemSetExpected,
            LeagueItemSetWriteResult itemSet)
        {
            var result = new LeagueAutoApplyAttemptResult
            {
                BuildStatus = build == null ? (buildExpected ? "failed" : "not-available") : build.Status,
                ItemSetStatus = itemSet == null ? (itemSetExpected ? "failed" : "not-available") : itemSet.Status
            };

            var expected = (buildExpected ? 1 : 0) + (itemSetExpected ? 1 : 0);
            if (expected == 0)
            {
                result.Status = "failed";
                return result;
            }

            var buildComplete = !buildExpected ||
                (build != null && string.Equals(build.Status, "success", StringComparison.OrdinalIgnoreCase));
            var itemSetComplete = !itemSetExpected || (itemSet != null && itemSet.Succeeded);
            var anySucceeded = (build != null && build.AnyApplied) || (itemSet != null && itemSet.Succeeded);

            if (buildComplete && itemSetComplete)
                result.Status = "success";
            else if (anySucceeded)
                result.Status = "partial";
            else
                result.Status = "failed";
            return result;
        }
    }

    internal interface ILeagueAutoApplyExecutor : IDisposable
    {
        Task<LeagueAutoApplyAttemptResult> ExecuteAsync(
            LeagueBuildAdvisorSnapshot snapshot,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// One stable-context transaction. It uses one structured OP.GG payload for both Gate 2 and
    /// Gate 3 plan parsers, then delegates all writes to their already-hardened apply services.
    /// </summary>
    internal sealed class LeagueAutoApplyExecutor : ILeagueAutoApplyExecutor
    {
        private readonly LeagueBuildApplyService _buildApply;
        private readonly LeagueItemSetService _itemSet;
        private readonly IOpggBuildApi _opgg;
        private readonly bool _ownsOpgg;

        public LeagueAutoApplyExecutor(
            LeagueBuildApplyService buildApply,
            LeagueItemSetService itemSet)
            : this(buildApply, itemSet, new OpggBuildApiClient(), true)
        {
        }

        internal LeagueAutoApplyExecutor(
            LeagueBuildApplyService buildApply,
            LeagueItemSetService itemSet,
            IOpggBuildApi opgg,
            bool ownsOpgg = false)
        {
            _buildApply = buildApply ?? throw new ArgumentNullException(nameof(buildApply));
            _itemSet = itemSet ?? throw new ArgumentNullException(nameof(itemSet));
            _opgg = opgg ?? throw new ArgumentNullException(nameof(opgg));
            _ownsOpgg = ownsOpgg;
        }

        public async Task<LeagueAutoApplyAttemptResult> ExecuteAsync(
            LeagueBuildAdvisorSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(LeagueAutoApplyCoordinator.BuildFingerprint(snapshot)))
                return LeagueAutoApplyAttemptResult.Aggregate(false, null, false, null);

            byte[] bytes;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                var path = LeagueBuildAdvisorDataService.BuildPath(
                    snapshot.ChampionId,
                    snapshot.Mode,
                    snapshot.Position,
                    snapshot.Version);
                bytes = await _opgg.TryGetBytesAsync(path, timeout.Token).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes == null || bytes.Length == 0)
                return LeagueAutoApplyAttemptResult.Aggregate(true, null, true, null);

            var buildPlan = DecorateBuildPlan(_buildApply.ParsePlan(bytes), snapshot);
            var itemSetPlan = DecorateItemSetPlan(_itemSet.ParsePlan(bytes), snapshot);
            var buildExpected = buildPlan != null && (buildPlan.HasRunes || buildPlan.HasSpells);
            var itemSetExpected = itemSetPlan != null && itemSetPlan.HasItems;

            LeagueBuildApplyResult buildResult = null;
            LeagueItemSetWriteResult itemSetResult = null;

            if (buildExpected)
            {
                buildResult = await _buildApply.ApplyAsync(buildPlan, cancellationToken).ConfigureAwait(false);
                // A blocked Gate 2 recheck means the Champ Select context drifted. Do not proceed to disk.
                if (buildResult != null && string.Equals(buildResult.Status, "blocked", StringComparison.OrdinalIgnoreCase))
                    return LeagueAutoApplyAttemptResult.Aggregate(true, buildResult, itemSetExpected, null);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (itemSetExpected)
                itemSetResult = await _itemSet.ApplyAsync(itemSetPlan, cancellationToken).ConfigureAwait(false);

            return LeagueAutoApplyAttemptResult.Aggregate(
                buildExpected,
                buildResult,
                itemSetExpected,
                itemSetResult);
        }

        public void Dispose()
        {
            if (_ownsOpgg)
            {
                var disposable = _opgg as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }

        private static LeagueBuildApplyPlan DecorateBuildPlan(
            LeagueBuildApplyPlan plan,
            LeagueBuildAdvisorSnapshot snapshot)
        {
            if (plan == null || snapshot == null) return null;
            plan.ChampionId = snapshot.ChampionId;
            plan.ChampionName = snapshot.ChampionName;
            plan.QueueId = snapshot.QueueId;
            plan.Mode = snapshot.Mode;
            plan.Position = snapshot.Position;
            plan.Version = snapshot.Version;
            return plan;
        }

        private static LeagueItemSetPlan DecorateItemSetPlan(
            LeagueItemSetPlan plan,
            LeagueBuildAdvisorSnapshot snapshot)
        {
            if (plan == null || snapshot == null) return null;
            plan.ChampionId = snapshot.ChampionId;
            plan.ChampionName = snapshot.ChampionName;
            plan.QueueId = snapshot.QueueId;
            plan.Mode = snapshot.Mode;
            plan.Position = snapshot.Position;
            plan.Version = snapshot.Version;
            plan.Uid = BuildItemSetUid(plan);
            plan.Title = BuildItemSetTitle(plan);
            return plan;
        }

        private static string BuildItemSetUid(LeagueItemSetPlan plan)
        {
            return LeagueItemSetService.FilePrefix +
                   plan.ChampionId.ToString(CultureInfo.InvariantCulture) + "-" +
                   SafeSegment(plan.Mode) + "-global-all-" +
                   SafeSegment(plan.Position) + "-" +
                   SafeSegment(plan.Version);
        }

        private static string BuildItemSetTitle(LeagueItemSetPlan plan)
        {
            var champion = string.IsNullOrWhiteSpace(plan.ChampionName)
                ? "#" + plan.ChampionId.ToString(CultureInfo.InvariantCulture)
                : plan.ChampionName.Trim();
            var title = "[OP.GG] " + champion;
            if (!string.IsNullOrWhiteSpace(plan.Mode)) title += " - " + plan.Mode.Trim();
            if (!string.IsNullOrWhiteSpace(plan.Position) &&
                !string.Equals(plan.Position, "none", StringComparison.OrdinalIgnoreCase))
                title += " - " + plan.Position.Trim();
            return title.Length <= 100 ? title : title.Substring(0, 100);
        }

        private static string SafeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "_";
            var input = value.Trim();
            var output = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') ||
                    (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.')
                    output.Append(ch);
                else
                    output.Append('_');
            }
            return output.Length == 0 ? "_" : output.ToString();
        }
    }

    internal sealed class LeagueAutoApplyStatusChangedEventArgs : EventArgs
    {
        public LeagueAutoApplyStatusChangedEventArgs(string status, string fingerprint)
        {
            Status = status ?? string.Empty;
            Fingerprint = fingerprint;
        }

        public string Status { get; private set; }
        public string Fingerprint { get; private set; }
    }

    /// <summary>
    /// Lifecycle owner for the optional background observation loop. Disabled means no League/OP.GG
    /// polling at all. The loop is serial and never starts a second transaction while one is active.
    /// </summary>
    internal sealed class LeagueAutoApplyController : IDisposable
    {
        internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        private readonly object _sync = new object();
        private readonly AppSettings _settings;
        private readonly LeagueBuildAdvisorDataService _readService;
        private readonly ILeagueAutoApplyExecutor _executor;
        private readonly LeagueAutoApplyCoordinator _coordinator;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private Task _loopTask;
        private CancellationTokenSource _activeApply;
        private bool _disposed;
        private string _lastStatus;

        public LeagueAutoApplyController(
            AppSettings settings,
            LeagueBuildAdvisorDataService readService,
            LeagueBuildApplyService buildApply,
            LeagueItemSetService itemSet)
            : this(settings, readService, new LeagueAutoApplyExecutor(buildApply, itemSet), new LeagueAutoApplyCoordinator())
        {
        }

        internal LeagueAutoApplyController(
            AppSettings settings,
            LeagueBuildAdvisorDataService readService,
            ILeagueAutoApplyExecutor executor,
            LeagueAutoApplyCoordinator coordinator)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _readService = readService ?? throw new ArgumentNullException(nameof(readService));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _lastStatus = settings.LeagueAutoApplyRecommended ? "waiting" : "disabled";
        }

        public event EventHandler<LeagueAutoApplyStatusChangedEventArgs> StatusChanged;

        public bool Enabled
        {
            get { return _settings.LeagueAutoApplyRecommended; }
        }

        public string LastStatus
        {
            get { lock (_sync) { return _lastStatus; } }
        }

        public void Start()
        {
            ThrowIfDisposed();
            lock (_sync)
            {
                if (_loopTask != null) return;
                _loopTask = Task.Run(() => LoopAsync(_lifetime.Token));
            }
            Publish(Enabled ? "waiting" : "disabled", null);
        }

        public void SetEnabled(bool enabled)
        {
            ThrowIfDisposed();
            if (_settings.LeagueAutoApplyRecommended == enabled)
            {
                Publish(enabled ? "waiting" : "disabled", null);
                return;
            }

            _settings.LeagueAutoApplyRecommended = enabled;
            _settings.Save();
            if (!enabled)
            {
                _coordinator.CancelPending();
                CancelActiveApply();
                Publish("disabled", null);
            }
            else
            {
                Publish("waiting", null);
            }
        }

        internal async Task RunOneIterationAsync(DateTime utcNow, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!Enabled)
            {
                _coordinator.Observe(null, false, utcNow);
                return;
            }

            var snapshot = await _readService.RefreshAsync(false, cancellationToken).ConfigureAwait(false);
            if (!Enabled)
            {
                _coordinator.Observe(null, false, utcNow);
                return;
            }

            var decision = _coordinator.Observe(snapshot, true, utcNow);
            if (!decision.ShouldExecute) return;

            CancellationTokenSource active;
            lock (_sync)
            {
                if (!Enabled)
                {
                    _coordinator.ReleaseAttempt(decision.Fingerprint);
                    return;
                }
                active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
                _activeApply = active;
            }

            try
            {
                Publish("applying", decision.Fingerprint);
                var result = await _executor.ExecuteAsync(snapshot, active.Token).ConfigureAwait(false);
                Publish(result == null ? "failed" : result.Status, decision.Fingerprint);
            }
            catch (OperationCanceledException)
            {
                if (!Enabled)
                {
                    _coordinator.ReleaseAttempt(decision.Fingerprint);
                    Publish("disabled", decision.Fingerprint);
                }
                else if (!_lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    Publish("failed", decision.Fingerprint);
                }
                else
                {
                    throw;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("League OP.GG auto apply failed", exception);
                Publish("failed", decision.Fingerprint);
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_activeApply, active)) _activeApply = null;
                }
                active.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _lifetime.Cancel(); } catch { }
            CancelActiveApply();
            try
            {
                var loop = _loopTask;
                if (loop != null) loop.Wait(TimeSpan.FromSeconds(1));
            }
            catch { }
            _executor.Dispose();
            _lifetime.Dispose();
        }

        private async Task LoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (Enabled)
                        await RunOneIterationAsync(DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
                    else
                        _coordinator.Observe(null, false, DateTime.UtcNow);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                }
                catch (Exception exception)
                {
                    AppLog.Error("League OP.GG auto apply observer failed", exception);
                    Publish("failed", null);
                }

                try
                {
                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private void CancelActiveApply()
        {
            CancellationTokenSource active;
            lock (_sync) { active = _activeApply; }
            if (active == null) return;
            try { active.Cancel(); } catch { }
        }

        private void Publish(string status, string fingerprint)
        {
            EventHandler<LeagueAutoApplyStatusChangedEventArgs> handler;
            lock (_sync)
            {
                _lastStatus = status ?? string.Empty;
                handler = StatusChanged;
            }
            if (handler != null)
                handler(this, new LeagueAutoApplyStatusChangedEventArgs(status, fingerprint));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LeagueAutoApplyController));
        }
    }
}
