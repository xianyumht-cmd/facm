using System;
using System.Linq;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceAdvancedToolsAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Func<LeagueEfficiencyModule> _owner;

        internal UnifiedWorkspaceAdvancedToolsAdapter(Func<bool> canPublish,
            Action<string> publish, Func<LeagueEfficiencyModule> owner)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        internal void Sync()
        {
            if (!_canPublish()) return;
            var module = _owner();
            if (module == null) { PostResult("all", "unavailable"); return; }
            var policies = LeagueInvitationPolicies.Parse(module.InvitationStrategiesJson);
            _publish(new JavaScriptSerializer().Serialize(new
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

        internal void Apply(string request)
        {
            if (!_canPublish() || string.IsNullOrEmpty(request) || request.Length > 3200) return;
            var index = request.IndexOf(':');
            if (index < 1 || index > 18) return;
            var group = request.Substring(0, index);
            UnifiedAdvancedChange change;
            if (!UnifiedWorkspaceAdvancedContract.TryParse(group, request.Substring(index + 1), out change))
            { PostResult(group, "invalid"); return; }
            var module = _owner();
            if (module == null) { PostResult(group, "unavailable"); return; }
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
                PostResult(group, "saved");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified advanced setting save failed: " + exception.GetType().Name);
                PostResult(group, "failed");
            }
        }

        private void PostResult(string group, string status)
        {
            if (_canPublish()) _publish(new JavaScriptSerializer().Serialize(new
            { type = "advanced-result", group, status }));
        }

        internal static void ValidateForSmokeTest()
        {
            var messages = 0;
            var hidden = new UnifiedWorkspaceAdvancedToolsAdapter(() => false,
                delegate { throw new InvalidOperationException("Hidden advanced action published."); }, () => null);
            var visible = new UnifiedWorkspaceAdvancedToolsAdapter(() => true,
                delegate(string json)
                {
                    if (!json.Contains("\"type\":\"advanced-result\""))
                        throw new InvalidOperationException("Advanced tools status contract changed.");
                    messages++;
                }, () => null);
            hidden.Sync();
            visible.Sync();
            visible.Apply("reply:{\"enabled\":false,\"awayOnly\":true,\"text\":\"x\"}");
            if (messages != 2)
                throw new InvalidOperationException("Advanced tools adapter route isolation failed.");
        }
    }
}
