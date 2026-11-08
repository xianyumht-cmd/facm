using System.Windows.Forms;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceMayhemAdapter _mayhemAdapter;
        private UnifiedWorkspaceRecommendationAdapter _recommendationAdapter;

        private UnifiedWorkspaceMayhemAdapter MayhemAdapter
        {
            get
            {
                if (_mayhemAdapter == null)
                    _mayhemAdapter = new UnifiedWorkspaceMayhemAdapter(
                        delegate { return CanPublishOnRoute(LeagueHubNavigation.Mayhem); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); });
                return _mayhemAdapter;
            }
        }

        private UnifiedWorkspaceRecommendationAdapter RecommendationAdapter
        {
            get
            {
                if (_recommendationAdapter == null)
                    _recommendationAdapter = new UnifiedWorkspaceRecommendationAdapter(
                        delegate { return CanPublishOnRoute(LeagueHubNavigation.Recommendation); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); });
                return _recommendationAdapter;
            }
        }

        internal void OpenMayhemPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Mayhem);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            PostLeaguePageStatus("mayhem-failed", "ready");
        }

        internal void StartMayhemQuery(string query)
        {
            if (_navigation.Current == LeagueHubNavigation.Mayhem)
                MayhemAdapter.Start(LeagueHubUiBridge.UnifiedMayhem, query);
        }

        internal void CancelMayhemRead()
        {
            if (_mayhemAdapter != null) _mayhemAdapter.Cancel();
        }

        internal void OpenRecommendationPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Recommendation);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            PostLeaguePageStatus("recommendation-failed", "loading");
            if (_ready) StartRecommendationRead(false);
        }

        internal void StartRecommendationRead(bool force)
        {
            RecommendationAdapter.Start(LeagueHubUiBridge.UnifiedAdvisor, force);
        }

        internal void ApplyUnifiedRecommendation(string json)
        {
            RecommendationAdapter.Apply(LeagueHubUiBridge.UnifiedAdvisor, json);
        }

        internal void CancelRecommendationRead()
        {
            if (_recommendationAdapter != null) _recommendationAdapter.Cancel();
        }

    }
}
