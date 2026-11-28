using System;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FACM.League;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceQuickToolsAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Func<LeagueEfficiencyModule> _owner;
        private bool _actionBusy;
        private bool _insightsOpen;
        private string _insightPhase;
        private CancellationTokenSource _insightCancellation;
        private CancellationTokenSource _roomCancellation;
        private LeagueLobbyRoomSnapshot _roomSnapshot;
        private int _roomRevision;

        internal UnifiedWorkspaceQuickToolsAdapter(Func<bool> canPublish,
            Action<string> publish, Func<LeagueEfficiencyModule> owner)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        internal void Apply(string request)
        {
            if (!_canPublish()) return;
            string key;
            bool enabled;
            if (!TryParseSetting(request, out key, out enabled)) return;
            var module = _owner();
            if (module == null)
            {
                _publish("{\"type\":\"tool-error\"}");
                return;
            }
            try
            {
                if (key == "autoSearch" || key == "autoAccept")
                    module.UpdateMatchmakingSettings(
                        key == "autoSearch" ? enabled : module.AutoMatchmakingEnabled,
                        key == "autoAccept" ? enabled : module.AutoAcceptEnabled,
                        module.AutoMatchmakingMinPartySize, module.AutoMatchmakingStartDelayMs,
                        module.AutoAcceptDelayMs, module.AutoMatchmakingStopPolicy,
                        module.AutoMatchmakingStopAfterMs, module.AutoMatchmakingWaitForInvitees,
                        module.AutoMatchmakingRoomNotices);
                else if (key == "autoReconnect") module.UpdateReconnectSetting(enabled);
                else if (key == "autoHonor" || key == "autoReturn")
                    module.UpdatePostGameSettings(key == "autoHonor" ? enabled : module.AutoHonorEnabled,
                        key == "autoReturn" ? enabled : module.AutoReturnLobbyEnabled,
                        module.AutoHonorStrategy);
                Sync();
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified workspace quick setting update failed", exception);
                if (_canPublish()) _publish("{\"type\":\"tool-error\"}");
            }
        }

        internal async void CloseLobby()
        {
            if (!_canPublish() || _actionBusy) return;
            var module = _owner();
            if (module == null)
            {
                _publish("{\"type\":\"quick-action-result\",\"status\":\"unavailable\"}");
                return;
            }
            _actionBusy = true;
            try
            {
                var result = await module.RunCloseLobbyAsync();
                if (_canPublish())
                    _publish(new JavaScriptSerializer().Serialize(new {
                        type = "quick-action-result", status = result == null ? "failed" : result.Status
                    }));
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified quick close-lobby failed: " + exception.GetType().Name);
                if (_canPublish()) _publish("{\"type\":\"quick-action-result\",\"status\":\"failed\"}");
            }
            finally { _actionBusy = false; }
        }

        internal void SaveHotkeys(string request)
        {
            if (!_canPublish()) return;
            var module = _owner();
            if (module == null || string.IsNullOrEmpty(request) || request.Length > 256)
            {
                PostHotkeysResult(false, "当前快捷键不可用");
                return;
            }
            try
            {
                var values = new JavaScriptSerializer { MaxJsonLength = 512 }
                    .Deserialize<Dictionary<string, object>>(request);
                if (values == null || values.Count != 2 ||
                    !values.ContainsKey("exit") || !values.ContainsKey("lobby") ||
                    !(values["exit"] is string) || !(values["lobby"] is string))
                {
                    PostHotkeysResult(false, "快捷键格式无效");
                    return;
                }
                string error;
                var saved = module.TryUpdateBindings((string)values["exit"], (string)values["lobby"], out error);
                if (saved) Sync();
                PostHotkeysResult(saved, saved ? "快捷键已保存" : (error ?? "快捷键冲突或格式无效"));
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified quick hotkey update failed: " + exception.GetType().Name);
                PostHotkeysResult(false, "快捷键保存失败");
            }
        }

        private void PostHotkeysResult(bool ok, string message)
        {
            if (_canPublish()) _publish(new JavaScriptSerializer().Serialize(new {
                type = "quick-hotkeys-result", ok, message
            }));
        }

        internal void Sync()
        {
            if (!_canPublish()) return;
            var module = _owner();
            if (module == null) return;
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "quick-tools",
                values = new
                {
                    autoSearch = module.AutoMatchmakingEnabled,
                    autoAccept = module.AutoAcceptEnabled,
                    autoReconnect = module.AutoReconnectEnabled,
                    autoHonor = module.AutoHonorEnabled,
                    autoReturn = module.AutoReturnLobbyEnabled
                },
                exitHotkey = module.ExitGameHotkey,
                lobbyHotkey = module.CloseLobbyHotkey
            }));
        }

        internal void OpenInsights()
        {
            if (!_canPublish()) return;
            _insightsOpen = true;
            RefreshInsights();
        }

        internal void InsightPhaseChanged(LeagueDashboardPhaseState state)
        {
            if (!_insightsOpen || !_canPublish()) return;
            var phase = state != null && state.Connected ? state.Phase : null;
            if (_roomCancellation == null && _roomSnapshot != null && _roomSnapshot.Phase != phase)
                PublishRoom(null, "房间状态已变化，请刷新");
            if (!string.Equals(_insightPhase, phase, StringComparison.Ordinal)) RefreshInsights();
        }

        internal void CancelInsights()
        {
            _insightsOpen = false;
            var room = _roomCancellation;
            _roomCancellation = null;
            _roomSnapshot = null;
            _roomRevision++;
            if (room != null) room.Cancel();
            CancelInsightRequest();
            _insightPhase = null;
        }

        private void CancelInsightRequest()
        {
            var previous = _insightCancellation;
            _insightCancellation = null;
            if (previous != null) previous.Cancel();
        }

        internal async void RefreshInsights()
        {
            if (!_insightsOpen || !_canPublish()) return;
            CancelInsightRequest();
            var module = _owner();
            _insightPhase = module == null ? null : module.InsightPhase;
            var phase = _insightPhase;
            if (string.IsNullOrEmpty(phase))
            {
                PublishInsights(null, phase);
                return;
            }

            var source = new CancellationTokenSource();
            _insightCancellation = source;
            _publish("{\"type\":\"quick-insights-loading\"}");
            try
            {
                var result = await module.ReadInsightsAsync(phase, source.Token);
                if (InsightCurrent(module, source, phase)) PublishInsights(result, phase);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                AppLog.Info("League insight view unavailable: " + e.GetType().Name);
                if (InsightCurrent(module, source, phase)) PublishInsights(null, phase);
            }
            finally
            {
                if (ReferenceEquals(_insightCancellation, source)) _insightCancellation = null;
                source.Dispose();
            }
        }

        private bool InsightCurrent(LeagueEfficiencyModule module, CancellationTokenSource source, string phase)
        {
            return _insightsOpen && _canPublish() && ReferenceEquals(_insightCancellation, source) &&
                !source.IsCancellationRequested && ReferenceEquals(module, _owner()) &&
                string.Equals(phase, module.InsightPhase, StringComparison.Ordinal);
        }

        private void PublishInsights(LeagueInsightSnapshot data, string phase)
        {
            if (!_canPublish() || !_insightsOpen) return;
            var values = LeagueInsightPresentation.Project(data, phase);
            values.Add("type", "quick-insights");
            _publish(new JavaScriptSerializer().Serialize(values));
        }

        internal static bool TryParseRoomCommand(string message, out int revision, out LeagueLobbyRoomAction action, out long value)
        {
            revision = 0;
            action = LeagueLobbyRoomAction.Queue;
            value = 0;
            if (string.IsNullOrEmpty(message) || message.Length > 96) return false;
            var parts = message.Split(':');
            if (parts.Length != 5 || parts[0] != "room" || parts[1] != "apply" ||
                !int.TryParse(parts[2], out revision) || revision <= 0 ||
                !Enum.TryParse(parts[3], false, out action) || !Enum.IsDefined(typeof(LeagueLobbyRoomAction), action) ||
                action.ToString() != parts[3] || !long.TryParse(parts[4], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out value)) return false;
            string method, path, body;
            return LeagueMatchmakingWriteApiClient.TryBuildRoomRequest(action, value, out method, out path, out body);
        }

        internal async void HandleRoom(string request)
        {
            if (!_insightsOpen || !_canPublish() || _roomCancellation != null) return;
            var module = _owner();
            if (module == null) { PublishRoom(null, "房间状态不可用"); return; }
            var read = request == "room:read";
            int revision = 0;
            var action = LeagueLobbyRoomAction.Queue;
            long value = 0;
            if (!read && (!TryParseRoomCommand(request, out revision, out action, out value) ||
                revision != _roomRevision || _roomSnapshot == null || !_roomSnapshot.Allows(action, value)))
            {
                PublishRoom(null, "房间状态已变化，请刷新");
                return;
            }
            var expected = _roomSnapshot;
            var source = new CancellationTokenSource();
            _roomCancellation = source;
            _publish("{\"type\":\"room-loading\"}");
            try
            {
                if (read)
                {
                    var snapshot = await module.ReadLobbyRoomAsync(source.Token);
                    if (RoomCurrent(module, source))
                        PublishRoom(snapshot, null);
                }
                else
                {
                    var result = await module.ApplyLobbyRoomAsync(expected, action, value, source.Token);
                    if (RoomCurrent(module, source))
                        PublishRoom(result.Snapshot, LeagueLobbyRoomText.Status(result.State));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                AppLog.Info("League room action unavailable: " + error.GetType().Name);
                if (RoomCurrent(module, source)) PublishRoom(null, "房间状态不可用");
            }
            finally
            {
                if (ReferenceEquals(_roomCancellation, source)) _roomCancellation = null;
                source.Dispose();
            }
        }

        private bool RoomCurrent(LeagueEfficiencyModule module, CancellationTokenSource source)
        {
            return _insightsOpen && _canPublish() && ReferenceEquals(_roomCancellation, source) &&
                !source.IsCancellationRequested && ReferenceEquals(module, _owner());
        }

        private void PublishRoom(LeagueLobbyRoomSnapshot snapshot, string status)
        {
            if (!_insightsOpen || !_canPublish()) return;
            _roomSnapshot = snapshot;
            _roomRevision++;
            var members = snapshot == null ? new LeagueLobbyRoomMember[0] :
                snapshot.Members.Where(x => x.Id != snapshot.SelfId).ToArray();
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "room-state", revision = _roomRevision,
                available = snapshot != null,
                canQueue = snapshot != null && snapshot.CanQueue,
                canLeave = snapshot != null && snapshot.CanLeave,
                canManage = snapshot != null && snapshot.CanManageMembers && members.Length > 0,
                canSetPositions = snapshot != null && snapshot.CanSetPositions,
                canSpectate = snapshot != null && snapshot.CanSpectate,
                firstRole = snapshot == null ? 0 : snapshot.FirstRole,
                secondRole = snapshot == null ? 0 : snapshot.SecondRole,
                roles = Enumerable.Range(0, 7).Select(id => new { id, label = LeagueLobbyRoomText.RoleLabel(id) }).ToArray(),
                queueId = snapshot == null ? 0 : snapshot.QueueId,
                description = snapshot == null ? "房间状态不可用" : snapshot.Phase == "None" ? "未加入房间" :
                    snapshot.QueueName + " · " + snapshot.Members.Count + " 人" +
                    (snapshot.IsLeader ? " · 队长" : "") + (snapshot.IsSpectator == true ? " · 观战" : ""),
                status = string.IsNullOrEmpty(status) ? LeagueLobbyRoomText.CatalogStatus(snapshot) : status,
                queues = (snapshot?.QueueChoices ?? new System.Collections.Generic.Dictionary<int, string>())
                    .OrderBy(x => x.Key).Select(x => new { id = x.Key, label = x.Value }).ToArray(),
                members = members.Select(x => new
                {
                    id = x.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), label = x.Name,
                    canGrant = snapshot.Allows(LeagueLobbyRoomAction.GrantInvite, x.Id),
                    canRevoke = snapshot.Allows(LeagueLobbyRoomAction.RevokeInvite, x.Id)
                }).ToArray()
            }));
        }

        private static bool TryParseSetting(string request, out string key, out bool enabled)
        {
            key = null;
            enabled = false;
            if (string.IsNullOrEmpty(request) || request.Length > 64) return false;
            var divider = request.LastIndexOf(':');
            if (divider < 1) return false;
            key = request.Substring(0, divider);
            var value = request.Substring(divider + 1);
            if (value != "true" && value != "false") return false;
            if (key != "autoSearch" && key != "autoAccept" && key != "autoReconnect" &&
                key != "autoHonor" && key != "autoReturn") return false;
            enabled = value == "true";
            return true;
        }

        internal static void ValidateForSmokeTest()
        {
            int revision;
            LeagueLobbyRoomAction action;
            long value;
            if (!TryParseRoomCommand("room:apply:3:Kick:3000000002", out revision, out action, out value) ||
                revision != 3 || action != LeagueLobbyRoomAction.Kick || value != 3000000002L ||
                TryParseRoomCommand("room:apply:3:Queue:999999", out revision, out action, out value) ||
                TryParseRoomCommand("room:apply:3:99:1", out revision, out action, out value) ||
                TryParseRoomCommand("room:apply:0:Leave:0", out revision, out action, out value))
                throw new InvalidOperationException("Room commands escaped their revision, action or value contract.");
            string key;
            bool enabled;
            foreach (var name in new[] { "autoSearch", "autoAccept", "autoReconnect", "autoHonor", "autoReturn" })
            {
                if (!TryParseSetting(name + ":true", out key, out enabled) || key != name || !enabled ||
                    !TryParseSetting(name + ":false", out key, out enabled) || enabled)
                    throw new InvalidOperationException("Quick tools setting allowlist changed.");
            }
            foreach (var invalid in new[] { "unknown:true", "autoSearch:1", "autoSearch:TRUE",
                "autoSearch:true:extra", "autoHonor:", ":true", null })
                if (TryParseSetting(invalid, out key, out enabled))
                    throw new InvalidOperationException("Quick tools accepted an invalid command.");
            var called = 0;
            var hidden = new UnifiedWorkspaceQuickToolsAdapter(() => false,
                delegate { throw new InvalidOperationException("Hidden quick tools published."); },
                delegate { called++; return null; });
            hidden.Sync();
            hidden.Apply("autoSearch:true");
            var visible = new UnifiedWorkspaceQuickToolsAdapter(() => true,
                delegate { throw new InvalidOperationException("Missing module published quick settings."); },
                delegate { called++; return null; });
            visible.Apply("notASetting:true");
            visible.Sync();
            if (called != 1) throw new InvalidOperationException("Quick tools owner or route gating changed.");
        }
    }
}
