using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueRemotePlayerHistoryForm : Form
    {
        private readonly LeaguePlayerDataService _service;
        private readonly UiTextCatalog _ui;
        private readonly LeaguePlayerProfile _expected;
        private readonly Func<LeagueClientSession> _currentSession;
        private readonly Func<LeagueClientSession, string, string, LeaguePlayerMatchSummary,
            CancellationToken, Task<LeagueEncounterReplayResult>> _replay;
        private readonly string _puuid;
        private readonly string _fallbackName;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Label _playerLabel;
        private readonly Label _statusLabel;
        private readonly ListView _matches;
        private readonly Button _replayButton;
        private readonly ComboBox _pageSize;
        private readonly Button _previousPage;
        private readonly Button _nextPage;
        private readonly Button _goToPage;
        private readonly Button _retryPage;
        private readonly NumericUpDown _pageNumber;
        private readonly Label _currentPage;
        private int _startIndex;
        private int _count = LeaguePlayerDataService.InitialMatchCount;
        private bool _hasMore;
        private bool _loading;
        private bool _loaded;
        private bool _replayRunning;
        private LeagueClientSession _pageSession;

        internal LeagueRemotePlayerHistoryForm(LeaguePlayerDataService service, UiTextCatalog ui,
            LeaguePlayerProfile current, string targetPuuid, string targetName,
            Func<LeagueClientSession> currentSession = null,
            Func<LeagueClientSession, string, string, LeaguePlayerMatchSummary,
                CancellationToken, Task<LeagueEncounterReplayResult>> replay = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _expected = new LeaguePlayerProfile { PuuId = current == null ? null : current.PuuId };
            _puuid = targetPuuid;
            _fallbackName = targetName ?? string.Empty;
            _currentSession = currentSession;
            _replay = replay;

            Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteHistoryTitle);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 500);
            MinimumSize = new Size(650, 380);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            _playerLabel = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, Text = _fallbackName,
                ForeColor = FacmDesignSystem.Text, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(FacmThemeRuntime.Current.FontName, 13F, FontStyle.Bold)
            };
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = FacmDesignSystem.TextMuted,
                Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteLoading)
            };
            _matches = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HideSelection = false, MultiSelect = false, GridLines = false,
                BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text
            };
            _matches.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerTime), 120);
            _matches.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerMode), 150);
            _matches.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerChampion), 135);
            _matches.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerKda), 130);
            _matches.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerResult), 90);
            _matches.Resize += (sender, args) => FitMatchColumns();
            _matches.SelectedIndexChanged += (sender, args) => UpdateReplayButton();

            _replayButton = PageButton(UiTextKeys.LeaguePlayerReplayAction, 142);
            _replayButton.Enabled = false;
            _replayButton.Click += async (sender, args) => await ReplaySelectedAsync();

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
                BackColor = FacmDesignSystem.Canvas, Padding = Padding.Empty
            };
            _pageSize = new ComboBox { Width = 80, Height = 27, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text,
                AccessibleName = _ui.Get(UiTextKeys.LeaguePlayerRemotePageSize) };
            foreach (var size in new[] { 10, 20, 30, 40, 50, 100, 200 })
                _pageSize.Items.Add(size);
            _pageSize.SelectedIndex = 0;
            _previousPage = PageButton(UiTextKeys.LeaguePlayerRemotePrevPage, 64);
            _nextPage = PageButton(UiTextKeys.LeaguePlayerRemoteNextPage, 64);
            _currentPage = new Label { Width = 82, Height = 27, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleCenter, ForeColor = FacmDesignSystem.Text };
            _pageNumber = new NumericUpDown { Width = 76, Minimum = 1, Maximum = 100000,
                Value = 1, Height = 27, BackColor = FacmDesignSystem.Surface,
                ForeColor = FacmDesignSystem.Text,
                AccessibleName = _ui.Get(UiTextKeys.LeaguePlayerRemoteGoPage) };
            _goToPage = PageButton(UiTextKeys.LeaguePlayerRemoteGoPage, 52);
            _retryPage = PageButton(UiTextKeys.LeaguePlayerRemoteRetry, 52);
            footer.Controls.Add(new Label { Text = _ui.Get(UiTextKeys.LeaguePlayerRemotePageSize),
                Height = 27, Width = 44, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = FacmDesignSystem.TextMuted });
            footer.Controls.Add(_pageSize);
            footer.Controls.Add(_previousPage);
            footer.Controls.Add(_currentPage);
            footer.Controls.Add(_nextPage);
            footer.Controls.Add(_pageNumber);
            footer.Controls.Add(_goToPage);
            footer.Controls.Add(_retryPage);
            _previousPage.Click += async (sender, args) => await LoadPageAsync(Math.Max(0, _startIndex - _count), _count);
            _nextPage.Click += async (sender, args) =>
            {
                if (_hasMore && _startIndex <= int.MaxValue - 2 * _count)
                    await LoadPageAsync(_startIndex + _count, _count);
            };
            _goToPage.Click += async (sender, args) =>
            {
                int start;
                if (TryResolvePageStartForSmokeTest((int)_pageNumber.Value, _count, out start))
                    await LoadPageAsync(start, _count);
            };
            _retryPage.Click += async (sender, args) => await LoadPageAsync(_startIndex, _count);
            _pageSize.SelectedIndexChanged += async (sender, args) =>
            {
                if (_loaded && !_loading && _pageSize.SelectedItem is int)
                    await LoadPageAsync(0, (int)_pageSize.SelectedItem);
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 20),
                ColumnCount = 1, RowCount = 4, BackColor = FacmDesignSystem.Canvas
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
            heading.Controls.Add(_playerLabel, 0, 0);
            heading.Controls.Add(_replayButton, 1, 0);
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(_statusLabel, 0, 1);
            layout.Controls.Add(_matches, 0, 2);
            layout.Controls.Add(footer, 0, 3);
            Controls.Add(layout);

            UpdatePageButtons();
            Shown += async (sender, args) => { FitMatchColumns(); await LoadPageAsync(0, _count); };
            FormClosing += (sender, args) => _lifetime.Cancel();
            Disposed += (sender, args) => _lifetime.Dispose();
        }

        private Button PageButton(string key, int width)
        {
            var button = new Button
            {
                Text = _ui.Get(key), Width = width, Height = 27,
                FlatStyle = FlatStyle.Flat, BackColor = FacmDesignSystem.SurfaceRaised,
                ForeColor = FacmDesignSystem.Text
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        internal static int[] ResolveColumnWidthsForSmokeTest(int width)
        {
            var content = Math.Max(450, width);
            var values = new[] { 19, 24, 22, 21 };
            var result = new int[5];
            var used = 0;
            for (var index = 0; index < values.Length; index++)
            {
                result[index] = content * values[index] / 100;
                used += result[index];
            }
            result[4] = content - used;
            return result;
        }

        internal static bool TryResolvePageStartForSmokeTest(int page, int count, out int start)
        {
            start = 0;
            if (page < 1 || count < 1 || count > LeaguePlayerDataService.MaximumRemoteMatchPageCount ||
                page - 1 > (int.MaxValue - count) / count) return false;
            start = (page - 1) * count;
            return true;
        }

        private void FitMatchColumns()
        {
            if (_matches == null || _matches.Columns.Count != 5) return;
            var widths = ResolveColumnWidthsForSmokeTest(_matches.ClientSize.Width -
                SystemInformation.VerticalScrollBarWidth - 4);
            for (var index = 0; index < widths.Length; index++)
                _matches.Columns[index].Width = widths[index];
        }

        private void UpdatePageButtons()
        {
            if (IsDisposed) return;
            _pageSize.Enabled = _loaded && !_loading;
            _previousPage.Enabled = _loaded && !_loading && _startIndex > 0;
            _nextPage.Enabled = _loaded && !_loading && _hasMore &&
                _startIndex <= int.MaxValue - 2 * _count;
            _goToPage.Enabled = _loaded && !_loading;
            _pageNumber.Enabled = _loaded && !_loading;
            _retryPage.Enabled = !_loading;
            var number = _startIndex / _count + 1;
            _currentPage.Text = _ui.Get(UiTextKeys.LeaguePlayerRemotePageLabel)
                .Replace("{0}", number.ToString());
            if (number <= _pageNumber.Maximum) _pageNumber.Value = number;
            UpdateReplayButton();
        }

        private LeaguePlayerMatchSummary SelectedReplayMatch()
        {
            return _matches.SelectedItems.Count == 1
                ? _matches.SelectedItems[0].Tag as LeaguePlayerMatchSummary : null;
        }

        private bool SessionMatches(LeagueClientSession expected)
        {
            return LeagueEncounterReplayService.SessionMatches(expected,
                _currentSession == null ? null : _currentSession());
        }

        private void UpdateReplayButton()
        {
            if (_replayButton == null || IsDisposed) return;
            var match = SelectedReplayMatch();
            _replayButton.Enabled = _loaded && !_loading && !_replayRunning && _replay != null &&
                LeagueEncounterReplayService.CanOfferHistoryReplay(_pageSession,
                    _currentSession == null ? null : _currentSession(), _expected.PuuId, match);
        }

        private async Task ReplaySelectedAsync()
        {
            var match = SelectedReplayMatch();
            var expectedSession = _pageSession;
            if (match == null || !_loaded || _loading || _replayRunning || _replay == null ||
                !SessionMatches(expectedSession)) return;
            _replayRunning = true;
            UpdateReplayButton();
            _statusLabel.Text = LeagueEfficiencyText.Get(_ui,
                LeagueEfficiencyUiTextKeys.DynamicEncounterReplayChecking);
            try
            {
                var result = await _replay(expectedSession, _expected.PuuId, _puuid, match, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested ||
                    !ReferenceEquals(match, SelectedReplayMatch())) return;
                _statusLabel.Text = LeagueEncounterMatchScoreboardDialog.ReplayStatusText(result,
                    key => LeagueEfficiencyText.Get(_ui, key));
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    _statusLabel.Text = LeagueEfficiencyText.Get(_ui,
                        LeagueEfficiencyUiTextKeys.DynamicEncounterReplayUnavailable);
            }
            finally
            {
                _replayRunning = false;
                if (!IsDisposed) UpdateReplayButton();
            }
        }

        private async Task LoadPageAsync(int startIndex, int count)
        {
            if (_loading || IsDisposed || _lifetime.IsCancellationRequested) return;
            var expectedSession = _currentSession == null ? null : _currentSession();
            _pageSession = null;
            _loading = true;
            _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteLoading);
            UpdatePageButtons();
            try
            {
                var result = await _service.LoadRemotePlayerHistoryAsync(_expected, _puuid,
                    _lifetime.Token, startIndex, count);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (result.Status != LeagueRiotPlayerSearchStatus.Found)
                {
                    if (result.Status == LeagueRiotPlayerSearchStatus.AccountChanged)
                    {
                        _loaded = false;
                        _hasMore = false;
                        _playerLabel.Text = string.Empty;
                        _matches.Items.Clear();
                    }
                    _statusLabel.Text = _ui.Get(result.Status == LeagueRiotPlayerSearchStatus.AccountChanged
                        ? UiTextKeys.LeaguePlayerRemoteAccountChanged
                        : result.Status == LeagueRiotPlayerSearchStatus.NotFound
                            ? UiTextKeys.LeaguePlayerRemoteHistoryNotFound : UiTextKeys.LeaguePlayerRemoteHistoryUnavailable);
                    return;
                }
                var profile = result.Profile;
                if (profile != null)
                {
                    _playerLabel.Text = string.IsNullOrWhiteSpace(profile.AccountName) ? _fallbackName : profile.AccountName;
                    if (profile.SummonerLevel > 0)
                        _playerLabel.Text += "  ·  " + _ui.Get(UiTextKeys.LeagueDashboardLevel) + " " + profile.SummonerLevel;
                }

                var page = result.Page;
                _matches.BeginUpdate();
                try
                {
                    _matches.Items.Clear();
                    foreach (var match in page.Matches)
                    {
                        if (match == null) continue;
                        var when = match.GameCreationLocal == DateTime.MinValue
                            ? "—" : match.GameCreationLocal.ToString("MM-dd HH:mm");
                        var champion = match.ChampionId > 0
                            ? (string.IsNullOrWhiteSpace(match.ChampionName) ? "#" + match.ChampionId : match.ChampionName) : "—";
                        var kda = match.ParticipantResolved && match.StatisticsAvailable
                            ? match.Kills + "/" + match.Deaths + "/" + match.Assists : "—";
                        var outcome = match.ParticipantResolved && match.StatisticsAvailable
                            ? _ui.Get(match.Win ? UiTextKeys.LeaguePlayerWin : UiTextKeys.LeaguePlayerLoss) : "—";
                        var row = new ListViewItem(when);
                        row.SubItems.Add(string.IsNullOrWhiteSpace(match.GameMode) ? "—" : match.GameMode);
                        row.SubItems.Add(champion);
                        row.SubItems.Add(kda);
                        row.SubItems.Add(outcome);
                        row.Tag = match;
                        _matches.Items.Add(row);
                    }
                }
                finally { _matches.EndUpdate(); }
                _startIndex = startIndex;
                _count = count;
                _hasMore = page.HasMore;
                _loaded = true;
                _pageSession = SessionMatches(expectedSession) ? expectedSession : null;
                _statusLabel.Text = _matches.Items.Count == 0
                    ? _ui.Get(UiTextKeys.LeaguePlayerRemoteNoMatches)
                    : _ui.Get(UiTextKeys.LeaguePlayerRemoteRecentCount)
                        .Replace("{0}", _matches.Items.Count.ToString());
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!IsDisposed) _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteHistoryUnavailable);
            }
            finally
            {
                if (!IsDisposed)
                {
                    // Revert a page-size draft if its request did not succeed.
                    if (!Equals(_pageSize.SelectedItem, _count)) _pageSize.SelectedItem = _count;
                }
                _loading = false;
                if (!IsDisposed) UpdatePageButtons();
            }
        }
    }
}
