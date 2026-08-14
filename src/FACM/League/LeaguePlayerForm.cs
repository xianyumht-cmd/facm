using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeaguePlayerForm : Form
    {
        private readonly LeaguePlayerDataService _service;
        private readonly UiTextCatalog _ui;
        private readonly Label _accountLabel;
        private readonly Label _statusLabel;
        private readonly Label _statsSection;
        private readonly ListView _championStatsList;
        private readonly ListView _matchesList;
        private readonly Button _refreshButton;
        private readonly Button _loadMoreButton;
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
            BackColor = Color.FromArgb(14, 19, 30);
            ForeColor = Color.FromArgb(238, 243, 252);
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePlayerTitle),
                Location = new Point(28, 20),
                Size = new Size(300, 36),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePlayerHint),
                Location = new Point(30, 60),
                Size = new Size(760, 24),
                ForeColor = Color.FromArgb(146, 161, 188)
            };
            _accountLabel = new Label
            {
                Location = new Point(30, 96),
                Size = new Size(760, 32),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                AutoEllipsis = true
            };
            _statusLabel = new Label
            {
                Location = new Point(30, 132),
                Size = new Size(760, 22),
                ForeColor = Color.FromArgb(139, 157, 190)
            };

            _statsSection = new Label
            {
                Text = FormatChampionStatsTitle(0),
                Location = new Point(30, 166),
                Size = new Size(500, 25),
                ForeColor = Color.FromArgb(190, 205, 231),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };
            _championStatsList = new ListView
            {
                Location = new Point(28, 194),
                Size = new Size(804, 112),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(18, 25, 39),
                ForeColor = Color.FromArgb(226, 234, 247),
                BorderStyle = BorderStyle.FixedSingle,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = true,
                View = View.Details
            };
            _championStatsList.Columns.Add(ChampionHeaderText(), 300);
            _championStatsList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerResult), 160);
            _championStatsList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerKda), 250);

            var section = new Label
            {
                Text = _ui.Get(UiTextKeys.LeaguePlayerRecentMatches),
                Location = new Point(30, 320),
                Size = new Size(300, 25),
                ForeColor = Color.FromArgb(190, 205, 231),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };

            _matchesList = new ListView
            {
                Location = new Point(28, 352),
                Size = new Size(804, 302),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(22, 29, 44),
                ForeColor = Color.FromArgb(238, 243, 252),
                BorderStyle = BorderStyle.FixedSingle,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = true,
                View = View.Details,
                VirtualMode = true,
                VirtualListSize = 0
            };
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerTime), 122);
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerMode), 140);
            _matchesList.Columns.Add(ChampionHeaderText(), 122);
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerKda), 106);
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerCs), 66);
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerResult), 72);
            _matchesList.Columns.Add(_ui.Get(UiTextKeys.LeaguePlayerDuration), 76);
            _matchesList.RetrieveVirtualItem += RetrieveVirtualItem;

            _refreshButton = CreateButton(UiTextKeys.LeaguePlayerRefresh, Color.FromArgb(55, 104, 214));
            _refreshButton.Location = new Point(542, 672);
            _refreshButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _refreshButton.Click += async delegate { await RefreshAllAsync(true); };

            _loadMoreButton = CreateButton(UiTextKeys.LeaguePlayerLoadMore, Color.FromArgb(35, 43, 60));
            _loadMoreButton.Location = new Point(640, 672);
            _loadMoreButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _loadMoreButton.Click += async delegate { await LoadMoreAsync(); };

            var close = CreateButton(UiTextKeys.Close, Color.FromArgb(35, 43, 60));
            close.Location = new Point(738, 672);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += delegate { Close(); };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_accountLabel);
            Controls.Add(_statusLabel);
            Controls.Add(_statsSection);
            Controls.Add(_championStatsList);
            Controls.Add(section);
            Controls.Add(_matchesList);
            Controls.Add(_refreshButton);
            Controls.Add(_loadMoreButton);
            Controls.Add(close);

            ApplyCached();
            Shown += async delegate { await RefreshAllAsync(true); };
            FormClosed += delegate
            {
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _lifetime.Dispose();
            };
        }

        private Button CreateButton(string key, Color background)
        {
            var button = new Button
            {
                Text = _ui.Get(key),
                Size = new Size(92, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = background,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(68, 79, 101);
            return button;
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
        }

        private async Task RefreshAllAsync(bool force)
        {
            if (_loading || IsDisposed || _lifetime.IsCancellationRequested) return;
            SetLoading(true);
            try
            {
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerLoadingProfile);
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
                    return;
                }

                _profile = profile;
                ApplyProfile(profile);
                _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerLoadingMatches);
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
                if (!_lifetime.IsCancellationRequested) _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerUnknown);
            }
            catch (Exception exception)
            {
                AppLog.Info("League Player refresh skipped: " + exception.Message);
                if (!IsDisposed) _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerUnknown);
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
                if (!IsDisposed) _statusLabel.Text = _ui.Get(UiTextKeys.LeaguePlayerUnknown);
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
            item.ForeColor = match.ParticipantResolved
                ? (match.Win ? Color.FromArgb(103, 218, 166) : Color.FromArgb(244, 145, 145))
                : Color.FromArgb(180, 190, 207);
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
            if (!string.IsNullOrWhiteSpace(name)) return name + " #" + championId;
            return championId > 0 ? championId.ToString() : string.Empty;
        }

        private void SetLoading(bool loading)
        {
            _loading = loading;
            if (IsDisposed) return;
            _refreshButton.Enabled = !loading;
            _loadMoreButton.Enabled = !loading && _requestedCount < LeaguePlayerDataService.MaximumMatchCount && _hasMore;
        }
    }
}
