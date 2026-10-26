using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeaguePlayerTagBrowserDialog : Form
    {
        private const int PageSize = 80;
        private readonly LeagueEfficiencyModule _module;
        private readonly UiTextCatalog _ui;
        private readonly TextBox _search;
        private readonly ListView _list;
        private readonly Label _pageLabel;
        private readonly Button _previous, _next, _edit, _delete;
        private IReadOnlyList<LeaguePlayerTagRecord> _records = new LeaguePlayerTagRecord[0];
        private int _page;

        internal LeaguePlayerTagBrowserDialog(LeagueEfficiencyModule module, UiTextCatalog ui)
        {
            _module = module; _ui = ui;
            Text = T(LeagueEfficiencyUiTextKeys.DynamicTagBrowse);
            Width = 880; Height = 490; MinimumSize = new Size(650, 360);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;
            ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Font;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12),
                ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            var filterRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filterRow.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left,
                Text = T(LeagueEfficiencyUiTextKeys.DynamicTagSearch) }, 0, 0);
            _search = new TextBox { Dock = DockStyle.Fill, MaxLength = 120,
                AccessibleName = T(LeagueEfficiencyUiTextKeys.DynamicTagSearch) };
            _search.TextChanged += (sender, e) => { _page = 0; FillPage(); };
            filterRow.Controls.Add(_search, 1, 0);
            layout.Controls.Add(filterRow, 0, 0);

            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HideSelection = false, MultiSelect = false, GridLines = true,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text };
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagTarget), 220);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagTitle), 180);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagSelf), 220);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagRegion), 110);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagUpdated), 135);
            _list.SelectedIndexChanged += (sender, e) => RefreshActions();
            _list.DoubleClick += (sender, e) => EditSelected();
            layout.Controls.Add(_list, 0, 1);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var paging = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            _previous = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagPrevious), Width = 76, Height = 30 };
            _next = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagNext), Width = 76, Height = 30 };
            _previous.Click += (sender, e) => { _page--; FillPage(); };
            _next.Click += (sender, e) => { _page++; FillPage(); };
            _pageLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left,
                Margin = new Padding(8, 8, 0, 0) };
            paging.Controls.Add(_previous); paging.Controls.Add(_next); paging.Controls.Add(_pageLabel);
            footer.Controls.Add(paging, 0, 0);
            var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagCancel), Width = 80, Height = 30 };
            close.Click += (sender, e) => Close();
            _delete = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagRemove), Width = 90, Height = 30 };
            _edit = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagEdit), Width = 90, Height = 30 };
            _delete.Click += (sender, e) => DeleteSelected();
            _edit.Click += (sender, e) => EditSelected();
            actions.Controls.Add(close); actions.Controls.Add(_delete); actions.Controls.Add(_edit);
            footer.Controls.Add(actions, 1, 0);
            layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);
            CancelButton = close;
            Reload();
        }

        private void Reload()
        {
            try { _records = _module.ReadStoredPlayerTags(); }
            catch (Exception)
            {
                MessageBox.Show(this, T(LeagueEfficiencyUiTextKeys.DynamicTagUnavailable),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _records = new LeaguePlayerTagRecord[0];
            }
            FillPage();
        }

        private void FillPage()
        {
            if (_list == null) return;
            var filter = _search.Text.Trim();
            var matches = _records.Where(p => filter.Length == 0 ||
                Contains(p.Puuid, filter) || Contains(p.SelfPuuid, filter) ||
                Contains(p.Tag, filter) || Contains(p.Region, filter) || Contains(p.PlatformId, filter)).ToList();
            var pages = Math.Max(1, (matches.Count + PageSize - 1) / PageSize);
            _page = Math.Max(0, Math.Min(_page, pages - 1));
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var record in matches.Skip(_page * PageSize).Take(PageSize))
                {
                    var item = new ListViewItem(record.Puuid) { Tag = record };
                    item.SubItems.Add(record.Tag);
                    item.SubItems.Add(record.SelfPuuid);
                    item.SubItems.Add(record.Region + " / " + record.PlatformId);
                    item.SubItems.Add(record.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                    _list.Items.Add(item);
                }
            }
            finally { _list.EndUpdate(); }
            _previous.Enabled = _page > 0;
            _next.Enabled = _page < pages - 1;
            _pageLabel.Text = T(LeagueEfficiencyUiTextKeys.DynamicTagPage)
                .Replace("{0}", (_page + 1).ToString()).Replace("{1}", pages.ToString())
                .Replace("{2}", matches.Count.ToString());
            RefreshActions();
        }

        private void RefreshActions()
        {
            var selected = _list.SelectedItems.Count == 1;
            _edit.Enabled = selected;
            _delete.Enabled = selected;
        }

        private LeaguePlayerTagRecord Selected()
        {
            return _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as LeaguePlayerTagRecord : null;
        }

        private void EditSelected()
        {
            var record = Selected();
            if (record == null) return;
            using (var editor = new Form { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagTitle),
                Width = 390, Height = 165, FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false,
                ShowInTaskbar = false, BackColor = FacmDesignSystem.Canvas, ForeColor = FacmDesignSystem.Text })
            {
                var input = new TextBox { Left = 12, Top = 16, Width = 346,
                    MaxLength = LeaguePlayerTagStore.MaximumTagLength, Text = record.Tag };
                var save = new Button { Text = T(LeagueEfficiencyUiTextKeys.AdvancedSave),
                    DialogResult = DialogResult.OK, Left = 190, Top = 58, Width = 80 };
                var cancel = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagCancel),
                    DialogResult = DialogResult.Cancel, Left = 278, Top = 58, Width = 80 };
                editor.Controls.Add(input); editor.Controls.Add(save); editor.Controls.Add(cancel);
                editor.AcceptButton = save; editor.CancelButton = cancel;
                if (editor.ShowDialog(this) == DialogResult.OK) ApplyChange(record, input.Text);
            }
        }

        private void DeleteSelected()
        {
            var record = Selected();
            if (record == null || MessageBox.Show(this, T(LeagueEfficiencyUiTextKeys.DynamicTagDeleteConfirm),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ApplyChange(record, string.Empty);
        }

        private void ApplyChange(LeaguePlayerTagRecord record, string tag)
        {
            try
            {
                if (!_module.TryEditStoredPlayerTag(record, tag))
                    MessageBox.Show(this, T(LeagueEfficiencyUiTextKeys.DynamicTagChanged),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception)
            {
                MessageBox.Show(this, T(LeagueEfficiencyUiTextKeys.DynamicTagUnavailable),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Reload();
        }

        private static bool Contains(string value, string filter)
        {
            return (value ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string T(string key) { return LeagueEfficiencyText.Get(_ui, key); }
    }
}
