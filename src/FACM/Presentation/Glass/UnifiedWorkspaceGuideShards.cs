using System;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed class MayhemGuideShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.mayhem-guide"; } }
        public string Route { get { return LeagueHubNavigation.Mayhem; } }

        public bool Recognizes(string message)
        {
            return message == "mayhem-cancel" ||
                (message != null && message.StartsWith("mayhem-query:", StringComparison.Ordinal));
        }

        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        {
            workspace.OpenMayhemPage(pushHistory);
        }

        public void Ready(UnifiedWorkspaceForm workspace) { }

        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "mayhem-cancel")
            {
                workspace.CancelMayhemRead();
                workspace.PostLeaguePageStatus("mayhem-failed", "canceled");
            }
            else workspace.StartMayhemQuery(message.Substring("mayhem-query:".Length));
        }

        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        {
            if (nextRoute != Route) workspace.CancelMayhemRead();
        }
    }

    internal sealed class RecommendationShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.recommendation"; } }
        public string Route { get { return LeagueHubNavigation.Recommendation; } }

        public bool Recognizes(string message)
        {
            return message == "recommendation-refresh" ||
                (message != null && message.StartsWith("recommendation-apply:", StringComparison.Ordinal));
        }

        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        {
            workspace.OpenRecommendationPage(pushHistory);
        }

        public void Ready(UnifiedWorkspaceForm workspace)
        {
            workspace.StartRecommendationRead(false);
        }

        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "recommendation-refresh") workspace.StartRecommendationRead(true);
            else workspace.ApplyUnifiedRecommendation(message.Substring("recommendation-apply:".Length));
        }

        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        {
            if (nextRoute != Route) workspace.CancelRecommendationRead();
        }
    }

}
