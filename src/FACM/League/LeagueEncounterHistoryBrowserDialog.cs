using System;
using System.Drawing;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    // Player-scoped, on-demand local history query. No live match-detail requests or background workers.
    internal sealed class LeagueEncounterHistoryBrowserDialog : Form
    {
        private const int PageSize = 40;
        private readonly UiTextCatalog _ui;
        private readonly Func<bool> _isCurrent;
        private readonly Func<int, int, string, bool, LeagueEncounterHistoryPage> _query;
        private readonly Func<long, bool> _deleteRecord;
        private readonly TextBox _queueFilter;
        private readonly ComboBox _timeOrder;
        private readonly ListView _list;
        private readonly Label _pageLabel;
        private readonly Button _previous;
        private readonly Button _next;
        private readonly Button _delete;
        private int _page = 1;

        internal LeagueEncounterHistoryBrowserDialog(LeagueEfficiencyModule module, UiTextCatalog ui,
            LeagueDynamicMessageSnapshot snapshot, string playerPuuid, Func<bool> isCurrent)
            : this(ui, isCurrent,
                (page, size, queue, newest) => module.QueryPlayerEncounters(snapshot, playerPuuid, page, size, queue, newest),
                gameId => module.DeletePlayerEncounter(snapshot, playerPuuid, gameId))
        {
        }

        internal LeagueEncounterHistoryBrowserDialog(LeagueEfficiencyModule module, UiTextCatalog ui,
            LeagueSavedPlayerOverviewRecord savedPlayer)
            : this(ui, () => true,
                (page, size, queue, newest) => module.QueryStoredPlayerEncounters(savedPlayer, page, size, queue, newest),
                gameId => module.DeleteStoredPlayerEncounter(savedPlayer, gameId))
        {
        }

        private LeagueEncounterHistoryBrowserDialog(UiTextCatalog ui, Func<bool> isCurrent,
            Func<int, int, string, bool, LeagueEncounterHistoryPage> query, Func<long, bool> deleteRecord)
        {
            _ui = ui;
            _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _deleteRecord = deleteRecord ?? throw new ArgumentNullException(nameof(deleteRecord));
            Text = T(LeagueEfficiencyUiTextKeys.DynamicEncounterTitle);
            Width = 775; Height = 410; MinimumSize = new Size(610, 340);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12),
                ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
            _list.SelectedIndexChanged += (sender, e) => _delete.Enabled = _list.SelectedItems.Count == 1;
            layout.Controls.Add(_list, 0, 1);

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
            _delete.Click += (sender, e) => DeleteSelected();
            actions.Controls.Add(close); actions.Controls.Add(_delete);
            footer.Controls.Add(actions, 1, 0);
            layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);
            CancelButton = close;
            Reload();
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
            _delete.Enabled = false;
            _pageLabel.Text = T(LeagueEfficiencyUiTextKeys.DynamicEncounterPage)
                .Replace("{0}", _page.ToString()).Replace("{1}", pages.ToString())
                .Replace("{2}", result.Total.ToString());
        }

        private void DeleteSelected()
        {
            if (!_isCurrent()) { Close(); return; }
            if (_list.SelectedItems.Count != 1) return;
            var record = _list.SelectedItems[0].Tag as LeagueEncounterRecord;
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
