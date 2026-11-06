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
    internal sealed partial class UnifiedWorkspaceForm
    {
        private CancellationTokenSource _regionalReadCancellation;
        private int _regionalReadGeneration;
        private LeagueRemotePlayerHistoryResult _regionalHistory;
        private LeagueClientSession _regionalSession;
        private LeaguePlayerProfile _regionalOwner;
        private string _regionalTargetPuuid;

        private void SendRegionalOptions()
        {
            if (_navigation.Current != "player-advanced" || !_ready || IsDisposed ||
                _closing || _browser.CoreWebView2 == null) return;
            var session = LeagueHubUiBridge.UnifiedPlayer == null ? null :
                LeagueHubUiBridge.UnifiedPlayer.UnifiedHistorySession;
            var servers = LeagueSgpMatchHistoryApiClient.AvailableServers(session);
            var current = LeagueSgpMatchHistoryApiClient.CurrentServerId(session);
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "history-region-options",
                available = servers.Length > 0,
                servers = servers.Where(x => x.Id != current).Take(64)
                    .Select(x => new { id = x.Id, name = x.Name }).ToArray()
            }));
        }

        private async void RefreshRegionalCatalog()
        {
            if (_navigation.Current != "player-advanced" || _regionalReadCancellation != null) return;
            var module = LeagueHubUiBridge.UnifiedPlayer;
            if (module == null) { SendRegionalOptions(); return; }
            var session = module.UnifiedHistorySession;
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            _regionalReadCancellation = source;
            var generation = _regionalReadGeneration;
            try
            {
                await LeagueSgpServerCatalog.TryRefreshAsync(source.Token);
                if (RegionalRequestCurrent(module, session, source, generation)) SendRegionalOptions();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("SGP region catalog refresh skipped: " + exception.GetType().Name);
            }
            finally
            {
                if (ReferenceEquals(source, _regionalReadCancellation))
                    _regionalReadCancellation = null;
                source.Dispose();
            }
        }

        private async void SearchRegionalRiotId(string request)
        {
            if (_navigation.Current != "player-advanced" || string.IsNullOrWhiteSpace(request) ||
                request.Length > 220) return;
            string riotId, server;
            if (!TryParseRegionalRequest(request, out riotId, out server))
            {
                PostRegionalResult("InvalidQuery", null, null); return;
            }
            var module = LeagueHubUiBridge.UnifiedPlayer;
            var own = _advancedHistorySnapshot;
            var session = module == null ? null : module.UnifiedHistorySession;
            if (module == null || own == null || own.Profile == null || session == null ||
                !LeagueEncounterReplayService.SessionMatches(_advancedHistorySession, session))
            {
                PostRegionalResult("AccountChanged", null, null); return;
            }
            if (server != null && LeagueSgpMatchHistoryApiClient.ResolveServer(session, server) == null)
            {
                PostRegionalResult("Unavailable", null, null); return;
            }
            CancelRegionalHistoryRead();
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(27));
            _regionalReadCancellation = source;
            var generation = _regionalReadGeneration;
            var expectedProfile = own.Profile;
            try
            {
                var found = await module.SearchUnifiedRiotPlayerAsync(
                    expectedProfile, riotId, server, source.Token);
                if (!RegionalRequestCurrent(module, session, source, generation)) return;
                if (found == null || found.Status != LeagueRiotPlayerSearchStatus.Found ||
                    found.Player == null)
                {
                    PostRegionalResult(found == null ? "Unavailable" : found.Status.ToString(), null, null);
                    return;
                }
                var result = await module.ReadUnifiedRegionalHistoryAsync(
                    expectedProfile, found.Player.PuuId, server, source.Token);
                if (!RegionalRequestCurrent(module, session, source, generation)) return;
                if (result == null || result.Status != LeagueRiotPlayerSearchStatus.Found ||
                    result.Page == null)
                {
                    PostRegionalResult(result == null ? "Unavailable" : result.Status.ToString(), null, null);
                    return;
                }
                _regionalHistory = result;
                _regionalSession = session;
                _regionalOwner = expectedProfile;
                _regionalTargetPuuid = found.Player.PuuId;
                PostRegionalResult("Found", found.Player.PlayerName,
                    server == null ? LeagueSgpMatchHistoryApiClient.CurrentServerId(session) : server);
            }
            catch (OperationCanceledException)
            {
                if (RegionalRequestCurrent(module, session, source, generation, false, false))
                    PostRegionalResult("Unavailable", null, null);
            }
            catch (Exception exception)
            {
                AppLog.Info("Regional Riot ID lookup unavailable: " + exception.GetType().Name);
                if (RegionalRequestCurrent(module, session, source, generation, false, false))
                    PostRegionalResult("Unavailable", null, null);
            }
            finally
            {
                if (ReferenceEquals(source, _regionalReadCancellation))
                    _regionalReadCancellation = null;
                source.Dispose();
            }
        }

        private void ReadRegionalMatchDetail(string raw)
        {
            if (_navigation.Current != "player-advanced" || string.IsNullOrEmpty(raw) || raw.Length > 20)
                return;
            long id;
            if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out id) ||
                id <= 0 || _regionalHistory == null || _regionalHistory.Page == null ||
                _regionalSession == null || _regionalOwner == null ||
                !LeagueDynamicMessageService.SafePuuid(_regionalTargetPuuid))
            {
                PostAdvancedDetailStatus(raw, "Unavailable"); return;
            }
            var module = LeagueHubUiBridge.UnifiedPlayer;
            if (module == null || !LeagueEncounterReplayService.SessionMatches(
                _regionalSession, module.UnifiedHistorySession))
            {
                PostAdvancedDetailStatus(raw, "WrongAccount");return;
            }
            var match = _regionalHistory.Page.Matches.FirstOrDefault(x => x.GameId == id);
            if (match == null) { PostAdvancedDetailStatus(raw, "Unavailable"); return; }
            // Repeated selections invalidate the former in-flight detail read.
            CancelAdvancedHistoryDetail();
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            _advancedHistoryDetailCancellation = source;
            var generation = _advancedHistoryDetailGeneration;
            ReadRegionalDetailAsync(module, match, _regionalSession, _regionalOwner.PuuId,
                _regionalTargetPuuid, raw, source, generation);
        }

        private async void ReadRegionalDetailAsync(LeaguePlayerModule module,
            LeaguePlayerMatchSummary match, LeagueClientSession session, string self, string target,
            string raw, CancellationTokenSource source, int generation)
        {
            try
            {
                var detail = await module.ReadUnifiedRemoteDetailAsync(session, self, target,
                    match, source.Token);
                if (!AdvancedHistoryRequestCurrent(module, session, source, generation)) return;
                if (detail == null || detail.Status != LeagueEncounterMatchDetailStatus.Ready ||
                    detail.Match == null)
                {
                    PostAdvancedDetailStatus(raw, detail == null ? "Unavailable" : detail.Status.ToString());
                    return;
                }
                RememberAdvancedMatchDetail(raw, detail, session);
                PostAdvancedHistoryDetail(raw, detail);
            }
            catch (OperationCanceledException)
            {
                if (AdvancedHistoryRequestCurrent(module, session, source, generation, false))
                    PostAdvancedDetailStatus(raw, "Unavailable");
            }
            catch (Exception exception)
            {
                AppLog.Info("Regional match detail skipped: " + exception.GetType().Name);
                if (AdvancedHistoryRequestCurrent(module, session, source, generation, false))
                    PostAdvancedDetailStatus(raw, "Unavailable");
            }
            finally
            {
                if (ReferenceEquals(source, _advancedHistoryDetailCancellation))
                    _advancedHistoryDetailCancellation = null;
                source.Dispose();
            }
        }

        private void PostRegionalResult(string status, string name, string server)
        {
            if (!_ready || _closing || IsDisposed || _browser.CoreWebView2 == null ||
                _navigation.Current != "player-advanced") return;
            var page = _regionalHistory == null ? null : _regionalHistory.Page;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
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

        private bool RegionalRequestCurrent(LeaguePlayerModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true,
            bool checkCancellation = true)
        {
            return !IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                _navigation.Current == "player-advanced" &&
                ReferenceEquals(source, _regionalReadCancellation) &&
                generation == _regionalReadGeneration &&
                (!checkCancellation || !source.IsCancellationRequested) &&
                (!checkSession || LeagueEncounterReplayService.SessionMatches(
                    session, module.UnifiedHistorySession));
        }

        private static bool TryParseRegionalRequest(string request, out string riotId, out string server)
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

        private void CancelRegionalHistoryRead()
        {
            _regionalReadGeneration++;
            _regionalHistory = null;
            _regionalOwner = null;
            _regionalSession = null;
            _regionalTargetPuuid = null;
            var source = _regionalReadCancellation;
            _regionalReadCancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        private void PostAdvancedHistoryDetail(string raw, LeagueEncounterMatchDetailResult detail)
        {
                _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                {
                    type = "history-advanced-detail",
                    status = "Ready",
                    id = raw,
                    mode = detail.Match.GameMode,
                    queue = detail.Match.QueueId,
                    duration = detail.Match.GameDurationSeconds,
                    source = detail.Match.DataSource.ToString(),
                    teams = detail.Teams == null ? new object[0] :
                        detail.Teams.Take(12).Select(team => new
                        {
                            id = team.TeamId,
                            placement = team.SubteamPlacement,
                            kills = team.Kills,
                            deaths = team.Deaths,
                            assists = team.Assists,
                            damage = team.DamageToChampions,
                            towers = team.TowerKills,
                            dragons = team.DragonKills,
                            barons = team.BaronKills
                        }).ToArray(),
                    players = detail.Participants == null ? new object[0] :
                        detail.Participants.Take(30).Select(player => new
                        {
                            name = player.PlayerName,
                            participantId = player.ParticipantId,
                            local = player.IsTarget,
                            championId = player.ChampionId,
                            champion = player.ChampionName,
                            team = player.TeamId,
                            position = player.Position,
                            kills = player.Kills,
                            deaths = player.Deaths,
                            assists = player.Assists,
                            damage = player.DamageAvailable ? (int?)player.DamageToChampions : null,
                            gold = player.GoldAvailable ? (int?)player.GoldEarned : null,
                            cs = player.CreepScoreAvailable ? (int?)player.CreepScore : null,
                            vision = player.VisionScore,
                            items = (player.ItemIds ?? new int[0]).Take(7).ToArray(),
                            spells = (player.SpellIds ?? new int[0]).Take(2).ToArray(),
                            perks = (player.PerkIds ?? new int[0]).Take(9).ToArray(),
                            augments = (player.AugmentIds ?? new int[0]).Take(6).ToArray()
                        }).ToArray()
                }));
        }
    }
}
