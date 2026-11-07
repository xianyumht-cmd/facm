using System;

namespace FACM.Presentation.Glass
{
    internal sealed class CleanupShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "ggman.cleanup"; } }
        public string Route { get { return "cleanup"; } }
        public bool Recognizes(string message)
        {
            return message == "cleanup-refresh" || message == "cleanup-select" ||
                message == "cleanup-detect" || message == "cleanup-driver" ||
                message == "cleanup-preview" || message == "cleanup-cancel" ||
                message == "cleanup-elevate" ||
                (message != null && message.StartsWith("cleanup-confirm:", StringComparison.Ordinal));
        }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenUnifiedCleanupPage(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace)
        { workspace.SyncUnifiedCleanupState(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "cleanup-refresh") workspace.SyncUnifiedCleanupState();
            else if (message == "cleanup-select") workspace.SelectUnifiedCleanupDirectory();
            else if (message == "cleanup-detect") workspace.DetectUnifiedCleanupDirectory();
            else if (message == "cleanup-driver") workspace.OpenUnifiedDriverRepair();
            else if (message == "cleanup-preview") workspace.PreviewUnifiedCleanup();
            else if (message == "cleanup-cancel")
            {
                workspace.ResetCleanupPreview();
                workspace.SyncUnifiedCleanupState();
            }
            else if (message == "cleanup-elevate") workspace.RestartUnifiedCleanupElevated();
            else workspace.ConfirmUnifiedCleanup(message.Substring("cleanup-confirm:".Length));
        }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        { if (nextRoute != Route) workspace.ResetCleanupPreview(); }
    }

    internal sealed class CloudSyncShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "ggman.cloud-sync"; } }
        public string Route { get { return "cloud-sync"; } }
        public bool Recognizes(string message)
        {
            return message == "cloud-refresh" || message == "cloud-publish" ||
                message == "cloud-keep-local" ||
                (message != null && message.StartsWith("cloud-toggle:", StringComparison.Ordinal));
        }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenCloudPage(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace) { workspace.SyncCloudState(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "cloud-refresh") workspace.SyncCloudState();
            else if (message == "cloud-publish") workspace.PublishCloudGameProfile();
            else if (message == "cloud-keep-local") workspace.ResolveCloudConflict();
            else workspace.ApplyCloudToggle(message.Substring("cloud-toggle:".Length));
        }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        { if (nextRoute != Route) workspace.LeaveCloudPage(); }
    }

    internal sealed class UpdatesShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "ggman.updates"; } }
        public string Route { get { return "updates"; } }
        public bool Recognizes(string message)
        {
            return message == "updates-refresh" || message == "updates-install" ||
                message == "updates-stop" || message == "updates-exit" ||
                message == "updates-link" ||
                (message != null && message.StartsWith("updates-auto:", StringComparison.Ordinal));
        }
        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        { workspace.OpenUpdatesPage(pushHistory); }
        public void Ready(UnifiedWorkspaceForm workspace)
        { workspace.SyncOnlineInitialState(); }
        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "updates-refresh") workspace.StartOnlineRead();
            else if (message == "updates-install") workspace.BeginOnlineInstallation();
            else if (message == "updates-stop") workspace.CancelOnlineInstallation();
            else if (message == "updates-exit") workspace.ExitForcedOnline();
            else if (message == "updates-link") workspace.OpenOnlineAnnouncementLink();
            else workspace.ApplyOnlineAutoSetting(message.Substring("updates-auto:".Length));
        }
        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        { if (nextRoute != Route) workspace.CancelOnlineRead(); }
    }
}
