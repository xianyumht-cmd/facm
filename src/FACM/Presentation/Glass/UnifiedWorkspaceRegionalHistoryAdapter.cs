using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceRegionalHistoryAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Action _cancelSelected;
        private CancellationTokenSource _cancellation;
        private int _generation;
        private LeagueRemotePlayerHistoryResult _history;
        private LeagueClientSession _session;
        private LeaguePlayerProfile _owner;
        private string _targetPuuid;

        internal UnifiedWorkspaceRegionalHistoryAdapter(Func<bool> canPublish,
            Action<string> publish, Action cancelSelected)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _cancelSelected = cancelSelected ?? throw new ArgumentNullException(nameof(cancelSelected));
        }

        internal void SendOptions(LeaguePlayerModule module)
        {
            if (!_canPublish()) return;
            var session = module == null ? null : module.UnifiedHistorySession;
            var servers = LeagueSgpMatchHistoryApiClient.AvailableServers(session);
            var current = LeagueSgpMatchHistoryApiClient.CurrentServerId(session);
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "history-region-options",
                available = servers.Length > 0,
                servers = servers.Where(x => x.Id != current).Take(64)
                    .Select(x => new { id = x.Id, name = x.Name }).ToArray()
            }));
        }

        internal async void RefreshCatalog(LeaguePlayerModule module)
        {
            if (!_canPublish() || _cancellation != null) return;
            if (module == null) { SendOptions(null); return; }
            var session = module.UnifiedHistorySession;
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            _cancellation = source;
            var generation = _generation;
            try
            {
                await LeagueSgpServerCatalog.TryRefreshAsync(source.Token);
                if (Current(module, session, source, generation)) SendOptions(module);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("SGP region catalog refresh skipped: " + exception.GetType().Name);
            }
            finally { Finish(source); }
        }

        internal async void Search(LeaguePlayerModule module, LeagueUnifiedHistorySnapshot own,
            LeagueClientSession ownSession, string request)
        {
            if (!_canPublish() || string.IsNullOrWhiteSpace(request) || request.Length > 220) return;
            string riotId, server;
            if (!TryParseRequest(request, out riotId, out server))
            { PostResult("InvalidQuery", null, null); return; }
            var session = module == null ? null : module.UnifiedHistorySession;
            if (module == null || own == null || own.Profile == null || session == null ||
                !LeagueEncounterReplayService.SessionMatches(ownSession, session))
            { PostResult("AccountChanged", null, null); return; }
            if (server != null && LeagueSgpMatchHistoryApiClient.ResolveServer(session, server) == null)
            { PostResult("Unavailable", null, null); return; }
            _cancelSelected();
            Cancel();
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(27));
            _cancellation = source;
            var generation = _generation;
            var expectedProfile = own.Profile;
            try
            {
                var found = await module.SearchUnifiedRiotPlayerAsync(
                    expectedProfile, riotId, server, source.Token);
                if (!Current(module, session, source, generation))
                {
                    if (Current(module, session, source, generation, false))
                        PostResult("AccountChanged", null, null);
                    return;
                }
                if (found == null || found.Status != LeagueRiotPlayerSearchStatus.Found ||
                    found.Player == null)
                {
                    PostResult(found == null ? "Unavailable" : found.Status.ToString(), null, null);
                    return;
                }
                var result = await module.ReadUnifiedRegionalHistoryAsync(
                    expectedProfile, found.Player.PuuId, server, source.Token);
                if (!Current(module, session, source, generation))
                {
                    if (Current(module, session, source, generation, false))
                        PostResult("AccountChanged", null, null);
                    return;
                }
                if (result == null || result.Status != LeagueRiotPlayerSearchStatus.Found ||
                    result.Page == null)
                {
                    PostResult(result == null ? "Unavailable" : result.Status.ToString(), null, null);
                    return;
                }
                _history = result;
                _session = session;
                _owner = expectedProfile;
                _targetPuuid = found.Player.PuuId;
                PostResult("Found", found.Player.PlayerName,
                    server == null ? LeagueSgpMatchHistoryApiClient.CurrentServerId(session) : server);
            }
            catch (OperationCanceledException)
            {
                if (Current(module, session, source, generation, false, false))
                    PostResult("Unavailable", null, null);
            }
            catch (Exception exception)
            {
                AppLog.Info("Regional Riot ID lookup unavailable: " + exception.GetType().Name);
                if (Current(module, session, source, generation, false, false))
                    PostResult("Unavailable", null, null);
            }
            finally { Finish(source); }
        }

        internal void ReadDetail(LeaguePlayerModule module, UnifiedWorkspaceHistoryDetailAdapter detail,
            string raw)
        {
            detail.StartRegional(module, _history, _session, _owner, _targetPuuid, raw);
        }

        private void PostResult(string status, string name, string server)
        {
            if (!_canPublish()) return;
            var page = _history == null ? null : _history.Page;
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "history-region-results", status, name, region = server,
                rows = status != "Found" || page == null ? new object[0] :
                    page.Matches.Take(20).Select(m => new
                    {
                        id = m.GameId.ToString(CultureInfo.InvariantCulture),
                        played = m.GameCreationLocal.ToString("yyyy-MM-dd HH:mm"),
                        champion = m.ChampionName,
                        championId = m.ChampionId,
                        queue = m.QueueId,
                        mode = m.GameMode,
                        outcome = m.StatisticsAvailable ? m.Win ? "win" : "loss" : "unknown",
                        kills = m.Kills, deaths = m.Deaths, assists = m.Assists
                    }).ToArray()
            }));
        }

        private bool Current(LeaguePlayerModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true,
            bool checkCancellation = true)
        {
            return _canPublish() && ReferenceEquals(source, _cancellation) &&
                generation == _generation && (!checkCancellation || !source.IsCancellationRequested) &&
                (!checkSession || LeagueEncounterReplayService.SessionMatches(
                    session, module.UnifiedHistorySession));
        }

        private static bool TryParseRequest(string request, out string riotId, out string server)
        {
            riotId = null; server = null;
            try
            {
                var fields = new JavaScriptSerializer { MaxJsonLength = 220 }
                    .Deserialize<System.Collections.Generic.Dictionary<string, object>>(request);
                if (fields == null || fields.Count != 2) return false;
                object name, region;
                if (!fields.TryGetValue("riotId", out name) ||
                    !fields.TryGetValue("serverId", out region)) return false;
                riotId = name as string;
                var originalServer = region as string;
                if (riotId == null || originalServer == null || originalServer.Length > 45 ||
                    originalServer.Any(char.IsControl)) return false;
                server = originalServer.Length == 0 ? null : originalServer;
                string gameName, tag;
                return riotId.Length <= 52 && LeaguePlayerDataService.TryParseRiotId(
                    riotId, out gameName, out tag);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private void Finish(CancellationTokenSource source)
        {
            if (ReferenceEquals(source, _cancellation)) _cancellation = null;
            source.Dispose();
        }

        internal void Cancel()
        {
            _generation++;
            _history = null;
            _owner = null;
            _session = null;
            _targetPuuid = null;
            var source = _cancellation;
            _cancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        internal static void ValidateForSmokeTest()
        {
            string riotId, server;
            if (TryParseRequest("{\"riotId\":\"A#B\",\"serverId\":1}", out riotId, out server) ||
                TryParseRequest("{\"riotId\":\"A#B\",\"serverId\":\"\",\"extra\":true}",
                    out riotId, out server))
                throw new InvalidOperationException("Regional history request contract widened.");
            var adapter = new UnifiedWorkspaceRegionalHistoryAdapter(() => false,
                delegate { throw new InvalidOperationException("Hidden regional request published."); }, () => { });
            adapter.Cancel();
        }
    }
}
