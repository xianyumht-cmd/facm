using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;
using FACM.Online;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceHistoryExtrasAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private LeagueEncounterMatchDetailResult _detail;
        private LeagueClientSession _session;
        private string _gameId;
        private CancellationTokenSource _cancellation;
        private int _generation;

        internal UnifiedWorkspaceHistoryExtrasAdapter(Func<bool> canPublish, Action<string> publish)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        }

        internal void Remember(string id, LeagueEncounterMatchDetailResult detail, LeagueClientSession session)
        {
            CancelRead();
            _detail = detail;
            _session = session;
            _gameId = id;
        }

        internal void Clear()
        {
            CancelRead();
            _detail = null;
            _session = null;
            _gameId = null;
        }

        private void CancelRead()
        {
            _generation++;
            var source = _cancellation;
            _cancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        private bool SelectedCurrent(LeaguePlayerModule module)
        {
            return module != null && _canPublish() && _detail != null && _session != null &&
                !string.IsNullOrWhiteSpace(_gameId) &&
                LeagueEncounterReplayService.SessionMatches(_session, module.UnifiedHistorySession) &&
                (_detail.IsCurrentSession == null || _detail.IsCurrentSession());
        }

        private bool Current(LeaguePlayerModule module, CancellationTokenSource source,
            int generation, bool checkSession = true, bool checkCancellation = true)
        {
            return _canPublish() && (!checkCancellation || !source.IsCancellationRequested) &&
                generation == _generation && ReferenceEquals(source, _cancellation) &&
                (!checkSession || SelectedCurrent(module));
        }

        private static double?[] SafeMetricValues(double?[] values)
        {
            if (values == null) return new double?[0];
            return values.Take(7).Select(value => value.HasValue &&
                !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) ? value : null).ToArray();
        }

        internal void ReadRadar(LeaguePlayerModule module, string raw)
        {
            int participantId;
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out participantId) ||
                participantId < 1 || participantId > 20) return;
            if (!SelectedCurrent(module)) { PostStatus("radar", "unavailable"); return; }
            if (!CloudRouteControl.HistoryRadarEnabled) { PostStatus("radar", "maintenance"); return; }
            var selected = _detail.Participants == null ? null :
                _detail.Participants.FirstOrDefault(p => p.ParticipantId == participantId);
            if (selected == null) { PostStatus("radar", "unavailable"); return; }
            var data = LeagueMatchRadarPanel.Project(_detail.Participants, _detail.Teams,
                LeagueCherryTeams.IsCherry(_detail.Match.GameMode), selected);
            if (data == null) { PostStatus("radar", "unavailable"); return; }
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "history-advanced-radar",
                id = _gameId,
                status = "ready",
                player = SafeMetricValues(data.PlayerRatio),
                team = SafeMetricValues(data.TeamAverageRatio),
                playerRaw = SafeMetricValues(data.Player),
                teamRaw = SafeMetricValues(data.TeamAverage)
            }));
        }

        internal static object ProjectTimelineReview(LeagueEncounterMatchDetailResult detail,
            LeagueMatchTimeline timeline)
        {
            var match = detail == null ? null : detail.Match;
            if (match == null || timeline == null || timeline.GameId != match.GameId ||
                !match.ParticipantResolved || match.ParticipantId < 1 || match.ParticipantId > 20 ||
                detail.Participants == null) return null;
            var targets = detail.Participants.Where(p => p != null && p.IsTarget).ToArray();
            if (targets.Length != 1 || targets[0].ParticipantId != match.ParticipantId) return null;
            var target = match.ParticipantId;
            var classic = match.MapId == 11 &&
                string.Equals(match.GameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase) &&
                (match.TeamId == 100 || match.TeamId == 200);
            var standard = classic || (match.MapId == 12 &&
                string.Equals(match.GameMode, "ARAM", StringComparison.OrdinalIgnoreCase));
            var kills = timeline.Events.Where(e => e != null && e.Type == "CHAMPION_KILL" &&
                e.Timestamp >= 0 && e.VictimId > 0).OrderBy(e => e.Timestamp).ToArray();
            var nodes = new System.Collections.Generic.List<object>();
            var ordered = new System.Collections.Generic.List<System.Tuple<long, string, string>>();
            int deaths = 0, takedowns = 0, objectives = 0;
            foreach (var e in kills)
            {
                if (e.VictimId == target)
                { deaths++; ordered.Add(System.Tuple.Create(e.Timestamp, "death", "")); }
                else if (e.KillerId == target)
                { takedowns++; ordered.Add(System.Tuple.Create(e.Timestamp, "kill", "")); }
                else if (e.Assists != null && e.Assists.Contains(target))
                { takedowns++; ordered.Add(System.Tuple.Create(e.Timestamp, "assist", "")); }
            }
            if (classic)
            {
                foreach (var e in timeline.Events.Where(e => e != null && e.Timestamp >= 0 &&
                    e.Type == "ELITE_MONSTER_KILL"))
                {
                    var monster = LeagueMatchTimelineParser.Text(e.Data, "monsterType");
                    if (monster != "DRAGON" && monster != "HORDE" && monster != "RIFTHERALD" &&
                        monster != "BARON_NASHOR") continue;
                    var team = LeagueMatchTimelineParser.Number(e.Data, "killerTeamId");
                    var side = team == match.TeamId ? "ally" :
                        team == 100 || team == 200 ? "enemy" : "unknown";
                    objectives++;
                    ordered.Add(System.Tuple.Create(e.Timestamp, monster, side));
                }
            }
            // A time cluster alone does not establish that the kills were one team fight.
            int clusters = 0;
            if (standard)
            {
                for (var i = 0; i < kills.Length;)
                {
                    var end = i + 1;
                    while (end < kills.Length && kills[end].Timestamp - kills[i].Timestamp <= 30000) end++;
                    if (end - i >= 3 && kills.Skip(i).Take(end - i).Any(e => e.VictimId == target ||
                        e.KillerId == target || (e.Assists != null && e.Assists.Contains(target))))
                    {
                        clusters++;
                        ordered.Add(System.Tuple.Create(kills[i].Timestamp, "cluster",
                            (end - i).ToString(CultureInfo.InvariantCulture)));
                        i = end;
                    }
                    else i++;
                }
            }
            foreach (var row in ordered.OrderBy(r => r.Item1).Take(80))
                nodes.Add(new { time = row.Item1, kind = row.Item2, value = row.Item3 });
            var checkpoints = new System.Collections.Generic.List<object>();
            foreach (var minute in new[] { 5, 10, 15, 20, 25, 30 })
            {
                if (!standard) break;
                var time = minute * 60000L;
                if (timeline.Frames.Count == 0 || timeline.Frames[timeline.Frames.Count - 1].Timestamp < time) continue;
                var frame = timeline.Frames.LastOrDefault(p => p.Timestamp <= time && p.Timestamp >= time - 60000 &&
                    p.Participants.ContainsKey(target));
                if (frame == null) continue;
                var stats = frame.Participants[target];
                var gold = LeagueTimelineMetrics.Value(stats, 0);
                var cs = classic ? LeagueTimelineMetrics.Value(stats, 1) : null;
                if (gold.HasValue || cs.HasValue)
                    checkpoints.Add(new { time = frame.Timestamp, gold, cs });
            }
            return new { deaths, takedowns, objectives = classic ? (int?)objectives : null,
                clusters = standard ? (int?)clusters : null, nodeCount = ordered.Count, nodes, checkpoints };
        }

        internal async void ReadTimeline(LeaguePlayerModule module, string requestedId)
        {
            if (!SelectedCurrent(module) || _gameId != requestedId || _detail.LoadTimeline == null)
            { PostStatus("timeline", "unavailable"); return; }
            if (!CloudRouteControl.HistoryTimelineEnabled) { PostStatus("timeline", "maintenance"); return; }
            CancelRead();
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(14));
            _cancellation = source;
            var generation = _generation;
            var detail = _detail;
            var selected = detail.Participants == null ? null :
                detail.Participants.FirstOrDefault(p => p.IsTarget && p.ParticipantId > 0);
            var participantId = selected == null ? 0 : selected.ParticipantId;
            var id = _gameId;
            try
            {
                var timeline = await detail.LoadTimeline(source.Token);
                if (!Current(module, source, generation))
                {
                    if (Current(module, source, generation, false, false))
                        PostStatus("timeline", "session-changed");
                    return;
                }
                if (timeline == null || timeline.GameId != detail.Match.GameId)
                { PostStatus("timeline", "unavailable"); return; }
                var frames = timeline.Frames;
                var stride = Math.Max(1, (frames.Count + 159) / 160);
                var samples = frames.Where((frame, index) => index % stride == 0)
                    .Take(160).Select(frame =>
                    {
                        System.Collections.Generic.Dictionary<string, object> stats = null;
                        var found = participantId > 0 && frame.Participants.TryGetValue(participantId, out stats);
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
                _publish(new JavaScriptSerializer().Serialize(new
                {
                    type = "history-advanced-timeline", status = "ready", id,
                    frameCount = frames.Count, eventCount = timeline.Events.Count,
                    participantId, frames = samples, events,
                    review = ProjectTimelineReview(detail, timeline)
                }));
            }
            catch (OperationCanceledException)
            {
                if (Current(module, source, generation, false, false))
                    PostStatus("timeline", "unavailable");
            }
            catch (Exception exception)
            {
                AppLog.Info("Advanced timeline unavailable: " + exception.GetType().Name);
                if (Current(module, source, generation, false, false))
                    PostStatus("timeline", "unavailable");
            }
            finally { Finish(source); }
        }

        internal async void StartReplay(LeaguePlayerModule module,
            LeagueUnifiedHistorySnapshot loaded, string requestedId)
        {
            if (!SelectedCurrent(module) || _gameId != requestedId ||
                loaded == null || loaded.Profile == null || loaded.Page == null) return;
            var gameId = _detail.Match.GameId;
            var match = loaded.Page.Matches.FirstOrDefault(m => m.GameId == gameId);
            if (match == null || !LeagueEncounterReplayService.CanOfferHistoryReplay(
                _session, module.UnifiedHistorySession, loaded.Profile.PuuId, match))
            { PostStatus("replay", "Unavailable"); return; }
            if (_cancellation != null) { PostStatus("replay", "Checking"); return; }
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(13));
            _cancellation = source;
            var generation = _generation;
            var session = _session;
            var id = _gameId;
            try
            {
                var result = await module.ExecuteUnifiedHistoryReplayAsync(
                    session, loaded.Profile.PuuId, match, source.Token);
                if (!Current(module, source, generation))
                {
                    if (Current(module, source, generation, false, false))
                        PostStatus("replay", "WrongAccount");
                    return;
                }
                _publish(new JavaScriptSerializer().Serialize(new
                {
                    type = "history-advanced-replay-result", kind = "replay", id,
                    status = result == null ? "Unavailable" : result.Status.ToString(),
                    progress = result == null ? 0 : result.Progress
                }));
            }
            catch (OperationCanceledException)
            {
                if (Current(module, source, generation, false, false))
                    PostStatus("replay", "Unavailable");
            }
            catch (Exception exception)
            {
                AppLog.Info("Advanced replay failed: " + exception.GetType().Name);
                if (Current(module, source, generation, false, false))
                    PostStatus("replay", "Unavailable");
            }
            finally { Finish(source); }
        }

        internal async void ReadReplayFolder(LeaguePlayerModule module,
            LeagueUnifiedHistorySnapshot loaded)
        {
            if (!SelectedCurrent(module) || loaded == null || loaded.Profile == null ||
                _cancellation != null) return;
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _cancellation = source;
            var generation = _generation;
            var id = _gameId;
            try
            {
                var result = await module.ReadUnifiedReplayDirectoryAsync(
                    _session, loaded.Profile.PuuId, source.Token);
                if (!Current(module, source, generation)) return;
                _publish(new JavaScriptSerializer().Serialize(new
                {
                    type = "history-advanced-replay-result", kind = "folder", id,
                    status = result == null ? "Unavailable" : result.Status.ToString(),
                    path = result == null ? null : result.Path,
                    playingGame = result != null && result.PlayingGame == true
                }));
            }
            catch (OperationCanceledException)
            {
                if (Current(module, source, generation, false, false))
                    PostStatus("replay", "Unavailable");
            }
            catch (Exception exception)
            {
                AppLog.Info("Advanced replay directory unavailable: " + exception.GetType().Name);
                if (Current(module, source, generation, false, false))
                    PostStatus("replay", "Unavailable");
            }
            finally { Finish(source); }
        }

        private void Finish(CancellationTokenSource source)
        {
            if (ReferenceEquals(source, _cancellation)) _cancellation = null;
            source.Dispose();
        }

        private void PostStatus(string kind, string status)
        {
            if (!_canPublish()) return;
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = kind == "radar" ? "history-advanced-radar" :
                    kind == "timeline" ? "history-advanced-timeline" :
                    "history-advanced-replay-result",
                id = _gameId, kind, status
            }));
        }

        internal static void ValidateForSmokeTest()
        {
            var match = new LeaguePlayerMatchSummary { GameId = 42, ParticipantResolved = true,
                ParticipantId = 1, TeamId = 100, MapId = 11, GameMode = "CLASSIC" };
            var detail = new LeagueEncounterMatchDetailResult { Match = match,
                Participants = new System.Collections.Generic.List<LeaguePlayerMatchParticipant>
                { new LeaguePlayerMatchParticipant { ParticipantId = 1, IsTarget = true } } };
            var timeline = new LeagueMatchTimeline { GameId = 42 };
            for (var i = 0; i < 121; i++)
                timeline.Events.Add(new LeagueMatchTimelineEvent { Type = "ITEM_PURCHASED", Timestamp = i });
            timeline.Events.Add(new LeagueMatchTimelineEvent { Type = "CHAMPION_KILL", Timestamp = 600000, VictimId = 1 });
            timeline.Events.Add(new LeagueMatchTimelineEvent { Type = "CHAMPION_KILL", Timestamp = 610000, VictimId = 2, KillerId = 1 });
            timeline.Events.Add(new LeagueMatchTimelineEvent { Type = "CHAMPION_KILL", Timestamp = 620000, VictimId = 3, Assists = new[] { 1 } });
            timeline.Events.Add(new LeagueMatchTimelineEvent { Type = "ELITE_MONSTER_KILL", Timestamp = 630000,
                Data = new System.Collections.Generic.Dictionary<string, object> { { "monsterType", "DRAGON" }, { "killerTeamId", 200 } } });
            var frame = new LeagueMatchTimelineFrame { Timestamp = 600000 };
            frame.Participants.Add(1, new System.Collections.Generic.Dictionary<string, object>
                { { "totalGold", 4000 }, { "minionsKilled", 70 }, { "jungleMinionsKilled", 10 } });
            timeline.Frames.Add(frame);
            var serializer = new JavaScriptSerializer();
            var json = serializer.Serialize(ProjectTimelineReview(detail, timeline));
            if (!json.Contains("\"deaths\":1") || !json.Contains("\"takedowns\":2") ||
                !json.Contains("\"objectives\":1") || !json.Contains("\"clusters\":1") ||
                !json.Contains("\"value\":\"enemy\"") || !json.Contains("\"gold\":4000") ||
                !json.Contains("\"cs\":80"))
                throw new InvalidOperationException("Timeline review lost late events, target or frame evidence.");
            match.GameMode = "CHERRY";
            json = serializer.Serialize(ProjectTimelineReview(detail, timeline));
            if (!json.Contains("\"objectives\":null") || !json.Contains("\"clusters\":null") ||
                !json.Contains("\"checkpoints\":[]"))
                throw new InvalidOperationException("Arena timeline exposed incompatible objectives.");
            timeline.GameId = 43;
            if (ProjectTimelineReview(detail, timeline) != null)
                throw new InvalidOperationException("Wrong match produced timeline review.");
            var messages = 0;
            var adapter = new UnifiedWorkspaceHistoryExtrasAdapter(() => true,
                delegate(string json)
                {
                    if (!json.Contains("\"type\":\"history-advanced-radar\""))
                        throw new InvalidOperationException("Radar status contract changed.");
                    messages++;
                });
            adapter.ReadRadar(null, "1");
            adapter.Clear();
            if (messages != 1) throw new InvalidOperationException("Advanced extras empty-state changed.");
        }
    }
}
