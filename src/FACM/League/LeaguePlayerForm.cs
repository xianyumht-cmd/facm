using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeaguePlayerForm : Form
    {
        private readonly LeaguePlayerDataService _service;
        private readonly UiTextCatalog _ui;
        private readonly Func<LeagueClientSession> _currentSession;
        private readonly Func<LeagueClientSession, string, string, LeaguePlayerMatchSummary,
            CancellationToken, Task<LeagueEncounterReplayResult>> _replay;
        private readonly Label _accountLabel;
        private readonly Panel _accountSurface;
        private readonly Panel _accountAccent;
        private readonly Label _statusLabel;
        private readonly Label _statsSection;
        private readonly Label _recentSection;
        private readonly Label _emptyStateLabel;
        private readonly Label _titleLabel;
        private readonly Label _hintLabel;
        private readonly FacmActionButton _closeButton;
        private readonly ListView _championStatsList;
        private readonly ListView _matchesList;
        private readonly FlowLayoutPanel _filterPanel;
        private readonly ComboBox _winFilter;
        private readonly ComboBox _timeFilter;
        private readonly Button _championFilter;
        private readonly Button _playerFilter;
        private readonly Button _advancedFilterButton;
        private readonly CheckedListBox _championChecks;
        private readonly CheckedListBox _playerChecks;
        private readonly ToolStripTextBox _championSearch;
        private readonly ToolStripTextBox _playerSearch;
        private readonly Button _playerLookupButton;
        private readonly Button _playerHistoryButton;
        private readonly Button _browseHistoryButton;
        private readonly ToolStripLabel _playerLookupStatus;
        private readonly ToolStripDropDown _championMenu;
        private readonly ToolStripDropDown _playerMenu;
        private readonly HashSet<int> _selectedChampions = new HashSet<int>();
        private readonly HashSet<string> _selectedPlayers = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _selectedPlayerNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _remotePlayers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly FacmActionButton _refreshButton;
        private readonly FacmActionButton _loadMoreButton;
        private readonly FacmActionButton _replayButton;
        private readonly FacmActionButton _collectButton;
        private readonly FacmActionButton _stopCollectButton;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<LeaguePlayerMatchSummary> _rows = new List<LeaguePlayerMatchSummary>();
        private LeaguePlayerMatchPage _loadedPage;
        private LeagueAdvancedMatchHistoryFilter _advancedFilter;
        private bool _updatingFilterChoices;
        private LeaguePlayerProfile _profile;
        private LeagueClientSession _pageSession;
        private bool _replayRunning;
        private bool _loading;
        private bool _loadingChampionOptions;
        private bool _loadingPlayerLookup;
        private string _lastRemotePlayerPuuid;
        private string _lastRemotePlayerName;
        private bool _hasMore;
        private int _requestedCount = LeaguePlayerDataService.InitialMatchCount;
        private CancellationTokenSource _collectionCancellation;
        private LeagueMatchCollectionResult _collectionResult;
        private LeagueMatchCollectionSettings _collectionSettings = new LeagueMatchCollectionSettings();

        public LeaguePlayerForm(LeaguePlayerDataService service, UiTextCatalog ui,
            Func<LeagueClientSession> currentSession = null,
            Func<LeagueClientSession, string, string, LeaguePlayerMatchSummary,
                CancellationToken, Task<LeagueEncounterReplayResult>> replay = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _currentSession = currentSession;
            _replay = replay;

            Text = _ui.Get(UiTextKeys.LeaguePlayerWindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(860, 720);
            MinimumSize = new Size(760, 620);
            MaximizeBox = true;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            _titleLabel = new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePlayerTitle),
                Location = new Point(28, 20),
                Size = new Size(300, 34),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 15F, FontStyle.Bold)
            };
            _hintLabel = new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePlayerHint),
                Location = new Point(30, 58),
                Size = new Size(760, 24),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };
            _accountSurface = new Panel
            {
                BackColor = FacmDesignSystem.Surface,
                TabStop = false
            };
            _accountAccent = new Panel
            {
                BackColor = FacmDesignSystem.Accent,
                TabStop = false
            };
            _accountSurface.Controls.Add(_accountAccent);
            _accountLabel = new Label
            {
                Location = new Point(30, 94),
                Size = new Size(760, 32),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 13F, FontStyle.Bold),
                AutoEllipsis = true
            };
            _statusLabel = new Label
            {
                Location = new Point(30, 128),
                Size = new Size(760, 22),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };

            _statsSection = CreateSectionLabel(FormatChampionStatsTitle(0), new Point(30, 164), 500);
            _championStatsList = CreateListView(new Rectangle(28, 194, 804, 112), false);
            _championStatsList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _championStatsList.Columns.Add(ChampionHeaderText(), 300);
            _championStatsList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerResult), 160);
            _championStatsList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerKda), 250);

            _recentSection = CreateSectionLabel(_ui.Get(UiTextKeys.LeaguePlayerRecentMatches), new Point(30, 320), 300);

            _matchesList = CreateListView(new Rectangle(28, 352, 804, 302), true);
            _matchesList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerTime), 122);
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerMode), 140);
            _matchesList.Columns.Add(ChampionHeaderText(), 122);
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerKda), 106);
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerCs), 66);
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerResult), 72);
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerDuration), 76);
            _matchesList.RetrieveVirtualItem += RetrieveVirtualItem;
            _matchesList.SelectedIndexChanged += (sender, e) => UpdateReplayButton();

            _filterPanel = new FlowLayoutPanel { Dock = DockStyle.None, WrapContents = false,
                AutoScroll = true, BackColor = FacmDesignSystem.Canvas };
            _filterPanel.Controls.Add(FilterLabel(UiTextKeys.LeaguePlayerFilterWinLoss));
            _winFilter = FilterCombo(90);
            _winFilter.AccessibleName = _ui.Get(UiTextKeys.LeaguePlayerFilterWinLoss);
            _winFilter.Items.Add(_ui.Get(UiTextKeys.LeaguePlayerFilterAll));
            _winFilter.Items.Add(_ui.Get(UiTextKeys.LeaguePlayerWin));
            _winFilter.Items.Add(_ui.Get(UiTextKeys.LeaguePlayerLoss));
            _winFilter.SelectedIndex = 0;
            _filterPanel.Controls.Add(_winFilter);

            _filterPanel.Controls.Add(FilterLabel(UiTextKeys.LeaguePlayerFilterTimeRange));
            _timeFilter = FilterCombo(114);
            _timeFilter.AccessibleName = _ui.Get(UiTextKeys.LeaguePlayerFilterTimeRange);
            foreach (var key in new[] { UiTextKeys.LeaguePlayerFilterAll, UiTextKeys.LeaguePlayerFilter3Hours,
                UiTextKeys.LeaguePlayerFilter12Hours, UiTextKeys.LeaguePlayerFilter24Hours,
                UiTextKeys.LeaguePlayerFilter3Days, UiTextKeys.LeaguePlayerFilter7Days,
                UiTextKeys.LeaguePlayerFilter30Days })
                _timeFilter.Items.Add(_ui.Get(key));
            _timeFilter.SelectedIndex = 0;
            _filterPanel.Controls.Add(_timeFilter);

            _filterPanel.Controls.Add(FilterLabel(UiTextKeys.LeaguePlayerFilterChampion));
            _championFilter = FilterMultiButton(116, UiTextKeys.LeaguePlayerFilterChampion);
            _championChecks = FilterCheckList();
            _championSearch = FilterSearchBox(UiTextKeys.LeaguePlayerFilterSearchChampion);
            _championMenu = FilterCheckMenu(_championChecks, _championSearch, () =>
            {
                _selectedChampions.Clear();
                UpdateFilterChoices(_loadedPage);
                ApplySimpleFilter();
            });
            _filterPanel.Controls.Add(_championFilter);
            _championFilter.Click += async (sender, e) =>
            {
                _championMenu.Show(_championFilter, 0, _championFilter.Height);
                if (_loadingChampionOptions || _lifetime.IsCancellationRequested) return;
                _loadingChampionOptions = true;
                try
                {
                    await _service.LoadChampionOptionsAsync(_lifetime.Token);
                    if (!IsDisposed && !_lifetime.IsCancellationRequested) UpdateFilterChoices(_loadedPage);
                }
                catch (OperationCanceledException) { }
                catch (Exception exception) { AppLog.Info("League Player hero options unavailable: " + exception.Message); }
                finally { _loadingChampionOptions = false; }
            };

            _filterPanel.Controls.Add(FilterLabel(UiTextKeys.LeaguePlayerFilterPlayer));
            _playerFilter = FilterMultiButton(132, UiTextKeys.LeaguePlayerFilterPlayer);
            _playerChecks = FilterCheckList();
            _playerSearch = FilterSearchBox(UiTextKeys.LeaguePlayerFilterSearchPlayer);
            _playerMenu = FilterCheckMenu(_playerChecks, _playerSearch, () =>
            {
                _selectedPlayers.Clear();
                _selectedPlayerNames.Clear();
                UpdateFilterChoices(_loadedPage);
                ApplySimpleFilter();
            });
            _playerLookupButton = new Button { Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteLookup),
                Size = new Size(236, 26), FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text };
            _playerLookupButton.FlatAppearance.BorderSize = 0;
            _playerHistoryButton = new Button { Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteHistoryAction),
                Size = new Size(236, 26), FlatStyle = FlatStyle.Flat, Enabled = false,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text };
            _playerHistoryButton.FlatAppearance.BorderSize = 0;
            _playerLookupStatus = new ToolStripLabel { AutoSize = false, Size = new Size(236, 24),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = FacmDesignSystem.TextMuted };
            _playerMenu.Items.Insert(2, new ToolStripControlHost(_playerLookupButton)
            {
                AutoSize = false, Size = _playerLookupButton.Size, Padding = Padding.Empty, Margin = Padding.Empty
            });
            _playerMenu.Items.Insert(3, new ToolStripControlHost(_playerHistoryButton)
            {
                AutoSize = false, Size = _playerHistoryButton.Size, Padding = Padding.Empty, Margin = Padding.Empty
            });
            _playerMenu.Items.Insert(4, _playerLookupStatus);
            _playerLookupButton.Click += async (sender, e) => await SearchRemotePlayerAsync();
            _playerHistoryButton.Click += (sender, e) => OpenRemotePlayerHistory();
            _filterPanel.Controls.Add(_playerFilter);
            _playerFilter.Click += (sender, e) => _playerMenu.Show(_playerFilter, 0, _playerFilter.Height);
            _browseHistoryButton = new Button { Text = _ui.Get(UiTextKeys.LeaguePlayerHistoryBrowse),
                Size = new Size(126, 26), FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text };
            _browseHistoryButton.FlatAppearance.BorderSize = 0;
            _browseHistoryButton.Click += (sender, e) =>
            {
                if (_profile == null || _loading) return;
                using (var dialog = new LeagueRemotePlayerHistoryForm(_service, _ui, _profile,
                    _profile.PuuId, _profile.AccountName, _currentSession, _replay)) dialog.ShowDialog(this);
            };
            _filterPanel.Controls.Add(_browseHistoryButton);
            _championSearch.TextChanged += (sender, e) => UpdateFilterChoices(_loadedPage);
            _playerSearch.TextChanged += (sender, e) =>
            {
                _playerLookupStatus.Text = string.Empty;
                _lastRemotePlayerPuuid = null;
                _lastRemotePlayerName = null;
                _playerHistoryButton.Enabled = false;
                UpdateFilterChoices(_loadedPage);
            };
            _championChecks.ItemCheck += (sender, e) =>
            {
                if (_updatingFilterChoices) return;
                var choice = _championChecks.Items[e.Index] as ChampionChoice;
                if (choice == null) return;
                if (e.NewValue == CheckState.Checked) _selectedChampions.Add(choice.Id);
                else _selectedChampions.Remove(choice.Id);
                UpdateFilterButtonLabels();
                ApplySimpleFilter();
            };
            _playerChecks.ItemCheck += (sender, e) =>
            {
                if (_updatingFilterChoices) return;
                var choice = _playerChecks.Items[e.Index] as PlayerChoice;
                if (choice == null) return;
                if (e.NewValue == CheckState.Checked)
                {
                    _selectedPlayers.Add(choice.PuuId);
                    _selectedPlayerNames[choice.PuuId] = choice.Name;
                }
                else
                {
                    _selectedPlayers.Remove(choice.PuuId);
                    _selectedPlayerNames.Remove(choice.PuuId);
                }
                UpdateFilterButtonLabels();
                ApplySimpleFilter();
            };
            _winFilter.SelectedIndexChanged += (sender, e) => ApplySimpleFilter();
            _timeFilter.SelectedIndexChanged += (sender, e) => ApplySimpleFilter();
            _advancedFilterButton = FilterMultiButton(152, UiTextKeys.LeaguePlayerAdvancedFilter);
            _advancedFilterButton.Click += (sender, e) =>
            {
                var accountAtOpen = _profile == null ? null : _profile.PuuId;
                using (var dialog = new LeagueAdvancedMatchHistoryForm(_advancedFilter))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    if (!string.Equals(accountAtOpen, _profile == null ? null : _profile.PuuId,
                        StringComparison.Ordinal)) return;
                    _advancedFilter = dialog.Result;
                }
                UpdateAdvancedFilterLabel();
                ApplySimpleFilter();
            };
            _filterPanel.Controls.Add(_advancedFilterButton);
            UpdateAdvancedFilterLabel();
            UpdateFilterButtonLabels();

            _refreshButton = CreateButton(UiTextKeys.LeaguePlayerRefresh, FacmButtonTone.Primary);
            _refreshButton.Location = new Point(542, 672);
            _refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _refreshButton.Click += async delegate { await RefreshAllAsync(true); };

            _loadMoreButton = CreateButton(UiTextKeys.LeaguePlayerLoadMore, FacmButtonTone.Secondary);
            _loadMoreButton.Location = new Point(640, 672);
            _loadMoreButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _loadMoreButton.Click += async delegate { await LoadMoreAsync(); };

            _replayButton = CreateButton(UiTextKeys.LeaguePlayerReplayAction, FacmButtonTone.Secondary);
            _replayButton.Size = new Size(146, 30);
            _replayButton.Enabled = false;
            _replayButton.Click += async delegate { await ReplaySelectedAsync(); };

            _collectButton = CreateButton(UiTextKeys.LeaguePlayerCollectAction, FacmButtonTone.Secondary);
            _collectButton.Size = new Size(108, 30);
            _collectButton.Click += async delegate { await StartCollectionAsync(); };
            _stopCollectButton = CreateButton(UiTextKeys.LeaguePlayerCollectStop, FacmButtonTone.Secondary);
            _stopCollectButton.Size = new Size(82, 30);
            _stopCollectButton.Enabled = false;
            _stopCollectButton.Click += delegate
            {
                if (_collectionCancellation == null) return;
                _collectionCancellation.Cancel();
                _stopCollectButton.Enabled = false;
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerCollectStopping);
            };

            _closeButton = CreateButton(UiTextKeys.Close, FacmButtonTone.Secondary);
            _closeButton.Location = new Point(738, 672);
            _closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _closeButton.Click += delegate { Close(); };

            _emptyStateLabel = new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePlayerLoadingProfile),
                Size = new Size(320, 52),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = FacmDesignSystem.CanvasRaised,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                Visible = false
            };

            Controls.Add(_accountSurface);
            Controls.Add(_titleLabel);
            Controls.Add(_hintLabel);
            Controls.Add(_accountLabel);
            Controls.Add(_statusLabel);
            Controls.Add(_statsSection);
            Controls.Add(_championStatsList);
            Controls.Add(_recentSection);
            Controls.Add(_filterPanel);
            Controls.Add(_matchesList);
            Controls.Add(_refreshButton);
            Controls.Add(_loadMoreButton);
            Controls.Add(_replayButton);
            Controls.Add(_collectButton);
            Controls.Add(_stopCollectButton);
            Controls.Add(_closeButton);
            Controls.Add(_emptyStateLabel);

            _accountSurface.SendToBack();
            FacmThemeRuntime.ThemeChanged += RefreshPlayerPalette;
            Disposed += delegate { FacmThemeRuntime.ThemeChanged -= RefreshPlayerPalette; };
            ApplyCached();
            ResizePlayerPage();
            Resize += delegate { ResizePlayerPage(); };
            Shown += async delegate { await RefreshAllAsync(true); };
            FormClosed += delegate
            {
                _championMenu.Dispose();
                _playerMenu.Dispose();
                if (_collectionCancellation != null) _collectionCancellation.Cancel();
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _lifetime.Dispose();
            };
        }

        private void RefreshPlayerPalette(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                if (IsHandleCreated)
                    BeginInvoke(new Action(() => RefreshPlayerPalette(sender, e)));
                return;
            }
            BackColor = FacmDesignSystem.Canvas;
            _accountSurface.BackColor = FacmDesignSystem.Surface;
            _accountAccent.BackColor = FacmDesignSystem.Accent;
            _championStatsList.BackColor = FacmDesignSystem.Surface;
            _championStatsList.ForeColor = FacmDesignSystem.Text;
            _matchesList.BackColor = FacmDesignSystem.Surface;
            _matchesList.ForeColor = FacmDesignSystem.Text;
            _filterPanel.BackColor = FacmDesignSystem.Canvas;
            foreach (Control control in _filterPanel.Controls)
                if (control is Label) control.ForeColor = FacmDesignSystem.TextMuted;
            foreach (var combo in new[] { _winFilter, _timeFilter })
            {
                combo.BackColor = FacmDesignSystem.SurfaceRaised;
                combo.ForeColor = FacmDesignSystem.Text;
            }
            foreach (var button in new[] { _championFilter, _playerFilter, _advancedFilterButton })
            {
                button.BackColor = FacmDesignSystem.SurfaceRaised;
                button.ForeColor = FacmDesignSystem.Text;
            }
            foreach (var checks in new[] { _championChecks, _playerChecks })
            {
                checks.BackColor = FacmDesignSystem.Surface;
                checks.ForeColor = FacmDesignSystem.Text;
            }
            foreach (var search in new[] { _championSearch, _playerSearch })
            {
                search.BackColor = FacmDesignSystem.Surface;
                search.ForeColor = FacmDesignSystem.Text;
            }
            _playerLookupButton.BackColor = FacmDesignSystem.SurfaceRaised;
            _playerLookupButton.ForeColor = FacmDesignSystem.Text;
            _playerHistoryButton.BackColor = FacmDesignSystem.SurfaceRaised;
            _playerHistoryButton.ForeColor = FacmDesignSystem.Text;
            _playerLookupStatus.ForeColor = FacmDesignSystem.TextMuted;
            _hintLabel.ForeColor = FacmDesignSystem.TextMuted;
            _accountLabel.ForeColor = FacmDesignSystem.Text;
            _statusLabel.ForeColor = FacmDesignSystem.TextMuted;
            _emptyStateLabel.BackColor = FacmDesignSystem.SurfaceRaised;
            _emptyStateLabel.ForeColor = FacmDesignSystem.TextMuted;
        }

        private static Label CreateSectionLabel(string text, Point location, int width)
        {
            return new Label
            {
                Text = text,
                Location = location,
                Size = new Size(width, 25),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 11F, FontStyle.Bold)
            };
        }

        private Label FilterLabel(string key)
        {
            return new Label { Text = _ui.Get(key), AutoSize = true,
                Margin = new Padding(0, 7, 6, 0), ForeColor = FacmDesignSystem.TextMuted };
        }

        private static ComboBox FilterCombo(int width)
        {
            return new ComboBox { Width = width, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text,
                Margin = new Padding(0, 1, 12, 0) };
        }

        private Button FilterMultiButton(int width, string key)
        {
            var button = new Button { Width = width, Height = 26,
                FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text,
                Margin = new Padding(0, 1, 12, 0), AccessibleName = _ui.Get(key) };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static CheckedListBox FilterCheckList()
        {
            return new CheckedListBox { CheckOnClick = true, IntegralHeight = false,
                HorizontalScrollbar = true,
                BorderStyle = BorderStyle.None, Size = new Size(236, 200),
                BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text };
        }

        private ToolStripTextBox FilterSearchBox(string key)
        {
            return new ToolStripTextBox { AutoSize = false, Size = new Size(236, 25),
                MaxLength = 64, AccessibleName = _ui.Get(key), ToolTipText = _ui.Get(key),
                BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text };
        }

        private ToolStripDropDown FilterCheckMenu(CheckedListBox list, ToolStripTextBox search, Action clear)
        {
            var menu = new ToolStripDropDown { AutoClose = true, Padding = Padding.Empty };
            menu.Items.Add(new ToolStripLabel(search.AccessibleName));
            menu.Items.Add(search);
            menu.Items.Add(new ToolStripControlHost(list) { AutoSize = false, Size = list.Size,
                Margin = Padding.Empty, Padding = Padding.Empty });
            var clearItem = new ToolStripMenuItem(_ui.Get(UiTextKeys.LeaguePlayerFilterClear));
            clearItem.Click += (sender, e) => clear();
            menu.Items.Add(clearItem);
            return menu;
        }

        private sealed class ChampionChoice
        {
            internal readonly int Id;
            private readonly string _name;

            internal ChampionChoice(int id, string name) { Id = id; _name = name; }
            public override string ToString() { return _name; }
        }

        private sealed class PlayerChoice
        {
            internal readonly string PuuId;
            internal readonly string Name;

            internal PlayerChoice(string puuId, string name)
            {
                PuuId = puuId;
                Name = name;
            }
            public override string ToString()
            {
                return (string.IsNullOrWhiteSpace(Name) ? "" : Name + " · ") + AbbreviatePuuid(PuuId);
            }
        }

        private static ListView CreateListView(Rectangle bounds, bool virtualMode)
        {
            return new ListView
            {
                Bounds = bounds,
                BackColor = FacmDesignSystem.Surface,
                ForeColor = FacmDesignSystem.Text,
                BorderStyle = BorderStyle.None,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                View = View.Details,
                VirtualMode = virtualMode,
                VirtualListSize = 0
            };
        }

        private FacmActionButton CreateButton(string key, FacmButtonTone tone)
        {
            return new FacmActionButton
            {
                Text = _ui.Get(key),
                Size = new Size(92, 30),
                Tone = tone,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F, FontStyle.Bold)
            };
        }

        private void ApplyCached()
        {
            var cachedProfile = _service.TryGetCachedProfile();
            if (cachedProfile != null)
            {
                _profile = cachedProfile;
                ApplyProfile(cachedProfile);
            }
            var cachedPage = _service.TryGetCachedPage();
            if (cachedPage != null)
            {
                _requestedCount = Math.Max(LeaguePlayerDataService.InitialMatchCount, Math.Min(LeaguePlayerDataService.MaximumMatchCount, cachedPage.RequestedCount));
                ApplyPage(cachedPage);
            }
            if (cachedProfile == null && cachedPage == null)
            {
                _accountLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerLoadingProfile);
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerLoadingProfile);
                _championStatsList.Items.Clear();
            }
            UpdateEmptyState();
        }

        private async Task RefreshAllAsync(bool force)
        {
            if (_loading || IsDisposed || _lifetime.IsCancellationRequested) return;
            var expectedSession = _currentSession == null ? null : _currentSession();
            _pageSession = null;
            UpdateReplayButton();
            SetLoading(true);
            try
            {
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerLoadingProfile);
                UpdateEmptyState();
                var profile = await _service.LoadProfileAsync(force, _lifetime.Token);
                if (IsDisposed) return;
                if (profile == null || string.IsNullOrWhiteSpace(profile.PuuId))
                {
                    _profile = null;
                    _pageSession = null;
                    _loadedPage = null;
                    _hasMore = false;
                    _rows.Clear();
                    ResetFilterChoices();
                    _matchesList.VirtualListSize = 0;
                    _championStatsList.Items.Clear();
                    _accountLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerClientRequired);
                    _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerClientRequired);
                    UpdateEmptyState();
                    return;
                }

                if (_profile != null && !string.Equals(_profile.PuuId, profile.PuuId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    _loadedPage = null;
                    _hasMore = false;
                    _rows.Clear();
                    ResetFilterChoices();
                    _matchesList.VirtualListSize = 0;
                    _championStatsList.Items.Clear();
                }
                _profile = profile;
                ApplyProfile(profile);
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerLoadingMatches);
                UpdateEmptyState();
                var page = await _service.LoadRecentMatchesAsync(profile, 0, _requestedCount, force, _lifetime.Token);
                if (IsDisposed) return;
                _pageSession = SessionMatches(expectedSession) ? expectedSession : null;
                _collectionResult = null;
                ApplyPage(page);

                var enriched = await _service.EnrichIncompleteMatchesAsync(profile, page, _lifetime.Token);
                var finalPage = enriched ?? page;
                if (!IsDisposed && finalPage != null) ApplyPage(finalPage);

                var named = await _service.EnrichChampionNamesAsync(profile, finalPage, _lifetime.Token);
                if (!IsDisposed && named != null) ApplyPage(named);
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested)
                {
                    _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerUnknown);
                    UpdateEmptyState();
                }
            }
            catch (Exception exception)
            {
                AppLog.Info("League Player refresh skipped: " + exception.Message);
                if (!IsDisposed)
                {
                    _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerUnknown);
                    UpdateEmptyState();
                }
            }
            finally
            {
                SetLoading(false);
            }
        }

        private async Task LoadMoreAsync()
        {
            if (_loading || _profile == null || !_hasMore || _requestedCount >= LeaguePlayerDataService.MaximumMatchCount) return;
            var expectedSession = _pageSession;
            _requestedCount = LeaguePlayerDataService.MaximumMatchCount;
            SetLoading(true);
            try
            {
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerLoadingMatches);
                UpdateEmptyState();
                var page = await _service.LoadRecentMatchesAsync(_profile, 0, _requestedCount, false, _lifetime.Token);
                if (IsDisposed) return;
                _pageSession = SessionMatches(expectedSession) ? expectedSession : null;
                _collectionResult = null;
                ApplyPage(page);

                var enriched = await _service.EnrichIncompleteMatchesAsync(_profile, page, _lifetime.Token);
                var finalPage = enriched ?? page;
                if (!IsDisposed && finalPage != null) ApplyPage(finalPage);

                var named = await _service.EnrichChampionNamesAsync(_profile, finalPage, _lifetime.Token);
                if (!IsDisposed && named != null) ApplyPage(named);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("League Player load-more skipped: " + exception.Message);
                if (!IsDisposed)
                {
                    _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerUnknown);
                    UpdateEmptyState();
                }
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void ApplyProfile(LeaguePlayerProfile profile)
        {
            var name = string.IsNullOrWhiteSpace(profile.AccountName) ? _ui.Get(UiTextKeys.LeaguePlayerUnknown) : profile.AccountName;
            if (profile.SummonerLevel > 0)
                name += "  ·  " + _ui.Get(UiTextKeys.LeagueDashboardLevel) + " " + profile.SummonerLevel;
            _accountLabel.Text = name;
        }

        private void ApplyPage(LeaguePlayerMatchPage page)
        {
            _loadedPage = page;
            _hasMore = page != null && page.HasMore;
            UpdateFilterChoices(page);
            ApplySimpleFilter();
            UpdateReplayButton();
        }

        private bool HasCollectionFilter()
        {
            return _winFilter.SelectedIndex > 0 || _timeFilter.SelectedIndex > 0 ||
                _selectedChampions.Count > 0 || _selectedPlayers.Count > 0 ||
                (_advancedFilter != null && _advancedFilter.Root.Children.Count > 0);
        }

        internal static LeagueMatchCollectionSettings ShowCollectionSettings(
            IWin32Window owner, UiTextCatalog ui, Font font, LeagueMatchCollectionSettings previous)
        {
            using (var dialog = new Form
            {
                Text = ui.Get(UiTextKeys.LeaguePlayerCollectTitle),
                ClientSize = new Size(438, 242), FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                Font = font, BackColor = FacmDesignSystem.Canvas, ForeColor = FacmDesignSystem.Text
            })
            {
                var settings = new[]
                {
                    new { Key = UiTextKeys.LeaguePlayerCollectBatch, Value = previous.CountPerIteration, Maximum = 200 },
                    new { Key = UiTextKeys.LeaguePlayerCollectExpected, Value = previous.ExpectedCount, Maximum = 1000 },
                    new { Key = UiTextKeys.LeaguePlayerCollectIterations, Value = previous.MaxIterations, Maximum = 100 }
                };
                var values = new List<NumericUpDown>();
                var hint = new Label
                {
                    Text = ui.Get(UiTextKeys.LeaguePlayerCollectHint),
                    Bounds = new Rectangle(18, 14, 400, 36), AutoEllipsis = true,
                    ForeColor = FacmDesignSystem.TextMuted
                };
                dialog.Controls.Add(hint);
                for (var index = 0; index < settings.Length; index++)
                {
                    var y = 58 + index * 42;
                    dialog.Controls.Add(new Label
                    {
                        Text = ui.Get(settings[index].Key),
                        Bounds = new Rectangle(18, y + 4, 260, 25),
                        ForeColor = FacmDesignSystem.Text
                    });
                    var numeric = new NumericUpDown
                    {
                        Minimum = 1, Maximum = settings[index].Maximum,
                        Value = settings[index].Value, Bounds = new Rectangle(290, y, 122, 27),
                        BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text
                    };
                    values.Add(numeric);
                    dialog.Controls.Add(numeric);
                }
                var start = new Button
                {
                    Text = ui.Get(UiTextKeys.LeaguePlayerCollectStart),
                    DialogResult = DialogResult.OK, Bounds = new Rectangle(220, 200, 92, 30),
                    FlatStyle = FlatStyle.Flat, BackColor = FacmDesignSystem.Accent,
                    ForeColor = FacmDesignSystem.Canvas
                };
                var cancel = new Button
                {
                    Text = ui.Get(UiTextKeys.LeaguePlayerAdvancedCancel),
                    DialogResult = DialogResult.Cancel, Bounds = new Rectangle(322, 200, 92, 30),
                    FlatStyle = FlatStyle.Flat, BackColor = FacmDesignSystem.SurfaceRaised,
                    ForeColor = FacmDesignSystem.Text
                };
                start.FlatAppearance.BorderSize = cancel.FlatAppearance.BorderSize = 0;
                dialog.Controls.Add(start);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = start;
                dialog.CancelButton = cancel;
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                return new LeagueMatchCollectionSettings
                {
                    CountPerIteration = (int)values[0].Value,
                    ExpectedCount = (int)values[1].Value,
                    MaxIterations = (int)values[2].Value
                };
            }
        }

        private async Task StartCollectionAsync()
        {
            if (_loading || _profile == null || !HasCollectionFilter() ||
                _collectionCancellation != null || _lifetime.IsCancellationRequested) return;
            var settings = ShowCollectionSettings(this, _ui, Font, _collectionSettings);
            if (settings == null || !settings.IsValid || IsDisposed) return;
            _collectionSettings = settings;
            var profile = _profile;
            var session = _currentSession == null ? null : _currentSession();
            var simple = new LeagueSimpleMatchHistoryFilter
            {
                WinLoss = (LeagueMatchWinLossFilter)Math.Max(0, _winFilter.SelectedIndex),
                TimeRange = (LeagueMatchTimeRangeFilter)Math.Max(0, _timeFilter.SelectedIndex),
                ChampionIds = _selectedChampions.ToArray(),
                PlayerPuuids = _selectedPlayers.ToArray()
            };
            var advanced = _advancedFilter;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _collectionCancellation = cancellation;
            SetLoading(true);
            _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerCollectProgress)
                .Replace("{0}", "0").Replace("{1}", "0").Replace("{2}", "0");
            var progress = new Progress<LeagueMatchCollectionProgress>(current =>
            {
                if (IsDisposed || _collectionCancellation != cancellation || cancellation.IsCancellationRequested) return;
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerCollectProgress)
                    .Replace("{0}", current.CollectedCount.ToString())
                    .Replace("{1}", current.ScannedCount.ToString())
                    .Replace("{2}", current.Iterations.ToString());
                UpdateEmptyState();
            });
            try
            {
                var result = await _service.CollectMatchesAsync(profile, settings, simple, advanced,
                    progress, cancellation.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (!string.Equals(_profile == null ? null : _profile.PuuId, profile.PuuId,
                    StringComparison.Ordinal) || !SessionMatches(session) ||
                    result.Status == LeagueMatchCollectionStatus.AccountChanged)
                {
                    _loadedPage = null;
                    _collectionResult = null;
                    _profile = null;
                    _pageSession = null;
                    _rows.Clear();
                    _matchesList.VirtualListSize = 0;
                    _championStatsList.Items.Clear();
                    ResetFilterChoices();
                    _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteAccountChanged);
                    UpdateEmptyState();
                    return;
                }

                var names = _service.TryGetCachedChampionNames();
                if (names != null)
                    foreach (var match in result.Page.Matches)
                    {
                        string name;
                        if (match.ChampionId > 0 && names.TryGetValue(match.ChampionId, out name))
                            match.ChampionName = name;
                    }
                _collectionResult = result;
                _pageSession = session;
                ApplyPage(result.Page);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _collectionCancellation = null;
                cancellation.Dispose();
                if (!IsDisposed) SetLoading(false);
            }
        }

        private async Task SearchRemotePlayerAsync()
        {
            if (_loadingPlayerLookup || IsDisposed || _lifetime.IsCancellationRequested) return;
            _loadingPlayerLookup = true;
            _playerLookupButton.Enabled = false;
            _playerHistoryButton.Enabled = false;
            _lastRemotePlayerPuuid = null;
            _lastRemotePlayerName = null;
            _playerLookupStatus.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteLoading);
            var profile = _profile;
            var query = _playerSearch.Text;
            try
            {
                var result = await _service.SearchRiotIdAsync(profile, query, _lifetime.Token, true);
                if (IsDisposed || _lifetime.IsCancellationRequested ||
                    profile == null || _profile == null ||
                    !string.Equals(query, _playerSearch.Text, StringComparison.Ordinal) ||
                    !string.Equals(profile.PuuId, _profile.PuuId, StringComparison.Ordinal)) return;
                switch (result.Status)
                {
                    case LeagueRiotPlayerSearchStatus.Found:
                        if (result.Player == null || string.IsNullOrEmpty(result.Player.PuuId)) break;
                        if (_remotePlayers.Count >= 10 && !_remotePlayers.ContainsKey(result.Player.PuuId))
                            _remotePlayers.Clear();
                        _remotePlayers[result.Player.PuuId] = result.Player.PlayerName;
                        _lastRemotePlayerPuuid = result.Player.PuuId;
                        _lastRemotePlayerName = result.Player.PlayerName;
                        _playerHistoryButton.Enabled = true;
                        UpdateFilterChoices(_loadedPage);
                        _playerLookupStatus.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteFound);
                        break;
                    case LeagueRiotPlayerSearchStatus.InvalidQuery:
                        _playerLookupStatus.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteInvalid);
                        break;
                    case LeagueRiotPlayerSearchStatus.NotFound:
                        _playerLookupStatus.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteNotFound);
                        break;
                    case LeagueRiotPlayerSearchStatus.AccountChanged:
                        _playerLookupStatus.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteAccountChanged);
                        break;
                    default:
                        _playerLookupStatus.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteUnavailable);
                        break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!IsDisposed) _playerLookupStatus.Text = _ui.Get(UiTextKeys.LeaguePlayerRemoteUnavailable); }
            finally
            {
                _loadingPlayerLookup = false;
                if (!IsDisposed) _playerLookupButton.Enabled = true;
            }
        }

        private void OpenRemotePlayerHistory()
        {
            if (IsDisposed || _profile == null || string.IsNullOrEmpty(_lastRemotePlayerPuuid)) return;
            var puuid = _lastRemotePlayerPuuid;
            var name = _lastRemotePlayerName;
            _playerMenu.Close();
            using (var dialog = new LeagueRemotePlayerHistoryForm(_service, _ui, _profile, puuid, name,
                _currentSession, _replay))
                dialog.ShowDialog(this);
        }

        private LeaguePlayerMatchSummary SelectedReplayMatch()
        {
            if (_matchesList.SelectedIndices.Count != 1) return null;
            var index = _matchesList.SelectedIndices[0];
            return index >= 0 && index < _rows.Count ? _rows[index] : null;
        }

        private bool SessionMatches(LeagueClientSession expected)
        {
            var current = _currentSession == null ? null : _currentSession();
            return LeagueEncounterReplayService.SessionMatches(expected, current);
        }

        private void UpdateReplayButton()
        {
            if (_replayButton == null || IsDisposed) return;
            var selected = SelectedReplayMatch();
            _replayButton.Enabled = !_loading && !_replayRunning && _replay != null &&
                LeagueEncounterReplayService.CanOfferHistoryReplay(_pageSession,
                    _currentSession == null ? null : _currentSession(),
                    _profile == null ? null : _profile.PuuId, selected);
        }

        private async Task ReplaySelectedAsync()
        {
            var match = SelectedReplayMatch();
            var owner = _profile == null ? null : _profile.PuuId;
            var session = _pageSession;
            if (match == null || _replay == null || _loading || _replayRunning ||
                !SessionMatches(session) || string.IsNullOrWhiteSpace(owner)) return;
            _replayRunning = true;
            UpdateReplayButton();
            _statusLabel.Text = LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.DynamicEncounterReplayChecking);
            try
            {
                var result = await _replay(session, owner, owner, match, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested ||
                    !string.Equals(_profile == null ? null : _profile.PuuId, owner, StringComparison.Ordinal) ||
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

        private void ResetFilterChoices()
        {
            _advancedFilter = null;
            UpdateAdvancedFilterLabel();
            _selectedChampions.Clear();
            _selectedPlayers.Clear();
            _selectedPlayerNames.Clear();
            _remotePlayers.Clear();
            _lastRemotePlayerPuuid = null;
            _lastRemotePlayerName = null;
            _playerHistoryButton.Enabled = false;
            _championMenu.Close();
            _playerMenu.Close();
            _championSearch.Text = string.Empty;
            _playerSearch.Text = string.Empty;
            _playerLookupStatus.Text = string.Empty;
            _winFilter.SelectedIndex = 0;
            _timeFilter.SelectedIndex = 0;
            UpdateFilterChoices(null);
        }

        private static string AbbreviatePuuid(string puuid)
        {
            return puuid.Length <= 18 ? puuid : puuid.Substring(0, 8) + "…" + puuid.Substring(puuid.Length - 6);
        }

        internal static bool FilterOptionMatchesForSmokeTest(string name, string id, string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            var term = search.Trim();
            return (name != null && name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (id != null && id.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void UpdateFilterChoices(LeaguePlayerMatchPage page)
        {
            var known = new SortedDictionary<int, string>();
            var players = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var catalog = _service.TryGetCachedChampionNames();
            if (catalog != null)
                foreach (var candidate in catalog)
                    if (candidate.Key > 0 && !string.IsNullOrWhiteSpace(candidate.Value))
                        known[candidate.Key] = candidate.Value + " (#" + candidate.Key + ")";
            if (page != null)
            {
                foreach (var match in page.Matches)
                {
                    if (match == null) continue;
                    if (match.MatchChampionIds != null)
                        foreach (var id in match.MatchChampionIds)
                            if (id > 0 && !known.ContainsKey(id)) known[id] = "#" + id;
                    if (match.ChampionId > 0 && !known.ContainsKey(match.ChampionId) &&
                        !string.IsNullOrWhiteSpace(match.ChampionName))
                        known[match.ChampionId] = match.ChampionName + " (#" + match.ChampionId + ")";
                    if (match.MatchPlayers != null)
                        foreach (var player in match.MatchPlayers)
                            if (player != null && !string.IsNullOrEmpty(player.PuuId) &&
                                (!players.ContainsKey(player.PuuId) ||
                                (players[player.PuuId] == null && !string.IsNullOrWhiteSpace(player.PlayerName))))
                                players[player.PuuId] = string.IsNullOrWhiteSpace(player.PlayerName) ? null : player.PlayerName;
                }
            }
            foreach (var remote in _remotePlayers)
                if (!players.ContainsKey(remote.Key) || players[remote.Key] == null)
                    players[remote.Key] = remote.Value;
            foreach (var id in _selectedChampions)
                if (!known.ContainsKey(id)) known[id] = "#" + id;
            foreach (var puuid in _selectedPlayers)
            {
                string selectedName;
                _selectedPlayerNames.TryGetValue(puuid, out selectedName);
                if (!players.ContainsKey(puuid)) players[puuid] = selectedName;
                else if (!string.IsNullOrWhiteSpace(players[puuid])) _selectedPlayerNames[puuid] = players[puuid];
            }

            _updatingFilterChoices = true;
            try
            {
                _championChecks.BeginUpdate();
                _playerChecks.BeginUpdate();
                _championChecks.Items.Clear();
                _playerChecks.Items.Clear();
                foreach (var candidate in known)
                    if (FilterOptionMatchesForSmokeTest(candidate.Value, candidate.Key.ToString(), _championSearch.Text))
                        _championChecks.Items.Add(new ChampionChoice(candidate.Key, candidate.Value),
                            _selectedChampions.Contains(candidate.Key));
                foreach (var candidate in players)
                    if (FilterOptionMatchesForSmokeTest(candidate.Value, candidate.Key, _playerSearch.Text))
                        _playerChecks.Items.Add(new PlayerChoice(candidate.Key, candidate.Value),
                            _selectedPlayers.Contains(candidate.Key));
            }
            finally
            {
                _championChecks.EndUpdate();
                _playerChecks.EndUpdate();
                _updatingFilterChoices = false;
            }
            UpdateFilterButtonLabels();
        }

        private void UpdateFilterButtonLabels()
        {
            var all = _ui.Get(UiTextKeys.LeaguePlayerFilterAll);
            var selected = _ui.Get(UiTextKeys.LeaguePlayerFilterSelected);
            _championFilter.Text = _selectedChampions.Count == 0 ? all : selected.Replace("{0}", _selectedChampions.Count.ToString());
            _playerFilter.Text = _selectedPlayers.Count == 0 ? all : selected.Replace("{0}", _selectedPlayers.Count.ToString());
        }

        private void UpdateAdvancedFilterLabel()
        {
            _advancedFilterButton.Text = _ui.Get(_advancedFilter == null ?
                UiTextKeys.LeaguePlayerAdvancedFilter : UiTextKeys.LeaguePlayerAdvancedActive);
        }

        private void ApplySimpleFilter()
        {
            if (_updatingFilterChoices || _matchesList == null || IsDisposed) return;
            var filter = new LeagueSimpleMatchHistoryFilter
            {
                WinLoss = (LeagueMatchWinLossFilter)Math.Max(0, _winFilter.SelectedIndex),
                TimeRange = (LeagueMatchTimeRangeFilter)Math.Max(0, _timeFilter.SelectedIndex),
                ChampionIds = _selectedChampions.Count == 0 ? null : _selectedChampions.ToArray(),
                PlayerPuuids = _selectedPlayers.Count == 0 ? null : _selectedPlayers.ToArray()
            };
            _rows.Clear();
            if (_loadedPage != null)
            {
                var selected = LeagueSimpleMatchHistoryFilter.Apply(_loadedPage.Matches, filter, DateTime.UtcNow);
                _rows.AddRange(LeagueAdvancedMatchHistoryFilter.Apply(selected, _advancedFilter));
            }
            _matchesList.VirtualListSize = _rows.Count;
            _matchesList.Invalidate();
            UpdateReplayButton();
            var visiblePage = new LeaguePlayerMatchPage();
            visiblePage.Matches.AddRange(_rows);
            ApplyChampionStats(visiblePage);
            var loaded = _loadedPage == null ? 0 : _loadedPage.Matches.Count;
            _statusLabel.Text = loaded == 0 ? _ui.Get(UiTextKeys.LeaguePlayerNoMatches) :
                _rows.Count == 0 ? _ui.Get(UiTextKeys.LeaguePlayerNoFilteredMatches) :
                _rows.Count == loaded ? _ui.Get(UiTextKeys.LeaguePlayerRecentMatches) + "  ·  " + loaded :
                _ui.Get(UiTextKeys.LeaguePlayerFilteredCount)
                    .Replace("{0}", _rows.Count.ToString()).Replace("{1}", loaded.ToString());
            if (_collectionResult != null)
            {
                var status = _collectionResult.Status == LeagueMatchCollectionStatus.Stopped
                    ? _ui.Get(UiTextKeys.LeaguePlayerCollectStopped)
                    : _collectionResult.Status == LeagueMatchCollectionStatus.Unavailable
                        ? _ui.Get(UiTextKeys.LeaguePlayerCollectUnavailable) : string.Empty;
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerCollectSummary)
                    .Replace("{0}", _rows.Count.ToString())
                    .Replace("{1}", _collectionResult.ScannedCount.ToString())
                    .Replace("{2}", _collectionResult.Iterations.ToString()) + status;
            }
            _loadMoreButton.Enabled = !_loading && _requestedCount < LeaguePlayerDataService.MaximumMatchCount && _hasMore;
            _collectButton.Enabled = !_loading && _profile != null && HasCollectionFilter();
            UpdateEmptyState();
        }

        private void ApplyChampionStats(LeaguePlayerMatchPage page)
        {
            _championStatsList.BeginUpdate();
            try
            {
                _championStatsList.Items.Clear();
                _statsSection.Text = FormatChampionStatsTitle(_rows.Count);
                foreach (var stat in _service.BuildChampionStats(page))
                {
                    var champion = FormatChampion(stat.ChampionName, stat.ChampionId) + " ×" + stat.Games;
                    var winRate = stat.WinRate.ToString("0") + "%";
                    var averageKda = stat.AverageKills.ToString("0.0") + " / " +
                        stat.AverageDeaths.ToString("0.0") + " / " + stat.AverageAssists.ToString("0.0");
                    _championStatsList.Items.Add(new ListViewItem(new[] { champion, winRate, averageKda }));
                }
            }
            finally
            {
                _championStatsList.EndUpdate();
            }
        }

        private void RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= _rows.Count)
            {
                e.Item = new ListViewItem(string.Empty);
                return;
            }
            var match = _rows[e.ItemIndex];
            if (match == null)
            {
                e.Item = new ListViewItem(_ui.Get(UiTextKeys.LeaguePlayerUnknown));
                return;
            }
            var unknown = _ui.Get(UiTextKeys.LeaguePlayerUnknown);
            var time = match.GameCreationLocal == DateTime.MinValue ? unknown : match.GameCreationLocal.ToString("MM-dd HH:mm");
            var mode = string.IsNullOrWhiteSpace(match.GameMode) ? unknown : match.GameMode;
            if (match.QueueId > 0) mode += " #" + match.QueueId;
            var champion = match.ParticipantResolved && match.ChampionId > 0
                ? FormatChampion(match.ChampionName, match.ChampionId)
                : unknown;
            var kda = match.ParticipantResolved ? match.Kills + " / " + match.Deaths + " / " + match.Assists : unknown;
            var cs = match.ParticipantResolved ? match.CreepScore.ToString() : unknown;
            var result = match.ParticipantResolved
                ? _ui.Get(match.Win ? UiTextKeys.LeaguePlayerWin : UiTextKeys.LeaguePlayerLoss)
                : unknown;
            var duration = match.GameDurationSeconds > 0
                ? TimeSpan.FromSeconds(match.GameDurationSeconds).ToString(@"mm\:ss")
                : unknown;
            var item = new ListViewItem(new[] { time, mode, champion, kda, cs, result, duration });
            item.UseItemStyleForSubItems = false;
            foreach (ListViewItem.ListViewSubItem cell in item.SubItems)
                cell.ForeColor = match.ParticipantResolved ? FacmDesignSystem.Text : FacmDesignSystem.TextMuted;
            if (match.ParticipantResolved)
                item.SubItems[5].ForeColor = match.Win ? FacmDesignSystem.Success : FacmDesignSystem.Error;
            e.Item = item;
        }

        private string ChampionHeaderText()
        {
            var text = _ui.Get(UiTextKeys.LeaguePlayerChampion);
            return !string.IsNullOrWhiteSpace(text) && text.EndsWith(" ID", StringComparison.OrdinalIgnoreCase)
                ? text.Substring(0, text.Length - 3).TrimEnd()
                : text;
        }

        private string FormatChampionStatsTitle(int count)
        {
            var format = _ui.Get(UiTextKeys.LeaguePlayerChampionStatsFormat);
            try
            {
                return string.Format(format, Math.Max(0, count));
            }
            catch (FormatException)
            {
                return format + " · " + Math.Max(0, count);
            }
        }

        private static string FormatChampion(string name, int championId)
        {
            if (!string.IsNullOrWhiteSpace(name)) return name;
            return championId > 0 ? championId.ToString() : string.Empty;
        }

        private void SetLoading(bool loading)
        {
            _loading = loading;
            if (IsDisposed) return;
            _refreshButton.Enabled = !loading;
            _loadMoreButton.Enabled = !loading && _requestedCount < LeaguePlayerDataService.MaximumMatchCount && _hasMore;
            _filterPanel.Enabled = !loading;
            _collectButton.Enabled = !loading && _profile != null && HasCollectionFilter();
            _browseHistoryButton.Enabled = !loading && _profile != null;
            _stopCollectButton.Enabled = _collectionCancellation != null &&
                !_collectionCancellation.IsCancellationRequested;
            UpdateReplayButton();
            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            if (IsDisposed) return;
            _emptyStateLabel.Text = _statusLabel.Text;
            _emptyStateLabel.Visible = _rows.Count == 0;
            if (_emptyStateLabel.Visible) _emptyStateLabel.BringToFront();
        }

        internal static Rectangle ResolveMatchesBoundsForSmokeTest(int clientWidth, int clientHeight)
        {
            var statsHeight = clientHeight >= 670 ? 115 : 98;
            var top = 194 + statsHeight + 74;
            return new Rectangle(
                24, top,
                Math.Max(160, clientWidth - 48),
                Math.Max(96, clientHeight - top - 64));
        }

        internal static int[] ResolveMatchColumnWidthsForSmokeTest(int listWidth)
        {
            var widths = new[] { 100, 104, 122, 104, 52, 68, 70 };
            var extra = Math.Max(0, listWidth - 10 - 620);
            var weights = new[] { 0.11, 0.23, 0.28, 0.19, 0.04, 0.07 };
            var distributed = 0;
            for (var i = 0; i < weights.Length; i++)
            {
                var addition = (int)(extra * weights[i]);
                widths[i] += addition;
                distributed += addition;
            }
            widths[6] += extra - distributed;
            return widths;
        }

        private void ResizePlayerPage()
        {
            if (_matchesList == null || _matchesList.IsDisposed) return;
            var width = ClientSize.Width;
            var height = ClientSize.Height;
            var contentWidth = Math.Max(160, width - 48);

            _accountSurface.Bounds = new Rectangle(24, 88, contentWidth, 72);
            _accountAccent.Bounds = new Rectangle(0, 0, 3, 72);
            _titleLabel.Width = contentWidth;
            _hintLabel.Width = contentWidth;
            _accountLabel.Width = Math.Max(160, contentWidth - 12);
            _statusLabel.Width = Math.Max(160, contentWidth - 12);
            _statsSection.Width = contentWidth;

            var statsHeight = height >= 670 ? 115 : 98;
            _championStatsList.Bounds = new Rectangle(24, 194, contentWidth, statsHeight);
            var matchesBounds = ResolveMatchesBoundsForSmokeTest(width, height);
            _recentSection.Bounds = new Rectangle(26, matchesBounds.Top - 65, contentWidth, 25);
            _filterPanel.Bounds = new Rectangle(matchesBounds.Left, matchesBounds.Top - 37, contentWidth, 33);
            _matchesList.Bounds = matchesBounds;
            _emptyStateLabel.Bounds = new Rectangle(
                matchesBounds.Left + 16,
                matchesBounds.Top + 44,
                Math.Max(160, matchesBounds.Width - 32),
                Math.Min(72, Math.Max(40, matchesBounds.Height - 50)));

            var toolbarTop = Math.Max(matchesBounds.Bottom + 10, height - 46);
            _closeButton.Location = new Point(Math.Max(24, width - 24 - _closeButton.Width), toolbarTop);
            _loadMoreButton.Location = new Point(_closeButton.Left - 8 - _loadMoreButton.Width, toolbarTop);
            _refreshButton.Location = new Point(_loadMoreButton.Left - 8 - _refreshButton.Width, toolbarTop);
            _replayButton.Location = new Point(matchesBounds.Left, toolbarTop);
            var collectAvailable = Math.Max(145, _refreshButton.Left - _replayButton.Right - 20);
            _collectButton.Width = Math.Min(108, Math.Max(78, (collectAvailable - 8) / 2));
            _stopCollectButton.Width = Math.Min(82, Math.Max(60, collectAvailable - _collectButton.Width - 8));
            _collectButton.Location = new Point(_replayButton.Right + 8, toolbarTop);
            _stopCollectButton.Location = new Point(_collectButton.Right + 8, toolbarTop);

            var available = Math.Max(320, _championStatsList.ClientSize.Width - 10);
            var championWidth = Math.Max(135, available * 44 / 100);
            var resultWidth = Math.Max(85, available * 17 / 100);
            _championStatsList.Columns[0].Width = championWidth;
            _championStatsList.Columns[1].Width = resultWidth;
            _championStatsList.Columns[2].Width = Math.Max(100, available - championWidth - resultWidth);

            var matchWidths = ResolveMatchColumnWidthsForSmokeTest(_matchesList.ClientSize.Width);
            for (var i = 0; i < matchWidths.Length; i++)
                _matchesList.Columns[i].Width = matchWidths[i];
            if (_emptyStateLabel.Visible) _emptyStateLabel.BringToFront();
        }
    }
}
