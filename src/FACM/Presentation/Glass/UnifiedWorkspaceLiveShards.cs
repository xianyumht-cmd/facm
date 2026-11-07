using System;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed class LiveMatchShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.live-match"; } }
        public string Route { get { return LeagueHubNavigation.Live; } }

        public bool Recognizes(string message)
        {
            return message == "live-refresh" || message == "live-sgp";
        }

        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        {
            workspace.OpenLivePage(pushHistory);
        }

        public void Ready(UnifiedWorkspaceForm workspace)
        {
            workspace.StartLiveRead(false);
        }

        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            workspace.StartLiveRead(message == "live-sgp");
        }

        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        {
            if (nextRoute != Route) workspace.CancelLiveRead();
        }
    }

    internal sealed class LiveBenchShard : IUnifiedWorkspaceShard
    {
        public string Id { get { return "league.live-bench"; } }
        public string Route { get { return "live-advanced"; } }

        public bool Recognizes(string message)
        {
            return message == "bench-refresh" ||
                (message != null && message.StartsWith("bench-swap:", StringComparison.Ordinal));
        }

        public void Open(UnifiedWorkspaceForm workspace, bool pushHistory)
        {
            workspace.OpenLiveBenchPage(pushHistory);
        }

        public void Ready(UnifiedWorkspaceForm workspace)
        {
            workspace.StartLiveBenchRead();
        }

        public void Handle(UnifiedWorkspaceForm workspace, string message)
        {
            if (message == "bench-refresh") workspace.StartLiveBenchRead();
            else workspace.SwapLiveBenchChampion(message.Substring("bench-swap:".Length));
        }

        public void CancelOutside(UnifiedWorkspaceForm workspace, string nextRoute)
        {
            if (nextRoute != Route) workspace.CancelLiveBenchRead();
        }
    }
}
