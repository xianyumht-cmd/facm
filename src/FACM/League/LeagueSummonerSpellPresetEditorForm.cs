using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueSummonerSpellPresetEditorForm : Form
    {
        private readonly ComboBox _first = new ComboBox();
        private readonly ComboBox _second = new ComboBox();
        private readonly Button _save = new Button();
        private readonly int _championId;
        private readonly string _group;
        internal LeagueChampionSpellPreset Preset { get; private set; }

        internal LeagueSummonerSpellPresetEditorForm(IReadOnlyList<LeagueRuneOption> options,
            int championId, string championName, string group,
            LeagueChampionSpellPreset existing, UiTextCatalog ui)
        {
            if (options == null || options.Count < 2) throw new ArgumentException("Spell catalog unavailable.");
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            _championId = championId;
            _group = group;
            Text = LeagueAdvisorText.Get(ui, LeagueChampionSpellPresetUiTextKeys.SpellEditorTitle) +
                   (string.IsNullOrWhiteSpace(championName) ? championId.ToString() : championName);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(490, 185);
            MinimumSize = new Size(450, 185);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 106, ColumnCount = 2,
                Padding = new Padding(18, 12, 18, 6)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(table, 0, LeagueAdvisorText.Get(ui, LeagueChampionSpellPresetUiTextKeys.SpellSlot1), _first);
            AddRow(table, 1, LeagueAdvisorText.Get(ui, LeagueChampionSpellPresetUiTextKeys.SpellSlot2), _second);
            _first.Items.AddRange(options.Cast<object>().ToArray());
            _second.Items.AddRange(options.Cast<object>().ToArray());
            Select(_first, existing == null ? 0 : existing.Spell1Id);
            Select(_second, existing == null ? 0 : existing.Spell2Id);
            _first.SelectedIndexChanged += delegate { UpdateSave(); };
            _second.SelectedIndexChanged += delegate { UpdateSave(); };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 53, Padding = new Padding(9),
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = FacmDesignSystem.Surface
            };
            _save.Text = LeagueAdvisorText.Get(ui, LeagueChampionSpellPresetUiTextKeys.SpellEditorSave);
            _save.Width = 105;
            _save.Height = 32;
            _save.Click += delegate
            {
                if (!Valid()) return;
                Preset = new LeagueChampionSpellPreset
                {
                    ChampionId = _championId, Group = _group,
                    Spell1Id = Id(_first), Spell2Id = Id(_second)
                };
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancel = new Button
            {
                Text = LeagueAdvisorText.Get(ui, LeagueChampionSpellPresetUiTextKeys.EditorCancel),
                Width = 90, Height = 32, DialogResult = DialogResult.Cancel
            };
            buttons.Controls.Add(_save);
            buttons.Controls.Add(cancel);
            Controls.Add(table);
            Controls.Add(buttons);
            AcceptButton = _save;
            CancelButton = cancel;
            UpdateSave();
        }

        private static void AddRow(TableLayoutPanel table, int row, string text, ComboBox combo)
        {
            table.Controls.Add(new Label
            {
                Text = text, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = FacmDesignSystem.Text
            }, 0, row);
            combo.Dock = DockStyle.Fill;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            table.Controls.Add(combo, 1, row);
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        }

        private static int Id(ComboBox combo)
        {
            var option = combo.SelectedItem as LeagueRuneOption;
            return option == null ? 0 : option.Id;
        }

        private static void Select(ComboBox combo, int id)
        {
            var index = -1;
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (((LeagueRuneOption)combo.Items[i]).Id == id) { index = i; break; }
            }
            combo.SelectedIndex = index >= 0 ? index : combo.Items.Count > 0 ? 0 : -1;
        }

        private bool Valid() { return Id(_first) > 0 && Id(_second) > 0 && Id(_first) != Id(_second); }
        private void UpdateSave() { _save.Enabled = Valid(); }
    }
}
