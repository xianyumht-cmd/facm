using System;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed class PlayerHistoryShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.player-history"; } }
        public string Route { get { return LeagueHubNavigation.Player; } }

        public bool Recognizes(string message)
        {
            return message == "history-refresh" || message == "history-more";
        }

        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        {
            workspace.OpenHistoryPage(pushHistory);
        }

        public void Ready(UnifiedWorkspaceForm workspace)
        {
            workspace.StartHistoryRead(10, false);
        }

        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            workspace.StartHistoryRead(message == "history-more" ? 20 : 10, true);
        }

        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        {
            // The advanced page can reuse the current account's already loaded history.
            if (nextRoute != Route && nextRoute != "player-advanced") workspace.CancelHistoryRead();
        }
    }

    internal sealed class AdvancedHistoryShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.advanced-history"; } }
        public string Route { get { return "player-advanced"; } }

        public bool Recognizes(string message)
        {
            return message == "history-advanced-replay-folder" ||
                message == "history-region-options" || message == "history-region-catalog" ||
                message == "history-advanced-refresh" ||
                (message != null && (
                    message.StartsWith("history-advanced-timeline:", StringComparison.Ordinal) ||
                    message.StartsWith("history-advanced-radar:", StringComparison.Ordinal) ||
                    message.StartsWith("history-advanced-replay:", StringComparison.Ordinal) ||
                    message.StartsWith("history-region-search:", StringComparison.Ordinal) ||
                    message.StartsWith("history-region-detail:", StringComparison.Ordinal) ||
                    message.StartsWith("history-advanced-detail:", StringComparison.Ordinal)));
        }

        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        {
            workspace.OpenAdvancedHistoryPage(pushHistory);
        }

        public void Ready(UnifiedWorkspaceForm workspace)
        {
            workspace.OpenAdvancedHistoryData();
            workspace.SendRegionalOptions();
        }

        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "history-advanced-replay-folder") workspace.ReadAdvancedReplayFolder();
            else if (message == "history-region-options") workspace.SendRegionalOptions();
            else if (message == "history-region-catalog") workspace.RefreshRegionalCatalog();
            else if (message == "history-advanced-refresh") workspace.StartHistoryRead(20, true);
            else if (message.StartsWith("history-advanced-timeline:", StringComparison.Ordinal))
                workspace.ReadAdvancedTimeline(message.Substring("history-advanced-timeline:".Length));
            else if (message.StartsWith("history-advanced-radar:", StringComparison.Ordinal))
                workspace.ReadAdvancedRadar(message.Substring("history-advanced-radar:".Length));
            else if (message.StartsWith("history-advanced-replay:", StringComparison.Ordinal))
                workspace.StartAdvancedReplay(message.Substring("history-advanced-replay:".Length));
            else if (message.StartsWith("history-region-search:", StringComparison.Ordinal))
                workspace.SearchRegionalRiotId(message.Substring("history-region-search:".Length));
            else if (message.StartsWith("history-region-detail:", StringComparison.Ordinal))
                workspace.ReadRegionalMatchDetail(message.Substring("history-region-detail:".Length));
            else if (message.StartsWith("history-advanced-detail:", StringComparison.Ordinal))
                workspace.LoadAdvancedHistoryDetail(message.Substring("history-advanced-detail:".Length));
        }

        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        {
            if (nextRoute == Route) return;
            workspace.CancelAdvancedHistoryDetail();
            workspace.CancelRegionalHistoryRead();
        }
    }

}
