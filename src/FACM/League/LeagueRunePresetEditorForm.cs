using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FACM.Theming;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueRunePresetEditorForm : Form
    {
        private readonly LeagueRuneCatalog _catalog;
        private readonly UiTextCatalog _ui;
        private readonly int _championId;
        private readonly string _group;
        private readonly LeagueChampionRunePreset _original;
        private readonly ComboBox _primary = new ComboBox();
        private readonly ComboBox _secondary = new ComboBox();
        private readonly ComboBox[] _perks = new ComboBox[9];
        private readonly Button _save = new Button();
        private bool _updating;

        internal LeagueChampionRunePreset Preset { get; private set; }

        internal LeagueRunePresetEditorForm(LeagueRuneCatalog catalog, int championId,
            string championName, string group, LeagueChampionRunePreset original, UiTextCatalog ui)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _championId = championId;
            _group = group;
            _original = original;
            Text = T(LeagueChampionSpellPresetUiTextKeys.EditorTitle) + (string.IsNullOrWhiteSpace(championName) ? championId.ToString() : championName);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(660, 590);
            MinimumSize = new Size(580, 480);
            Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2,
                RowCount = 12, Padding = new Padding(18, 12, 18, 12)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var labels = new[]
            {
                T(LeagueChampionSpellPresetUiTextKeys.PrimaryStyle),
                T(LeagueChampionSpellPresetUiTextKeys.SecondaryStyle),
                T(LeagueChampionSpellPresetUiTextKeys.Keystone),
                T(LeagueChampionSpellPresetUiTextKeys.PrimaryRow1),
                T(LeagueChampionSpellPresetUiTextKeys.PrimaryRow2),
                T(LeagueChampionSpellPresetUiTextKeys.PrimaryRow3),
                T(LeagueChampionSpellPresetUiTextKeys.SecondaryRow1),
                T(LeagueChampionSpellPresetUiTextKeys.SecondaryRow2),
                T(LeagueChampionSpellPresetUiTextKeys.StatRow1),
                T(LeagueChampionSpellPresetUiTextKeys.StatRow2),
                T(LeagueChampionSpellPresetUiTextKeys.StatRow3)
            };
            AddRow(table, 0, labels[0], _primary);
            AddRow(table, 1, labels[1], _secondary);
            for (var i = 0; i < _perks.Length; i++)
            {
                _perks[i] = new ComboBox();
                AddRow(table, i + 2, labels[i + 2], _perks[i]);
                _perks[i].SelectedIndexChanged += delegate { UpdateSave(); };
            }
            var note = new Label
            {
                Text = T(LeagueChampionSpellPresetUiTextKeys.EditorHint),
                AutoSize = true, ForeColor = FacmDesignSystem.TextMuted, Margin = new Padding(4, 10, 0, 8)
            };
            table.Controls.Add(note, 0, 11);
            table.SetColumnSpan(note, 2);
            table.RowStyles.Clear();
            for (var i = 0; i < 11; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft,
                Height = 55, Padding = new Padding(10), BackColor = FacmDesignSystem.Surface
            };
            _save.Text = T(LeagueChampionSpellPresetUiTextKeys.EditorSave);
            _save.Width = 110;
            _save.Height = 32;
            _save.Click += delegate
            {
                var preset = BuildPreset();
                if (!_catalog.Valid(preset)) return;
                Preset = preset;
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancel = new Button { Text = T(LeagueChampionSpellPresetUiTextKeys.EditorCancel), Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
            actions.Controls.Add(_save);
            actions.Controls.Add(cancel);
            Controls.Add(table);
            Controls.Add(actions);
            AcceptButton = _save;
            CancelButton = cancel;

            _primary.Items.AddRange(_catalog.Styles.Cast<object>().ToArray());
            _primary.SelectedIndexChanged += delegate { RefreshSecondary(); };
            _secondary.SelectedIndexChanged += delegate { RefreshPerks(); };
            SelectStyle(_primary, original == null ? 0 : original.PrimaryStyleId);
            RefreshSecondary();
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, ComboBox combo)
        {
            var caption = new Label
            {
                Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = FacmDesignSystem.Text
            };
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Dock = DockStyle.Fill;
            combo.Margin = new Padding(2, 4, 6, 4);
            table.Controls.Add(caption, 0, row);
            table.Controls.Add(combo, 1, row);
        }

        private static void SelectStyle(ComboBox combo, int id)
        {
            var index = -1;
            for (var i = 0; i < combo.Items.Count; i++)
            {
                var style = combo.Items[i] as LeagueRuneStyle;
                if (style != null && style.Id == id) { index = i; break; }
            }
            combo.SelectedIndex = index < 0 && combo.Items.Count > 0 ? 0 : index;
        }

        private static int Id(ComboBox combo)
        {
            var style = combo.SelectedItem as LeagueRuneStyle;
            if (style != null) return style.Id;
            var perk = combo.SelectedItem as LeagueRuneOption;
            return perk == null ? 0 : perk.Id;
        }

        private static void Populate(ComboBox combo, IEnumerable<LeagueRuneOption> choices, int preferred)
        {
            var items = choices.ToArray();
            combo.Items.Clear();
            combo.Items.AddRange(items.Cast<object>().ToArray());
            var index = Array.FindIndex(items, item => item.Id == preferred);
            combo.SelectedIndex = index < 0 && items.Length > 0 ? 0 : index;
        }

        private void RefreshSecondary()
        {
            if (_updating) return;
            _updating = true;
            var primary = _catalog.Style(Id(_primary));
            var old = Id(_secondary);
            _secondary.Items.Clear();
            if (primary != null)
                _secondary.Items.AddRange(_catalog.Styles.Where(style =>
                    style.Id != primary.Id && primary.AllowedSubStyles.Contains(style.Id))
                    .Cast<object>().ToArray());
            SelectStyle(_secondary, old > 0 ? old : _original == null ? 0 : _original.SecondaryStyleId);
            _updating = false;
            RefreshPerks();
        }

        private void RefreshPerks()
        {
            if (_updating) return;
            _updating = true;
            var primary = _catalog.Style(Id(_primary));
            var secondary = _catalog.Style(Id(_secondary));
            var previous = _perks.Select(Id).ToArray();
            for (var i = 0; i < 4; i++)
                Populate(_perks[i], primary == null ? Enumerable.Empty<LeagueRuneOption>() :
                    _catalog.Choices(primary.Slots[i]), Preferred(i, previous[i]));
            var secondaryOptions = secondary == null ? new List<LeagueRuneOption>() :
                _catalog.Choices(secondary.Slots.Skip(1).Take(3).SelectMany(row => row));
            for (var i = 4; i < 6; i++)
                Populate(_perks[i], secondaryOptions, Preferred(i, previous[i]));
            for (var i = 6; i < 9; i++)
                Populate(_perks[i], _catalog.StatChoices(Id(_primary), i - 6), Preferred(i, previous[i]));
            if (secondary != null && _perks[5].Items.Count > 0 &&
                Id(_perks[4]) == Id(_perks[5]))
            {
                for (var i = 0; i < _perks[5].Items.Count; i++)
                {
                    var option = (LeagueRuneOption)_perks[5].Items[i];
                    var candidate = BuildPreset();
                    if (candidate == null) break;
                    candidate.SelectedPerkIds[5] = option.Id;
                    if (_catalog.Valid(candidate)) { _perks[5].SelectedIndex = i; break; }
                }
            }
            _updating = false;
            UpdateSave();
        }

        private int Preferred(int index, int previous)
        {
            if (previous > 0) return previous;
            return _original != null && _original.SelectedPerkIds != null &&
                   _original.SelectedPerkIds.Count == 9 ? _original.SelectedPerkIds[index] : 0;
        }

        private LeagueChampionRunePreset BuildPreset()
        {
            return new LeagueChampionRunePreset
            {
                ChampionId = _championId, Group = _group,
                PrimaryStyleId = Id(_primary), SecondaryStyleId = Id(_secondary),
                SelectedPerkIds = _perks.Select(Id).ToList()
            };
        }

        private void UpdateSave()
        {
            if (!_updating) _save.Enabled = _catalog.Valid(BuildPreset());
        }

        private string T(string key)
        {
            return LeagueAdvisorText.Get(_ui, key);
        }
    }
}
