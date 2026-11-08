using System;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceQuickToolsAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Func<LeagueEfficiencyModule> _owner;

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
            if (module == null) return;
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
                }
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
