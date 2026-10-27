using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    // Local history remains offline; explicit detail clicks can read one authenticated LCU match.
    internal sealed class LeagueEncounterHistoryBrowserDialog : Form
    {
        private const int PageSize = 40;
        private readonly UiTextCatalog _ui;
        private readonly Func<bool> _isCurrent;
        private readonly Func<int, int, string, bool, LeagueEncounterHistoryPage> _query;
        private readonly Func<long, bool> _deleteRecord;
        private readonly Func<LeagueEncounterRecord, CancellationToken, Task<LeagueEncounterMatchDetailResult>> _loadDetail;
        private readonly Func<string, CancellationToken, Task<Bitmap>> _loadAsset;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly TextBox _queueFilter;
        private readonly ComboBox _timeOrder;
        private readonly ListView _list;
        private readonly Label _pageLabel;
        private readonly Button _previous;
        private readonly Button _next;
        private readonly Button _delete;
        private readonly Button _details;
        private readonly Label _detailStatus;
        private bool _detailLoading;
        private int _page = 1;

        internal LeagueEncounterHistoryBrowserDialog(LeagueEfficiencyModule module, UiTextCatalog ui,
            LeagueDynamicMessageSnapshot snapshot, string playerPuuid, Func<bool> isCurrent)
            : this(ui, isCurrent,
                (page, size, queue, newest) => module.QueryPlayerEncounters(snapshot, playerPuuid, page, size, queue, newest),
                gameId => module.DeletePlayerEncounter(snapshot, playerPuuid, gameId),
                (record, token) => module.LoadEncounterMatchDetailAsync(record, token),
                (reference, token) => module.LoadEncounterAssetAsync(reference, token))
        {
        }

        internal LeagueEncounterHistoryBrowserDialog(LeagueEfficiencyModule module, UiTextCatalog ui,
            LeagueSavedPlayerOverviewRecord savedPlayer)
            : this(ui, () => true,
                (page, size, queue, newest) => module.QueryStoredPlayerEncounters(savedPlayer, page, size, queue, newest),
                gameId => module.DeleteStoredPlayerEncounter(savedPlayer, gameId),
                (record, token) => module.LoadEncounterMatchDetailAsync(record, token),
                (reference, token) => module.LoadEncounterAssetAsync(reference, token))
        {
        }

        private LeagueEncounterHistoryBrowserDialog(UiTextCatalog ui, Func<bool> isCurrent,
            Func<int, int, string, bool, LeagueEncounterHistoryPage> query, Func<long, bool> deleteRecord,
            Func<LeagueEncounterRecord, CancellationToken, Task<LeagueEncounterMatchDetailResult>> loadDetail,
            Func<string, CancellationToken, Task<Bitmap>> loadAsset)
        {
            _ui = ui;
            _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _deleteRecord = deleteRecord ?? throw new ArgumentNullException(nameof(deleteRecord));
            _loadDetail = loadDetail ?? throw new ArgumentNullException(nameof(loadDetail));
            _loadAsset = loadAsset ?? throw new ArgumentNullException(nameof(loadAsset));
            Text = T(LeagueEfficiencyUiTextKeys.DynamicEncounterTitle);
            Width = 775; Height = 490; MinimumSize = new Size(650, 400);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12),
                ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));

            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            filters.Controls.Add(new Label { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.DynamicEncounterFilter),
                Margin = new Padding(0, 8, 6, 0) });
            _queueFilter = new TextBox { Width = 155, MaxLength = 64,
                AccessibleName = T(LeagueEfficiencyUiTextKeys.DynamicEncounterFilter) };
            filters.Controls.Add(_queueFilter);
            _timeOrder = new ComboBox { Width = 112, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = T(LeagueEfficiencyUiTextKeys.DynamicEncounterOrderNewest) };
            _timeOrder.Items.Add(T(LeagueEfficiencyUiTextKeys.DynamicEncounterOrderNewest));
            _timeOrder.Items.Add(T(LeagueEfficiencyUiTextKeys.DynamicEncounterOrderOldest));
            _timeOrder.SelectedIndex = 0;
            _timeOrder.SelectedIndexChanged += (sender, e) => { _page = 1; Reload(); };
            filters.Controls.Add(_timeOrder);
            var apply = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicEncounterApply), Width = 84, Height = 29 };
            apply.Click += (sender, e) => { _page = 1; Reload(); };
            filters.Controls.Add(apply);
            AcceptButton = apply;
            layout.Controls.Add(filters, 0, 0);

            _list = new ListView { Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text };
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicEncounterGame), 180);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicEncounterQueue), 160);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicEncounterDate), 235);
            _list.SelectedIndexChanged += (sender, e) => RefreshDetailActions();
            layout.Controls.Add(_list, 0, 1);

            _detailStatus = new Label { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4),
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                AccessibleName = T(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchDetail) };
            layout.Controls.Add(_detailStatus, 0, 2);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var paging = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _previous = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagPrevious), Width = 75, Height = 30 };
            _next = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagNext), Width = 75, Height = 30 };
            _previous.Click += (sender, e) => { _page--; Reload(); };
            _next.Click += (sender, e) => { _page++; Reload(); };
            _pageLabel = new Label { AutoSize = true, Margin = new Padding(8, 9, 0, 0) };
            paging.Controls.Add(_previous); paging.Controls.Add(_next); paging.Controls.Add(_pageLabel);
            footer.Controls.Add(paging, 0, 0);
            var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagCancel), Width = 80, Height = 30 };
            close.Click += (sender, e) => Close();
            _delete = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicEncounterDelete),
                Width = 120, Height = 30, Enabled = false };
            _details = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchDetail),
                Width = 120, Height = 30, Enabled = false };
            _delete.Click += (sender, e) => DeleteSelected();
            _details.Click += async (sender, e) => await ShowDetailAsync();
            actions.Controls.Add(close); actions.Controls.Add(_delete); actions.Controls.Add(_details);
            footer.Controls.Add(actions, 1, 0);
            layout.Controls.Add(footer, 0, 3);
            Controls.Add(layout);
            CancelButton = close;
            FormClosed += (sender, e) => { _lifetime.Cancel(); _lifetime.Dispose(); };
            Reload();
        }

        private LeagueEncounterRecord SelectedRecord()
        {
            return _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as LeagueEncounterRecord : null;
        }

        private void RefreshDetailActions()
        {
            var selected = SelectedRecord() != null && !_detailLoading;
            _delete.Enabled = selected;
            _details.Enabled = selected;
            if (!_detailLoading) _detailStatus.Text = string.Empty;
        }

        private async Task ShowDetailAsync()
        {
            var record = SelectedRecord();
            if (record == null || _detailLoading || !_isCurrent()) return;
            _detailLoading = true;
            _detailStatus.Text = T(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLoading);
            RefreshDetailActions();
            try
            {
                var detail = await _loadDetail(record, _lifetime.Token);
                if (IsDisposed || !ReferenceEquals(record, SelectedRecord()) || !_isCurrent()) return;
                _detailStatus.Text = FormatDetail(detail);
                if (detail != null && detail.Status == LeagueEncounterMatchDetailStatus.Ready &&
                    detail.Participants != null && detail.Participants.Count > 0)
                {
                    using (var scoreboard = new LeagueEncounterMatchScoreboardDialog(_ui, detail, _loadAsset))
                        scoreboard.ShowDialog(this);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!IsDisposed) _detailStatus.Text = T(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchUnavailable);
            }
            finally
            {
                _detailLoading = false;
                if (!IsDisposed)
                {
                    if (!ReferenceEquals(record, SelectedRecord())) _detailStatus.Text = string.Empty;
                    _delete.Enabled = SelectedRecord() != null;
                    _details.Enabled = SelectedRecord() != null;
                }
            }
        }

        private string FormatDetail(LeagueEncounterMatchDetailResult detail)
        {
            if (detail == null || detail.Status == LeagueEncounterMatchDetailStatus.Unavailable)
                return T(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchUnavailable);
            if (detail.Status == LeagueEncounterMatchDetailStatus.NoClient)
                return T(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchNoClient);
            if (detail.Status == LeagueEncounterMatchDetailStatus.WrongAccount)
                return T(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWrongAccount);
            var match = detail.Match;
            if (match == null) return T(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchUnavailable);
            var champion = string.IsNullOrWhiteSpace(match.ChampionName) ? match.ChampionId.ToString() : match.ChampionName;
            var duration = TimeSpan.FromSeconds(Math.Max(0, match.GameDurationSeconds));
            return T(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchSummary)
                .Replace("{0}", match.Win ? T(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon) :
                    T(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost))
                .Replace("{1}", champion).Replace("{2}", match.Kills.ToString())
                .Replace("{3}", match.Deaths.ToString()).Replace("{4}", match.Assists.ToString())
                .Replace("{5}", match.CreepScore.ToString())
                .Replace("{6}", ((int)duration.TotalMinutes).ToString() + ":" + duration.Seconds.ToString("D2"));
        }

        private void Reload()
        {
            if (!_isCurrent()) { Close(); return; }
            LeagueEncounterHistoryPage result;
            try
            {
                result = _query(_page, PageSize, _queueFilter.Text.Trim(), _timeOrder.SelectedIndex != 1);
                var pageCount = Math.Max(1, (result.Total + PageSize - 1) / PageSize);
                if (_page > pageCount)
                {
                    _page = pageCount;
                    result = _query(_page, PageSize, _queueFilter.Text.Trim(), _timeOrder.SelectedIndex != 1);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(this, T(LeagueEfficiencyUiTextKeys.DynamicEncounterUnavailable),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var record in result.Records)
                {
                    var item = new ListViewItem(record.GameId.ToString()) { Tag = record };
                    item.SubItems.Add(record.QueueType);
                    item.SubItems.Add(record.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                    _list.Items.Add(item);
                }
            }
            finally { _list.EndUpdate(); }
            var pages = Math.Max(1, (result.Total + PageSize - 1) / PageSize);
            _previous.Enabled = _page > 1;
            _next.Enabled = _page < pages;
            RefreshDetailActions();
            _pageLabel.Text = T(LeagueEfficiencyUiTextKeys.DynamicEncounterPage)
                .Replace("{0}", _page.ToString()).Replace("{1}", pages.ToString())
                .Replace("{2}", result.Total.ToString());
        }

        private void DeleteSelected()
        {
            if (!_isCurrent()) { Close(); return; }
            var record = SelectedRecord();
            if (record == null || MessageBox.Show(this, T(LeagueEfficiencyUiTextKeys.DynamicEncounterConfirm),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (!_isCurrent()) { Close(); return; }
            try
            {
                if (_deleteRecord(record.GameId)) Reload();
            }
            catch (Exception)
            {
                MessageBox.Show(this, T(LeagueEfficiencyUiTextKeys.DynamicEncounterUnavailable),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string T(string key) { return LeagueEfficiencyText.Get(_ui, key); }
    }
}
