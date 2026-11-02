using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    // Local preference editor. It never writes to LCU; the existing phase-scoped
    // auto-apply owner remains responsible for applying the saved configuration.
    internal sealed class LeagueRunePresetManagerForm : Form
    {
        private readonly LeagueRuneCatalog _catalog;
        private readonly LeagueAutoApplyController _controller;
        private readonly UiTextCatalog _ui;
        private readonly ComboBox _champions = new ComboBox();
        private readonly ComboBox _groups = new ComboBox();
        private readonly ListView _saved = new ListView();
        private readonly Button _edit = new Button();
        private readonly Button _delete = new Button();
        private readonly Button _editSpells = new Button();
        private readonly Button _deleteSpells = new Button();
        private readonly Button _importAkari = new Button();
        private readonly Button _exportAkari = new Button();
        private readonly IReadOnlyList<LeagueRuneOption> _spellOptions;
        private readonly IReadOnlyList<LeagueRuneOption> _championOptions;

        internal LeagueRunePresetManagerForm(LeagueRuneCatalog catalog,
            IReadOnlyList<LeagueRuneOption> champions, IReadOnlyList<LeagueRuneOption> spells,
            LeagueAutoApplyController controller, UiTextCatalog ui, int selectedChampion, string selectedGroup)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _championOptions = champions ?? throw new ArgumentNullException(nameof(champions));
            _spellOptions = spells ?? throw new ArgumentNullException(nameof(spells));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            Text = T(LeagueChampionSpellPresetUiTextKeys.ManagerTitle);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 520);
            MinimumSize = new Size(650, 450);
            Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 100, ColumnCount = 2,
                Padding = new Padding(18, 12, 18, 6)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _champions.DropDownStyle = ComboBoxStyle.DropDownList;
            _champions.Dock = DockStyle.Fill;
            _champions.Items.AddRange(_championOptions.Cast<object>().ToArray());
            _groups.DropDownStyle = ComboBoxStyle.DropDownList;
            _groups.Dock = DockStyle.Fill;
            _groups.Items.AddRange(LeagueChampionSpellPresets.KnownGroups.Cast<object>().ToArray());
            AddRow(header, 0, T(LeagueChampionSpellPresetUiTextKeys.ManagerChampion), _champions);
            AddRow(header, 1, T(LeagueChampionSpellPresetUiTextKeys.ManagerGroup), _groups);
            Controls.Add(header);

            _saved.Dock = DockStyle.Fill;
            _saved.View = View.Details;
            _saved.FullRowSelect = true;
            _saved.MultiSelect = false;
            _saved.HideSelection = false;
            _saved.Columns.Add(T(LeagueChampionSpellPresetUiTextKeys.ManagerChampion), 210);
            _saved.Columns.Add(T(LeagueChampionSpellPresetUiTextKeys.ManagerGroup), 165);
            _saved.Columns.Add(T(LeagueChampionSpellPresetUiTextKeys.ManagerRunes), 120);
            _saved.Columns.Add(T(LeagueChampionSpellPresetUiTextKeys.ManagerSpells), 120);
            _saved.SelectedIndexChanged += delegate { SelectSaved(); };
            Controls.Add(_saved);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 90,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10), BackColor = FacmDesignSystem.Surface
            };
            var close = new Button
            {
                Text = T(LeagueChampionSpellPresetUiTextKeys.EditorCancel),
                Width = 100, Height = 32, DialogResult = DialogResult.Cancel
            };
            _edit.Text = T(LeagueChampionSpellPresetUiTextKeys.ManagerEdit);
            _edit.Width = 115;
            _edit.Height = 32;
            _edit.Click += delegate { EditSelected(); };
            _delete.Text = T(LeagueChampionSpellPresetUiTextKeys.ManagerDelete);
            _delete.Width = 115;
            _delete.Height = 32;
            _delete.Click += delegate { DeleteSelected(); };
            _editSpells.Text = T(LeagueChampionSpellPresetUiTextKeys.ManagerEditSpells);
            _editSpells.Width = 115;
            _editSpells.Height = 32;
            _editSpells.Click += delegate { EditSpells(); };
            _deleteSpells.Text = T(LeagueChampionSpellPresetUiTextKeys.ManagerDeleteSpells);
            _deleteSpells.Width = 115;
            _deleteSpells.Height = 32;
            _deleteSpells.Click += delegate { DeleteSpells(); };
            _importAkari.Text = T(LeagueChampionSpellPresetUiTextKeys.ManagerImportAkari);
            _importAkari.Width = 105;
            _importAkari.Height = 32;
            _importAkari.Click += delegate { ImportAkari(); };
            _exportAkari.Text = T(LeagueChampionSpellPresetUiTextKeys.ManagerExportAkari);
            _exportAkari.Width = 105;
            _exportAkari.Height = 32;
            _exportAkari.Click += delegate { ExportAkari(); };
            buttons.Controls.Add(close);
            buttons.Controls.Add(_exportAkari);
            buttons.Controls.Add(_importAkari);
            buttons.Controls.Add(_deleteSpells);
            buttons.Controls.Add(_delete);
            buttons.Controls.Add(_editSpells);
            buttons.Controls.Add(_edit);
            Controls.Add(buttons);
            // Restore docking order: header top, buttons bottom, saved fills remaining space.
            Controls.SetChildIndex(header, 0);
            Controls.SetChildIndex(buttons, 1);
            Controls.SetChildIndex(_saved, 2);
            CancelButton = close;

            SelectChampion(selectedChampion);
            _groups.SelectedItem = LeagueChampionSpellPresets.ValidGroup(selectedGroup)
                ? selectedGroup : "ranked-default";
            if (_groups.SelectedIndex < 0 && _groups.Items.Count > 0) _groups.SelectedIndex = 0;
            _champions.SelectedIndexChanged += delegate { UpdateButtons(); };
            _groups.SelectedIndexChanged += delegate { UpdateButtons(); };
            RefreshSaved();
        }

        private static void AddRow(TableLayoutPanel table, int row, string text, ComboBox combo)
        {
            table.Controls.Add(new Label
            {
                Text = text, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = FacmDesignSystem.Text
            }, 0, row);
            table.Controls.Add(combo, 1, row);
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        }

        private void SelectChampion(int id)
        {
            var index = -1;
            for (var i = 0; i < _champions.Items.Count; i++)
            {
                if (((LeagueRuneOption)_champions.Items[i]).Id == id) { index = i; break; }
            }
            _champions.SelectedIndex = index >= 0 ? index : _champions.Items.Count > 0 ? 0 : -1;
        }

        private void SelectSaved()
        {
            if (_saved.SelectedItems.Count == 0) return;
            var row = _saved.SelectedItems[0].Tag as Tuple<int, string>;
            if (row == null) return;
            SelectChampion(row.Item1);
            _groups.SelectedItem = row.Item2;
            UpdateButtons();
        }

        private void RefreshSaved()
        {
            _saved.BeginUpdate();
            try
            {
                _saved.Items.Clear();
                var runes = _controller.ListRunePresets();
                var spells = _controller.ListSpellPresets();
                var keys = runes.Select(row => Tuple.Create(row.ChampionId, row.Group))
                    .Concat(spells.Select(row => Tuple.Create(row.ChampionId, row.Group)))
                    .Distinct().OrderBy(row => row.Item1).ThenBy(row => row.Item2, StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    var champion = _championOptions.FirstOrDefault(option => option.Id == key.Item1);
                    var name = champion == null ? key.Item1.ToString() : champion.Name;
                    var item = new ListViewItem(name);
                    item.SubItems.Add(key.Item2);
                    item.SubItems.Add(runes.Any(row => row.ChampionId == key.Item1 && row.Group == key.Item2) ? "✓" : "—");
                    item.SubItems.Add(spells.Any(row => row.ChampionId == key.Item1 && row.Group == key.Item2) ? "✓" : "—");
                    item.Tag = key;
                    _saved.Items.Add(item);
                }
            }
            finally { _saved.EndUpdate(); }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _edit.Enabled = _champions.SelectedItem is LeagueRuneOption &&
                            LeagueChampionSpellPresets.ValidGroup(_groups.SelectedItem as string);
            _delete.Enabled = _edit.Enabled && CurrentPreset() != null;
            _editSpells.Enabled = _edit.Enabled && _spellOptions.Count(
                option => option.SupportsGroup(_groups.SelectedItem as string)) >= 2;
            _deleteSpells.Enabled = _edit.Enabled && CurrentSpellPreset() != null;
        }

        private LeagueChampionRunePreset CurrentPreset()
        {
            var champion = _champions.SelectedItem as LeagueRuneOption;
            var group = _groups.SelectedItem as string;
            return champion == null ? null : _controller.ListRunePresets().FirstOrDefault(
                preset => preset.ChampionId == champion.Id && preset.Group == group);
        }

        private LeagueChampionSpellPreset CurrentSpellPreset()
        {
            var champion = _champions.SelectedItem as LeagueRuneOption;
            var group = _groups.SelectedItem as string;
            return champion == null ? null : _controller.ListSpellPresets().FirstOrDefault(
                preset => preset.ChampionId == champion.Id && preset.Group == group);
        }

        private void EditSpells()
        {
            var champion = _champions.SelectedItem as LeagueRuneOption;
            var group = _groups.SelectedItem as string;
            if (champion == null || !LeagueChampionSpellPresets.ValidGroup(group)) return;
            using (var editor = new LeagueSummonerSpellPresetEditorForm(
                _spellOptions, champion.Id, champion.Name, group, CurrentSpellPreset(), _ui))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _controller.SaveSpellPresetByKey(editor.Preset);
                    RefreshSaved();
                }
                catch (Exception error)
                {
                    AppLog.Error("League spell preset save failed", error);
                    MessageBox.Show(this, T(LeagueChampionSpellPresetUiTextKeys.SaveFailed),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void DeleteSpells()
        {
            var champion = _champions.SelectedItem as LeagueRuneOption;
            var group = _groups.SelectedItem as string;
            if (champion == null || CurrentSpellPreset() == null) return;
            if (MessageBox.Show(this, T(LeagueChampionSpellPresetUiTextKeys.ManagerDeleteSpellsConfirm),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                _controller.RemoveSpellPresetByKey(champion.Id, group);
                RefreshSaved();
            }
            catch (Exception error)
            {
                AppLog.Error("League spell preset removal failed", error);
                MessageBox.Show(this, T(LeagueChampionSpellPresetUiTextKeys.SaveFailed),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void EditSelected()
        {
            var champion = _champions.SelectedItem as LeagueRuneOption;
            var group = _groups.SelectedItem as string;
            if (champion == null || !LeagueChampionSpellPresets.ValidGroup(group)) return;
            using (var editor = new LeagueRunePresetEditorForm(
                _catalog, champion.Id, champion.Name, group, CurrentPreset(), _ui))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _controller.SaveRunePresetByKey(editor.Preset);
                    RefreshSaved();
                }
                catch (Exception error)
                {
                    AppLog.Error("League rune preset save failed", error);
                    MessageBox.Show(this, T(LeagueChampionSpellPresetUiTextKeys.SaveFailed),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void DeleteSelected()
        {
            var champion = _champions.SelectedItem as LeagueRuneOption;
            var group = _groups.SelectedItem as string;
            if (champion == null || CurrentPreset() == null) return;
            if (MessageBox.Show(this, T(LeagueChampionSpellPresetUiTextKeys.ManagerDeleteConfirm),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                _controller.RemoveRunePresetByKey(champion.Id, group);
                RefreshSaved();
            }
            catch (Exception error)
            {
                AppLog.Error("League rune preset removal failed", error);
                MessageBox.Show(this, T(LeagueChampionSpellPresetUiTextKeys.SaveFailed),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ImportAkari()
        {
            try
            {
                if (!Clipboard.ContainsText(TextDataFormat.UnicodeText)) return;
                var bundle = LeagueAkariChampionPresetTransfer.Import(
                    Clipboard.GetText(TextDataFormat.UnicodeText));
                if (MessageBox.Show(this, T(LeagueChampionSpellPresetUiTextKeys.ManagerImportConfirm) +
                    " (" + bundle.RuneCount + " / " + bundle.SpellCount + ")",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                _controller.ImportAkariChampionPresets(bundle);
                RefreshSaved();
                MessageBox.Show(this, T(LeagueChampionSpellPresetUiTextKeys.ManagerImportSuccess),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                AppLog.Error("League champion preset clipboard import failed", error);
                MessageBox.Show(this, T(LeagueChampionSpellPresetUiTextKeys.ManagerTransferFailed),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExportAkari()
        {
            try
            {
                Clipboard.SetText(_controller.ExportAkariChampionPresets(), TextDataFormat.UnicodeText);
                MessageBox.Show(this, T(LeagueChampionSpellPresetUiTextKeys.ManagerExportSuccess),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                AppLog.Error("League champion preset clipboard export failed", error);
                MessageBox.Show(this, T(LeagueChampionSpellPresetUiTextKeys.ManagerTransferFailed),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string T(string key) { return LeagueAdvisorText.Get(_ui, key); }
    }
}
