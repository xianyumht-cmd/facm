using System.Globalization;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.League;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspaceHistoryAdapter _historyAdapter;

        private UnifiedWorkspaceHistoryAdapter HistoryAdapter
        {
            get
            {
                if (_historyAdapter == null)
                    _historyAdapter = new UnifiedWorkspaceHistoryAdapter(
                        delegate { return CanPublishOnRoute(LeagueHubNavigation.Player, "player-advanced"); },
                        delegate { return _navigation.Current == "player-advanced"; },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        ResetAdvancedHistorySnapshot,
                        SyncAdvancedHistoryList);
                return _historyAdapter;
            }
        }

        internal void OpenHistoryPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Player);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready && (pushHistory || !HistoryAdapter.PublishCurrent(LeagueHubUiBridge.UnifiedPlayer)))
                StartHistoryRead(10, false);
        }

        internal void OpenRecentMatchDetail(string raw)
        {
            if (!CanPublishOnRoute(LeagueHubNavigation.Player) || RequiresForcedOnlineUpdate || HasActiveCleanup) return;
            long gameId;
            if (string.IsNullOrEmpty(raw) || raw.Length > 19 ||
                !long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out gameId)) return;
            if (!HistoryAdapter.ContainsMatch(LeagueHubUiBridge.UnifiedPlayer, gameId))
            {
                PostLeaguePageStatus("history-failed", "context-changed");
                return;
            }
            UnifiedWorkspaceShardCatalog.CancelOutside(this, "player-advanced");
            OpenAdvancedHistoryPage(true, true);
            if (!CanPublishOnRoute("player-advanced")) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(
                new { type = "history-select-local", id = gameId.ToString(CultureInfo.InvariantCulture) }));
            LoadAdvancedHistoryDetail(gameId.ToString(CultureInfo.InvariantCulture));
        }

        internal void StartHistoryRead(int count, bool force)
        {
            HistoryAdapter.Start(LeagueHubUiBridge.UnifiedPlayer, count, force);
        }

        internal void CancelHistoryRead()
        {
            if (_historyAdapter != null) _historyAdapter.Cancel();
        }
    }
}
