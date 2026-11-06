using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private LeagueUnifiedHistorySnapshot _advancedHistorySnapshot;
        private LeagueClientSession _advancedHistorySession;
        private CancellationTokenSource _advancedHistoryDetailCancellation;
        private int _advancedHistoryDetailGeneration;

        private void OpenAdvancedHistoryPage(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open("player-advanced");
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) OpenAdvancedHistoryData();
        }

        private void OpenAdvancedHistoryData()
        {
            var module = LeagueHubUiBridge.UnifiedPlayer;
            if (module != null && _advancedHistorySnapshot != null &&
                _advancedHistorySession != null &&
                LeagueEncounterReplayService.SessionMatches(
                    _advancedHistorySession, module.UnifiedHistorySession) &&
                _advancedHistorySnapshot.Page != null &&
                _advancedHistorySnapshot.Page.Matches.Count >= 20)
                SyncAdvancedHistoryList();
            else
                StartHistoryRead(20, false);
        }

        private void SyncAdvancedHistoryList()
        {
            if (_navigation.Current != "player-advanced" || !_ready || IsDisposed ||
                _closing || _browser.CoreWebView2 == null) return;
            var loaded = _advancedHistorySnapshot;
            var module = LeagueHubUiBridge.UnifiedPlayer;
            if (loaded == null || loaded.Profile == null ||
                loaded.Page == null || module == null ||
                !LeagueEncounterReplayService.SessionMatches(_advancedHistorySession,
                    module.UnifiedHistorySession))
            {
                _browser.CoreWebView2.PostWebMessageAsJson(
                    "{\"type\":\"history-advanced-list\",\"error\":true,\"rows\":[]}");
                return;
            }
            var payload = new JavaScriptSerializer().Serialize(new
            {
                type = "history-advanced-list",
                name = loaded.Profile.AccountName,
                hasMore = loaded.Page.HasMore,
                rows = loaded.Page.Matches.Take(20).Select(m => new
                {
                    id = m.GameId.ToString(CultureInfo.InvariantCulture),
                    played = m.GameCreationLocal.ToString("yyyy-MM-dd HH:mm"),
                    champion = m.ChampionName,
                    championId = m.ChampionId,
                    queue = m.QueueId,
                    mode = m.GameMode,
                    outcome = m.StatisticsAvailable ? m.Win ? "win" : "loss" : "unknown",
                    kills = m.Kills,
                    deaths = m.Deaths,
                    assists = m.Assists
                }).ToArray()
            });
            _browser.CoreWebView2.PostWebMessageAsJson(payload);
        }

        private async void LoadAdvancedHistoryDetail(string raw)
        {
            if (_navigation.Current != "player-advanced" ||
                string.IsNullOrEmpty(raw) || raw.Length > 20) return;
            long gameId;
            if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out gameId) ||
                gameId <= 0) { PostAdvancedDetailStatus(raw, "Unavailable"); return; }
            var loaded = _advancedHistorySnapshot;
            var module = LeagueHubUiBridge.UnifiedPlayer;
            var session = _advancedHistorySession;
            if (loaded == null || loaded.Profile == null || loaded.Page == null ||
                module == null || session == null ||
                !LeagueEncounterReplayService.SessionMatches(session, module.UnifiedHistorySession))
            {
                PostAdvancedDetailStatus(raw, "WrongAccount"); return;
            }
            var match = loaded.Page.Matches.FirstOrDefault(m => m.GameId == gameId);
            if (match == null) { PostAdvancedDetailStatus(raw, "Unavailable"); return; }
            // A new explicit selection cancels the old detail request instead of
            // leaving the second page indefinitely in a loading state.
            CancelAdvancedHistoryDetail();
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            _advancedHistoryDetailCancellation = source;
            var generation = _advancedHistoryDetailGeneration;
            try
            {
                var detail = await module.ReadUnifiedMatchDetailAsync(
                    session, loaded.Profile.PuuId, match, source.Token);
                if (!AdvancedHistoryRequestCurrent(module, session, source, generation))
                {
                    if (AdvancedHistoryRequestCurrent(module, session, source, generation, false))
                    {
                        ResetAdvancedHistorySnapshot();
                        SyncAdvancedHistoryList();
                        PostAdvancedDetailStatus(raw, "WrongAccount");
                    }
                    return;
                }
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
                AppLog.Info("Unified history detail failed: " + exception.GetType().Name);
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

        private bool AdvancedHistoryRequestCurrent(LeaguePlayerModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                _navigation.Current == "player-advanced" &&
                generation == _advancedHistoryDetailGeneration &&
                ReferenceEquals(source, _advancedHistoryDetailCancellation) &&
                (!checkSession || LeagueEncounterReplayService.SessionMatches(
                    session, module.UnifiedHistorySession));
        }

        private void PostAdvancedDetailStatus(string gameId, string status)
        {
            if (!_ready || _closing || IsDisposed || _browser.CoreWebView2 == null ||
                _navigation.Current != "player-advanced") return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "history-advanced-detail",
                id = gameId,
                status
            }));
        }

        private void ResetAdvancedHistorySnapshot()
        {
            CancelAdvancedHistoryDetail();
            _advancedHistorySnapshot = null;
            _advancedHistorySession = null;
        }

        private void CancelAdvancedHistoryDetail()
        {
            ClearAdvancedMatchDetail();
            _advancedHistoryDetailGeneration++;
            var source = _advancedHistoryDetailCancellation;
            _advancedHistoryDetailCancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }
    }
}
