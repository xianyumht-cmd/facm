using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.League;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedAdvancedChange
    {
        internal bool Enabled, Secondary, Tertiary;
        internal string Text, Queue, Tier, Division, Policies;
    }

    // Native writes accept only a bounded, typed schema for each known advanced operation.
    internal static class UnifiedWorkspaceAdvancedContract
    {
        internal static bool TryParse(string group, string json, out UnifiedAdvancedChange result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > 3072) return false;
            try
            {
                var fields = new JavaScriptSerializer { MaxJsonLength = 3072 }
                    .Deserialize<Dictionary<string, object>>(json);
                if (fields == null) return false;
                var value = new UnifiedAdvancedChange();
                switch (group)
                {
                    case "reply":
                        if (fields.Count != 3 || !ReadBool(fields, "enabled", out value.Enabled) ||
                            !ReadBool(fields, "awayOnly", out value.Secondary) ||
                            !ReadString(fields, "text", 500, out value.Text) ||
                            !string.Equals(value.Text, LeagueAutoReplyController.NormalizeText(value.Text),
                                StringComparison.Ordinal) ||
                            (value.Enabled && value.Text.Length == 0)) return false;
                        break;
                    case "invitations":
                        if (fields.Count != 4 || !ReadBool(fields, "enabled", out value.Enabled) ||
                            !ReadBool(fields, "skipAway", out value.Secondary) ||
                            !ReadBool(fields, "skipLeader", out value.Tertiary)) return false;
                        object raw;
                        if (!fields.TryGetValue("policies", out raw)) return false;
                        var policies = raw as Dictionary<string, object>;
                        if (policies == null || policies.Count > LeagueInvitationPolicies.GameTypes.Length)
                            return false;
                        var known = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (var row in policies)
                        {
                            var choice = row.Value as string;
                            if (!LeagueInvitationPolicies.GameTypes.Contains(row.Key, StringComparer.Ordinal) ||
                                (choice != LeagueInvitationPolicies.Accept &&
                                 choice != LeagueInvitationPolicies.Decline &&
                                 choice != LeagueInvitationPolicies.Ignore)) return false;
                            known.Add(row.Key, choice);
                        }
                        value.Policies = LeagueInvitationPolicies.Serialize(known);
                        break;
                    case "aram":
                        if (fields.Count != 2 || !ReadBool(fields, "enabled", out value.Enabled) ||
                            !ReadBool(fields, "visible", out value.Secondary)) return false;
                        break;
                    case "signature":
                        if (fields.Count != 2 || !ReadBool(fields, "enabled", out value.Enabled) ||
                            !ReadString(fields, "message", 512, out value.Text) ||
                            !string.Equals(value.Text,
                                LeagueLoginStatusRestoreController.NormalizeSavedMessage(value.Text),
                                StringComparison.Ordinal)) return false;
                        break;
                    case "rank":
                        if (fields.Count != 4 || !ReadBool(fields, "enabled", out value.Enabled) ||
                            !ReadString(fields, "queue", 40, out value.Queue) ||
                            !ReadString(fields, "tier", 40, out value.Tier) ||
                            !ReadString(fields, "division", 8, out value.Division)) return false;
                        string queue, tier, division;
                        if (!LeaguePresenceService.TryNormalizeRankedStatusForSmokeTest(
                                value.Queue, value.Tier, value.Division,
                                out queue, out tier, out division) ||
                            queue != value.Queue || tier != value.Tier || division != value.Division)
                            return false;
                        break;
                    case "offline":
                    case "respawn":
                        if (fields.Count != 1 || !ReadBool(fields, "enabled", out value.Enabled))
                            return false;
                        break;
                    default: return false;
                }
                result = value;
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        private static bool ReadBool(IDictionary<string, object> fields, string key, out bool value)
        {
            value = false;
            object raw;
            if (!fields.TryGetValue(key, out raw) || !(raw is bool)) return false;
            value = (bool)raw;
            return true;
        }

        private static bool ReadString(IDictionary<string, object> fields, string key, int maxLength, out string value)
        {
            value = null;
            object raw;
            if (!fields.TryGetValue(key, out raw)) return false;
            value = raw as string;
            if (value == null || value.Length > maxLength) return false;
            return !value.Any(char.IsControl);
        }

        internal static void ValidateForSmokeTest()
        {
            UnifiedAdvancedChange change;
            if (!TryParse("reply", "{\"enabled\":true,\"awayOnly\":true,\"text\":\"稍后回复\"}", out change) ||
                change.Text != "稍后回复" || !change.Enabled ||
                TryParse("reply", "{\"enabled\":true,\"awayOnly\":\"true\",\"text\":\"hi\"}", out change) ||
                TryParse("reply", "{\"enabled\":true,\"awayOnly\":false,\"text\":\"\"}", out change) ||
                TryParse("reply", "{\"enabled\":false,\"awayOnly\":false,\"text\":\"hi\",\"extra\":1}", out change) ||
                TryParse("reply", "{\"enabled\":false,\"awayOnly\":false,\"text\":\"hi\\nthere\"}", out change) ||
                TryParse("invitations", "{\"enabled\":true,\"skipAway\":false,\"skipLeader\":false,\"policies\":{\"CUSTOM\":\"accept\"}}", out change) ||
                !TryParse("invitations", "{\"enabled\":true,\"skipAway\":false,\"skipLeader\":true,\"policies\":{\"<DEFAULT>\":\"ignore\"}}", out change) ||
                TryParse("aram", "{\"enabled\":1,\"visible\":false}", out change) ||
                TryParse("rank", "{\"enabled\":true,\"queue\":\"INVALID\",\"tier\":\"GOLD\",\"division\":\"I\"}", out change) ||
                !TryParse("offline", "{\"enabled\":true}", out change) ||
                TryParse("offline", "{\"enabled\":true,\"other\":true}", out change) ||
                TryParse("exec", "{\"enabled\":true}", out change))
                throw new InvalidOperationException("Advanced settings allowlist accepted an invalid native operation.");
        }
    }

    internal sealed partial class UnifiedWorkspaceForm
    {
        private void OpenAdvancedTools(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("efficiency-advanced");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncAdvancedTools();
        }

        private void SyncAdvancedTools()
        {
            if (!_ready || IsDisposed || _closing || _browser.CoreWebView2 == null ||
                _navigation.Current != "efficiency-advanced") return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null)
            {
                PostAdvancedResult("all", "unavailable");
                return;
            }
            var policies = LeagueInvitationPolicies.Parse(module.InvitationStrategiesJson);
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "advanced-settings",
                replyEnabled = module.AutoReplyEnabled,
                replyAway = module.AutoReplyAwayOnly,
                replyText = module.AutoReplyText,
                inviteEnabled = module.AutoHandleInvitationsEnabled,
                inviteAway = module.SkipInvitationsWhenAway,
                inviteLeader = module.AutoSkipLeaderEnabled,
                invitationTypes = LeagueInvitationPolicies.GameTypes.Select(item => new
                {
                    value = item,
                    action = policies.ContainsKey(item) ? policies[item] : LeagueInvitationPolicies.Ignore,
                    label = item == LeagueInvitationPolicies.Default ? "所有模式（默认）" : item
                }).ToArray(),
                aramEnabled = module.AutoAramTeamSideEnabled,
                aramVisible = module.AutoAramTeamSideVisibleToTeam,
                loginSignatureEnabled = module.AutoLoginStatusMessageEnabled,
                loginSignature = module.AutoLoginStatusMessage,
                loginRankEnabled = module.AutoLoginRankedStatusEnabled,
                loginRankQueue = module.AutoLoginRankedQueue,
                loginRankTier = module.AutoLoginRankedTier,
                loginRankDivision = module.AutoLoginRankedDivision,
                rankedQueues = LeaguePresenceService.RankedQueues,
                rankedTiers = LeaguePresenceService.RankedTiers,
                rankedDivisions = LeaguePresenceService.RankedDivisions,
                offlineLocked = module.LockOfflineStatusEnabled,
                respawnEnabled = module.RespawnOverlayEnabled
            }));
        }

        private void ApplyAdvancedTools(string request)
        {
            if (_navigation.Current != "efficiency-advanced" || string.IsNullOrEmpty(request) ||
                request.Length > 3200) return;
            var index = request.IndexOf(':');
            if (index < 1 || index > 18) return;
            var group = request.Substring(0, index);
            UnifiedAdvancedChange change;
            if (!UnifiedWorkspaceAdvancedContract.TryParse(group, request.Substring(index + 1),
                    out change))
            {
                PostAdvancedResult(group, "invalid"); return;
            }
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) { PostAdvancedResult(group, "unavailable"); return; }
            try
            {
                switch (group)
                {
                    case "reply":
                        module.UpdateAutoReply(change.Enabled, change.Secondary, change.Text);
                        break;
                    case "invitations":
                        module.UpdateInvitationSettings(change.Enabled, change.Secondary,
                            change.Policies, change.Tertiary);
                        break;
                    case "aram":
                        module.UpdateAramTeamSideSettings(change.Enabled, change.Secondary);
                        break;
                    case "signature":
                        module.UpdateLoginStatusMessage(change.Enabled, change.Text);
                        break;
                    case "rank":
                        module.UpdateLoginRankedStatus(change.Enabled, change.Queue,
                            change.Tier, change.Division);
                        break;
                    case "offline":
                        module.UpdateLockOfflineStatus(change.Enabled);
                        break;
                    case "respawn":
                        module.UpdateRespawnOverlayEnabled(change.Enabled);
                        break;
                    default: return;
                }
                PostAdvancedResult(group, "saved");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified advanced setting save failed: " + exception.GetType().Name);
                PostAdvancedResult(group, "failed");
            }
        }

        private void PostAdvancedResult(string group, string status)
        {
            if (!_ready || IsDisposed || _closing || _browser.CoreWebView2 == null ||
                _navigation.Current != "efficiency-advanced") return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "advanced-result", group, status
            }));
        }
    }
}
