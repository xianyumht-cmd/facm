using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class UsageTelemetryModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            CloudSyncModule.ModuleId
        };

        private static readonly HashSet<string> AllowedEventNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "app_launch",
            "league_dashboard_open",
            "league_player_open",
            "league_live_open",
            "mayhem_lookup_open",
            "personal_stats_open",
            "opgg_advisor_open",
            "efficiency_open",
            "game_repair_open",
            "presence_open",
            "champ_select_companion_open"
        };

        private readonly CloudSyncModule _cloudSync;
        private readonly UsageTelemetryConsentStore _consentStore = new UsageTelemetryConsentStore();
        private readonly object _sync = new object();
        private readonly Dictionary<string, int> _pending = new Dictionary<string, int>(StringComparer.Ordinal);
        private CancellationTokenSource _cancellation;
        private System.Windows.Forms.Timer _flushTimer;
        private bool _enabled;
        private bool _disposed;

        public const string ModuleId = "usage-telemetry";

        public UsageTelemetryModule(CloudSyncModule cloudSync)
        {
            _cloudSync = cloudSync ?? throw new ArgumentNullException(nameof(cloudSync));
        }

        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }
        internal bool Enabled { get { return _enabled; } }

        internal static UsageTelemetryModule Current { get; private set; }

        public void Initialize()
        {
            _enabled = _consentStore.Load();
            Current = this;
            _cancellation = new CancellationTokenSource();
            _flushTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _flushTimer.Tick += HandleFlushTimerTick;
            _flushTimer.Start();
        }

        internal static void Record(string eventName)
        {
            var current = Current;
            if (current != null) current.RecordEvent(eventName);
        }

        internal static bool IsEnabled()
        {
            return Current != null && Current.Enabled;
        }

        internal static void SetEnabled(bool enabled)
        {
            var current = Current;
            if (current != null) current.SetConsent(enabled);
        }

        private void RecordEvent(string eventName)
        {
            if (_disposed || !_enabled || !AllowedEventNames.Contains(eventName)) return;
            lock (_sync)
            {
                int count;
                _pending.TryGetValue(eventName, out count);
                _pending[eventName] = Math.Min(1000, count + 1);
            }
        }

        private void SetConsent(bool enabled)
        {
            if (_disposed) return;
            if (_enabled == enabled)
            {
                _consentStore.Save(enabled);
                return;
            }

            _enabled = enabled;
            _consentStore.Save(enabled);
            if (!enabled)
            {
                lock (_sync) _pending.Clear();
            }
        }

        private async void HandleFlushTimerTick(object sender, EventArgs e)
        {
            await FlushAsync().ConfigureAwait(true);
        }

        private async Task FlushAsync()
        {
            if (_disposed || !_enabled || !_cloudSync.IsReady) return;

            Dictionary<string, int> batch;
            lock (_sync)
            {
                if (_pending.Count == 0) return;
                batch = new Dictionary<string, int>(_pending, StringComparer.Ordinal);
                _pending.Clear();
            }

            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                await _cloudSync.RecordUsageAsync(
                    batch,
                    version == null ? "0.0.0.0" : version.ToString(),
                    _cancellation.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                Requeue(batch);
            }
            catch (Exception exception)
            {
                AppLog.Info("Usage telemetry flush skipped: " + exception.GetType().Name);
                Requeue(batch);
            }
        }

        private void Requeue(IReadOnlyDictionary<string, int> batch)
        {
            if (!_enabled) return;
            lock (_sync)
            {
                foreach (var item in batch)
                {
                    int existing;
                    _pending.TryGetValue(item.Key, out existing);
                    _pending[item.Key] = Math.Min(1000, existing + item.Value);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_flushTimer != null)
            {
                _flushTimer.Stop();
                _flushTimer.Tick -= HandleFlushTimerTick;
                _flushTimer.Dispose();
                _flushTimer = null;
            }

            var cancellation = _cancellation;
            _cancellation = null;
            if (cancellation != null)
            {
                try { cancellation.Cancel(); }
                catch { }
                cancellation.Dispose();
            }

            if (ReferenceEquals(Current, this)) Current = null;
            lock (_sync) _pending.Clear();
        }

        internal static void ValidateForSmokeTest()
        {
            Require(AllowedEventNames.Contains("league_dashboard_open"), "Telemetry rejected a known event name.");
            Require(AllowedEventNames.Contains("champ_select_companion_open"), "Telemetry rejected a known event name.");
            Require(!AllowedEventNames.Contains("account_hash"), "Telemetry allowlist accepted an account-like event.");
            Require(!AllowedEventNames.Contains("League Dashboard Open"), "Telemetry allowlist accepted an invalid event name.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
