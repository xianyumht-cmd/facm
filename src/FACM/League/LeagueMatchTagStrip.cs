using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FACM.Theming;

namespace FACM.League
{
    // A bounded native equivalent of MatchCard's single-line ManyTags surface.
    internal sealed class LeagueMatchTagStrip : Panel
    {
        private readonly Func<string, string> _text;
        private readonly ToolTip _tooltip = new ToolTip();
        private readonly List<string> _tags = new List<string>();
        private ContextMenuStrip _overflow;

        internal LeagueMatchTagStrip(Func<string, string> text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            Dock = DockStyle.Fill;
            BackColor = FacmDesignSystem.CanvasRaised;
            AccessibleName = "对局表现标签";
            TabStop = false;
        }

        internal void SetTags(IEnumerable<string> tags)
        {
            _tags.Clear();
            if (tags != null) _tags.AddRange(tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Take(32));
            AccessibleDescription = string.Join(" · ", _tags);
            Rebuild();
        }

        internal static string ChipLabel(string fullText)
        {
            if (string.IsNullOrWhiteSpace(fullText)) return string.Empty;
            var detailStart = fullText.IndexOf('（');
            return detailStart < 0 ? fullText : fullText.Substring(0, detailStart);
        }

        internal static int VisibleCount(int width, IList<int> chipWidths)
        {
            if (width <= 0 || chipWidths == null || chipWidths.Count == 0) return 0;
            var total = chipWidths.Sum() + (chipWidths.Count - 1) * 4;
            if (total <= width) return chipWidths.Count;
            var used = 0;
            var visible = 0;
            foreach (var chipWidth in chipWidths)
            {
                var next = used + (visible > 0 ? 4 : 0) + chipWidth;
                if (next + 4 + 44 > width) break;
                used = next;
                visible++;
            }
            return visible;
        }

        internal static Color TagColor(string label, Func<string, string> t)
        {
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillPenta,
                LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillQuadra,
                LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillTriple,
                LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillDouble))
                return Color.FromArgb(190, 18, 60);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderDamageMatch))
                return Color.FromArgb(153, 27, 27);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderDamageTeam))
                return Color.FromArgb(185, 28, 28);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderEfficiencyMatch))
                return Color.FromArgb(77, 124, 15);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderEfficiencyTeam))
                return Color.FromArgb(101, 163, 13);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderHealMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderHealTeam))
                return Color.FromArgb(4, 120, 87);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTakenMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTakenTeam))
                return Color.FromArgb(51, 65, 85);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderShieldMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderShieldTeam))
                return Color.FromArgb(3, 105, 161);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCcMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCcTeam))
                return Color.FromArgb(162, 28, 175);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKillsMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKillsTeam))
                return Color.FromArgb(109, 40, 217);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKpMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKpTeam))
                return Color.FromArgb(14, 116, 144);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderSolo))
                return Color.FromArgb(67, 56, 202);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderGoldMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderGoldTeam,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCsAdvantage))
                return Color.FromArgb(180, 83, 9);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerTeam,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerUnder))
                return Color.FromArgb(68, 64, 60);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerDive))
                return Color.FromArgb(225, 29, 72);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCs))
                return Color.FromArgb(194, 65, 12);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKnockUp))
                return Color.FromArgb(126, 34, 206);
            return Color.FromArgb(71, 85, 105);
        }

        private static bool Matches(string label, Func<string, string> t, params string[] keys)
        {
            if (string.IsNullOrWhiteSpace(label) || t == null) return false;
            foreach (var key in keys)
            {
                var template = t(key);
                if (string.IsNullOrWhiteSpace(template)) continue;
                var marker = template.IndexOfAny(new[] { '{', '（' });
                var prefix = (marker < 0 ? template : template.Substring(0, marker)).TrimEnd();
                if (prefix.Length > 0 && label.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void Rebuild()
        {
            if (_overflow != null) { _overflow.Dispose(); _overflow = null; }
            while (Controls.Count > 0)
            {
                var child = Controls[0];
                Controls.RemoveAt(0);
                child.Dispose();
            }
            if (_tags.Count == 0 || ClientSize.Width <= 0) return;
            var widths = _tags.Select(tag => Math.Min(320,
                TextRenderer.MeasureText(ChipLabel(tag), Font,
                    new Size(int.MaxValue, 25), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 18))
                .ToList();
            var visible = VisibleCount(Math.Max(0, ClientSize.Width - 6), widths);
            var left = 3;
            for (var i = 0; i < visible; i++)
            {
                var label = ChipLabel(_tags[i]);
                var chip = new Label {
                    Text = label, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                    Bounds = new Rectangle(left, 3, widths[i], 23),
                    BackColor = TagColor(label, _text), ForeColor = Color.White,
                    AccessibleName = label, AccessibleDescription = _tags[i]
                };
                Controls.Add(chip);
                _tooltip.SetToolTip(chip, _tags[i]);
                left += widths[i] + 4;
            }
            if (visible == _tags.Count) return;
            var hidden = _tags.Skip(visible).ToList();
            var overflowButton = new Button {
                Text = "+" + Math.Min(hidden.Count, 99), Width = 44, Height = 23,
                Location = new Point(left, 3), FlatStyle = FlatStyle.Flat,
                BackColor = FacmDesignSystem.Canvas, ForeColor = FacmDesignSystem.Text,
                AccessibleName = "更多对局表现标签 +" + hidden.Count,
                AccessibleDescription = string.Join(" · ", hidden),
                TabStop = true, Cursor = Cursors.Hand
            };
            overflowButton.FlatAppearance.BorderSize = 0;
            Controls.Add(overflowButton);
            _tooltip.SetToolTip(overflowButton, string.Join("\n", hidden));
            _overflow = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false };
            foreach (var fullText in hidden)
            {
                var label = ChipLabel(fullText);
                _overflow.Items.Add(new ToolStripMenuItem(label) {
                    BackColor = TagColor(label, _text), ForeColor = Color.White,
                    ToolTipText = fullText, AutoToolTip = true
                });
            }
            overflowButton.Click += (sender, e) => _overflow.Show(overflowButton, new Point(0, overflowButton.Height));
        }

        protected override void OnResize(EventArgs eventArgs)
        {
            base.OnResize(eventArgs);
            if (_tags != null) Rebuild();
        }

        protected override void OnFontChanged(EventArgs eventArgs)
        {
            base.OnFontChanged(eventArgs);
            if (_tags != null) Rebuild();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_overflow != null) _overflow.Dispose();
                _tooltip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
