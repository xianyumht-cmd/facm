using System;
using System.Collections.Generic;
using System.Drawing;
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
        private readonly Label _accountLabel;
        private readonly Label _statusLabel;
        private readonly Label _statsSection;
        private readonly Label _recentSection;
        private readonly Label _emptyStateLabel;
        private readonly Label _titleLabel;
        private readonly Label _hintLabel;
        private readonly FacmActionButton _closeButton;
        private readonly ListView _championStatsList;
        private readonly ListView _matchesList;
        private readonly FacmActionButton _refreshButton;
        private readonly FacmActionButton _loadMoreButton;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<LeaguePlayerMatchSummary> _rows = new List<LeaguePlayerMatchSummary>();
        private LeaguePlayerProfile _profile;
        private bool _loading;
        private bool _hasMore;
        private int _requestedCount = LeaguePlayerDataService.InitialMatchCount;

        public LeaguePlayerForm(LeaguePlayerDataService service, UiTextCatalog ui)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));

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
                Font = new Font(FacmThemeRuntime.Current.FontName, 17F, FontStyle.Bold)
            };
            _hintLabel = new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePlayerHint),
                Location = new Point(30, 58),
                Size = new Size(760, 24),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };
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

            _refreshButton = CreateButton(UiTextKeys.LeaguePlayerRefresh, FacmButtonTone.Primary);
            _refreshButton.Location = new Point(542, 672);
            _refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _refreshButton.Click += async delegate { await RefreshAllAsync(true); };

            _loadMoreButton = CreateButton(UiTextKeys.LeaguePlayerLoadMore, FacmButtonTone.Secondary);
            _loadMoreButton.Location = new Point(640, 672);
            _loadMoreButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _loadMoreButton.Click += async delegate { await LoadMoreAsync(); };

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

            Controls.Add(_titleLabel);
            Controls.Add(_hintLabel);
            Controls.Add(_accountLabel);
            Controls.Add(_statusLabel);
            Controls.Add(_statsSection);
            Controls.Add(_championStatsList);
            Controls.Add(_recentSection);
            Controls.Add(_matchesList);
            Controls.Add(_refreshButton);
            Controls.Add(_loadMoreButton);
            Controls.Add(_closeButton);
            Controls.Add(_emptyStateLabel);

            ApplyCached();
            ResizePlayerPage();
            Resize += delegate { ResizePlayerPage(); };
            Shown += async delegate { await RefreshAllAsync(true); };
            FormClosed += delegate
            {
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _lifetime.Dispose();
            };
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 10F, FontStyle.Bold)
            };
        }

        private static ListView CreateListView(Rectangle bounds, bool virtualMode)
        {
            return new ListView
            {
                Bounds = bounds,
                BackColor = FacmDesignSystem.CanvasRaised,
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
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.6F, FontStyle.Bold)
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
                    _hasMore = false;
                    _rows.Clear();
                    _matchesList.VirtualListSize = 0;
                    _championStatsList.Items.Clear();
                    _accountLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerClientRequired);
                    _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerClientRequired);
                    UpdateEmptyState();
                    return;
                }

                _profile = profile;
                ApplyProfile(profile);
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerLoadingMatches);
                UpdateEmptyState();
                var page = await _service.LoadRecentMatchesAsync(profile, 0, _requestedCount, force, _lifetime.Token);
                if (IsDisposed) return;
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
            _requestedCount = LeaguePlayerDataService.MaximumMatchCount;
            SetLoading(true);
            try
            {
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerLoadingMatches);
                UpdateEmptyState();
                var page = await _service.LoadRecentMatchesAsync(_profile, 0, _requestedCount, false, _lifetime.Token);
                if (IsDisposed) return;
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
            _rows.Clear();
            if (page != null) _rows.AddRange(page.Matches);
            _hasMore = page != null && page.HasMore;
            _matchesList.VirtualListSize = _rows.Count;
            _matchesList.Invalidate();
            ApplyChampionStats(page);
            _statusLabel.Text = _rows.Count == 0
                ? _ui.Get(UiTextKeys.LeaguePlayerNoMatches)
                : _ui.Get(UiTextKeys.LeaguePlayerRecentMatches) + "  ·  " + _rows.Count;
            _loadMoreButton.Enabled = !_loading && _requestedCount < LeaguePlayerDataService.MaximumMatchCount && _hasMore;
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
            var top = 194 + statsHeight + 40;
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

            _titleLabel.Width = contentWidth;
            _hintLabel.Width = contentWidth;
            _accountLabel.Width = contentWidth;
            _statusLabel.Width = contentWidth;
            _statsSection.Width = contentWidth;

            var statsHeight = height >= 670 ? 115 : 98;
            _championStatsList.Bounds = new Rectangle(24, 194, contentWidth, statsHeight);
            var matchesBounds = ResolveMatchesBoundsForSmokeTest(width, height);
            _recentSection.Bounds = new Rectangle(26, matchesBounds.Top - 31, contentWidth, 25);
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
