using System;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed class PostgameSettingsShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.postgame-settings"; } }
        public string Route { get { return "postgame-settings"; } }
        public bool Recognizes(string message)
        { return message != null && message.StartsWith("save-postgame:", StringComparison.Ordinal); }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenPostgameSettings(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace) { workspace.SyncPostgameSettings(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        { workspace.ApplyPostgameSettings(message.Substring("save-postgame:".Length)); }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute) { }
    }

    internal sealed class AutoSelectionShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.auto-selection"; } }
        public string Route { get { return "auto-selection"; } }
        public bool Recognizes(string message)
        {
            return message != null && (
                message.StartsWith("save-selection:", StringComparison.Ordinal) ||
                message.StartsWith("selection-pause:", StringComparison.Ordinal));
        }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenAutoSelection(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace)
        {
            workspace.SyncAutoSelection();
            workspace.StartAutoSelectionCatalog();
        }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message.StartsWith("save-selection:", StringComparison.Ordinal))
                workspace.ApplyAutoSelection(message.Substring("save-selection:".Length));
            else workspace.ApplySelectionPause(message.Substring("selection-pause:".Length));
        }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        { if (nextRoute != Route) workspace.CancelChampionCatalog(); }
    }

    internal sealed class MatchmakingSettingsShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.matchmaking-settings"; } }
        public string Route { get { return "matchmaking-settings"; } }
        public bool Recognizes(string message)
        { return message != null && message.StartsWith("save-matchmaking:", StringComparison.Ordinal); }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenMatchmakingSettings(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace) { workspace.SyncMatchmakingSettings(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        { workspace.ApplyMatchmakingSettings(message.Substring("save-matchmaking:".Length)); }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute) { }
    }

    internal sealed class EfficiencyShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.efficiency"; } }
        public string Route { get { return LeagueHubNavigation.Efficiency; } }
        public bool Recognizes(string message)
        { return message != null && message.StartsWith("toggle:", StringComparison.Ordinal); }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenQuickTools(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace) { workspace.SyncQuickSettings(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        { workspace.ApplyQuickSetting(message.Substring("toggle:".Length)); }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute) { }
    }

    internal sealed class AppearanceShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "ggman.appearance"; } }
        public string Route { get { return "appearance"; } }
        public bool Recognizes(string message)
        {
            return message == "pet-refresh" || message == "pet-disable" ||
                message == "pet-reset" ||
                (message != null && (message.StartsWith("pet-select:", StringComparison.Ordinal) ||
                                     message.StartsWith("theme:", StringComparison.Ordinal)));
        }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenAppearance(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace)
        { workspace.SyncUnifiedPetOptions(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "pet-refresh") workspace.SyncUnifiedPetOptions();
            else if (message.StartsWith("pet-select:", StringComparison.Ordinal))
                workspace.ApplyUnifiedPetChoice(message.Substring("pet-select:".Length));
            else if (message == "pet-disable" || message == "pet-reset")
                workspace.ApplyUnifiedPetAction(message.Substring("pet-".Length));
            else if (message.StartsWith("theme:", StringComparison.Ordinal))
                workspace.ApplyTheme(message.Substring("theme:".Length));
        }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute) { }
    }
}
