using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeaguePlayerProfileDialog : Form
    {
        private readonly LeaguePlayerDataService _service;
        private readonly UiTextCatalog _ui;
        private readonly string _self, _target, _server;
        private readonly LeagueClientSession _session;
        private readonly Func<LeagueClientSession> _currentSession;
        private readonly LeaguePlayerHistorySource _source;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly TabControl _tabs;
        private readonly ListView _ranked, _masteries;
        private readonly TextBox _search, _masteryDetails;
        private readonly Label _profile, _status;
        private readonly Button _refresh;
        private readonly Dictionary<int, string> _names;
        private List<Dictionary<string, object>> _masteryRows;
        private bool _busy;

        internal LeaguePlayerProfileDialog(LeaguePlayerDataService service, UiTextCatalog ui, string self,
            string target, string name, LeagueClientSession session, Func<LeagueClientSession> currentSession,
            string server = null, LeaguePlayerHistorySource source = LeaguePlayerHistorySource.Automatic)
        {
            _service = service; _ui = ui; _self = self; _target = target; _session = session;
            _currentSession = currentSession; _server = server; _source = source;
            _names = service.TryGetCachedChampionNames() ?? new Dictionary<int, string>();
            Text = ui.Get(UiTextKeys.LeagueProfileDetailsTitle) + " · " + name;
            ClientSize = new Size(700, 490); MinimumSize = new Size(560, 380);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            _profile = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Text = name };
            _tabs = new TabControl { Dock = DockStyle.Fill };
            _ranked = List();
            _ranked.Columns.Add(ui.Get(UiTextKeys.LeagueProfileField), 180);
            _ranked.Columns.Add(ui.Get(UiTextKeys.LeagueProfileSolo), 205);
            _ranked.Columns.Add(ui.Get(UiTextKeys.LeagueProfileFlex), 205);
            AddTab(UiTextKeys.LeagueProfileRanked, _ranked);
            var masteryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            masteryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            masteryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            masteryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            _search = new TextBox { Dock = DockStyle.Fill, AccessibleName = ui.Get(UiTextKeys.LeagueProfileMasterySearch) };
            _search.TextChanged += (sender, args) => RenderMasteries();
            _masteries = List();
            foreach (var key in new[] { UiTextKeys.LeaguePlayerChampion, UiTextKeys.LeagueProfileMasteryLevel,
                UiTextKeys.LeagueProfileMasteryPoints, UiTextKeys.LeagueProfileMasteryLast })
                _masteries.Columns.Add(ui.Get(key), 145);
            _masteries.SelectedIndexChanged += (sender, args) => ShowMastery();
            _masteryDetails = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
                ScrollBars = ScrollBars.Vertical, BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text };
            masteryLayout.Controls.Add(_search, 0, 0); masteryLayout.Controls.Add(_masteries, 0, 1);
            masteryLayout.Controls.Add(_masteryDetails, 0, 2);
            AddTab(UiTextKeys.LeagueProfileMastery, masteryLayout);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            _refresh = new Button { Text = ui.Get(UiTextKeys.LeaguePlayerRemoteRetry), Dock = DockStyle.Fill };
            _refresh.Click += async (sender, args) => await LoadSelectedAsync(true);
            _tabs.SelectedIndexChanged += async (sender, args) => await LoadSelectedAsync(false);
            footer.Controls.Add(_status, 0, 0); footer.Controls.Add(_refresh, 1, 0);
            layout.Controls.Add(_profile, 0, 0); layout.Controls.Add(_tabs, 0, 1); layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);
            Shown += async (sender, args) => await LoadSelectedAsync(true);
        }

        private async Task LoadSelectedAsync(bool force)
        {
            if (_busy || IsDisposed || _lifetime.IsCancellationRequested) return;
            if (!IsCurrent()) { Clear(); _status.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteAccountChanged); return; }
            var mastery = _tabs.SelectedIndex == 1;
            if (!force && (mastery ? _masteryRows != null : _ranked.Items.Count > 0)) return;
            _busy = true; _refresh.Enabled = false; _tabs.Enabled = false;
            _status.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteLoading);
            try
            {
                LeaguePlayerDetailsStatus status;
                if (mastery)
                {
                    _masteryRows = null; RenderMasteries();
                    var result = await _service.LoadPlayerMasteriesAsync(_self, _target, _session, _server, _lifetime.Token);
                    if (IsDisposed || _lifetime.IsCancellationRequested) return;
                    status = result.Status;
                    if (!IsCurrent()) status = LeaguePlayerDetailsStatus.AccountChanged;
                    if (status != LeaguePlayerDetailsStatus.AccountChanged) _masteryRows = result.Masteries;
                    RenderMasteries();
                }
                else
                {
                    _ranked.Items.Clear();
                    var result = await _service.LoadPlayerDetailsAsync(_self, _target, _session, _server, _source, _lifetime.Token);
                    if (IsDisposed || _lifetime.IsCancellationRequested) return;
                    status = result.Status;
                    if (!IsCurrent()) status = LeaguePlayerDetailsStatus.AccountChanged;
                    if (status != LeaguePlayerDetailsStatus.AccountChanged) RenderProfile(result);
                }
                if (status == LeaguePlayerDetailsStatus.AccountChanged) Clear();
                _status.Text = StatusText(status);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!IsDisposed) _status.Text = StatusText(LeaguePlayerDetailsStatus.Unavailable); }
            finally
            {
                _busy = false;
                if (!IsDisposed) { _refresh.Enabled = true; _tabs.Enabled = true; }
            }
        }

        private void RenderProfile(LeaguePlayerProfileDetails details)
        {
            _profile.Text = details.Profile == null ? "—" : details.Profile.AccountName + " · " +
                _ui.Get(UiTextKeys.LeagueDashboardLevel) + " " + (details.Profile.SummonerLevel > 0 ? details.Profile.SummonerLevel.ToString() : "—") +
                "\r\n" + _ui.Get(UiTextKeys.LeagueProfileBackground).Replace("{0}", Number(details.Cosmetics, "backgroundSkinId"));
            if (details.Status == LeaguePlayerDetailsStatus.UnsupportedRegion) return;
            var solo = LeaguePlayerDataService.RankedEntry(details.Ranked, "RANKED_SOLO_5x5");
            var flex = LeaguePlayerDataService.RankedEntry(details.Ranked, "RANKED_FLEX_SR");
            AddRank(UiTextKeys.LeagueProfileTier, Tier(solo, "tier", "division"), Tier(flex, "tier", "division"));
            foreach (var pair in new[] {
                new[] { "leaguePoints", UiTextKeys.LeagueProfileLp }, new[] { "wins", UiTextKeys.LeagueProfileWins },
                new[] { "losses", UiTextKeys.LeagueProfileLosses }, new[] { "ratedRating", UiTextKeys.LeagueProfileRating },
                new[] { "provisionalGamesRemaining", UiTextKeys.LeagueProfileProvisional },
                new[] { "currentSeasonWinsForRewards", UiTextKeys.LeagueProfileRewardWins } })
                AddRank(pair[1], Number(solo, pair[0]), Number(flex, pair[0]));
            AddRank(UiTextKeys.LeagueProfileHighest, Tier(solo, "highestTier", "highestDivision"), Tier(flex, "highestTier", "highestDivision"));
            AddRank(UiTextKeys.LeagueProfilePrevious, Tier(solo, "previousSeasonEndTier", "previousSeasonEndDivision"),
                Tier(flex, "previousSeasonEndTier", "previousSeasonEndDivision"));
            AddRank(UiTextKeys.LeagueProfileWinrate, Winrate(solo), Winrate(flex));
        }

        private void RenderMasteries()
        {
            _masteries.BeginUpdate(); _masteries.Items.Clear(); _masteryDetails.Text = string.Empty;
            var query = _search.Text.Trim();
            foreach (var row in _masteryRows ?? new List<Dictionary<string, object>>())
            {
                var id = (int)LeagueMatchTimelineParser.Number(row, "championId").Value;
                string name;
                if (!_names.TryGetValue(id, out name)) name = "#" + id;
                if (query.Length > 0 && name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    id.ToString().IndexOf(query, StringComparison.Ordinal) < 0) continue;
                var item = new ListViewItem(name) { Tag = row };
                item.SubItems.Add(Number(row, "championLevel")); item.SubItems.Add(Number(row, "championPoints"));
                item.SubItems.Add(LastPlay(row)); _masteries.Items.Add(item);
            }
            _masteries.EndUpdate();
        }

        private void ShowMastery()
        {
            if (_masteries.SelectedItems.Count != 1) { _masteryDetails.Text = string.Empty; return; }
            var row = _masteries.SelectedItems[0].Tag as Dictionary<string, object>;
            _masteryDetails.Text = _ui.Get(UiTextKeys.LeagueProfileMasteryProgress)
                .Replace("{0}", Number(row, "championPointsSinceLastLevel")).Replace("{1}", Number(row, "championPointsUntilNextLevel")) +
                "\r\n" + _ui.Get(UiTextKeys.LeagueProfileMasteryMarks).Replace("{0}", Number(row, "tokensEarned"))
                    .Replace("{1}", Number(row, "markRequiredForNextLevel")) +
                "\r\n" + _ui.Get(UiTextKeys.LeagueProfileMasterySeason).Replace("{0}", Number(row, "championSeasonMilestone"))
                    .Replace("{1}", LeagueMatchTimelineParser.Text(row, "highestGrade") ?? "—");
        }

        private static string Number(Dictionary<string, object> data, string key)
        { return LeagueMatchTimelineParser.Number(data, key)?.ToString("N0", CultureInfo.CurrentCulture) ?? "—"; }
        internal static string LastPlay(Dictionary<string, object> row)
        {
            var time = LeagueMatchTimelineParser.Number(row, "lastPlayTime");
            if (!(time > 0)) return "—";
            try { return DateTimeOffset.FromUnixTimeMilliseconds(time.Value).ToLocalTime().ToString("yyyy-MM-dd"); }
            catch (ArgumentOutOfRangeException) { return "—"; }
        }
        private string Tier(Dictionary<string, object> row, string tierKey, string divisionKey)
        {
            if (row == null) return "—";
            var tier = LeagueMatchTimelineParser.Text(row, tierKey);
            if (string.IsNullOrEmpty(tier)) return "—";
            if (tier == "NA" || tier == "NONE") return _ui.Get(UiTextKeys.LeagueProfileUnranked);
            var translated = _ui.Get(UiTextKeys.LeagueProfileTiers).Split('|');
            var index = Array.IndexOf(new[] { "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER" }, tier);
            return (index >= 0 && index < translated.Length ? translated[index] : tier) +
                (index >= 7 ? "" : " " + (LeagueMatchTimelineParser.Text(row, divisionKey) ?? "—"));
        }
        internal static string Winrate(Dictionary<string, object> row)
        {
            var wins = LeagueMatchTimelineParser.Number(row, "wins"); var losses = LeagueMatchTimelineParser.Number(row, "losses");
            if (!wins.HasValue || !losses.HasValue) return "—";
            return ((double)wins.Value + losses.Value > 0 ? wins.Value * 100d / ((double)wins.Value + losses.Value) : 0)
                .ToString("0.##", CultureInfo.CurrentCulture) + "%";
        }
        private string StatusText(LeaguePlayerDetailsStatus status)
        {
            return _ui.Get(status == LeaguePlayerDetailsStatus.AccountChanged ? UiTextKeys.LeaguePlayerRemoteAccountChanged :
                status == LeaguePlayerDetailsStatus.UnsupportedRegion ? UiTextKeys.LeagueProfileUnsupported :
                status == LeaguePlayerDetailsStatus.Ready ? UiTextKeys.LeagueProfileReady :
                status == LeaguePlayerDetailsStatus.Partial ? UiTextKeys.LeagueProfilePartial : UiTextKeys.LeaguePlayerRemoteHistoryUnavailable);
        }
        private bool IsCurrent() { return _currentSession == null || LeagueEncounterReplayService.SessionMatches(_session, _currentSession()); }
        private void Clear() { _profile.Text = string.Empty; _ranked.Items.Clear(); _masteryRows = null; RenderMasteries(); }
        private void AddRank(string key, string solo, string flex)
        { var row = new ListViewItem(_ui.Get(key)); row.SubItems.Add(solo); row.SubItems.Add(flex); _ranked.Items.Add(row); }
        private void AddTab(string key, Control control)
        { var page = new TabPage(_ui.Get(key)); page.Controls.Add(control); _tabs.TabPages.Add(page); }
        private static ListView List()
        { return new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
            MultiSelect = false, BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text }; }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed) { _lifetime.Cancel(); _lifetime.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
