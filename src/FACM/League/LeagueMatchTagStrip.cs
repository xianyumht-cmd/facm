using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FACM.Theming;

namespace FACM.League
{
    // A bounded native equivalent of MatchCard's single-line ManyTags surface.
    internal sealed class LeagueMatchTagStrip : Panel
    {
        private const int ChipHeight = 20;
        private readonly Func<string, string> _text;
        private readonly ToolTip _tooltip = new ToolTip();
        private readonly List<string> _tags = new List<string>();
        private ContextMenuStrip _overflow;

        internal LeagueMatchTagStrip(Func<string, string> text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            Dock = DockStyle.Fill;
            BackColor = FacmDesignSystem.CanvasRaised;
            AccessibleName = _text(LeagueEfficiencyUiTextKeys.DynamicEncounterTagStrip);
            TabStop = false;
            FacmThemeRuntime.ThemeChanged += OnThemeChanged;
        }

        internal void SetTags(IEnumerable<string> tags)
        {
            _tags.Clear();
            if (tags != null) _tags.AddRange(tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Take(32));
            AccessibleDescription = string.Join(" · ", _tags);
            Rebuild(false);
        }

        internal static string ChipLabel(string fullText)
        {
            if (string.IsNullOrWhiteSpace(fullText)) return string.Empty;
            var detailStart = fullText.IndexOfAny(new[] { '（', '(' });
            return detailStart < 0 ? fullText.TrimEnd() : fullText.Substring(0, detailStart).TrimEnd();
        }

        internal static string BoundedMenuLabel(string label, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(label) || maxWidth <= 0) return string.Empty;
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var measure = new Func<string, int>(value =>
                TextRenderer.MeasureText(value, font, new Size(int.MaxValue, 25), flags).Width);
            if (measure(label) <= maxWidth) return label;
            var boundaries = StringInfo.ParseCombiningCharacters(label);
            var low = 0;
            var high = boundaries.Length;
            while (low < high)
            {
                var mid = low + (high - low + 1) / 2;
                var end = mid == boundaries.Length ? label.Length : boundaries[mid];
                if (measure(label.Substring(0, end) + "…") <= maxWidth) low = mid;
                else high = mid - 1;
            }
            var cut = low == boundaries.Length ? label.Length : boundaries[low];
            return label.Substring(0, cut) + "…";
        }

        internal static int VisibleCount(int width, IList<int> chipWidths, int overflowWidth = 44)
        {
            if (width <= 0 || chipWidths == null || chipWidths.Count == 0) return 0;
            var total = chipWidths.Sum() + (chipWidths.Count - 1) * 4;
            if (total <= width) return chipWidths.Count;
            var used = 0;
            var visible = 0;
            foreach (var chipWidth in chipWidths)
            {
                var next = used + (visible > 0 ? 4 : 0) + chipWidth;
                if (next + 4 + Math.Max(0, overflowWidth) > width) break;
                used = next;
                visible++;
            }
            return visible;
        }

        internal static int OverflowButtonWidth(int availableWidth, int desiredWidth)
        {
            return Math.Max(0, Math.Min(availableWidth, desiredWidth));
        }

        internal static Color TagColor(string label, Func<string, string> t)
        {
            return TagColor(label, t, false);
        }

        internal static Color TagColor(string label, Func<string, string> t, bool dark)
        {
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillPenta,
                LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillQuadra,
                LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillTriple,
                LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillDouble))
                return dark ? Color.FromArgb(159,18,57) : Color.FromArgb(190,18,60);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderDamageMatch))
                return dark ? Color.FromArgb(153,27,27) : Color.FromArgb(153,27,27);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderDamageTeam))
                return dark ? Color.FromArgb(185,28,28) : Color.FromArgb(185,28,28);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderEfficiencyMatch))
                return dark ? Color.FromArgb(63,98,18) : Color.FromArgb(77,124,15);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderEfficiencyTeam))
                return dark ? Color.FromArgb(77,124,15) : Color.FromArgb(101,163,13);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderHealMatch))
                return dark ? Color.FromArgb(6,95,70) : Color.FromArgb(4,120,87);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderHealTeam))
                return dark ? Color.FromArgb(4,120,87) : Color.FromArgb(5,150,105);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTakenMatch))
                return dark ? Color.FromArgb(51,65,85) : Color.FromArgb(51,65,85);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTakenTeam))
                return dark ? Color.FromArgb(71,85,105) : Color.FromArgb(71,85,105);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderShieldMatch))
                return dark ? Color.FromArgb(7,89,133) : Color.FromArgb(3,105,161);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderShieldTeam))
                return dark ? Color.FromArgb(3,105,161) : Color.FromArgb(2,132,199);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCcMatch))
                return dark ? Color.FromArgb(134,25,143) : Color.FromArgb(162,28,175);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCcTeam))
                return dark ? Color.FromArgb(162,28,175) : Color.FromArgb(192,38,211);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKillsMatch))
                return dark ? Color.FromArgb(91,33,182) : Color.FromArgb(109,40,217);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKillsTeam))
                return dark ? Color.FromArgb(109,40,217) : Color.FromArgb(124,58,237);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKpMatch))
                return dark ? Color.FromArgb(21,94,117) : Color.FromArgb(14,116,144);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKpTeam))
                return dark ? Color.FromArgb(14,116,144) : Color.FromArgb(8,145,178);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderSolo))
                return dark ? Color.FromArgb(55,48,163) : Color.FromArgb(67,56,202);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderGoldMatch))
                return dark ? Color.FromArgb(146,64,14) : Color.FromArgb(180,83,9);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderGoldTeam,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCsAdvantage))
                return dark ? Color.FromArgb(180,83,9) : Color.FromArgb(217,119,6);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerMatch))
                return dark ? Color.FromArgb(87,83,78) : Color.FromArgb(68,64,60);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerTeam))
                return dark ? Color.FromArgb(120,113,108) : Color.FromArgb(87,83,78);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerUnder))
                return dark ? Color.FromArgb(87,83,78) : Color.FromArgb(120,113,108);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerDive))
                return dark ? Color.FromArgb(190,18,60) : Color.FromArgb(225,29,72);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCs))
                return dark ? Color.FromArgb(154,52,18) : Color.FromArgb(194,65,12);
            if (Matches(label, t, LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKnockUp))
                return dark ? Color.FromArgb(107,33,168) : Color.FromArgb(126,34,206);
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

        private void Rebuild(bool restoreFocus = true)
        {
            var focusedIndex = restoreFocus ? Controls.Cast<Control>().ToList().FindIndex(control => control.Focused) : -1;
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
                    new Size(int.MaxValue, 25), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 16))
                .ToList();
            var overflowTemplate = _text(LeagueEfficiencyUiTextKeys.DynamicEncounterTagOverflow);
            var widestOverflow = overflowTemplate.Replace("{0}", Math.Min(_tags.Count, 99).ToString());
            var overflowWidth = Math.Max(44, TextRenderer.MeasureText(widestOverflow, Font,
                new Size(int.MaxValue, 25), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 16);
            var visible = VisibleCount(Math.Max(0, ClientSize.Width - 6), widths, overflowWidth);
            var left = 3;
            for (var i = 0; i < visible; i++)
            {
                var label = ChipLabel(_tags[i]);
                var fullText = _tags[i];
                var chip = new Button {
                    Text = label, TextAlign = ContentAlignment.MiddleCenter,
                    Bounds = new Rectangle(left, Math.Max(0, (ClientSize.Height - ChipHeight) / 2), widths[i], ChipHeight),
                    FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, AutoEllipsis = true,
                    BackColor = TagColor(label, _text, !FacmThemeRuntime.Current.IsLight),
                    ForeColor = Color.White, Tag = label,
                    AccessibleName = label, AccessibleDescription = fullText,
                    TabStop = true, TabIndex = i, Cursor = Cursors.Help
                };
                chip.FlatAppearance.BorderSize = 0;
                chip.FlatAppearance.MouseOverBackColor = chip.BackColor;
                chip.FlatAppearance.MouseDownBackColor = chip.BackColor;
                chip.Enter += (sender, e) => {
                    chip.FlatAppearance.BorderColor = Color.White;
                    chip.FlatAppearance.BorderSize = 1;
                    _tooltip.Show(fullText, chip, 0, chip.Height, 5000);
                };
                chip.Leave += (sender, e) => {
                    chip.FlatAppearance.BorderSize = 0;
                    _tooltip.Hide(chip);
                };
                chip.Click += (sender, e) => _tooltip.Show(fullText, chip, 0, chip.Height, 5000);
                RoundTag(chip);
                Controls.Add(chip);
                _tooltip.SetToolTip(chip, fullText);
                left += widths[i] + 4;
            }
            if (visible == _tags.Count)
            {
                RestoreFocusedChip(focusedIndex);
                return;
            }
            var hidden = _tags.Skip(visible).ToList();
            var overflowButton = new Button {
                Text = overflowTemplate.Replace("{0}", Math.Min(hidden.Count, 99).ToString()),
                Width = OverflowButtonWidth(ClientSize.Width - left - 3, overflowWidth), Height = ChipHeight,
                Location = new Point(left, Math.Max(0, (ClientSize.Height - ChipHeight) / 2)), FlatStyle = FlatStyle.Flat,
                AutoEllipsis = true,
                BackColor = FacmDesignSystem.Canvas, ForeColor = FacmDesignSystem.Text,
                AccessibleName = _text(LeagueEfficiencyUiTextKeys.DynamicEncounterTagMore)
                    .Replace("{0}", hidden.Count.ToString()),
                AccessibleDescription = string.Join(" · ", hidden),
                TabStop = true, TabIndex = visible, Cursor = Cursors.Hand
            };
            overflowButton.FlatAppearance.BorderSize = 0;
            overflowButton.Enter += (sender, e) => {
                overflowButton.FlatAppearance.BorderColor = FacmDesignSystem.Accent;
                overflowButton.FlatAppearance.BorderSize = 1;
            };
            overflowButton.Leave += (sender, e) => overflowButton.FlatAppearance.BorderSize = 0;
            RoundTag(overflowButton);
            Controls.Add(overflowButton);
            _tooltip.SetToolTip(overflowButton, string.Join("\n", hidden));
            _overflow = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false };
            foreach (var fullText in hidden)
            {
                var label = ChipLabel(fullText);
                var item = new ToolStripMenuItem(BoundedMenuLabel(label, Font, 280)) {
                    BackColor = TagColor(label, _text, !FacmThemeRuntime.Current.IsLight),
                    ForeColor = Color.White, Tag = label,
                    ToolTipText = fullText, AutoToolTip = true,
                    AccessibleName = label, AccessibleDescription = fullText
                };
                item.Click += (sender, e) => _tooltip.Show(fullText, overflowButton, 0, overflowButton.Height, 5000);
                _overflow.Items.Add(item);
            }
            overflowButton.ContextMenuStrip = _overflow;
            overflowButton.Click += (sender, e) => _overflow.Show(overflowButton, new Point(0, overflowButton.Height));
            RestoreFocusedChip(focusedIndex);
        }

        private static void RoundTag(Button button)
        {
            if (button.Width < 2 || button.Height < 2) return;
            var diameter = Math.Min(24, Math.Min(button.Width, button.Height));
            using (var path = new GraphicsPath())
            {
                path.AddArc(0, 0, diameter, diameter, 180, 90);
                path.AddArc(button.Width - diameter, 0, diameter, diameter, 270, 90);
                path.AddArc(button.Width - diameter, button.Height - diameter, diameter, diameter, 0, 90);
                path.AddArc(0, button.Height - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                button.Region = new Region(path);
            }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            BackColor = FacmDesignSystem.CanvasRaised;
            foreach (Button button in Controls.OfType<Button>())
            {
                var label = button.Tag as string;
                button.BackColor = label == null ? FacmDesignSystem.Canvas :
                    TagColor(label, _text, !FacmThemeRuntime.Current.IsLight);
                button.ForeColor = label == null ? FacmDesignSystem.Text : Color.White;
                button.FlatAppearance.MouseOverBackColor = label == null ?
                    FacmDesignSystem.SurfaceHover : button.BackColor;
                button.FlatAppearance.MouseDownBackColor = label == null ?
                    FacmDesignSystem.SurfaceHover : button.BackColor;
                button.FlatAppearance.BorderSize = button.Focused ? 1 : 0;
                if (button.Focused)
                    button.FlatAppearance.BorderColor = label == null ? FacmDesignSystem.Accent : Color.White;
            }
            if (_overflow == null) return;
            foreach (ToolStripMenuItem item in _overflow.Items.OfType<ToolStripMenuItem>())
            {
                item.BackColor = TagColor(item.Tag as string, _text, !FacmThemeRuntime.Current.IsLight);
                item.ForeColor = Color.White;
            }
        }

        private void RestoreFocusedChip(int focusedIndex)
        {
            if (focusedIndex >= 0 && Controls.Count > 0)
                Controls[Math.Min(focusedIndex, Controls.Count - 1)].Focus();
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
                FacmThemeRuntime.ThemeChanged -= OnThemeChanged;
                if (_overflow != null) _overflow.Dispose();
                _tooltip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
