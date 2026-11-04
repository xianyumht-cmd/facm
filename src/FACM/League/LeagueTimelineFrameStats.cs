using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueTimelineFrameStats : UserControl
    {
        private static readonly string[][] Stats =
        {
            new[] { "health", UiTextKeys.LeagueFrameHealth, "healthMax" },
            new[] { "healthRegen", UiTextKeys.LeagueFrameHealthRegen },
            new[] { "power", UiTextKeys.LeagueFramePower, "powerMax" },
            new[] { "powerRegen", UiTextKeys.LeagueFramePowerRegen },
            new[] { "attackDamage", UiTextKeys.LeagueFrameAttackDamage },
            new[] { "attackSpeed", UiTextKeys.LeagueFrameAttackSpeed, "%" },
            new[] { "abilityPower", UiTextKeys.LeagueFrameAbilityPower },
            new[] { "abilityHaste", UiTextKeys.LeagueFrameAbilityHaste },
            new[] { "cooldownReduction", UiTextKeys.LeagueFrameCooldown, "%" },
            new[] { "armor", UiTextKeys.LeagueFrameArmor },
            new[] { "magicResist", UiTextKeys.LeagueFrameMagicResist },
            new[] { "armorPen", UiTextKeys.LeagueFrameArmorPen },
            new[] { "armorPenPercent", UiTextKeys.LeagueFrameArmorPenPercent, "%" },
            new[] { "bonusArmorPenPercent", UiTextKeys.LeagueFrameBonusArmorPen, "%" },
            new[] { "magicPen", UiTextKeys.LeagueFrameMagicPen },
            new[] { "magicPenPercent", UiTextKeys.LeagueFrameMagicPenPercent, "%" },
            new[] { "bonusMagicPenPercent", UiTextKeys.LeagueFrameBonusMagicPen, "%" },
            new[] { "movementSpeed", UiTextKeys.LeagueFrameMoveSpeed },
            new[] { "lifesteal", UiTextKeys.LeagueFrameLifesteal, "%" },
            new[] { "physicalVamp", UiTextKeys.LeagueFramePhysicalVamp, "%" },
            new[] { "spellVamp", UiTextKeys.LeagueFrameSpellVamp, "%" },
            new[] { "omnivamp", UiTextKeys.LeagueFrameOmnivamp, "%" },
            new[] { "ccReduction", UiTextKeys.LeagueFrameCcReduction, "%" }
        };
        private readonly TrackBar _time;
        private readonly Label _status;
        private readonly ListView _values;
        private readonly UiTextCatalog _ui;
        private LeagueMatchTimeline _timeline;
        private int _participant;
        private bool _updating;

        internal LeagueTimelineFrameStats(UiTextCatalog ui)
        {
            _ui = ui;
            Dock = DockStyle.Fill;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _time = new TrackBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 0, TickStyle = TickStyle.None,
                AccessibleName = ui.Get(UiTextKeys.LeagueTimelineTime) };
            _time.ValueChanged += (sender, args) => { if (!_updating) RefreshStats(); };
            _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            _values = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                BackColor = FacmDesignSystem.SurfaceRaised, ForeColor = FacmDesignSystem.Text };
            _values.Columns.Add(ui.Get(UiTextKeys.LeagueTimelineMetric), 210);
            _values.Columns.Add(ui.Get(UiTextKeys.LeagueFrameValue), 200);
            layout.Controls.Add(_time, 0, 0); layout.Controls.Add(_status, 0, 1); layout.Controls.Add(_values, 0, 2);
            Controls.Add(layout);
        }

        internal void SetData(LeagueMatchTimeline timeline, int participantId)
        {
            var changed = !ReferenceEquals(timeline, _timeline);
            _timeline = timeline; _participant = participantId;
            _updating = true;
            _time.Maximum = Math.Max(0, timeline == null ? 0 : timeline.Frames.Count - 1);
            if (changed) _time.Value = 0;
            _time.Enabled = _time.Maximum > 0;
            _updating = false;
            RefreshStats();
        }

        private void RefreshStats()
        {
            _values.BeginUpdate();
            try
            {
                _values.Items.Clear();
                Dictionary<string, object> participant = null;
                if (_timeline != null && _timeline.Frames.Count > _time.Value)
                    _timeline.Frames[_time.Value].Participants.TryGetValue(_participant, out participant);
                object value;
                var stats = participant != null && participant.TryGetValue("championStats", out value)
                    ? value as Dictionary<string, object> : null;
                if (stats == null)
                {
                    _status.Text = _ui.Get(UiTextKeys.LeagueFrameUnavailable);
                    return;
                }
                _status.Text = LeagueMatchTimelineParser.Time(_timeline.Frames[_time.Value].Timestamp);
                foreach (var stat in Stats)
                {
                    var text = Format(stats, stat[0]);
                    if (stat.Length > 2)
                        text = stat[2] == "%" ? text == "—" ? text : text + "%" : text + " / " + Format(stats, stat[2]);
                    var row = new ListViewItem(_ui.Get(stat[1]));
                    row.SubItems.Add(text);
                    _values.Items.Add(row);
                }
            }
            finally { _values.EndUpdate(); }
        }

        internal static string Format(Dictionary<string, object> data, string key)
        {
            object value;
            double number;
            if (data == null || !data.TryGetValue(key, out value) || value == null ||
                !double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out number) || double.IsNaN(number) || double.IsInfinity(number)) return "—";
            return number.ToString(Math.Abs(number) < 1000000000 ? "0.##" : "G6", CultureInfo.InvariantCulture);
        }
    }
}
