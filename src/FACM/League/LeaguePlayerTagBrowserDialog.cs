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
        private readonly ComboBox _viewMode;
        private readonly TextBox _search;
        private readonly ListView _list;
        private readonly Label _pageLabel;
        private readonly Button _previous, _next, _edit, _delete;
        private IReadOnlyList<LeaguePlayerTagRecord> _records = new LeaguePlayerTagRecord[0];
        private IReadOnlyList<LeagueSavedPlayerOverviewRecord> _overview = new LeagueSavedPlayerOverviewRecord[0];
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
            var filterRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _viewMode = new ComboBox { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = T(LeagueEfficiencyUiTextKeys.DynamicSavedPlayerAll) };
            _viewMode.Items.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagBrowse));
            _viewMode.Items.Add(T(LeagueEfficiencyUiTextKeys.DynamicSavedPlayerAll));
            _viewMode.SelectedIndex = 0;
            _viewMode.SelectedIndexChanged += (sender, e) => { _page = 0; _search.Clear(); Reload(); };
            filterRow.Controls.Add(_viewMode, 0, 0);
            filterRow.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left,
                Text = T(LeagueEfficiencyUiTextKeys.DynamicTagSearch) }, 1, 0);
            _search = new TextBox { Dock = DockStyle.Fill, MaxLength = 120,
                AccessibleName = T(LeagueEfficiencyUiTextKeys.DynamicTagSearch) };
            _search.TextChanged += (sender, e) => { _page = 0; FillPage(); };
            filterRow.Controls.Add(_search, 2, 0);
            layout.Controls.Add(filterRow, 0, 0);

            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HideSelection = false, MultiSelect = false, GridLines = true,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text };
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagTarget), 220);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagTitle), 180);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagSelf), 220);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagRegion), 110);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagUpdated), 135);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicSavedPlayerMetCount), 95);
            _list.Columns.Add(T(LeagueEfficiencyUiTextKeys.DynamicSavedPlayerLastMet), 135);
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
            Text = T(_viewMode.SelectedIndex == 1 ? LeagueEfficiencyUiTextKeys.DynamicSavedPlayerAll :
                LeagueEfficiencyUiTextKeys.DynamicTagBrowse);
            try { _records = _module.ReadStoredPlayerTags(); }
            catch (Exception)
            {
                MessageBox.Show(this, T(LeagueEfficiencyUiTextKeys.DynamicTagUnavailable),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _records = new LeaguePlayerTagRecord[0];
            }
            if (_viewMode.SelectedIndex == 1)
            {
                try { _overview = _module.ReadSavedPlayerOverview(); }
                catch (Exception)
                {
                    MessageBox.Show(this, T(LeagueEfficiencyUiTextKeys.DynamicEncounterUnavailable),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _overview = new LeagueSavedPlayerOverviewRecord[0];
                }
            }
            FillPage();
        }

        private void FillPage()
        {
            if (_list == null) return;
            var filter = _search.Text.Trim();
            var matches = _viewMode.SelectedIndex == 1
                ? _overview.Where(p => Matches(p.Puuid, p.SelfPuuid, p.Tag, p.Region, p.PlatformId, filter)).ToList()
                : _records.Where(p => Matches(p.Puuid, p.SelfPuuid, p.Tag, p.Region, p.PlatformId, filter))
                    .Select(p => new LeagueSavedPlayerOverviewRecord
                    {
                        Puuid = p.Puuid, SelfPuuid = p.SelfPuuid, Region = p.Region,
                        PlatformId = p.PlatformId, Tag = p.Tag, UpdatedUtc = p.UpdatedUtc
                    }).ToList();
            _list.Columns[5].Width = _viewMode.SelectedIndex == 1 ? 95 : 0;
            _list.Columns[6].Width = _viewMode.SelectedIndex == 1 ? 135 : 0;
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
                    item.SubItems.Add(record.EncounterCount > 0 ? record.EncounterCount.ToString() : "");
                    item.SubItems.Add(record.LastMetUtc.HasValue
                        ? record.LastMetUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "");
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
            var selected = Selected() != null;
            _edit.Enabled = selected;
            _delete.Enabled = selected;
        }

        private LeaguePlayerTagRecord Selected()
        {
            if (_list.SelectedItems.Count != 1) return null;
            var selected = _list.SelectedItems[0].Tag as LeagueSavedPlayerOverviewRecord;
            if (selected == null || string.IsNullOrWhiteSpace(selected.Tag)) return null;
            return _records.FirstOrDefault(p => p.Puuid == selected.Puuid && p.SelfPuuid == selected.SelfPuuid &&
                string.Equals(p.Region, selected.Region, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.PlatformId, selected.PlatformId, StringComparison.OrdinalIgnoreCase));
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

        private static bool Matches(string puuid, string self, string tag, string region, string platform, string filter)
        {
            return filter.Length == 0 || Contains(puuid, filter) || Contains(self, filter) ||
                Contains(tag, filter) || Contains(region, filter) || Contains(platform, filter);
        }

        private string T(string key) { return LeagueEfficiencyText.Get(_ui, key); }
    }
}
