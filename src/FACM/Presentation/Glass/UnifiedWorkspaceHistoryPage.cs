using System;
using System.Drawing;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.League;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private CancellationTokenSource _historyCancellation;
        private int _historyGeneration;

        internal void OpenHistoryPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Player);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartHistoryRead(10, false);
        }

        internal void StartHistoryRead(int count, bool force)
        {
            CancelHistoryRead();
            var module = LeagueHubUiBridge.UnifiedPlayer;
            if (module == null) { PostHistoryError(); return; }
            var source = new CancellationTokenSource();
            _historyCancellation = source;
            // History changes invalidate any previously selectable match identity.
            ResetAdvancedHistorySnapshot();
            LoadHistoryAsync(module, count, force, source, _historyGeneration);
        }

        private async void LoadHistoryAsync(LeaguePlayerModule module, int count, bool force,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var session = module.UnifiedHistorySession;
                var result = await module.ReadUnifiedHistoryAsync(count, force, source.Token);
                if (!HistoryReadIsCurrent(module, session, source, generation))
                {
                    if (HistoryReadIsCurrent(module, null, source, generation, false)) PostHistoryError();
                    return;
                }
                if (result == null || result.Profile == null) { PostHistoryError(); return; }
                _advancedHistorySnapshot = result;
                _advancedHistorySession = session;
                var page = result.Page;
                if (_navigation.Current == "player-advanced") SyncAdvancedHistoryList();
                var payload = new JavaScriptSerializer().Serialize(new
                {
                    type = "history-details",
                    name = result.Profile.AccountName,
                    level = result.Profile.SummonerLevel,
                    count = page == null ? 0 : page.Matches.Count,
                    hasMore = page != null && page.HasMore && count < 20,
                    rows = page == null ? new object[0] : page.Matches.Take(20).Select(match => new
                    {
                        gameId = match.GameId,
                        played = match.GameCreationLocal.ToString("yyyy-MM-dd HH:mm"),
                        champion = match.ChampionName,
                        championId = match.ChampionId,
                        mode = match.GameMode,
                        queueId = match.QueueId,
                        outcome = match.StatisticsAvailable
                            ? (match.Win ? "win" : "loss") : "unknown",
                        kills = match.Kills,
                        deaths = match.Deaths,
                        assists = match.Assists,
                        duration = match.GameDurationSeconds
                    }).ToArray()
                });
                _browser.CoreWebView2.PostWebMessageAsJson(payload);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified recent history read skipped: " + exception.GetType().Name);
                if (HistoryReadIsCurrent(module, null, source, generation, false)) PostHistoryError();
            }
            finally
            {
                if (ReferenceEquals(_historyCancellation, source)) _historyCancellation = null;
                source.Dispose();
            }
        }

        private bool HistoryReadIsCurrent(LeaguePlayerModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && !source.IsCancellationRequested &&
                generation == _historyGeneration &&
                ReferenceEquals(_historyCancellation, source) &&
                (_navigation.Current == LeagueHubNavigation.Player ||
                 _navigation.Current == "player-advanced") &&
                _ready && _browser.CoreWebView2 != null &&
                (!checkSession || (session == null ? module.UnifiedHistorySession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedHistorySession)));
        }

        internal void CancelHistoryRead()
        {
            _historyGeneration++;
            var previous = _historyCancellation;
            _historyCancellation = null;
            if (previous != null && !previous.IsCancellationRequested) previous.Cancel();
        }

        private void PostHistoryError()
        {
            if (_ready && !IsDisposed && _browser.CoreWebView2 != null)
            {
                _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"history-failed\"}");
                if (_navigation.Current == "player-advanced")
                    _browser.CoreWebView2.PostWebMessageAsJson(
                        "{\"type\":\"history-advanced-list\",\"error\":true,\"rows\":[]}");
            }
        }

    }
}
