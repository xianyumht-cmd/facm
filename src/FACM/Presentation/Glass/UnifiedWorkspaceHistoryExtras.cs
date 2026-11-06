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
        private LeagueEncounterMatchDetailResult _advancedSelectedDetail;
        private LeagueClientSession _advancedSelectedSession;
        private string _advancedSelectedGameId;
        private CancellationTokenSource _advancedExtraCancellation;
        private int _advancedExtraGeneration;

        private void RememberAdvancedMatchDetail(
            string id, LeagueEncounterMatchDetailResult detail, LeagueClientSession session)
        {
            CancelAdvancedExtraRead();
            _advancedSelectedDetail = detail;
            _advancedSelectedSession = session;
            _advancedSelectedGameId = id;
        }

        private void ClearAdvancedMatchDetail()
        {
            CancelAdvancedExtraRead();
            _advancedSelectedDetail = null;
            _advancedSelectedSession = null;
            _advancedSelectedGameId = null;
        }

        private void CancelAdvancedExtraRead()
        {
            _advancedExtraGeneration++;
            var source = _advancedExtraCancellation;
            _advancedExtraCancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        private bool SelectedDetailCurrent(LeaguePlayerModule module)
        {
            return module != null && _navigation.Current == "player-advanced" &&
                _advancedSelectedDetail != null && _advancedSelectedSession != null &&
                !string.IsNullOrWhiteSpace(_advancedSelectedGameId) &&
                LeagueEncounterReplayService.SessionMatches(
                    _advancedSelectedSession, module.UnifiedHistorySession) &&
                (_advancedSelectedDetail.IsCurrentSession == null ||
                    _advancedSelectedDetail.IsCurrentSession());
        }

        private bool ExtraReadCurrent(LeaguePlayerModule module, CancellationTokenSource source,
            int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                _navigation.Current == "player-advanced" && !source.IsCancellationRequested &&
                generation == _advancedExtraGeneration &&
                ReferenceEquals(source, _advancedExtraCancellation) &&
                (!checkSession || SelectedDetailCurrent(module));
        }

        private static double?[] SafeMetricValues(double?[] values)
        {
            if (values == null) return new double?[0];
            return values.Take(7).Select(value =>
                value.HasValue && !double.IsNaN(value.Value) &&
                !double.IsInfinity(value.Value) ? value : null).ToArray();
        }

        private void ReadAdvancedRadar(string raw)
        {
            int participantId;
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out participantId) ||
                participantId < 1 || participantId > 20) return;
            var module = LeagueHubUiBridge.UnifiedPlayer;
            if (!SelectedDetailCurrent(module)) { PostAdvancedExtra("radar", "unavailable"); return; }
            var selected = _advancedSelectedDetail.Participants == null ? null :
                _advancedSelectedDetail.Participants.FirstOrDefault(
                    p => p.ParticipantId == participantId);
            if (selected == null) { PostAdvancedExtra("radar", "unavailable"); return; }
            var data = LeagueMatchRadarPanel.Project(
                _advancedSelectedDetail.Participants, _advancedSelectedDetail.Teams,
                LeagueCherryTeams.IsCherry(_advancedSelectedDetail.Match.GameMode), selected);
            if (data == null) { PostAdvancedExtra("radar", "unavailable"); return; }
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "history-advanced-radar",
                id = _advancedSelectedGameId,
                status = "ready",
                player = SafeMetricValues(data.PlayerRatio),
                team = SafeMetricValues(data.TeamAverageRatio),
                playerRaw = SafeMetricValues(data.Player),
                teamRaw = SafeMetricValues(data.TeamAverage)
            }));
        }

        private async void ReadAdvancedTimeline(string requestedId)
        {
            var module = LeagueHubUiBridge.UnifiedPlayer;
            if (!SelectedDetailCurrent(module) || _advancedSelectedGameId != requestedId)
            {
                PostAdvancedExtra("timeline", "unavailable"); return;
            }
            if (_advancedSelectedDetail.LoadTimeline == null)
            {
                PostAdvancedExtra("timeline", "unavailable"); return;
            }
            CancelAdvancedExtraRead();
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(14));
            _advancedExtraCancellation = source;
            var generation = _advancedExtraGeneration;
            var detail = _advancedSelectedDetail;
            var selected = detail.Participants == null ? null :
                detail.Participants.FirstOrDefault(p => p.IsTarget && p.ParticipantId > 0);
            var participantId = selected == null ? 0 : selected.ParticipantId;
            var id = _advancedSelectedGameId;
            try
            {
                var timeline = await detail.LoadTimeline(source.Token);
                if (!ExtraReadCurrent(module, source, generation)) return;
                if (timeline == null || timeline.GameId != detail.Match.GameId)
                {
                    PostAdvancedExtra("timeline", "unavailable"); return;
                }
                var frames = timeline.Frames;
                var stride = Math.Max(1, (frames.Count + 159) / 160);
                var samples = frames.Where((frame, index) => index % stride == 0)
                    .Take(160).Select(frame =>
                    {
                        System.Collections.Generic.Dictionary<string, object> stats = null;
                        var found = participantId > 0 &&
                            frame.Participants.TryGetValue(participantId, out stats);
                        if (!found) stats = null;
                        return new
                        {
                            time = frame.Timestamp,
                            gold = LeagueTimelineMetrics.Value(stats, 0),
                            cs = LeagueTimelineMetrics.Value(stats, 1),
                            xp = LeagueTimelineMetrics.Value(stats, 2),
                            damage = LeagueTimelineMetrics.Value(stats, 3),
                            taken = LeagueTimelineMetrics.Value(stats, 4)
                        };
                    }).ToArray();
                var events = timeline.Events.Take(120).Select(e => new
                {
                    time = e.Timestamp,
                    type = e.Type,
                    participant = e.ParticipantId > 0 ? e.ParticipantId :
                        e.KillerId > 0 ? e.KillerId : e.VictimId
                }).ToArray();
                _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                {
                    type = "history-advanced-timeline", status = "ready", id,
                    frameCount = frames.Count, eventCount = timeline.Events.Count,
                    participantId, frames = samples, events
                }));
            }
            catch (OperationCanceledException)
            {
                if (ExtraReadCurrent(module, source, generation, false))
                    PostAdvancedExtra("timeline", "unavailable");
            }
            catch (Exception error)
            {
                AppLog.Info("Advanced timeline unavailable: " + error.GetType().Name);
                if (ExtraReadCurrent(module, source, generation, false))
                    PostAdvancedExtra("timeline", "unavailable");
            }
            finally
            {
                if (ReferenceEquals(source, _advancedExtraCancellation))
                    _advancedExtraCancellation = null;
                source.Dispose();
            }
        }

        private async void StartAdvancedReplay(string requestedId)
        {
            var module = LeagueHubUiBridge.UnifiedPlayer;
            var loaded = _advancedHistorySnapshot;
            if (!SelectedDetailCurrent(module) || _advancedSelectedGameId != requestedId ||
                loaded == null || loaded.Profile == null || loaded.Page == null) return;
            var gameId = _advancedSelectedDetail.Match.GameId;
            var match = loaded.Page.Matches.FirstOrDefault(m => m.GameId == gameId);
            if (match == null ||
                !LeagueEncounterReplayService.CanOfferHistoryReplay(
                    _advancedSelectedSession, module.UnifiedHistorySession, loaded.Profile.PuuId, match))
            {
                PostAdvancedExtra("replay", "Unavailable"); return;
            }
            if (_advancedExtraCancellation != null)
            {
                PostAdvancedExtra("replay", "Checking"); return;
            }
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(13));
            _advancedExtraCancellation = source;
            var generation = _advancedExtraGeneration;
            var session = _advancedSelectedSession;
            var id = _advancedSelectedGameId;
            try
            {
                var result = await module.ExecuteUnifiedHistoryReplayAsync(
                    session, loaded.Profile.PuuId, match, source.Token);
                if (!ExtraReadCurrent(module, source, generation)) return;
                _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                {
                    type = "history-advanced-replay-result", kind = "replay", id,
                    status = result == null ? "Unavailable" : result.Status.ToString(),
                    progress = result == null ? 0 : result.Progress
                }));
            }
            catch (OperationCanceledException)
            {
                if (ExtraReadCurrent(module, source, generation, false))
                    PostAdvancedExtra("replay", "Unavailable");
            }
            catch (Exception error)
            {
                AppLog.Info("Advanced replay failed: " + error.GetType().Name);
                if (ExtraReadCurrent(module, source, generation, false))
                    PostAdvancedExtra("replay", "Unavailable");
            }
            finally
            {
                if (ReferenceEquals(source, _advancedExtraCancellation))
                    _advancedExtraCancellation = null;
                source.Dispose();
            }
        }

        private async void ReadAdvancedReplayFolder()
        {
            var module = LeagueHubUiBridge.UnifiedPlayer;
            var loaded = _advancedHistorySnapshot;
            if (!SelectedDetailCurrent(module) || loaded == null || loaded.Profile == null) return;
            if (_advancedExtraCancellation != null) return;
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _advancedExtraCancellation = source;
            var generation = _advancedExtraGeneration;
            var id = _advancedSelectedGameId;
            try
            {
                var result = await module.ReadUnifiedReplayDirectoryAsync(
                    _advancedSelectedSession, loaded.Profile.PuuId, source.Token);
                if (!ExtraReadCurrent(module, source, generation)) return;
                _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                {
                    type = "history-advanced-replay-result", kind = "folder", id,
                    status = result == null ? "Unavailable" : result.Status.ToString(),
                    path = result == null ? null : result.Path,
                    playingGame = result != null && result.PlayingGame == true
                }));
            }
            catch (OperationCanceledException)
            {
                if (ExtraReadCurrent(module, source, generation, false))
                    PostAdvancedExtra("replay", "Unavailable");
            }
            catch (Exception error)
            {
                AppLog.Info("Advanced replay directory unavailable: " + error.GetType().Name);
                if (ExtraReadCurrent(module, source, generation, false))
                    PostAdvancedExtra("replay", "Unavailable");
            }
            finally
            {
                if (ReferenceEquals(source, _advancedExtraCancellation))
                    _advancedExtraCancellation = null;
                source.Dispose();
            }
        }

        private void PostAdvancedExtra(string kind, string status)
        {
            if (!_ready || _closing || IsDisposed || _browser.CoreWebView2 == null ||
                _navigation.Current != "player-advanced") return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = kind == "radar" ? "history-advanced-radar" :
                    kind == "timeline" ? "history-advanced-timeline" :
                    "history-advanced-replay-result",
                id = _advancedSelectedGameId,
                kind,
                status
            }));
        }
    }
}
