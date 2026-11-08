using System;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed class DashboardShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.dashboard"; } }
        public string Route { get { return LeagueHubNavigation.Dashboard; } }
        public bool Recognizes(string message) { return message == "dashboard-refresh"; }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenDashboardPage(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace) { workspace.StartDashboardRead(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        { workspace.StartDashboardRead(); }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        {
            if (nextRoute != Route && nextRoute != "dashboard-advanced")
                workspace.CancelDashboardRead();
        }
    }

    internal sealed class AdvancedDashboardShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.dashboard-advanced"; } }
        public string Route { get { return "dashboard-advanced"; } }
        public bool Recognizes(string message)
        { return message == "dashboard-refresh" || message == "dashboard-respawn-refresh"; }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenAdvancedDashboardPage(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace) { workspace.StartDashboardRead(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "dashboard-respawn-refresh") workspace.StartAdvancedRespawnRead();
            else workspace.StartDashboardRead();
        }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        {
            if (nextRoute != Route) workspace.CancelAdvancedRespawnRead();
        }
    }

    internal sealed class PresenceShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.presence"; } }
        public string Route { get { return LeagueHubNavigation.Presence; } }
        public bool Recognizes(string message)
        {
            return message == "presence-refresh" ||
                (message != null && (
                    message.StartsWith("presence-mode:", StringComparison.Ordinal) ||
                    message.StartsWith("presence-signature:", StringComparison.Ordinal) ||
                    message.StartsWith("presence-rank:", StringComparison.Ordinal)));
        }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenPresencePage(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace) { workspace.StartPresenceRead(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "presence-refresh") workspace.StartPresenceRead();
            else if (message.StartsWith("presence-mode:", StringComparison.Ordinal))
                workspace.ApplyPresenceChange("mode", message.Substring("presence-mode:".Length));
            else if (message.StartsWith("presence-signature:", StringComparison.Ordinal))
                workspace.ApplyPresenceChange("signature", message.Substring("presence-signature:".Length));
            else if (message.StartsWith("presence-rank:", StringComparison.Ordinal))
                workspace.ApplyPresenceChange("rank", message.Substring("presence-rank:".Length));
        }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        { if (nextRoute != Route) workspace.CancelPresenceOperation(); }
    }

    internal sealed class ProfileCustomizationShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.profile-customization"; } }
        public string Route { get { return "presence-advanced"; } }
        public bool Recognizes(string message)
        {
            return message == "profile-customization-refresh" ||
                (message != null && (
                    message.StartsWith("profile-customization-champion:", StringComparison.Ordinal) ||
                    message.StartsWith("profile-customization-skin:", StringComparison.Ordinal) ||
                    message.StartsWith("profile-customization-action:", StringComparison.Ordinal)));
        }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenProfileCustomizationPage(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace)
        { workspace.StartProfileCustomizationRead(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "profile-customization-refresh") workspace.StartProfileCustomizationRead();
            else if (message.StartsWith("profile-customization-champion:", StringComparison.Ordinal))
                workspace.LoadProfileSkins(message.Substring("profile-customization-champion:".Length));
            else if (message.StartsWith("profile-customization-skin:", StringComparison.Ordinal))
                workspace.ApplyProfileSkin(message.Substring("profile-customization-skin:".Length));
            else if (message.StartsWith("profile-customization-action:", StringComparison.Ordinal))
                workspace.ApplyProfileCustomizationAction(message.Substring("profile-customization-action:".Length));
        }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        { if (nextRoute != Route) workspace.CancelProfileCustomization(); }
    }

    internal sealed class LocalProfileShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.profile"; } }
        public string Route { get { return LeagueHubNavigation.Profile; } }
        public bool Recognizes(string message) { return message == "profile-refresh"; }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenLocalProfilePage(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace) { workspace.SyncLocalProfile(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        { workspace.SyncLocalProfile(); }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute) { }
    }

    internal sealed class RepairShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.repair"; } }
        public string Route { get { return LeagueHubNavigation.Repair; } }
        public bool Recognizes(string message)
        { return message != null && message.StartsWith("repair-action:", StringComparison.Ordinal); }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenRepairPage(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace) { workspace.SyncRepairState(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        { workspace.ApplyRepairAction(message.Substring("repair-action:".Length)); }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        { if (nextRoute != Route) workspace.CancelRepairOperation(); }
    }

    internal sealed class ClientStatusShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.client-status"; } }
        public string Route { get { return "client-status"; } }
        public bool Recognizes(string message) { return false; }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenClientStatus(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace) { }
        public void Handle(UnifiedWorkspaceForm workspace, string message) { }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute) { }
    }
}
