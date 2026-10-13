using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.League;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class GgmanAutoSyncModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            SettingsModule.ModuleId, LeagueClientModule.ModuleId, LeagueDashboardModule.ModuleId
        };
        private readonly SettingsModule _settings;
        private readonly LeagueClientModule _league;
        private readonly LeagueDashboardModule _dashboard;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private CancellationTokenSource _lifetime;
        private GgmanAutoSyncService _service;
        private DateTime _nextSummonerLookupUtc;
        private int _summonerLookupBusy;

        internal GgmanAutoSyncModule(SettingsModule settings, LeagueClientModule league,
            LeagueDashboardModule dashboard)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _league = league ?? throw new ArgumentNullException(nameof(league));
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        }

        public string Id { get { return "ggman-auto-sync"; } }
        public IReadOnlyList<string> Dependencies { get { return ModuleDependencies; } }

        public void Initialize()
        {
            if (_settings.Settings == null)
                throw new InvalidOperationException("Application settings must be loaded first.");
            _service = new GgmanAutoSyncService(_settings.Settings);
            _lifetime = new CancellationTokenSource();
            _dashboard.GameflowStateChanged += OnGameflowStateChanged;
        }

        private void OnGameflowStateChanged(LeagueDashboardPhaseState state)
        {
            var service = _service;
            if (service == null || state == null || _lifetime == null ||
                _lifetime.IsCancellationRequested) return;
            service.NotifyGameflowPhase(state.Phase);
            if (!state.Connected || GgmanAccountSession.Current == null ||
                !_settings.Settings.GameAutoRestoreEnabled ||
                DateTime.UtcNow < _nextSummonerLookupUtc ||
                Interlocked.CompareExchange(ref _summonerLookupBusy, 1, 0) != 0)
                return;
            _nextSummonerLookupUtc = DateTime.UtcNow.AddSeconds(5);
            _ = ReadCurrentSummonerAsync(_lifetime.Token);
        }

        private async Task ReadCurrentSummonerAsync(CancellationToken token)
        {
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    var bytes = await _league.TryGetBytesAsync(
                        LeagueDashboardDetailsService.SummonerPath, timeout.Token)
                        .ConfigureAwait(false);
                    if (bytes == null || bytes.Length == 0) return;
                    var data = _json.DeserializeObject(Encoding.UTF8.GetString(bytes))
                        as Dictionary<string, object>;
                    object raw;
                    if (data == null || !data.TryGetValue("puuid", out raw)) return;
                    var puuid = raw as string;
                    if (string.IsNullOrWhiteSpace(puuid) || puuid.Length > 128) return;
                    string hash;
                    using (var sha = SHA256.Create())
                        hash = BitConverter.ToString(sha.ComputeHash(
                            Encoding.UTF8.GetBytes(puuid))).Replace("-", string.Empty);
                    if (!token.IsCancellationRequested && GgmanAccountSession.Current != null)
                        _service?.NotifyRiotAccount(hash);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                AppLog.Info("Game profile account detection deferred: " + error.GetType().Name);
            }
            finally { Interlocked.Exchange(ref _summonerLookupBusy, 0); }
        }

        public void Dispose()
        {
            _dashboard.GameflowStateChanged -= OnGameflowStateChanged;
            if (_lifetime != null)
            {
                _lifetime.Cancel();
                _lifetime.Dispose();
                _lifetime = null;
            }
            if (_service != null) _service.Dispose();
            _service = null;
        }
    }
}
