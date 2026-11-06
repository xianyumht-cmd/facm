using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Live ARAM bench uses the original LCU owner. The HTML side cannot choose a
    // target outside the last observed bench or send an arbitrary LCU request.
    internal sealed partial class UnifiedWorkspaceForm
    {
        private CancellationTokenSource _liveBenchRead;
        private CancellationTokenSource _liveBenchSwap;
        private int _liveBenchGeneration;
        private LeagueClientSession _liveBenchSession;
        private HashSet<int> _liveBenchChoices = new HashSet<int>();
        private bool _liveBenchSwapping;

        private void OpenLiveBenchPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("live-advanced");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartLiveBenchRead();
        }

        private void StartLiveBenchRead()
        {
            if (_navigation.Current != "live-advanced" || _liveBenchSwapping) return;
            CancelLiveBenchRead();
            var module = LeagueHubUiBridge.UnifiedLive;
            if (module == null || module.UnifiedBenchQuickPick == null)
            {
                PostBenchMessage("bench-state", "unavailable");
                return;
            }
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            _liveBenchRead = source;
            ReadLiveBenchAsync(module, module.UnifiedLiveSession, source, _liveBenchGeneration);
        }

        private async void ReadLiveBenchAsync(LeagueLiveModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var state = await module.UnifiedBenchQuickPick.RefreshAsync(source.Token);
                if (!LiveBenchCurrent(module, session, source, generation))
                {
                    if (LiveBenchCurrentIgnoringTimeout(module, session, source, generation, false))
                        PostBenchMessage("bench-state", "session-changed");
                    return;
                }
                var ids = state == null || !state.SessionAvailable || !state.BenchEnabled
                    ? new int[0] : state.ChampionIds.Where(id => id > 0 && id <= 10000)
                        .Distinct().Take(32).ToArray();
                _liveBenchSession = state != null && state.SessionAvailable && state.BenchEnabled
                    ? session : null;
                _liveBenchChoices = new HashSet<int>(ids);
                _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                {
                    type = "bench-state",
                    status = state == null || !state.SessionAvailable ? "unavailable" :
                        !state.BenchEnabled ? "disabled" : "ready",
                    localChampion = state == null ? 0 : state.LocalChampionId,
                    phase = state == null ? null : state.TimerPhase,
                    queue = state == null ? 0 : state.QueueId,
                    mode = state == null ? null : state.GameMode,
                    championIds = ids
                }));

                // Native LCU icon cache only, no WebView browser fetch; images are
                // bounded, optional and never block showing the selectable champions.
                foreach (var championId in ids.Take(16))
                {
                    if (!LiveBenchCurrent(module, session, source, generation))
                    {
                        if (LiveBenchCurrentIgnoringTimeout(module, session, source, generation, false) &&
                            !source.IsCancellationRequested)
                            PostBenchMessage("bench-state", "session-changed");
                        break;
                    }
                    var bytes = await module.UnifiedBenchQuickPick.LoadChampionIconAsync(championId, source.Token);
                    if (bytes == null || bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024 ||
                        !LiveBenchCurrent(module, session, source, generation)) continue;
                    try
                    {
                        using (var input = new MemoryStream(bytes, false))
                        using (var image = Image.FromStream(input))
                        using (var thumb = new Bitmap(36, 36))
                        using (var output = new MemoryStream())
                        {
                            using (var graphics = Graphics.FromImage(thumb))
                                graphics.DrawImage(image, 0, 0, 36, 36);
                            thumb.Save(output, ImageFormat.Png);
                            if (output.Length > 24576) continue;
                            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                            {
                                type = "bench-icon",
                                id = championId,
                                image = "data:image/png;base64," + Convert.ToBase64String(output.ToArray())
                            }));
                        }
                    }
                    catch (ArgumentException) { }
                    catch (OutOfMemoryException) { }
                }
            }
            catch (OperationCanceledException)
            {
                if (LiveBenchCurrentIgnoringTimeout(module, session, source, generation))
                    PostBenchMessage("bench-state", "timeout");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified ARAM bench read unavailable: " + exception.GetType().Name);
                if (LiveBenchCurrentIgnoringTimeout(module, session, source, generation))
                    PostBenchMessage("bench-state", "failed");
            }
            finally
            {
                if (ReferenceEquals(_liveBenchRead, source)) _liveBenchRead = null;
                source.Dispose();
            }
        }

        private bool LiveBenchCurrentIgnoringTimeout(LeagueLiveModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                _navigation.Current == "live-advanced" &&
                ReferenceEquals(source, _liveBenchRead) && generation == _liveBenchGeneration &&
                (!checkSession || (session == null ? module.UnifiedLiveSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedLiveSession)));
        }

        private bool LiveBenchCurrent(LeagueLiveModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation)
        {
            return !source.IsCancellationRequested &&
                LiveBenchCurrentIgnoringTimeout(module, session, source, generation);
        }

        private async void SwapLiveBenchChampion(string request)
        {
            if (_navigation.Current != "live-advanced" || _liveBenchSwapping) return;
            int championId;
            if (string.IsNullOrEmpty(request) || request.Length > 5 ||
                !int.TryParse(request, NumberStyles.None, CultureInfo.InvariantCulture, out championId) ||
                championId < 1 || !_liveBenchChoices.Contains(championId))
            {
                PostBenchMessage("bench-swap-result", "not-on-bench");
                return;
            }
            var module = LeagueHubUiBridge.UnifiedLive;
            var session = _liveBenchSession;
            if (module == null || module.UnifiedBenchQuickPick == null || session == null ||
                !LeagueEncounterReplayService.SessionMatches(session, module.UnifiedLiveSession))
            {
                CancelLiveBenchRead();
                PostBenchMessage("bench-swap-result", "session-changed");
                return;
            }
            _liveBenchSwapping = true;
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(9));
            _liveBenchSwap = source;
            var generation = _liveBenchGeneration;
            PostBenchMessage("bench-swap-result", "swapping");
            try
            {
                // One explicit click produces one native POST, followed by its
                // bounded verification; there is no browser-side auto retry.
                var outcome = await module.UnifiedBenchQuickPick.TrySwapAsync(championId, source.Token);
                if (IsDisposed || _closing || source.IsCancellationRequested ||
                    _navigation.Current != "live-advanced" || generation != _liveBenchGeneration)
                    return;
                if (!LeagueEncounterReplayService.SessionMatches(session, module.UnifiedLiveSession))
                {
                    PostBenchMessage("bench-swap-result", "session-changed");
                    return;
                }
                _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                {
                    type = "bench-swap-result",
                    status = outcome == null ? "SessionUnavailable" : outcome.Status.ToString(),
                    championId,
                    elapsed = outcome == null ? 0L : outcome.ElapsedMilliseconds
                }));
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !_closing && _navigation.Current == "live-advanced" &&
                    generation == _liveBenchGeneration)
                    PostBenchMessage("bench-swap-result", "timeout");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified ARAM bench swap failed: " + exception.GetType().Name);
                if (!IsDisposed && !_closing && _navigation.Current == "live-advanced" &&
                    generation == _liveBenchGeneration)
                    PostBenchMessage("bench-swap-result", "failed");
            }
            finally
            {
                _liveBenchSwapping = false;
                if (ReferenceEquals(_liveBenchSwap, source)) _liveBenchSwap = null;
                source.Dispose();
                if (!IsDisposed && !_closing && _navigation.Current == "live-advanced" &&
                    generation == _liveBenchGeneration)
                    StartLiveBenchRead();
            }
        }

        private void PostBenchMessage(string type, string status)
        {
            if (!_ready || IsDisposed || _closing || _browser.CoreWebView2 == null ||
                _navigation.Current != "live-advanced") return;
            _browser.CoreWebView2.PostWebMessageAsJson(
                new JavaScriptSerializer().Serialize(new { type, status }));
        }

        private void CancelLiveBenchRead()
        {
            _liveBenchGeneration++;
            _liveBenchSession = null;
            _liveBenchChoices.Clear();
            var read = _liveBenchRead;
            _liveBenchRead = null;
            if (read != null && !read.IsCancellationRequested) read.Cancel();
            var swap = _liveBenchSwap;
            _liveBenchSwap = null;
            if (swap != null && !swap.IsCancellationRequested) swap.Cancel();
        }
    }
}
