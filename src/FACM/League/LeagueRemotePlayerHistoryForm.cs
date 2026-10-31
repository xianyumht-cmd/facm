using System;
using System.Drawing;
using System.Collections.Generic;
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
        private readonly Func<LeagueClientSession, string, string, LeaguePlayerMatchSummary,
            CancellationToken, Task<LeagueEncounterMatchDetailResult>> _loadDetail;
        private readonly Func<string, CancellationToken, Task<Bitmap>> _loadAsset;
        private readonly Button _detailButton;
        private readonly string _puuid;
        private readonly string _fallbackName;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Label _playerLabel;
        private readonly Label _statusLabel;
        private readonly ListView _matches;
        private readonly Button _replayButton;
        private readonly ComboBox _winFilter;
        private readonly Button _advancedFilterButton;
        private readonly Button _collectButton;
        private readonly Button _stopCollectButton;
        private readonly ComboBox _pageSize;
        private readonly ComboBox _historySource;
        private readonly ComboBox _historyRegion;
        private bool _updatingSource;
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
        private LeaguePlayerProfile _targetProfile;
        private LeaguePlayerMatchPage _activePage;
        private LeagueAdvancedMatchHistoryFilter _advancedFilter;
        private LeagueMatchCollectionResult _collectionResult;
        private LeagueMatchCollectionSettings _collectionSettings = new LeagueMatchCollectionSettings();
        private CancellationTokenSource _collectionCancellation;

        internal LeagueRemotePlayerHistoryForm(LeaguePlayerDataService service, UiTextCatalog ui,
            LeaguePlayerProfile current, string targetPuuid, string targetName,
            Func<LeagueClientSession> currentSession = null,
            Func<LeagueClientSession, string, string, LeaguePlayerMatchSummary,
                CancellationToken, Task<LeagueEncounterReplayResult>> replay = null,
            Func<LeagueClientSession, string, string, LeaguePlayerMatchSummary,
                CancellationToken, Task<LeagueEncounterMatchDetailResult>> loadDetail = null,
            Func<string, CancellationToken, Task<Bitmap>> loadAsset = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _expected = new LeaguePlayerProfile { PuuId = current == null ? null : current.PuuId };
            _puuid = targetPuuid;
            _fallbackName = targetName ?? string.Empty;
            _currentSession = currentSession;
            _replay = replay;
            _loadDetail = loadDetail;
            _loadAsset = loadAsset;

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

            _detailButton = PageButton(UiTextKeys.LeaguePlayerMatchDetail, 100);
            _detailButton.Enabled = false;
            _detailButton.Click += async (sender, args) => await ShowSelectedDetailAsync();
            _matches.DoubleClick += async (sender, args) => await ShowSelectedDetailAsync();

            var sourceRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
                BackColor = FacmDesignSystem.Canvas, Padding = Padding.Empty
            };
            _historySource = new ComboBox { Width = 136, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = _ui.Get(UiTextKeys.LeaguePlayerHistorySource) };
            foreach (var key in new[] { UiTextKeys.LeaguePlayerHistorySourceAuto,
                UiTextKeys.LeaguePlayerHistorySourceLcu, UiTextKeys.LeaguePlayerHistorySourceSgp })
                _historySource.Items.Add(_ui.Get(key));
            _historySource.SelectedIndex = 0;
            _historyRegion = new ComboBox { Width = 174, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = _ui.Get(UiTextKeys.LeaguePlayerHistoryRegion) };
            _historyRegion.Items.Add(new LeagueSgpServer { Name = _ui.Get(UiTextKeys.LeaguePlayerHistoryCurrentRegion) });
            var initialSession = _currentSession?.Invoke();
            foreach (var server in LeagueSgpMatchHistoryApiClient.AvailableServers(initialSession))
                if (server.Id != LeagueSgpMatchHistoryApiClient.CurrentServerId(initialSession)) _historyRegion.Items.Add(server);
            _historyRegion.SelectedIndex = 0;
            sourceRow.Controls.Add(new Label { Text = _ui.Get(UiTextKeys.LeaguePlayerHistoryRegion), Width = 40,
                Height = 27, ForeColor = FacmDesignSystem.TextMuted, TextAlign = ContentAlignment.MiddleLeft });
            sourceRow.Controls.Add(_historyRegion);
            sourceRow.Controls.Add(new Label { Text = _ui.Get(UiTextKeys.LeaguePlayerHistorySource), Width = 40,
                Height = 27, ForeColor = FacmDesignSystem.TextMuted, TextAlign = ContentAlignment.MiddleLeft });
            sourceRow.Controls.Add(_historySource);
            var profileDetails = PageButton(UiTextKeys.LeagueProfileDetailsTitle, 110);
            profileDetails.Click += (sender, args) =>
            {
                if (_loading || _replayRunning || _collectionCancellation != null) return;
                using (var dialog = new LeaguePlayerProfileDialog(_service, _ui, _expected.PuuId,
                    _puuid, _fallbackName, _currentSession?.Invoke(), _currentSession,
                    SelectedServerId(), (LeaguePlayerHistorySource)_historySource.SelectedIndex)) dialog.ShowDialog(this);
            };
            sourceRow.Controls.Add(profileDetails);
            _historySource.SelectedIndexChanged += async (sender, args) =>
            {
                if (!_updatingSource && !_loading && Visible) await LoadPageAsync(0, _count);
            };
            _historyRegion.SelectedIndexChanged += async (sender, args) =>
            {
                _updatingSource = true;
                if (_historyRegion.SelectedIndex > 0) _historySource.SelectedIndex = (int)LeaguePlayerHistorySource.Sgp;
                _updatingSource = false;
                if (!_updatingSource && !_loading && Visible) await LoadPageAsync(0, _count);
            };

            var filterRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
                BackColor = FacmDesignSystem.Canvas, Padding = Padding.Empty
            };
            _winFilter = new ComboBox
            {
                Width = 100, Height = 27, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = _ui.Get(UiTextKeys.LeaguePlayerFilterWinLoss)
            };
            _winFilter.Items.Add(_ui.Get(UiTextKeys.LeaguePlayerFilterAll));
            _winFilter.Items.Add(_ui.Get(UiTextKeys.LeaguePlayerWin));
            _winFilter.Items.Add(_ui.Get(UiTextKeys.LeaguePlayerLoss));
            _winFilter.SelectedIndex = 0;
            _winFilter.SelectedIndexChanged += (sender, args) =>
            {
                RenderRows();
                UpdatePageButtons();
            };
            _advancedFilterButton = PageButton(UiTextKeys.LeaguePlayerAdvancedFilter, 154);
            _advancedFilterButton.Click += (sender, args) => EditAdvancedFilter();
            _collectButton = PageButton(UiTextKeys.LeaguePlayerCollectAction, 122);
            _collectButton.Click += async (sender, args) => await StartCollectionAsync();
            _stopCollectButton = PageButton(UiTextKeys.LeaguePlayerCollectStop, 64);
            _stopCollectButton.Enabled = false;
            _stopCollectButton.Click += (sender, args) =>
            {
                if (_collectionCancellation == null) return;
                _collectionCancellation.Cancel();
                _stopCollectButton.Enabled = false;
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerCollectStopping);
            };
            filterRow.Controls.Add(new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePlayerFilterWinLoss), Width = 40, Height = 27,
                ForeColor = FacmDesignSystem.TextMuted, TextAlign = ContentAlignment.MiddleLeft
            });
            filterRow.Controls.Add(_winFilter);
            filterRow.Controls.Add(_advancedFilterButton);
            var analysisButton = PageButton(UiTextKeys.LeaguePlayerAnalysisAction, 90);
            analysisButton.Click += async (sender, args) =>
            {
                if (!_loaded || _loading || _collectionCancellation != null ||
                    !SessionMatches(_pageSession)) return;
                await LeaguePlayerHistoryAnalysis.ShowAsync(this, _ui, CurrentFilteredRows(), _service,
                    _expected.PuuId, _puuid, _pageSession, _lifetime.Token);
            };
            filterRow.Controls.Add(analysisButton);
            filterRow.Controls.Add(_collectButton);
            filterRow.Controls.Add(_stopCollectButton);

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
                ColumnCount = 1, RowCount = 6, BackColor = FacmDesignSystem.Canvas
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 254));
            heading.Controls.Add(_playerLabel, 0, 0);
            var matchActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            matchActions.Controls.Add(_detailButton);
            matchActions.Controls.Add(_replayButton);
            heading.Controls.Add(matchActions, 1, 0);
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(_statusLabel, 0, 1);
            layout.Controls.Add(sourceRow, 0, 2);
            layout.Controls.Add(filterRow, 0, 3);
            layout.Controls.Add(_matches, 0, 4);
            layout.Controls.Add(footer, 0, 5);
            Controls.Add(layout);

            UpdatePageButtons();
            Shown += async (sender, args) =>
            {
                FitMatchColumns();
                await RefreshRegionCatalogAsync();
                await LoadPageAsync(0, _count);
            };
            FormClosing += (sender, args) =>
            {
                if (_collectionCancellation != null) _collectionCancellation.Cancel();
                _lifetime.Cancel();
            };
            Disposed += (sender, args) => _lifetime.Dispose();
        }

        private async Task RefreshRegionCatalogAsync()
        {
            var expected = _currentSession?.Invoke();
            if (expected == null) return;
            try { await LeagueSgpServerCatalog.TryRefreshAsync(_lifetime.Token); }
            catch (OperationCanceledException) { return; }
            if (IsDisposed || _lifetime.IsCancellationRequested || !SessionMatches(expected)) return;
            var selected = SelectedServerId();
            var current = LeagueSgpMatchHistoryApiClient.CurrentServerId(expected);
            _updatingSource = true;
            _historyRegion.BeginUpdate();
            try
            {
                _historyRegion.Items.Clear();
                _historyRegion.Items.Add(new LeagueSgpServer
                    { Name = _ui.Get(UiTextKeys.LeaguePlayerHistoryCurrentRegion) });
                var index = 0;
                foreach (var server in LeagueSgpMatchHistoryApiClient.AvailableServers(expected))
                {
                    if (server.Id == current) continue;
                    var candidate = _historyRegion.Items.Add(server);
                    if (server.Id == selected) index = candidate;
                }
                _historyRegion.SelectedIndex = index;
            }
            finally
            {
                _historyRegion.EndUpdate();
                _updatingSource = false;
            }
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
            var regularPage = _collectionResult == null;
            _pageSize.Enabled = _loaded && !_loading && !_replayRunning && regularPage;
            _previousPage.Enabled = _loaded && !_loading && !_replayRunning && regularPage && _startIndex > 0;
            _nextPage.Enabled = _loaded && !_loading && !_replayRunning && regularPage && _hasMore &&
                _startIndex <= int.MaxValue - 2 * _count;
            _goToPage.Enabled = _loaded && !_loading && !_replayRunning && regularPage;
            _pageNumber.Enabled = _loaded && !_loading && !_replayRunning && regularPage;
            _retryPage.Enabled = !_loading && !_replayRunning;
            _historySource.Enabled = !_loading && !_replayRunning && _historyRegion.SelectedIndex == 0;
            _historyRegion.Enabled = !_loading && !_replayRunning && _historyRegion.Items.Count > 1;
            _retryPage.Text = _ui.Get(regularPage ? UiTextKeys.LeaguePlayerRemoteRetry :
                UiTextKeys.LeaguePlayerCollectBackToPage);
            _retryPage.Width = regularPage ? 52 : 80;
            var number = _startIndex / _count + 1;
            _currentPage.Text = regularPage
                ? _ui.Get(UiTextKeys.LeaguePlayerRemotePageLabel).Replace("{0}", number.ToString())
                : _ui.Get(UiTextKeys.LeaguePlayerCollectTitle);
            if (number <= _pageNumber.Maximum) _pageNumber.Value = number;
            _winFilter.Enabled = _loaded && !_loading && !_replayRunning;
            _advancedFilterButton.Enabled = _loaded && !_loading && !_replayRunning;
            _collectButton.Enabled = _loaded && !_loading && !_replayRunning &&
                _targetProfile != null && HasCollectionFilter() && SessionMatches(_pageSession);
            _stopCollectButton.Enabled = _collectionCancellation != null &&
                !_collectionCancellation.IsCancellationRequested;
            UpdateReplayButton();
        }

        private bool HasCollectionFilter()
        {
            return _winFilter.SelectedIndex > 0 ||
                (_advancedFilter != null && _advancedFilter.Root.Children.Count > 0);
        }

        private void EditAdvancedFilter()
        {
            if (_loading || !_loaded) return;
            var expectedSession = _pageSession;
            using (var dialog = new LeagueAdvancedMatchHistoryForm(_advancedFilter))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || !SessionMatches(expectedSession)) return;
                _advancedFilter = dialog.Result;
            }
            _advancedFilterButton.Text = _ui.Get(_advancedFilter == null
                ? UiTextKeys.LeaguePlayerAdvancedFilter : UiTextKeys.LeaguePlayerAdvancedActive);
            RenderRows();
            UpdatePageButtons();
        }

        private List<LeaguePlayerMatchSummary> CurrentFilteredRows()
        {
            if (_activePage == null) return new List<LeaguePlayerMatchSummary>();
            var rows = LeagueSimpleMatchHistoryFilter.Apply(_activePage.Matches,
                new LeagueSimpleMatchHistoryFilter {
                    WinLoss = (LeagueMatchWinLossFilter)Math.Max(0, _winFilter.SelectedIndex)
                }, DateTime.UtcNow);
            return LeagueAdvancedMatchHistoryFilter.Apply(rows, _advancedFilter);
        }

        private void RenderRows()
        {
            if (_matches == null || IsDisposed || _activePage == null) return;
            var filtered = CurrentFilteredRows();
            _matches.BeginUpdate();
            try
            {
                _matches.Items.Clear();
                foreach (var match in filtered)
                {
                    if (match == null) continue;
                    var when = match.GameCreationLocal == DateTime.MinValue
                        ? "—" : match.GameCreationLocal.ToString("MM-dd HH:mm");
                    var champion = match.ChampionId > 0
                        ? (string.IsNullOrWhiteSpace(match.ChampionName) ? "#" + match.ChampionId : match.ChampionName) : "—";
                    var kda = match.ParticipantResolved && match.StatisticsAvailable
                        ? match.Kills + "/" + match.Deaths + "/" + match.Assists : "—";
                    var outcome = LeaguePlayerHistoryAnalysis.OutcomeText(_ui, match);
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
            if (_collectionResult != null)
            {
                var note = _collectionResult.Status == LeagueMatchCollectionStatus.Stopped
                    ? _ui.Get(UiTextKeys.LeaguePlayerCollectStopped)
                    : _collectionResult.Status == LeagueMatchCollectionStatus.Unavailable
                        ? _ui.Get(UiTextKeys.LeaguePlayerCollectUnavailable) : string.Empty;
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerCollectSummary)
                    .Replace("{0}", _matches.Items.Count.ToString())
                    .Replace("{1}", _collectionResult.ScannedCount.ToString())
                    .Replace("{2}", _collectionResult.Iterations.ToString()) + note;
            }
            else if (HasCollectionFilter())
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerFilteredCount)
                    .Replace("{0}", _matches.Items.Count.ToString())
                    .Replace("{1}", _activePage.Matches.Count.ToString());
            else
                _statusLabel.Text = _matches.Items.Count == 0
                    ? _ui.Get(UiTextKeys.LeaguePlayerRemoteNoMatches)
                    : _ui.Get(UiTextKeys.LeaguePlayerRemoteRecentCount)
                        .Replace("{0}", _matches.Items.Count.ToString());
            UpdateReplayButton();
        }

        private async Task StartCollectionAsync()
        {
            if (_loading || _replayRunning || !_loaded || _collectionCancellation != null ||
                _targetProfile == null || !HasCollectionFilter() || !SessionMatches(_pageSession) ||
                _lifetime.IsCancellationRequested) return;
            var settings = LeaguePlayerForm.ShowCollectionSettings(this, _ui, Font, _collectionSettings);
            if (settings == null || !settings.IsValid || IsDisposed) return;
            _collectionSettings = settings;
            var session = _pageSession;
            var target = _targetProfile;
            var simple = new LeagueSimpleMatchHistoryFilter
            {
                WinLoss = (LeagueMatchWinLossFilter)Math.Max(0, _winFilter.SelectedIndex)
            };
            var advanced = _advancedFilter;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _collectionCancellation = cancellation;
            _loading = true;
            UpdatePageButtons();
            _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerCollectProgress)
                .Replace("{0}", "0").Replace("{1}", "0").Replace("{2}", "0");
            var progress = new Progress<LeagueMatchCollectionProgress>(current =>
            {
                if (IsDisposed || _collectionCancellation != cancellation || cancellation.IsCancellationRequested) return;
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerCollectProgress)
                    .Replace("{0}", current.CollectedCount.ToString())
                    .Replace("{1}", current.ScannedCount.ToString())
                    .Replace("{2}", current.Iterations.ToString());
            });
            try
            {
                var result = await _service.CollectMatchesAsync(_expected, target, settings,
                    simple, advanced, progress, cancellation.Token,
                    (LeaguePlayerHistorySource)_historySource.SelectedIndex, SelectedServerId());
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (!SessionMatches(session) || result.Status == LeagueMatchCollectionStatus.AccountChanged ||
                    !string.Equals(target.PuuId, _targetProfile == null ? null : _targetProfile.PuuId,
                        StringComparison.Ordinal))
                {
                    _loaded = false;
                    _targetProfile = null;
                    _activePage = null;
                    _collectionResult = null;
                    _pageSession = null;
                    _matches.Items.Clear();
                    _playerLabel.Text = string.Empty;
                    _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteAccountChanged);
                    return;
                }
                _activePage = result.Page;
                _collectionResult = result;
                RenderRows();
            }
            catch (OperationCanceledException) { }
            finally
            {
                _collectionCancellation = null;
                cancellation.Dispose();
                _loading = false;
                if (!IsDisposed) UpdatePageButtons();
            }
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
            _detailButton.Enabled = !_loading && !_replayRunning && _loadDetail != null &&
                match != null && match.GameId > 0 && SessionMatches(_pageSession);
            _replayButton.Enabled = _loaded && !_loading && !_replayRunning && _replay != null &&
                LeagueEncounterReplayService.CanOfferHistoryReplay(_pageSession,
                    _currentSession == null ? null : _currentSession(), _expected.PuuId, match);
        }

        private async Task ShowSelectedDetailAsync()
        {
            var selected = SelectedReplayMatch();
            var session = _pageSession;
            var owner = _expected.PuuId;
            if (selected == null || _loading || _replayRunning || _loadDetail == null ||
                string.IsNullOrEmpty(owner) || !SessionMatches(session)) return;
            _replayRunning = true;
            UpdatePageButtons();
            _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerMatchDetailLoading);
            try
            {
                var detail = await _loadDetail(session, owner, _puuid, selected, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested || !SessionMatches(session) ||
                    !ReferenceEquals(selected, SelectedReplayMatch())) return;
                if (detail == null || detail.Status != LeagueEncounterMatchDetailStatus.Ready)
                {
                    _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerMatchDetailUnavailable);
                    return;
                }
                using (var dialog = new LeagueEncounterMatchScoreboardDialog(_ui, detail, _loadAsset,
                    _replay != null && LeagueEncounterReplayService.CanOfferHistoryReplay(session,
                        _currentSession?.Invoke(), owner, detail.Match)
                        ? (Func<LeaguePlayerMatchSummary, CancellationToken, Task<LeagueEncounterReplayResult>>)
                            ((match, token) => _replay(session, owner, _puuid, match, token)) : null))
                    dialog.ShowDialog(this);
                if (!IsDisposed) _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerMatchDetailReady);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!IsDisposed && !_lifetime.IsCancellationRequested)
                    _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerMatchDetailUnavailable);
            }
            finally
            {
                _replayRunning = false;
                if (!IsDisposed) UpdatePageButtons();
            }
        }

        private async Task ReplaySelectedAsync()
        {
            var match = SelectedReplayMatch();
            var expectedSession = _pageSession;
            if (match == null || !_loaded || _loading || _replayRunning || _replay == null ||
                !SessionMatches(expectedSession)) return;
            _replayRunning = true;
            UpdatePageButtons();
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
                if (!IsDisposed) UpdatePageButtons();
            }
        }

        private string SelectedServerId()
        {
            return (_historyRegion.SelectedItem as LeagueSgpServer)?.Id;
        }

        private async Task LoadPageAsync(int startIndex, int count)
        {
            if (_loading || _replayRunning || IsDisposed || _lifetime.IsCancellationRequested) return;
            var expectedSession = _currentSession == null ? null : _currentSession();
            _pageSession = null;
            _activePage = null;
            _collectionResult = null;
            _targetProfile = null;
            _matches.Items.Clear();
            _loaded = false;
            _loading = true;
            _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteLoading);
            UpdatePageButtons();
            try
            {
                var result = await _service.LoadRemotePlayerHistoryAsync(_expected, _puuid,
                    _lifetime.Token, startIndex, count,
                    (LeaguePlayerHistorySource)_historySource.SelectedIndex, SelectedServerId());
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (!SessionMatches(expectedSession))
                {
                    _playerLabel.Text = string.Empty;
                    _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteAccountChanged);
                    return;
                }
                if (result.Status != LeagueRiotPlayerSearchStatus.Found)
                {
                    if (result.Status == LeagueRiotPlayerSearchStatus.AccountChanged)
                    {
                        _loaded = false;
                        _hasMore = false;
                        _targetProfile = null;
                        _activePage = null;
                        _collectionResult = null;
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
                _targetProfile = profile;
                _activePage = page;
                _collectionResult = null;
                _startIndex = startIndex;
                _count = count;
                _hasMore = page.HasMore;
                _loaded = true;
                _pageSession = SessionMatches(expectedSession) ? expectedSession : null;
                RenderRows();
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
