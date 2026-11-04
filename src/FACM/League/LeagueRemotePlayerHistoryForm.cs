using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueRemotePlayerHistoryForm : Form
    {
        private sealed class ParticipantOption
        {
            internal string Puuid;
            internal string Name;
            internal string DisplayName;
            public override string ToString() { return Name; }
        }

        private sealed class ChampionOption
        {
            internal int Id;
            internal string Name;
            public override string ToString() { return Name; }
        }

        private sealed class QueueTagOption
        {
            internal string Tag;
            internal string Label;
            public override string ToString() { return Label; }
        }
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
        private readonly ComboBox _timeFilter;
        private readonly ComboBox _modeFilter;
        private readonly Button _participantsFilterButton;
        private readonly Button _positionsFilterButton;
        private string[] _matchPositions = new string[0];
        private int[] _matchChampionIds = new int[0];
        private string[] _matchPuuids = new string[0];
        private Dictionary<string, string> _selectedSummonerNames =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private LeagueClientSession _nameCacheSession;
        private LeaguePlayerHistorySource _nameCacheSource;
        private string _nameCacheServer;
        private readonly Button _advancedFilterButton;
        private readonly Button _collectButton;
        private readonly Button _stopCollectButton;
        private readonly ComboBox _pageSize;
        private readonly ComboBox _historySource;
        private readonly ComboBox _historyRegion;
        private readonly ComboBox _queueTag;
        private Dictionary<int, string> _queueNames = new Dictionary<int, string>();
        private int? _currentLobbyQueueId;
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
        private int _cardChampionId;
        private string _cardPosition;
        private string _cardMode;
        private readonly bool _autoStartCardCollection;

        internal static bool ShouldAutoStartCardCollection(bool requested,
            int championId, string position, string mode)
        {
            return requested && (championId > 0 || !string.IsNullOrEmpty(position) ||
                !string.IsNullOrEmpty(mode));
        }

        internal LeagueRemotePlayerHistoryForm(LeaguePlayerDataService service, UiTextCatalog ui,
            LeaguePlayerProfile current, string targetPuuid, string targetName,
            Func<LeagueClientSession> currentSession = null,
            Func<LeagueClientSession, string, string, LeaguePlayerMatchSummary,
                CancellationToken, Task<LeagueEncounterReplayResult>> replay = null,
            Func<LeagueClientSession, string, string, LeaguePlayerMatchSummary,
                CancellationToken, Task<LeagueEncounterMatchDetailResult>> loadDetail = null,
            Func<string, CancellationToken, Task<Bitmap>> loadAsset = null,
            int cardChampionId = 0, string cardPosition = null, string cardMode = "",
            bool autoStartCardCollection = false)
        {
            if (cardChampionId < 0 || cardChampionId > 9999 ||
                !string.IsNullOrEmpty(cardPosition) &&
                !new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" }
                    .Contains(cardPosition, StringComparer.Ordinal) ||
                (cardMode != "" && cardMode != "NORMAL" && cardMode != "CHERRY"))
                throw new ArgumentException("Invalid player-card collection filter.");
            _cardChampionId = cardChampionId;
            _cardPosition = cardPosition ?? "";
            _cardMode = cardMode ?? "";
            _autoStartCardCollection = ShouldAutoStartCardCollection(
                autoStartCardCollection, _cardChampionId, _cardPosition, _cardMode);
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
            _queueTag = new ComboBox { Width = 126, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = _ui.Get(UiTextKeys.LeaguePlayerHistoryQueueTag) };
            RebuildQueueTagOptions(null);
            sourceRow.Controls.Add(new Label { Text = _ui.Get(UiTextKeys.LeaguePlayerHistoryQueueTag),
                Width = 40, Height = 27, ForeColor = FacmDesignSystem.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft });
            sourceRow.Controls.Add(_queueTag);
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
                if (_updatingSource || _loading || !Visible) return;
                if (_historySource.SelectedIndex == (int)LeaguePlayerHistorySource.Lcu &&
                    SelectedQueueTag() != null)
                {
                    _updatingSource = true;
                    _queueTag.SelectedIndex = 0;
                    _updatingSource = false;
                }
                await LoadPageAsync(0, _count);
            };
            _queueTag.SelectedIndexChanged += async (sender, args) =>
            {
                if (_updatingSource || _loading || !Visible) return;
                if (SelectedQueueTag() != null &&
                    _historySource.SelectedIndex == (int)LeaguePlayerHistorySource.Lcu)
                {
                    _updatingSource = true;
                    _historySource.SelectedIndex = (int)LeaguePlayerHistorySource.Sgp;
                    _updatingSource = false;
                }
                await LoadPageAsync(0, _count);
            };
            _historyRegion.SelectedIndexChanged += async (sender, args) =>
            {
                if (_updatingSource) return;
                _updatingSource = true;
                try
                {
                    var selectedTag = SelectedQueueTag();
                    if (_historyRegion.SelectedIndex > 0)
                        _historySource.SelectedIndex = (int)LeaguePlayerHistorySource.Sgp;
                    RebuildQueueTagOptions(selectedTag);
                }
                finally { _updatingSource = false; }
                if (!_loading && Visible) await LoadPageAsync(0, _count);
            };

            var filterRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
                BackColor = FacmDesignSystem.Canvas, Padding = Padding.Empty
            };
            var filterActionsRow = new FlowLayoutPanel
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
            _timeFilter = new ComboBox
            {
                Width = 112, Height = 27, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = _ui.Get(UiTextKeys.LeaguePlayerFilterTimeRange)
            };
            foreach (var key in new[]
            {
                UiTextKeys.LeaguePlayerFilterAll, UiTextKeys.LeaguePlayerTimeLast3Hours,
                UiTextKeys.LeaguePlayerTimeLast12Hours, UiTextKeys.LeaguePlayerTimeLast24Hours,
                UiTextKeys.LeaguePlayerTimeLast3Days, UiTextKeys.LeaguePlayerTimeLast7Days,
                UiTextKeys.LeaguePlayerTimeLast30Days
            }) _timeFilter.Items.Add(_ui.Get(key));
            _timeFilter.SelectedIndex = 0;
            _timeFilter.SelectedIndexChanged += (sender, args) =>
            {
                RenderRows();
                UpdatePageButtons();
            };
            _modeFilter = new ComboBox {
                Width = 116, Height = 27, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = _ui.Get(UiTextKeys.LeaguePlayerMode)
            };
            _modeFilter.Items.Add(_ui.Get(UiTextKeys.LeaguePlayerFilterAll));
            _modeFilter.Items.Add(LeagueEfficiencyText.Get(_ui,
                LeagueEfficiencyUiTextKeys.ProfileModeNormal));
            _modeFilter.Items.Add(LeagueEfficiencyText.Get(_ui,
                LeagueEfficiencyUiTextKeys.ProfileModeCherry));
            _modeFilter.SelectedIndex = _cardMode == "NORMAL" ? 1 : _cardMode == "CHERRY" ? 2 : 0;
            _modeFilter.SelectedIndexChanged += (sender, args) =>
            {
                _cardMode = _modeFilter.SelectedIndex == 1 ? "NORMAL" :
                    _modeFilter.SelectedIndex == 2 ? "CHERRY" : "";
                RenderRows();
                UpdatePageButtons();
            };
            _positionsFilterButton = PageButton(UiTextKeys.LeaguePlayerAnalysisPositions, 114);
            _positionsFilterButton.Click += (sender, args) => EditSgpPositionFilters();
            _participantsFilterButton = PageButton(UiTextKeys.LeaguePlayerParticipantsFilter, 120);
            _participantsFilterButton.Click += (sender, args) => EditParticipantFilters();
            _advancedFilterButton = PageButton(UiTextKeys.LeaguePlayerAdvancedFilter, 154);
            _advancedFilterButton.Click += (sender, args) => EditAdvancedFilter();
            var cardFilter = new Button {
                Width = 118, Height = 27,
                Text = (_cardChampionId > 0 ? "#" + _cardChampionId : "") +
                    (_cardPosition.Length > 0 ? " " + _cardPosition : "") + " ×",
                Visible = _cardChampionId > 0 || _cardPosition.Length > 0,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text
            };
            cardFilter.Click += (sender, args) => {
                _cardChampionId = 0;
                _cardPosition = "";
                cardFilter.Visible = false;
                RenderRows();
                UpdatePageButtons();
            };
            filterActionsRow.Controls.Add(cardFilter);
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
            filterRow.Controls.Add(_timeFilter);
            filterRow.Controls.Add(_modeFilter);
            filterRow.Controls.Add(_positionsFilterButton);
            filterRow.Controls.Add(_participantsFilterButton);
            filterRow.Controls.Add(_advancedFilterButton);
            var analysisButton = PageButton(UiTextKeys.LeaguePlayerAnalysisAction, 90);
            analysisButton.Click += async (sender, args) =>
            {
                if (!_loaded || _loading || _collectionCancellation != null ||
                    !SessionMatches(_pageSession)) return;
                await LeaguePlayerHistoryAnalysis.ShowAsync(this, _ui, CurrentFilteredRows(), _service,
                    _expected.PuuId, _puuid, _pageSession, _lifetime.Token);
            };
            filterActionsRow.Controls.Add(analysisButton);
            filterActionsRow.Controls.Add(_collectButton);
            filterActionsRow.Controls.Add(_stopCollectButton);

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
                ColumnCount = 1, RowCount = 7, BackColor = FacmDesignSystem.Canvas
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
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
            layout.Controls.Add(filterActionsRow, 0, 4);
            layout.Controls.Add(_matches, 0, 5);
            layout.Controls.Add(footer, 0, 6);
            Controls.Add(layout);

            UpdatePageButtons();
            Shown += async (sender, args) =>
            {
                FitMatchColumns();
                await RefreshRegionCatalogAsync();
                await LoadPageAsync(0, _count);
                if (_autoStartCardCollection && !IsDisposed && _loaded &&
                    !_lifetime.IsCancellationRequested && SessionMatches(_pageSession))
                    await StartCollectionAsync();
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
            try
            {
                var names = _service.LoadQueueNamesAsync(expected, _lifetime.Token);
                var lobby = _service.LoadCurrentLobbyQueueIdAsync(
                    _expected.PuuId, expected, _lifetime.Token);
                await Task.WhenAll(LeagueSgpServerCatalog.TryRefreshAsync(_lifetime.Token),
                    LeagueSgpSupportedQueues.TryRefreshAsync(_lifetime.Token), names, lobby);
                if (IsDisposed || _lifetime.IsCancellationRequested || !SessionMatches(expected)) return;
                _queueNames = await names;
                _currentLobbyQueueId = await lobby;
            }
            catch (OperationCanceledException) { return; }
            if (IsDisposed || _lifetime.IsCancellationRequested || !SessionMatches(expected)) return;
            var selected = SelectedServerId();
            var selectedTag = SelectedQueueTag();
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
                RebuildQueueTagOptions(selectedTag);
            }
            finally
            {
                _historyRegion.EndUpdate();
                _updatingSource = false;
            }
        }

        private void RebuildQueueTagOptions(string selectedTag)
        {
            _queueTag.BeginUpdate();
            try
            {
                _queueTag.Items.Clear();
                _queueTag.Items.Add(new QueueTagOption { Tag = null,
                    Label = _ui.Get(UiTextKeys.LeaguePlayerFilterAll) });
                _queueTag.Items.Add(new QueueTagOption { Tag = "ranked",
                    Label = _ui.Get(UiTextKeys.LeaguePlayerHistoryQueueRanked) });
                _queueTag.Items.Add(new QueueTagOption { Tag = "normal",
                    Label = _ui.Get(UiTextKeys.LeaguePlayerHistoryQueueNormal) });
                var index = 0;
                var supported = LeagueSgpSupportedQueues.Snapshot();
                if (_historyRegion.SelectedIndex == 0 && _currentLobbyQueueId.HasValue &&
                    _queueNames.ContainsKey(_currentLobbyQueueId.Value))
                {
                    var id = _currentLobbyQueueId.Value;
                    string name;
                    var label = _queueNames.TryGetValue(id, out name) ? name :
                        _ui.Get(UiTextKeys.LeaguePlayerHistoryQueueNumber)
                            .Replace("{0}", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var tag = "q_" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var item = _queueTag.Items.Add(new QueueTagOption { Tag = tag,
                        Label = _ui.Get(UiTextKeys.LeaguePlayerHistoryCurrentLobbyQueue)
                            .Replace("{0}", label) });
                    if (tag == selectedTag) index = item;
                }
                foreach (var id in supported)
                {
                    if (_historyRegion.SelectedIndex == 0 && id == _currentLobbyQueueId) continue;
                    var tag = "q_" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string name;
                    var item = _queueTag.Items.Add(new QueueTagOption { Tag = tag,
                        Label = _queueNames.TryGetValue(id, out name) ? name :
                            _ui.Get(UiTextKeys.LeaguePlayerHistoryQueueNumber)
                                .Replace("{0}", id.ToString(System.Globalization.CultureInfo.InvariantCulture)) });
                    if (tag == selectedTag) index = item;
                }
                if (selectedTag == "ranked") index = 1;
                if (selectedTag == "normal") index = 2;
                _queueTag.SelectedIndex = index;
            }
            finally { _queueTag.EndUpdate(); }
        }

        private string SelectedQueueTag()
        {
            return (_queueTag.SelectedItem as QueueTagOption)?.Tag;
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
            _queueTag.Enabled = !_loading && !_replayRunning && _collectionCancellation == null;
            _retryPage.Text = _ui.Get(regularPage ? UiTextKeys.LeaguePlayerRemoteRetry :
                UiTextKeys.LeaguePlayerCollectBackToPage);
            _retryPage.Width = regularPage ? 52 : 80;
            var number = _startIndex / _count + 1;
            _currentPage.Text = regularPage
                ? _ui.Get(UiTextKeys.LeaguePlayerRemotePageLabel).Replace("{0}", number.ToString())
                : _ui.Get(UiTextKeys.LeaguePlayerCollectTitle);
            if (number <= _pageNumber.Maximum) _pageNumber.Value = number;
            _winFilter.Enabled = _loaded && !_loading && !_replayRunning;
            _timeFilter.Enabled = _loaded && !_loading && !_replayRunning;
            _modeFilter.Enabled = _loaded && !_loading && !_replayRunning &&
                _collectionCancellation == null;
            _positionsFilterButton.Enabled = _loaded && !_loading && !_replayRunning &&
                _collectionCancellation == null &&
                _historySource.SelectedIndex == (int)LeaguePlayerHistorySource.Sgp;
            _participantsFilterButton.Enabled = _loaded && !_loading && !_replayRunning &&
                _collectionCancellation == null;
            _advancedFilterButton.Enabled = _loaded && !_loading && !_replayRunning;
            _collectButton.Enabled = _loaded && !_loading && !_replayRunning &&
                _targetProfile != null && HasCollectionFilter() && SessionMatches(_pageSession);
            _stopCollectButton.Enabled = _collectionCancellation != null &&
                !_collectionCancellation.IsCancellationRequested;
            UpdateReplayButton();
        }

        internal static bool SameHistorySelection(LeaguePlayerHistorySource expectedSource,
            string expectedServer, string expectedQueueTag, LeaguePlayerHistorySource currentSource,
            string currentServer, string currentQueueTag)
        {
            return expectedSource == currentSource &&
                string.Equals(expectedServer, currentServer, StringComparison.Ordinal) &&
                string.Equals(expectedQueueTag, currentQueueTag, StringComparison.Ordinal);
        }

        internal static LeagueMatchTimeRangeFilter TimeFilterFromSelection(int index)
        {
            return index >= 0 && index <= (int)LeagueMatchTimeRangeFilter.Last30Days
                ? (LeagueMatchTimeRangeFilter)index : LeagueMatchTimeRangeFilter.All;
        }

        private bool HasCollectionFilter()
        {
            return _winFilter.SelectedIndex > 0 || _timeFilter.SelectedIndex > 0 ||
                _matchChampionIds.Length > 0 || _matchPuuids.Length > 0 || SelectedQueueTag() != null ||
                LeagueSimpleMatchHistoryFilter.ShouldApplySgpPositions(
                    (LeaguePlayerHistorySource)_historySource.SelectedIndex, _matchPositions) ||
                _cardChampionId > 0 || _cardPosition.Length > 0 || _cardMode.Length > 0 ||
                (_advancedFilter != null && _advancedFilter.Root.Children.Count > 0);
        }

        private void EditSgpPositionFilters()
        {
            if (_loading || !_loaded || _collectionCancellation != null ||
                _historySource.SelectedIndex != (int)LeaguePlayerHistorySource.Sgp) return;
            var expectedSession = _pageSession;
            var roles = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
            using (var dialog = new Form
            {
                Text = _ui.Get(UiTextKeys.LeaguePlayerAnalysisPositions),
                StartPosition = FormStartPosition.CenterParent,
                AutoScaleMode = AutoScaleMode.Font,
                ClientSize = new Size(320, 278), MinimumSize = new Size(320, 278),
                BackColor = FacmDesignSystem.Canvas, ForeColor = FacmDesignSystem.Text,
                Font = Font
            })
            {
                var choices = new CheckedListBox
                {
                    Bounds = new Rectangle(16, 16, 288, 204),
                    CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle,
                    BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text
                };
                foreach (var role in roles)
                    choices.Items.Add(LeaguePlayerHistoryAnalysis.PositionName(_ui, role),
                        _matchPositions.Contains(role, StringComparer.Ordinal));
                var cancel = new Button
                {
                    Text = _ui.Get(UiTextKeys.LeaguePlayerAdvancedCancel),
                    Bounds = new Rectangle(112, 230, 92, 30), DialogResult = DialogResult.Cancel
                };
                var apply = new Button
                {
                    Text = _ui.Get(UiTextKeys.LeaguePlayerAdvancedApply),
                    Bounds = new Rectangle(212, 230, 92, 30)
                };
                string[] selected = null;
                apply.Click += (sender, args) =>
                {
                    if (!LeagueSimpleMatchHistoryFilter.TryNormalizePositions(
                        choices.CheckedIndices.Cast<int>().Select(index => roles[index]), out selected))
                        return;
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                };
                dialog.Controls.AddRange(new Control[] { choices, cancel, apply });
                dialog.AcceptButton = apply;
                dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) != DialogResult.OK ||
                    !SessionMatches(expectedSession) ||
                    _historySource.SelectedIndex != (int)LeaguePlayerHistorySource.Sgp) return;
                _matchPositions = selected;
            }
            _positionsFilterButton.Text = _ui.Get(UiTextKeys.LeaguePlayerAnalysisPositions) +
                (_matchPositions.Length > 0 ? " (" + _matchPositions.Length + ")" : "");
            RenderRows();
            UpdatePageButtons();
        }

        private void EditParticipantFilters()
        {
            if (_loading || !_loaded || _collectionCancellation != null) return;
            var expectedSession = _pageSession;
            using (var dialog = new Form
            {
                Text = _ui.Get(UiTextKeys.LeaguePlayerParticipantsFilter),
                StartPosition = FormStartPosition.CenterParent,
                AutoScaleMode = AutoScaleMode.Font,
                ClientSize = new Size(480, 430),
                MinimumSize = new Size(480, 430),
                BackColor = FacmDesignSystem.Canvas,
                ForeColor = FacmDesignSystem.Text,
                Font = Font
            })
            {
                var hint = new Label
                {
                    Text = _ui.Get(UiTextKeys.LeaguePlayerParticipantsHint),
                    Bounds = new Rectangle(18, 12, 444, 48),
                    ForeColor = FacmDesignSystem.TextMuted
                };
                var championLabel = new Label
                {
                    Text = _ui.Get(UiTextKeys.LeaguePlayerParticipantsChampions),
                    Bounds = new Rectangle(18, 68, 444, 20)
                };
                var championInput = new TextBox
                {
                    Bounds = new Rectangle(18, 90, 444, 26),
                    Text = string.Join(",", _matchChampionIds.Select(id => id.ToString()))
                };
                var playerLabel = new Label
                {
                    Text = _ui.Get(UiTextKeys.LeaguePlayerParticipantsPlayers),
                    Bounds = new Rectangle(18, 124, 444, 20)
                };
                var playerInput = new TextBox
                {
                    Bounds = new Rectangle(18, 146, 444, 26),
                    Text = string.Join(",", _matchPuuids)
                };
                var page = _activePage;
                var selectedNames = LeagueSimpleMatchHistoryFilter.RememberSelectedParticipantNames(
                    page?.Matches, _matchPuuids, _selectedSummonerNames);
                var championPicker = new ComboBox
                {
                    Bounds = new Rectangle(18, 188, 338, 27),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                    AutoCompleteSource = AutoCompleteSource.ListItems,
                    BackColor = FacmDesignSystem.SurfaceRaised,
                    ForeColor = FacmDesignSystem.Text
                };
                var championNames = _service.TryGetCachedChampionNames() ??
                    new Dictionary<int, string>();
                if (page != null)
                    foreach (var match in page.Matches.Where(m => m != null &&
                        m.ChampionId > 0 && m.ChampionId <= 10000 &&
                        LeagueSimpleMatchHistoryFilter.SafePlayerLabel(m.ChampionName)))
                        if (!championNames.ContainsKey(match.ChampionId))
                            championNames[match.ChampionId] = match.ChampionName;
                foreach (var option in championNames.Where(pair => pair.Key > 0 &&
                    pair.Key <= 10000 && LeagueSimpleMatchHistoryFilter.SafePlayerLabel(pair.Value))
                    .OrderBy(pair => pair.Value, StringComparer.CurrentCulture))
                    championPicker.Items.Add(new ChampionOption
                    {
                        Id = option.Key,
                        Name = option.Value + " · #" + option.Key
                    });
                if (championPicker.Items.Count > 0) championPicker.SelectedIndex = 0;
                var addChampion = new Button
                {
                    Text = _ui.Get(UiTextKeys.LeaguePlayerAdvancedAddCondition),
                    Bounds = new Rectangle(364, 188, 98, 27),
                    Enabled = championPicker.Items.Count > 0
                };
                addChampion.Click += (sender, args) =>
                {
                    var selected = championPicker.SelectedItem as ChampionOption;
                    string updated;
                    if (selected == null || !SessionMatches(expectedSession) ||
                        !ReferenceEquals(_activePage, page) ||
                        !LeagueSimpleMatchHistoryFilter.TryAppendChampionId(
                            championInput.Text, selected.Id, out updated))
                    {
                        MessageBox.Show(dialog, _ui.Get(UiTextKeys.LeaguePlayerParticipantsInvalid),
                            dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    championInput.Text = updated;
                };
                var picker = new ComboBox
                {
                    Bounds = new Rectangle(18, 232, 260, 27),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = FacmDesignSystem.SurfaceRaised,
                    ForeColor = FacmDesignSystem.Text
                };
                if (page != null)
                {
                    foreach (var candidate in page.Matches
                        .Where(match => match?.MatchPlayers != null)
                        .SelectMany(match => match.MatchPlayers)
                        .Where(player => player != null &&
                            LeagueSgpMatchHistoryApiClient.SafePuuid(player.PuuId))
                        .GroupBy(player => player.PuuId, StringComparer.Ordinal)
                        .Select(group => group.First())
                        .OrderBy(player => player.PlayerName ?? player.PuuId, StringComparer.Ordinal)
                        .Take(40))
                    {
                        picker.Items.Add(new ParticipantOption
                        {
                            Puuid = candidate.PuuId,
                            DisplayName = candidate.PlayerName,
                            Name = string.IsNullOrWhiteSpace(candidate.PlayerName)
                                ? candidate.PuuId : candidate.PlayerName + " · " + candidate.PuuId
                        });
                    }
                }
                foreach (var entry in selectedNames)
                    if (!picker.Items.Cast<ParticipantOption>().Any(option =>
                        string.Equals(option.Puuid, entry.Key, StringComparison.Ordinal)))
                        picker.Items.Add(new ParticipantOption
                        {
                            Puuid = entry.Key,
                            DisplayName = entry.Value,
                            Name = entry.Value + " · " + entry.Key
                        });
                if (picker.Items.Count > 0) picker.SelectedIndex = 0;
                var addPlayer = new Button
                {
                    Text = _ui.Get(UiTextKeys.LeaguePlayerAdvancedAddCondition),
                    Bounds = new Rectangle(286, 232, 82, 27),
                    Enabled = picker.Items.Count > 0
                };
                addPlayer.Click += (sender, args) =>
                {
                    var selected = picker.SelectedItem as ParticipantOption;
                    string updated;
                    if (selected == null || !SessionMatches(expectedSession) ||
                        !ReferenceEquals(_activePage, page) ||
                        !LeagueSimpleMatchHistoryFilter.TryAppendParticipantPuuid(
                            playerInput.Text, selected.Puuid, out updated))
                    {
                        MessageBox.Show(dialog, _ui.Get(UiTextKeys.LeaguePlayerParticipantsInvalid),
                            dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    playerInput.Text = updated;
                    if (LeagueSimpleMatchHistoryFilter.SafePlayerLabel(selected.DisplayName))
                        selectedNames[selected.Puuid] = selected.DisplayName.Trim();
                };
                var removePlayer = new Button
                {
                    Text = _ui.Get(UiTextKeys.LeaguePlayerAdvancedRemove),
                    Bounds = new Rectangle(376, 232, 86, 27),
                    Enabled = picker.Items.Count > 0
                };
                removePlayer.Click += (sender, args) =>
                {
                    var selected = picker.SelectedItem as ParticipantOption;
                    string updated;
                    if (selected == null || !SessionMatches(expectedSession) ||
                        !ReferenceEquals(_activePage, page) ||
                        !LeagueSimpleMatchHistoryFilter.TryRemoveParticipantPuuid(
                            playerInput.Text, selected.Puuid, out updated)) return;
                    playerInput.Text = updated;
                    selectedNames.Remove(selected.Puuid);
                };
                var riotId = new TextBox
                {
                    Bounds = new Rectangle(18, 275, 338, 26), MaxLength = 50,
                    AccessibleName = _ui.Get(UiTextKeys.LeaguePlayerFilterSearchPlayer)
                };
                var lookup = new Button
                {
                    Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteLookup),
                    Bounds = new Rectangle(364, 275, 98, 27),
                    Enabled = true
                };
                var lookupStatus = new Label
                {
                    Bounds = new Rectangle(18, 312, 444, 34), AutoEllipsis = true,
                    ForeColor = FacmDesignSystem.TextMuted
                };
                using (var lookupLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                {
                    dialog.FormClosed += (sender, args) => lookupLifetime.Cancel();
                    dialog.Shown += async (sender, args) =>
                    {
                        if (championPicker.Items.Count >= 100) return;
                        try
                        {
                            var allChampions = await _service.LoadChampionOptionsAsync(
                                lookupLifetime.Token);
                            if (dialog.IsDisposed || lookupLifetime.IsCancellationRequested ||
                                !SessionMatches(expectedSession) ||
                                !ReferenceEquals(_activePage, page) || allChampions == null) return;
                            var previous = (championPicker.SelectedItem as ChampionOption)?.Id ?? 0;
                            var known = new HashSet<int>(championPicker.Items
                                .Cast<ChampionOption>().Select(option => option.Id));
                            var options = championPicker.Items.Cast<ChampionOption>().ToList();
                            foreach (var entry in allChampions)
                                if (entry.Key > 0 && entry.Key <= 10000 &&
                                    LeagueSimpleMatchHistoryFilter.SafePlayerLabel(entry.Value) &&
                                    known.Add(entry.Key))
                                    options.Add(new ChampionOption
                                    {
                                        Id = entry.Key,
                                        Name = entry.Value + " · #" + entry.Key
                                    });
                            championPicker.BeginUpdate();
                            try
                            {
                                championPicker.Items.Clear();
                                foreach (var option in options.OrderBy(item => item.Name,
                                    StringComparer.CurrentCulture))
                                    championPicker.Items.Add(option);
                                var selectedIndex = options.FindIndex(item => item.Id == previous);
                                championPicker.SelectedIndex = selectedIndex >= 0
                                    ? championPicker.Items.IndexOf(options[selectedIndex])
                                    : championPicker.Items.Count > 0 ? 0 : -1;
                                addChampion.Enabled = championPicker.Items.Count > 0;
                            }
                            finally { championPicker.EndUpdate(); }
                        }
                        catch (OperationCanceledException) { }
                    };
                    lookup.Click += async (sender, args) =>
                    {
                        string name, tag;
                        if (!SessionMatches(expectedSession) ||
                            !ReferenceEquals(_activePage, page) ||
                            !LeaguePlayerDataService.TryParseRiotId(riotId.Text, out name, out tag))
                        {
                            lookupStatus.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteInvalid);
                            return;
                        }
                        var source = (LeaguePlayerHistorySource)_historySource.SelectedIndex;
                        var selectedServer = SelectedServerId();
                        var sgpServer = source == LeaguePlayerHistorySource.Sgp
                            ? selectedServer ?? LeagueSgpMatchHistoryApiClient.CurrentServerId(expectedSession)
                            : null;
                        if (source == LeaguePlayerHistorySource.Sgp &&
                            LeagueSgpMatchHistoryApiClient.ResolveServer(expectedSession, sgpServer) == null)
                        {
                            lookupStatus.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteUnavailable);
                            return;
                        }
                        lookup.Enabled = false;
                        lookupStatus.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteLoading);
                        try
                        {
                            var result = await _service.SearchRiotIdAsync(_expected,
                                riotId.Text.Trim(), lookupLifetime.Token, false, sgpServer);
                            if (dialog.IsDisposed || lookupLifetime.IsCancellationRequested ||
                                !SessionMatches(expectedSession) ||
                                !ReferenceEquals(_activePage, page) ||
                                source != (LeaguePlayerHistorySource)_historySource.SelectedIndex ||
                                !string.Equals(selectedServer, SelectedServerId(), StringComparison.Ordinal)) return;
                            string updated;
                            if (result.Status == LeagueRiotPlayerSearchStatus.Found &&
                                result.Player != null &&
                                LeagueSimpleMatchHistoryFilter.TryAppendParticipantPuuid(
                                    playerInput.Text, result.Player.PuuId, out updated))
                            {
                                playerInput.Text = updated;
                                if (LeagueSimpleMatchHistoryFilter.SafePlayerLabel(
                                    result.Player.PlayerName))
                                    selectedNames[result.Player.PuuId] = result.Player.PlayerName.Trim();
                                lookupStatus.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteFound);
                            }
                            else lookupStatus.Text = _ui.Get(result.Status == LeagueRiotPlayerSearchStatus.NotFound
                                ? UiTextKeys.LeaguePlayerRemoteNotFound :
                                result.Status == LeagueRiotPlayerSearchStatus.AccountChanged
                                    ? UiTextKeys.LeaguePlayerRemoteAccountChanged :
                                    UiTextKeys.LeaguePlayerRemoteUnavailable);
                        }
                        catch (OperationCanceledException) { }
                        finally { if (!dialog.IsDisposed) lookup.Enabled = true; }
                    };
                    var cancel = new Button
                    {
                        Text = _ui.Get(UiTextKeys.LeaguePlayerAdvancedCancel),
                        Bounds = new Rectangle(264, 373, 94, 30),
                        DialogResult = DialogResult.Cancel
                    };
                    var apply = new Button
                    {
                        Text = _ui.Get(UiTextKeys.LeaguePlayerAdvancedApply),
                        Bounds = new Rectangle(368, 373, 94, 30)
                    };
                    int[] nextChampions = null;
                    string[] nextPlayers = null;
                    apply.Click += (sender, args) =>
                    {
                        if (!LeagueSimpleMatchHistoryFilter.TryParseParticipantFilters(
                            championInput.Text, playerInput.Text, out nextChampions, out nextPlayers))
                        {
                            MessageBox.Show(dialog, _ui.Get(UiTextKeys.LeaguePlayerParticipantsInvalid),
                                dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        dialog.DialogResult = DialogResult.OK;
                        dialog.Close();
                    };
                    dialog.Controls.AddRange(new Control[] {
                        hint, championLabel, championInput, playerLabel, playerInput,
                        championPicker, addChampion, picker, addPlayer, removePlayer,
                        riotId, lookup, lookupStatus, cancel, apply
                    });
                    dialog.AcceptButton = apply;
                    dialog.CancelButton = cancel;
                    if (dialog.ShowDialog(this) != DialogResult.OK || !SessionMatches(expectedSession))
                        return;
                    _matchChampionIds = nextChampions;
                    _matchPuuids = nextPlayers;
                    _selectedSummonerNames =
                        LeagueSimpleMatchHistoryFilter.RememberSelectedParticipantNames(
                            page?.Matches, _matchPuuids, selectedNames);
                }
            }
            _participantsFilterButton.Text = _ui.Get(_matchChampionIds.Length + _matchPuuids.Length > 0
                ? UiTextKeys.LeaguePlayerParticipantsActive : UiTextKeys.LeaguePlayerParticipantsFilter);
            RenderRows();
            UpdatePageButtons();
        }

        private void EditAdvancedFilter()
        {
            if (_loading || !_loaded) return;
            var expectedSession = _pageSession;
            using (var dialog = new LeagueAdvancedMatchHistoryForm(_advancedFilter, _puuid))
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
                    WinLoss = (LeagueMatchWinLossFilter)Math.Max(0, _winFilter.SelectedIndex),
                    TimeRange = TimeFilterFromSelection(_timeFilter.SelectedIndex),
                    ChampionIds = _matchChampionIds.ToArray(), PlayerPuuids = _matchPuuids.ToArray(),
                    SubjectPositions = LeagueSimpleMatchHistoryFilter.ShouldApplySgpPositions(
                        (LeaguePlayerHistorySource)_historySource.SelectedIndex, _matchPositions)
                        ? _matchPositions.ToArray() : null,
                    SubjectChampionId = _cardChampionId, SubjectPosition = _cardPosition,
                    SubjectMode = _cardMode
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
                WinLoss = (LeagueMatchWinLossFilter)Math.Max(0, _winFilter.SelectedIndex),
                    TimeRange = TimeFilterFromSelection(_timeFilter.SelectedIndex),
                ChampionIds = _matchChampionIds.ToArray(), PlayerPuuids = _matchPuuids.ToArray(),
                SubjectPositions = LeagueSimpleMatchHistoryFilter.ShouldApplySgpPositions(
                    (LeaguePlayerHistorySource)_historySource.SelectedIndex, _matchPositions)
                    ? _matchPositions.ToArray() : null,
                    SubjectChampionId = _cardChampionId, SubjectPosition = _cardPosition,
                SubjectMode = _cardMode
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
                    (LeaguePlayerHistorySource)_historySource.SelectedIndex, SelectedServerId(),
                    SelectedQueueTag());
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
                if (detail.EogOnly != null)
                {
                    using (var dialog = new LeagueSgpEogOnlyDialog(_ui, detail,
                        _replay != null && LeagueEncounterReplayService.CanOfferHistoryReplay(session,
                            _currentSession?.Invoke(), owner, detail.Match)
                            ? (Func<LeaguePlayerMatchSummary, CancellationToken, Task<LeagueEncounterReplayResult>>)
                                ((match, token) => _replay(session, owner, _puuid, match, token)) : null))
                        dialog.ShowDialog(this);
                }
                else
                {
                    using (var dialog = new LeagueEncounterMatchScoreboardDialog(_ui, detail, _loadAsset,
                        _replay != null && LeagueEncounterReplayService.CanOfferHistoryReplay(session,
                            _currentSession?.Invoke(), owner, detail.Match)
                            ? (Func<LeaguePlayerMatchSummary, CancellationToken, Task<LeagueEncounterReplayResult>>)
                                ((match, token) => _replay(session, owner, _puuid, match, token)) : null))
                        dialog.ShowDialog(this);
                }
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
            if (_currentSession != null && expectedSession == null) return;
            var requestedSource = (LeaguePlayerHistorySource)_historySource.SelectedIndex;
            var requestedServer = SelectedServerId();
            var requestedTag = SelectedQueueTag();
            if (!LeagueSgpMatchHistoryApiClient.SameOwner(_nameCacheSession, expectedSession) ||
                !SameHistorySelection(_nameCacheSource, _nameCacheServer, null,
                    requestedSource, requestedServer, null))
            {
                _selectedSummonerNames.Clear();
                _nameCacheSession = expectedSession;
                _nameCacheSource = requestedSource;
                _nameCacheServer = requestedServer;
            }
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
                    requestedSource, requestedServer, requestedTag);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (!SessionMatches(expectedSession) ||
                    !SameHistorySelection(requestedSource, requestedServer, requestedTag,
                        (LeaguePlayerHistorySource)_historySource.SelectedIndex,
                        SelectedServerId(), SelectedQueueTag()))
                {
                    _selectedSummonerNames.Clear();
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
                _selectedSummonerNames =
                    LeagueSimpleMatchHistoryFilter.RememberSelectedParticipantNames(
                        page.Matches, _matchPuuids, _selectedSummonerNames);
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
