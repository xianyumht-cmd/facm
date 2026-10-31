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

    // This bridge only forwards the existing Gameflow owner; it owns no network or timer.
    internal sealed class LeaguePlayerPostgameRefreshDetector
    {
        private bool _sawGame;
        private bool _inPostgame;

        internal bool Observe(LeagueDashboardPhaseState state)
        {
            if (state == null || !state.Connected)
            {
                Reset();
                return false;
            }
            var phase = state.Phase ?? string.Empty;
            if (phase == "GameStart" || phase == "InProgress" || phase == "Reconnect")
                _sawGame = true;
            if (phase == "EndOfGame" || phase == "PreEndOfGame")
            {
                if (_inPostgame || !_sawGame) return false;
                _inPostgame = true;
                var shouldRefresh = _sawGame && state.IsRankedQueue == true;
                _sawGame = false;
                return shouldRefresh;
            }
            _inPostgame = false;
            if (phase == "Lobby" || phase == "None" || phase == "ChampSelect")
                _sawGame = false;
            return false;
        }

        internal void Reset()
        {
            _sawGame = false;
            _inPostgame = false;
        }
    }

    internal static class LeaguePlayerPostgameRefreshBridge
    {
        private static readonly object Sync = new object();
        private static readonly LeaguePlayerPostgameRefreshDetector Detector =
            new LeaguePlayerPostgameRefreshDetector();

        internal static event Action PostgameEntered;

        internal static void Observe(LeagueDashboardPhaseState state)
        {
            Action subscribers = null;
            lock (Sync)
                if (Detector.Observe(state)) subscribers = PostgameEntered;
            if (subscribers == null) return;
            foreach (var callback in subscribers.GetInvocationList())
            {
                try { ((Action)callback)(); }
                catch (Exception exception)
                {
                    AppLog.Info("Player postgame refresh subscriber skipped: " +
                        exception.GetType().Name);
                }
            }
        }

        internal static void Reset()
        {
            lock (Sync) Detector.Reset();
        }
    }

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
        private readonly ListView _ranked, _rankedQueues, _masteries, _challenges;
        private readonly TextBox _search, _masteryDetails;
        private readonly CheckBox _allMasteries;
        private readonly Label _profile, _status;
        private readonly Button _refresh;
        private readonly Dictionary<int, string> _names;
        private List<Dictionary<string, object>> _masteryRows;
        private Dictionary<string, object> _challengeData;
        private long? _masteryScore;
        private bool _busy;
        private bool _pendingPostgameRefresh;

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
            _rankedQueues = List();
            foreach (var column in new[] {
                new[] { UiTextKeys.LeagueProfileQueue, "140" },
                new[] { UiTextKeys.LeagueProfileTier, "105" },
                new[] { UiTextKeys.LeagueProfileLp, "60" },
                new[] { UiTextKeys.LeagueProfileWins, "60" },
                new[] { UiTextKeys.LeagueProfileLosses, "60" },
                new[] { UiTextKeys.LeagueProfilePrevious, "135" },
                new[] { UiTextKeys.LeagueProfilePreviousHighest, "135" },
                new[] { UiTextKeys.LeagueProfileHighest, "135" } })
                _rankedQueues.Columns.Add(ui.Get(column[0]), int.Parse(column[1], CultureInfo.InvariantCulture));
            var rankedLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            rankedLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
            rankedLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
            rankedLayout.Controls.Add(_ranked, 0, 0);
            rankedLayout.Controls.Add(_rankedQueues, 0, 1);
            AddTab(UiTextKeys.LeagueProfileRanked, rankedLayout);
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
            var masteryTools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            masteryTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            masteryTools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            _allMasteries = new CheckBox { Dock = DockStyle.Fill, Text = ui.Get(UiTextKeys.LeagueProfileMasteryAll) };
            _allMasteries.CheckedChanged += async (sender, args) => await LoadSelectedAsync(true);
            masteryTools.Controls.Add(_search, 0, 0); masteryTools.Controls.Add(_allMasteries, 1, 0);
            masteryLayout.Controls.Add(masteryTools, 0, 0); masteryLayout.Controls.Add(_masteries, 0, 1);
            masteryLayout.Controls.Add(_masteryDetails, 0, 2);
            AddTab(UiTextKeys.LeagueProfileMastery, masteryLayout);
            _challenges = List();
            _challenges.Columns.Add(ui.Get(UiTextKeys.LeagueProfileField), 260);
            _challenges.Columns.Add(ui.Get(UiTextKeys.LeagueProfileChallengeValue), 180);
            AddTab(UiTextKeys.LeagueProfileChallenges, _challenges);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            _refresh = new Button { Text = ui.Get(UiTextKeys.LeaguePlayerRemoteRetry), Dock = DockStyle.Fill };
            _refresh.Click += async (sender, args) => await LoadSelectedAsync(true);
            _tabs.SelectedIndexChanged += async (sender, args) =>
            {
                if (_tabs.SelectedIndex == 0 && _pendingPostgameRefresh)
                    await RefreshAfterGameAsync();
                else
                    await LoadSelectedAsync(false);
            };
            footer.Controls.Add(_status, 0, 0); footer.Controls.Add(_refresh, 1, 0);
            layout.Controls.Add(_profile, 0, 0); layout.Controls.Add(_tabs, 0, 1); layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);
            Shown += async (sender, args) =>
            {
                LeaguePlayerPostgameRefreshBridge.PostgameEntered += OnPostgameEntered;
                await LoadSelectedAsync(true);
            };
        }

        private void OnPostgameEntered()
        {
            if (IsDisposed || !IsHandleCreated || _lifetime.IsCancellationRequested) return;
            try
            {
                BeginInvoke((Action)(async () =>
                {
                    if (IsDisposed || _lifetime.IsCancellationRequested || !IsCurrent() ||
                        !string.Equals(_self, _target, StringComparison.Ordinal) ||
                        _source == LeaguePlayerHistorySource.Sgp || !string.IsNullOrEmpty(_server))
                        return;
                    _pendingPostgameRefresh = true;
                    if (_tabs.SelectedIndex == 0) await RefreshAfterGameAsync();
                }));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private async Task RefreshAfterGameAsync()
        {
            if (!_pendingPostgameRefresh || _busy || IsDisposed || _lifetime.IsCancellationRequested ||
                !IsCurrent() || _tabs.SelectedIndex != 0) return;
            _pendingPostgameRefresh = false;
            var before = RankedSnapshot();
            await LoadSelectedAsync(true);
            if (IsDisposed || _lifetime.IsCancellationRequested || !IsCurrent() ||
                _tabs.SelectedIndex != 0 || !ShouldRetryPostgameRanked(before, RankedSnapshot(), _ranked.Items.Count > 0))
                return;
            try { await Task.Delay(TimeSpan.FromSeconds(3), _lifetime.Token); }
            catch (OperationCanceledException) { return; }
            if (IsDisposed || _lifetime.IsCancellationRequested || !IsCurrent() ||
                _tabs.SelectedIndex != 0 || _busy || _pendingPostgameRefresh) return;
            await LoadSelectedAsync(true);
        }

        internal static bool ShouldRetryPostgameRanked(string before, string after, bool hasRows)
        {
            return hasRows && string.Equals(before, after, StringComparison.Ordinal);
        }

        private string RankedSnapshot()
        {
            return string.Join("|", _ranked.Items.Cast<ListViewItem>().Select(item =>
                string.Join(":", item.SubItems.Cast<ListViewItem.ListViewSubItem>()
                    .Select(subItem => subItem.Text))));
        }

        private async Task LoadSelectedAsync(bool force)
        {
            if (_busy || IsDisposed || _lifetime.IsCancellationRequested) return;
            if (!IsCurrent()) { Clear(); _status.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteAccountChanged); return; }
            var mastery = _tabs.SelectedIndex == 1;
            var challenges = _tabs.SelectedIndex == 2;
            if (!force && (challenges ? _challengeData != null : mastery ? _masteryRows != null : _ranked.Items.Count > 0)) return;
            _busy = true; _refresh.Enabled = false; _tabs.Enabled = false;
            _status.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteLoading);
            try
            {
                LeaguePlayerDetailsStatus status;
                if (challenges)
                {
                    _challengeData = null; _challenges.Items.Clear();
                    var result = await _service.LoadPlayerChallengesAsync(_self, _target, _session, _server, _lifetime.Token);
                    if (IsDisposed || _lifetime.IsCancellationRequested) return;
                    status = IsCurrent() ? result.Status : LeaguePlayerDetailsStatus.AccountChanged;
                    if (status == LeaguePlayerDetailsStatus.Ready) { _challengeData = result.Data; RenderChallenges(); }
                }
                else if (mastery)
                {
                    _masteryRows = null; RenderMasteries();
                    var result = await _service.LoadPlayerMasteriesAsync(_self, _target, _session, _server, _lifetime.Token, !_allMasteries.Checked);
                    if (IsDisposed || _lifetime.IsCancellationRequested) return;
                    if (result.Status == LeaguePlayerDetailsStatus.Ready && _names.Count == 0 && IsCurrent())
                    {
                        var names = await _service.LoadChampionOptionsAsync(_lifetime.Token);
                        if (IsDisposed || _lifetime.IsCancellationRequested) return;
                        if (names != null) foreach (var pair in names) _names[pair.Key] = pair.Value;
                    }
                    status = result.Status;
                    if (!IsCurrent()) status = LeaguePlayerDetailsStatus.AccountChanged;
                    if (status != LeaguePlayerDetailsStatus.AccountChanged) { _masteryRows = result.Masteries; _masteryScore = result.Score; }
                    RenderMasteries();
                }
                else
                {
                    _ranked.Items.Clear(); _rankedQueues.Items.Clear();
                    var result = await _service.LoadPlayerDetailsAsync(_self, _target, _session, _server, _source, _lifetime.Token);
                    if (IsDisposed || _lifetime.IsCancellationRequested) return;
                    status = result.Status;
                    if (!IsCurrent()) status = LeaguePlayerDetailsStatus.AccountChanged;
                    if (status != LeaguePlayerDetailsStatus.AccountChanged) RenderProfile(result);
                }
                if (status == LeaguePlayerDetailsStatus.AccountChanged) Clear();
                _status.Text = StatusText(status);
                if (mastery && status == LeaguePlayerDetailsStatus.Ready && !_allMasteries.Checked)
                    _status.Text += " · " + _ui.Get(UiTextKeys.LeagueProfileMasteryTop).Replace("{0}", _masteryScore?.ToString() ?? "—");
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!IsDisposed) _status.Text = StatusText(LeaguePlayerDetailsStatus.Unavailable); }
            finally
            {
                _busy = false;
                if (!IsDisposed)
                {
                    _refresh.Enabled = true;
                    _tabs.Enabled = true;
                    if (_pendingPostgameRefresh && _tabs.SelectedIndex == 0 &&
                        !_lifetime.IsCancellationRequested && IsCurrent())
                        await RefreshAfterGameAsync();
                }
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
            AddRank(UiTextKeys.LeagueProfilePreviousHighest,
                Tier(solo, "previousSeasonHighestTier", "previousSeasonHighestDivision"),
                Tier(flex, "previousSeasonHighestTier", "previousSeasonHighestDivision"));
            AddRank(UiTextKeys.LeagueProfileWinrate, Winrate(solo), Winrate(flex));
            _rankedQueues.BeginUpdate();
            try
            {
                foreach (var entry in LeaguePlayerDataService.RankedQueueEntries(details.Ranked))
                {
                    var row = entry.Value;
                    var name = entry.Key == "RANKED_SOLO_5x5" ? _ui.Get(UiTextKeys.LeagueProfileSolo) :
                        entry.Key == "RANKED_FLEX_SR" ? _ui.Get(UiTextKeys.LeagueProfileFlex) : entry.Key;
                    var item = new ListViewItem(name);
                    item.SubItems.Add(Tier(row, "tier", "division"));
                    item.SubItems.Add(RankedNumber(row, "leaguePoints"));
                    item.SubItems.Add(RankedNumber(row, "wins"));
                    item.SubItems.Add(RankedNumber(row, "losses"));
                    item.SubItems.Add(Tier(row, "previousSeasonEndTier", "previousSeasonEndDivision"));
                    item.SubItems.Add(Tier(row, "previousSeasonHighestTier", "previousSeasonHighestDivision"));
                    item.SubItems.Add(Tier(row, "highestTier", "highestDivision"));
                    _rankedQueues.Items.Add(item);
                }
            }
            finally { _rankedQueues.EndUpdate(); }
        }

        private void RenderChallenges()
        {
            object raw;
            var total = _challengeData.TryGetValue("totalPoints", out raw) ? raw as Dictionary<string, object> : null;
            ChallengeRow(UiTextKeys.LeagueProfileChallengePoints, MetricText(total, "current"));
            ChallengeRow(UiTextKeys.LeagueProfileChallengeLevel, LeagueMatchTimelineParser.Text(total, "level") ?? "—");
            var rows = (object[])_challengeData["playerChallenges"];
            var ids = new[] { 505001, 510001, 510011, 504003, 504002, 504004, 402106 };
            var labels = new[] { UiTextKeys.LeagueProfileOwnedChampions, UiTextKeys.LeagueProfileOwnedSkins,
                UiTextKeys.LeagueProfileOwnedChromas, UiTextKeys.LeagueProfileOwnedWards,
                UiTextKeys.LeagueProfileOwnedIcons, UiTextKeys.LeagueProfileOwnedEmotes, UiTextKeys.LeagueProfilePentas };
            for (var i = 0; i < ids.Length; i++)
            {
                var row = rows.Cast<Dictionary<string, object>>().FirstOrDefault(r => LeagueMatchTimelineParser.Number(r, "id") == ids[i]);
                ChallengeRow(labels[i], MetricText(row, "currentValue"));
            }
        }
        private void ChallengeRow(string key, string value)
        { var row = new ListViewItem(_ui.Get(key)); row.SubItems.Add(value); _challenges.Items.Add(row); }
        internal static string MetricText(Dictionary<string, object> data, string key)
        {
            object value; double number;
            return data != null && data.TryGetValue(key, out value) && value != null && !(value is bool) &&
                double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out number) && number >= 0 && !double.IsNaN(number) && !double.IsInfinity(number)
                ? number.ToString("0.##", CultureInfo.CurrentCulture) : "—";
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
        internal static string RankedNumber(Dictionary<string, object> row, string key)
        {
            var tier = LeagueMatchTimelineParser.Text(row, "tier");
            return string.IsNullOrEmpty(tier) || tier == "NA" || tier == "NONE" ? "—" : Number(row, key);
        }
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
        private void Clear() { _challengeData = null; _challenges.Items.Clear(); _profile.Text = string.Empty; _ranked.Items.Clear(); _rankedQueues.Items.Clear(); _masteryRows = null; RenderMasteries(); }
        private void AddRank(string key, string solo, string flex)
        { var row = new ListViewItem(_ui.Get(key)); row.SubItems.Add(solo); row.SubItems.Add(flex); _ranked.Items.Add(row); }
        private void AddTab(string key, Control control)
        { var page = new TabPage(_ui.Get(key)); page.Controls.Add(control); _tabs.TabPages.Add(page); }
        private static ListView List()
        { return new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
            MultiSelect = false, BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text }; }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                LeaguePlayerPostgameRefreshBridge.PostgameEntered -= OnPostgameEntered;
                _lifetime.Cancel();
                _lifetime.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
