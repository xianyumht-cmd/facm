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
        private GgmanRegisteredStats _registeredStats;
        private string _registeredStatsOwner;
        private GgmanRegisteredStatsClient _registeredClient;
        private string _localDeviceId;
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
            _registeredClient = new GgmanRegisteredStatsClient();
            _localDeviceId = CloudIdentityStore.CreateDefault().LoadOrCreate().DeviceId;
            _localSnapshot = _store.RecordLaunch(DateTimeOffset.Now);

            _dashboard.GameflowStateChanged += HandleGameflowStateChanged;
            Application.Idle += HandleIdle;
        }

        private void HandleIdle(object sender, EventArgs e)
        {
            Application.Idle -= HandleIdle;
            if (GgmanAccountSession.Current != null)
                _ = RefreshCloudStateAsync(_lifetime.Token);
        }

        private void HandleGameflowStateChanged(LeagueDashboardPhaseState state)
        {
            if (state == null || !state.Connected)
            {
                _lastCapturedAccountHash = string.Empty;
                return;
            }

            if (DateTime.UtcNow - _lastCaptureAttemptUtc < TimeSpan.FromSeconds(5)) return;
            if (Interlocked.CompareExchange(ref _captureInProgress, 1, 0) != 0) return;

            _lastCaptureAttemptUtc = DateTime.UtcNow;
            _ = CaptureCurrentAccountAsync(_lifetime.Token);
        }

        private async Task CaptureCurrentAccountAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_localDeviceId)) return;

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var bytes = await _leagueClient.TryGetBytesAsync(
                        LeagueDashboardDetailsService.SummonerPath,
                        timeout.Token).ConfigureAwait(false);
                    var current = ParseCurrentSummoner(bytes);
                    if (current == null || string.IsNullOrWhiteSpace(current.Puuid)) return;

                    var hash = PersonalStatsStore.CreateAccountKeyHash(_localDeviceId, current.Puuid);
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

                    var registered = GgmanAccountSession.Current;
                    if (registered != null)
                    {
                        try
                        {
                            await _registeredClient.TouchAsync(registered, cancellationToken).ConfigureAwait(false);
                            await _registeredClient.RecordAccountAsync(registered,
                                GgmanRegisteredStatsClient.CreateRegisteredAccountHash(registered.UserId,
                                    current.Puuid), cancellationToken).ConfigureAwait(false);
                            await RefreshCloudStateAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error)
                        {
                            AppLog.Info("Registered account stats update skipped: " + error.GetType().Name);
                        }
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
            var currentAccount = GgmanAccountSession.Current;
            var registered = currentAccount != null && currentAccount.UserId == _registeredStatsOwner
                ? _registeredStats : null;
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
                PersonalStatsEnabled = true,
                CloudRankingEnabled = currentAccount != null &&
                    (registered == null || registered.RankingVisible),
                CloudRank = registered == null ? 0 : registered.Rank,
                CloudRankedUsers = registered == null ? 0 : registered.TotalRankedUsers,
                CloudPercentile = registered == null ? 0D : registered.Percentile,
                CloudPlayedAccounts = registered == null ? 0 : registered.PlayedAccounts,
                RecentAccounts = BuildRecentAccountViews(local.RecentAccounts)
            };
        }

        internal Form CreateForm(UiTextCatalog ui)
        {
            return new LeaguePersonalStatsForm(this, _settingsModule.Settings, ui);
        }

        internal async Task RefreshCloudStateAsync(CancellationToken cancellationToken)
        {
            var account = GgmanAccountSession.Current;
            if (account == null)
            {
                _registeredStatsOwner = null;
                _registeredStats = null;
                RaiseStatsChanged();
                return;
            }
            if (_registeredStatsOwner != account.UserId)
            {
                _registeredStatsOwner = null;
                _registeredStats = null;
                RaiseStatsChanged();
            }
            try
            {
                await _registeredClient.TouchAsync(account, cancellationToken).ConfigureAwait(false);
                var current = await _registeredClient.GetStatsAsync(account, cancellationToken).ConfigureAwait(false);
                var active = GgmanAccountSession.Current;
                if (active == null || active.UserId != account.UserId) return;
                _registeredStatsOwner = account.UserId;
                _registeredStats = current;
                RaiseStatsChanged();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                _registeredStatsOwner = null;
                _registeredStats = null;
                AppLog.Info("Registered personal stats refresh skipped: " + exception.GetType().Name);
                RaiseStatsChanged();
            }
        }

        internal PersonalStatsLegacySummary ReadLegacySummary()
        {
            return _store.ReadLegacySummary(DateTimeOffset.Now);
        }

        internal async Task ImportLegacyAsync(CancellationToken token)
        {
            var account = GgmanAccountSession.Current;
            if (account == null) throw new InvalidOperationException("请先登录 GGman 邮箱账号。");
            var summary = _store.ReadLegacySummary(DateTimeOffset.Now);
            var source = GgmanRegisteredStatsClient.CreateLegacySourceKey(
                account.UserId, _localDeviceId);
            await _registeredClient.TouchAsync(account, token).ConfigureAwait(false);
            await _registeredClient.ImportLegacyAsync(account, source, summary.PlayedAccounts,
                summary.ActiveDays, token).ConfigureAwait(false);
            await RefreshCloudStateAsync(token).ConfigureAwait(false);
        }

        internal async Task SetRegisteredRankingVisibleAsync(bool visible, CancellationToken token)
        {
            var account = GgmanAccountSession.Current;
            if (account == null) throw new InvalidOperationException("请先登录 GGman 邮箱账号。");
            await _registeredClient.SetRankingVisibleAsync(account, visible, token).ConfigureAwait(false);
            await RefreshCloudStateAsync(token).ConfigureAwait(false);
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
            _registeredStats = null;
            _registeredStatsOwner = null;
            if (_registeredClient != null) _registeredClient.Dispose();
            _registeredClient = null;
            _localDeviceId = null;
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
