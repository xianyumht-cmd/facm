using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Per-workspace selection and request lifetime; LeagueLiveModule owns LCU transport.
    internal sealed class UnifiedWorkspaceLiveBenchAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Func<LeagueLiveModule> _owner;
        private CancellationTokenSource _liveBenchRead;
        private CancellationTokenSource _liveBenchSwap;
        private int _liveBenchGeneration;
        private LeagueClientSession _liveBenchSession;
        private HashSet<int> _liveBenchChoices = new HashSet<int>();
        private bool _liveBenchSwapping;

        internal UnifiedWorkspaceLiveBenchAdapter(Func<bool> canPublish, Action<string> publish,
            Func<LeagueLiveModule> owner)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        internal void Start()
        {
            if (!_canPublish() || _liveBenchSwapping) return;
            Cancel();
            var module = _owner();
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
                _publish(new JavaScriptSerializer().Serialize(new
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
                            if (!LiveBenchCurrent(module, session, source, generation)) continue;
                            _publish(new JavaScriptSerializer().Serialize(new
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
            return _canPublish() && ReferenceEquals(module, _owner()) &&
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

        internal async void Swap(string request)
        {
            if (!_canPublish() || _liveBenchSwapping) return;
            int championId;
            if (string.IsNullOrEmpty(request) || request.Length > 5 ||
                !int.TryParse(request, NumberStyles.None, CultureInfo.InvariantCulture, out championId) ||
                championId < 1 || !_liveBenchChoices.Contains(championId))
            {
                PostBenchMessage("bench-swap-result", "not-on-bench");
                return;
            }
            var module = _owner();
            var session = _liveBenchSession;
            if (module == null || module.UnifiedBenchQuickPick == null || session == null ||
                !LeagueEncounterReplayService.SessionMatches(session, module.UnifiedLiveSession))
            {
                Cancel();
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
                if (!_canPublish() || source.IsCancellationRequested ||
                    !ReferenceEquals(source, _liveBenchSwap) ||
                    !ReferenceEquals(module, _owner()) || generation != _liveBenchGeneration)
                    return;
                if (!LeagueEncounterReplayService.SessionMatches(session, module.UnifiedLiveSession))
                {
                    PostBenchMessage("bench-swap-result", "session-changed");
                    return;
                }
                _publish(new JavaScriptSerializer().Serialize(new
                {
                    type = "bench-swap-result",
                    status = outcome == null ? "SessionUnavailable" : outcome.Status.ToString(),
                    championId,
                    elapsed = outcome == null ? 0L : outcome.ElapsedMilliseconds
                }));
            }
            catch (OperationCanceledException)
            {
                if (_canPublish() && ReferenceEquals(source, _liveBenchSwap) &&
                    generation == _liveBenchGeneration)
                    PostBenchMessage("bench-swap-result", "timeout");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified ARAM bench swap failed: " + exception.GetType().Name);
                if (_canPublish() && ReferenceEquals(source, _liveBenchSwap) &&
                    generation == _liveBenchGeneration)
                    PostBenchMessage("bench-swap-result", "failed");
            }
            finally
            {
                var current = ReferenceEquals(_liveBenchSwap, source);
                if (current)
                {
                    _liveBenchSwap = null;
                    _liveBenchSwapping = false;
                }
                source.Dispose();
                if (current && _canPublish() && generation == _liveBenchGeneration)
                    Start();
            }
        }

        private void PostBenchMessage(string type, string status)
        {
            if (_canPublish())
                _publish(new JavaScriptSerializer().Serialize(new { type, status }));
        }

        internal void Cancel()
        {
            _liveBenchGeneration++;
            _liveBenchSession = null;
            _liveBenchChoices.Clear();
            _liveBenchSwapping = false;
            var read = _liveBenchRead;
            _liveBenchRead = null;
            if (read != null && !read.IsCancellationRequested) read.Cancel();
            var swap = _liveBenchSwap;
            _liveBenchSwap = null;
            if (swap != null && !swap.IsCancellationRequested) swap.Cancel();
        }

        internal static void ValidateForSmokeTest()
        {
            var one = 0;
            var two = 0;
            var first = new UnifiedWorkspaceLiveBenchAdapter(() => true,
                delegate { one++; }, () => null);
            var second = new UnifiedWorkspaceLiveBenchAdapter(() => false,
                delegate { two++; }, () => null);
            first.Start();
            first.Swap("12");
            first.Cancel();
            second.Start();
            if (one != 2 || two != 0 || first._liveBenchGeneration != 2 ||
                second._liveBenchGeneration != 0)
                throw new InvalidOperationException("Live bench adapters leaked route or request state.");
        }
    }
}
