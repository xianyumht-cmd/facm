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
        internal void OpenLivePage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Live);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartLiveRead(false);
        }

        internal void StartLiveRead(bool includeSgp)
        {
            CancelLiveRead();
            var module = LeagueHubUiBridge.UnifiedLive;
            if (module == null) { PostLiveError(); return; }
            var source = new CancellationTokenSource();
            _liveCancellation = source;
            LoadLiveAsync(module, includeSgp, source, _liveGeneration);
        }

        private async void LoadLiveAsync(LeagueLiveModule module, bool includeSgp,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var session = module.UnifiedLiveSession;
                var snapshot = await module.ReadUnifiedLiveAsync(includeSgp, source.Token);
                if (!LiveReadIsCurrent(module, session, source, generation))
                {
                    if (LiveReadIsCurrent(module, null, source, generation, false)) PostLiveError();
                    return;
                }
                if (snapshot == null) { PostLiveError(); return; }
                var payload = new JavaScriptSerializer().Serialize(new
                {
                    type = "live-details",
                    connected = snapshot.Connected,
                    phase = snapshot.Phase,
                    queue = snapshot.QueueName,
                    mode = snapshot.GameMode,
                    map = snapshot.MapName,
                    gameId = snapshot.GameId,
                    timer = snapshot.TimerMillisecondsLeft,
                    sgp = snapshot.SgpEnriched,
                    allyBans = snapshot.AllyBans.ToArray(),
                    enemyBans = snapshot.EnemyBans.ToArray(),
                    players = snapshot.Players.Select(player => new
                    {
                        side = player.Side,
                        name = player.AccountName,
                        position = player.Position,
                        champion = player.ChampionId,
                        local = player.IsLocalPlayer
                    }).Take(24).ToArray()
                });
                _browser.CoreWebView2.PostWebMessageAsJson(payload);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified live read skipped: " + exception.GetType().Name);
                if (LiveReadIsCurrent(module, null, source, generation, false)) PostLiveError();
            }
            finally
            {
                if (ReferenceEquals(_liveCancellation, source)) _liveCancellation = null;
                source.Dispose();
            }
        }

        private bool LiveReadIsCurrent(LeagueLiveModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && !source.IsCancellationRequested &&
                ReferenceEquals(_liveCancellation, source) && generation == _liveGeneration &&
                _navigation.Current == LeagueHubNavigation.Live &&
                _ready && _browser.CoreWebView2 != null &&
                (!checkSession || (session == null ? module.UnifiedLiveSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedLiveSession)));
        }

        internal void CancelLiveRead()
        {
            _liveGeneration++;
            var old = _liveCancellation;
            _liveCancellation = null;
            if (old != null && !old.IsCancellationRequested) old.Cancel();
        }

        private void PostLiveError()
        {
            if (_ready && !IsDisposed && _browser.CoreWebView2 != null)
                _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"live-failed\"}");
        }

    }
}
