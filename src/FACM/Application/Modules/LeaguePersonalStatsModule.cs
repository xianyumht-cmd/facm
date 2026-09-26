using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.League;
using FACM.Online;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeaguePersonalStatsModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            SettingsModule.ModuleId,
            CloudSyncModule.ModuleId,
            LeagueClientModule.ModuleId,
            LeagueDashboardModule.ModuleId
        };

        private readonly SettingsModule _settingsModule;
        private readonly CloudSyncModule _cloud;
        private readonly LeagueClientModule _leagueClient;
        private readonly LeagueDashboardModule _dashboard;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };

        private PersonalStatsStore _store;
        private CancellationTokenSource _lifetime;
        private PersonalStatsSnapshot _localSnapshot;
        private CloudPersonalRanking _cloudRanking;
        private string _lastCapturedAccountHash = string.Empty;
        private DateTime _lastCaptureAttemptUtc = DateTime.MinValue;
        private int _captureInProgress;

        public LeaguePersonalStatsModule(
            SettingsModule settingsModule,
            CloudSyncModule cloud,
            LeagueClientModule leagueClient,
            LeagueDashboardModule dashboard)
        {
            _settingsModule = settingsModule ?? throw new ArgumentNullException(nameof(settingsModule));
            _cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        }

        public const string ModuleId = "league-personal-stats";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        internal event Action StatsChanged;

        public void Initialize()
        {
            _store = PersonalStatsStore.CreateDefault();
            _lifetime = new CancellationTokenSource();

            var settings = _settingsModule.Settings;
            if (settings != null && settings.LeaguePersonalStatsEnabled)
                _localSnapshot = _store.RecordLaunch(DateTimeOffset.Now);
            else
                _localSnapshot = _store.ReadSnapshot(DateTimeOffset.Now);

            _dashboard.GameflowStateChanged += HandleGameflowStateChanged;
            Application.Idle += HandleIdle;
        }

        private void HandleIdle(object sender, EventArgs e)
        {
            Application.Idle -= HandleIdle;
            if (_settingsModule.Settings != null && _settingsModule.Settings.LeagueCloudRankingEnabled)
                _ = RefreshCloudStateAsync(_lifetime.Token);
        }

        private void HandleGameflowStateChanged(LeagueDashboardPhaseState state)
        {
            if (state == null || !state.Connected)
            {
                _lastCapturedAccountHash = string.Empty;
                return;
            }

            var settings = _settingsModule.Settings;
            if (settings == null || !settings.LeaguePersonalStatsEnabled) return;
            if (DateTime.UtcNow - _lastCaptureAttemptUtc < TimeSpan.FromSeconds(5)) return;
            if (Interlocked.CompareExchange(ref _captureInProgress, 1, 0) != 0) return;

            _lastCaptureAttemptUtc = DateTime.UtcNow;
            _ = CaptureCurrentAccountAsync(_lifetime.Token);
        }

        private async Task CaptureCurrentAccountAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_cloud.IsReady || string.IsNullOrWhiteSpace(_cloud.DeviceId)) return;

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var bytes = await _leagueClient.TryGetBytesAsync(
                        LeagueDashboardDetailsService.SummonerPath,
                        timeout.Token).ConfigureAwait(false);
                    var current = ParseCurrentSummoner(bytes);
                    if (current == null || string.IsNullOrWhiteSpace(current.Puuid)) return;

                    var hash = PersonalStatsStore.CreateAccountKeyHash(_cloud.DeviceId, current.Puuid);
                    if (string.Equals(hash, _lastCapturedAccountHash, StringComparison.OrdinalIgnoreCase))
                        return;

                    bool isNew;
                    _localSnapshot = _store.RecordAccount(
                        hash,
                        current.DisplayName,
                        current.Region,
                        DateTimeOffset.Now,
                        out isNew);
                    _lastCapturedAccountHash = hash;
                    RaiseStatsChanged();

                    var settings = _settingsModule.Settings;
                    if (settings != null && settings.LeagueCloudRankingEnabled)
                    {
                        await SynchronizeAccountAsync(hash, current.Region, cancellationToken).ConfigureAwait(false);
                        await RefreshCloudStateAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Info("Personal stats account capture skipped: " + exception.GetType().Name);
            }
            finally
            {
                Interlocked.Exchange(ref _captureInProgress, 0);
            }
        }

        internal LeaguePersonalStatsViewSnapshot GetSnapshot()
        {
            var local = _localSnapshot ?? (_store == null
                ? new PersonalStatsSnapshot()
                : _store.ReadSnapshot(DateTimeOffset.Now));
            var settings = _settingsModule.Settings;
            return new LeaguePersonalStatsViewSnapshot
            {
                PlayedAccounts = local.PlayedAccounts,
                ActiveDays = local.ActiveDays,
                CurrentStreakDays = local.CurrentStreakDays,
                Recent7ActiveDays = local.Recent7ActiveDays,
                Recent30ActiveDays = local.Recent30ActiveDays,
                NewAccountsThisMonth = local.NewAccountsThisMonth,
                FirstSeenUtc = local.FirstSeenUtc,
                LastSeenUtc = local.LastSeenUtc,
                PersonalStatsEnabled = settings == null || settings.LeaguePersonalStatsEnabled,
                CloudRankingEnabled = settings != null && settings.LeagueCloudRankingEnabled,
                CloudRank = _cloudRanking == null ? 0 : _cloudRanking.rank,
                CloudRankedUsers = _cloudRanking == null ? 0 : _cloudRanking.total_ranked_users,
                CloudPercentile = _cloudRanking == null ? 0D : _cloudRanking.percentile,
                CloudPlayedAccounts = _cloudRanking == null ? 0 : _cloudRanking.played_accounts,
                RecentAccounts = BuildRecentAccountViews(local.RecentAccounts)
            };
        }

        internal Form CreateForm(UiTextCatalog ui)
        {
            return new LeaguePersonalStatsForm(this, _settingsModule.Settings, ui);
        }

        internal async Task ApplyPreferencesAsync(
            bool personalStatsEnabled,
            bool cloudRankingEnabled,
            CancellationToken cancellationToken)
        {
            var settings = _settingsModule.Settings;
            if (settings == null) return;

            settings.LeaguePersonalStatsEnabled = personalStatsEnabled;
            settings.LeagueCloudRankingEnabled = personalStatsEnabled && cloudRankingEnabled;
            settings.Save();

            if (settings.LeaguePersonalStatsEnabled)
                _localSnapshot = _store.RecordLaunch(DateTimeOffset.Now);
            else
                _localSnapshot = _store.ReadSnapshot(DateTimeOffset.Now);

            _cloudRanking = null;
            RaiseStatsChanged();

            if (!_cloud.IsReady) return;

            try
            {
                await _cloud.SetRankingOptInAsync(settings.LeagueCloudRankingEnabled, cancellationToken).ConfigureAwait(false);
                if (settings.LeagueCloudRankingEnabled)
                {
                    await SynchronizeAllAccountsAsync(cancellationToken).ConfigureAwait(false);
                    await RefreshCloudStateAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLog.Info("Personal stats cloud preference sync skipped: " + exception.GetType().Name);
            }
        }

        internal async Task RefreshCloudStateAsync(CancellationToken cancellationToken)
        {
            var settings = _settingsModule.Settings;
            if (settings == null || !settings.LeagueCloudRankingEnabled || !_cloud.IsReady)
            {
                _cloudRanking = null;
                RaiseStatsChanged();
                return;
            }

            try
            {
                await _cloud.SetRankingOptInAsync(true, cancellationToken).ConfigureAwait(false);
                _cloudRanking = await _cloud.GetPersonalRankingAsync(cancellationToken).ConfigureAwait(false);
                RaiseStatsChanged();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _cloudRanking = null;
                AppLog.Info("Personal stats ranking refresh skipped: " + exception.GetType().Name);
                RaiseStatsChanged();
            }
        }

        private async Task SynchronizeAllAccountsAsync(CancellationToken cancellationToken)
        {
            foreach (var account in _store.ReadAccountsForSync(DateTimeOffset.Now))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _cloud.RecordAccountAsync(account, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task SynchronizeAccountAsync(
            string accountKeyHash,
            string region,
            CancellationToken cancellationToken)
        {
            var account = new PersonalStatsAccountRecord
            {
                AccountKeyHash = accountKeyHash,
                Region = region ?? string.Empty,
                FirstSeenUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("o"),
                LastSeenUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("o"),
                SeenCount = 1
            };

            var matching = _store.ReadAccountsForSync(DateTimeOffset.Now);
            foreach (var item in matching)
            {
                if (string.Equals(item.AccountKeyHash, accountKeyHash, StringComparison.OrdinalIgnoreCase))
                {
                    account = item;
                    break;
                }
            }
            await _cloud.RecordAccountAsync(account, cancellationToken).ConfigureAwait(false);
        }

        private CurrentSummonerIdentity ParseCurrentSummoner(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var root = _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                if (root == null) return null;
                var gameName = ReadString(root, "gameName");
                var tagLine = ReadString(root, "tagLine");
                return new CurrentSummonerIdentity
                {
                    Puuid = ReadString(root, "puuid"),
                    DisplayName = FirstNonEmpty(
                        ReadString(root, "displayName"),
                        string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(tagLine)
                            ? null
                            : gameName.Trim() + "#" + tagLine.Trim()),
                    Region = FirstNonEmpty(
                        ReadString(root, "platformId"),
                        ReadString(root, "region"))
                };
            }
            catch
            {
                return null;
            }
        }

        private static IReadOnlyList<LeaguePersonalStatsAccountView> BuildRecentAccountViews(
            IReadOnlyList<PersonalStatsAccountRecord> accounts)
        {
            if (accounts == null || accounts.Count == 0)
                return Array.Empty<LeaguePersonalStatsAccountView>();

            return accounts
                .Where(item => item != null)
                .Select(item => new LeaguePersonalStatsAccountView
                {
                    DisplayName = item.DisplayName ?? string.Empty,
                    AnonymousId = CreateAnonymousId(item.AccountKeyHash),
                    Region = item.Region ?? string.Empty,
                    FirstSeenUtc = ParseUtc(item.FirstSeenUtc),
                    LastSeenUtc = ParseUtc(item.LastSeenUtc),
                    SeenCount = Math.Max(1, item.SeenCount)
                })
                .ToArray();
        }

        private static string CreateAnonymousId(string accountKeyHash)
        {
            if (string.IsNullOrWhiteSpace(accountKeyHash) || accountKeyHash.Length < 8)
                return string.Empty;
            return accountKeyHash.Substring(0, 8).ToUpperInvariant();
        }

        private static DateTimeOffset? ParseUtc(string value)
        {
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(value, out parsed) ? parsed.ToUniversalTime() : (DateTimeOffset?)null;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : null;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return null;
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value)) return value;
            return null;
        }

        private void RaiseStatsChanged()
        {
            var handlers = StatsChanged;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception exception) { AppLog.Info("Personal stats UI notification skipped: " + exception.GetType().Name); }
            }
        }

        public void Dispose()
        {
            Application.Idle -= HandleIdle;
            _dashboard.GameflowStateChanged -= HandleGameflowStateChanged;

            var lifetime = _lifetime;
            _lifetime = null;
            if (lifetime != null)
            {
                try { lifetime.Cancel(); }
                catch { }
                lifetime.Dispose();
            }

            _store = null;
            _localSnapshot = null;
            _cloudRanking = null;
            _lastCapturedAccountHash = string.Empty;
        }

        private sealed class CurrentSummonerIdentity
        {
            public string Puuid { get; set; }
            public string DisplayName { get; set; }
            public string Region { get; set; }
        }
    }
}
