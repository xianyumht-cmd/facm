using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PoisonButton = ReaLTaiizor.Controls.PoisonButton;
using PoisonPanel = ReaLTaiizor.Controls.PoisonPanel;
using ReaLTaiizor.Enum.Poison;

namespace FACM.League
{
    internal sealed class GameRepairView : UserControl
    {
        private sealed class TaskCard
        {
            internal Panel Surface;
            internal Label Heading;
            internal Label Hint;
            internal PoisonButton Action;
            internal Panel Marker;
        }

        private readonly PoisonPanel viewport;
        private readonly Panel content;
        private readonly Label title;
        private readonly Label subtitle;
        private readonly Label windowHeading;
        private readonly Label lobbyHeading;
        private readonly Label status;
        private readonly Panel statusBar;
        private readonly List<Font> fonts = new List<Font>();
        private readonly TaskCard[] cards;
        private bool arranging;
        private Color canvasColor, surfaceColor, textColor, mutedColor, accentColor, dangerColor;
        internal event Action<int> ActionRequested;
        internal PoisonButton[] Actions { get; private set; }

        internal GameRepairView()
        {
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.None;
            viewport = new PoisonPanel { Dock = DockStyle.Fill, AutoScroll = true,
                UseCustomBackColor = true, HorizontalScrollbar = false, VerticalScrollbar = true, VerticalScrollbarSize = 10 };
            content = new Panel { TabStop = false };
            viewport.Controls.Add(content);
            title = Label(LeagueGameRepairUiText.Title, 23, FontStyle.Bold);
            subtitle = Label(LeagueGameRepairUiText.Hint, 10, FontStyle.Regular);
            windowHeading = Label(LeagueGameRepairUiText.WindowGroup, 10, FontStyle.Bold);
            lobbyHeading = Label(LeagueGameRepairUiText.LobbyGroup, 10, FontStyle.Bold);
            content.Controls.AddRange(new Control[] { title, subtitle, windowHeading, lobbyHeading });
            cards = new[] {
                Card(LeagueGameRepairUiText.FixNow, LeagueGameRepairUiText.FixNowHint, 0),
                Card(LeagueGameRepairUiText.FixAuto, LeagueGameRepairUiText.FixAutoHint, 1),
                Card(LeagueGameRepairUiText.SkipSettlement, LeagueGameRepairUiText.SkipSettlementHint, 2),
                Card(LeagueGameRepairUiText.RestartUx, LeagueGameRepairUiText.RestartUxHint, 3),
                Card(LeagueGameRepairUiText.ExitGame, LeagueGameRepairUiText.ExitGameHint, 4)
            };
            Actions = new[] { cards[0].Action, cards[1].Action, cards[2].Action, cards[3].Action, cards[4].Action };
            statusBar = new Panel { Dock = DockStyle.Bottom, Height = 54, TabStop = false };
            status = Label(LeagueGameRepairUiText.Ready, 9.5F, FontStyle.Regular);
            statusBar.Controls.Add(status);
            Controls.Add(viewport);
            Controls.Add(statusBar);
            viewport.SizeChanged += Arrange;
            statusBar.SizeChanged += Arrange;
            ApplyPalette(false, Color.FromArgb(17, 22, 30), Color.FromArgb(29, 37, 48),
                Color.FromArgb(240, 244, 250), Color.FromArgb(179, 193, 210), Color.FromArgb(135, 185, 255), Color.FromArgb(255, 161, 168));
        }

        private Label Label(string text, float size, FontStyle style)
        {
            var font = new Font("Microsoft YaHei UI", size, style);
            fonts.Add(font);
            return new Label { Text = text, Font = font, AutoSize = false, BackColor = Color.Transparent,
                UseMnemonic = false, TextAlign = ContentAlignment.TopLeft, TabStop = false };
        }

        private TaskCard Card(string name, string hint, int index)
        {
            var font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
            fonts.Add(font);
            var card = new TaskCard {
                Surface = new Panel { TabStop = false, TabIndex = index },
                Heading = Label(name, index == 0 ? 17 : 12, FontStyle.Bold),
                Hint = Label(hint, 10, FontStyle.Regular),
                Marker = new Panel { TabStop = false },
                Action = new PoisonButton { Text = name, AccessibleName = name, Font = font, UseCustomFont = true,
                    UseCustomBackColor = true, UseCustomForeColor = true, DisplayFocus = true, UseSelectable = true,
                    TabStop = true, TabIndex = 0 }
            };
            // Poison replaces a custom fill on focus; keep the leading action's accent behind its native focus/text.
            if (index == 0) card.Action.CustomPaint += (sender, e) => {
                if (card.Action.Enabled) e.Graphics.Clear(card.Action.BackColor);
            };
            card.Action.Click += delegate { ActionRequested?.Invoke(index); };
            card.Surface.Controls.AddRange(new Control[] { card.Marker, card.Heading, card.Hint, card.Action });
            content.Controls.Add(card.Surface);
            return card;
        }

        internal void ApplyPalette(bool light, Color canvas, Color surface, Color text, Color muted, Color accent, Color danger, Color? primaryText = null)
        {
            if (SystemInformation.HighContrast) {
                canvas = SystemColors.Window; surface = SystemColors.Window; text = SystemColors.WindowText;
                muted = SystemColors.WindowText; accent = SystemColors.Highlight; danger = SystemColors.WindowText;
                primaryText = SystemColors.HighlightText;
            }
            canvasColor = canvas; surfaceColor = surface; textColor = text; mutedColor = muted; accentColor = accent; dangerColor = danger;
            var theme = light ? ThemeStyle.Light : ThemeStyle.Dark;
            BackColor = viewport.BackColor = content.BackColor = canvasColor;
            viewport.Theme = theme;
            viewport.VerticalPoisonScrollbar.Theme = theme;
            title.ForeColor = textColor;
            subtitle.ForeColor = windowHeading.ForeColor = lobbyHeading.ForeColor = mutedColor;
            for (var i = 0; i < cards.Length; i++) {
                var card = cards[i];
                card.Surface.BackColor = surfaceColor;
                card.Marker.BackColor = i == 4 ? dangerColor : accentColor;
                card.Marker.Visible = i == 0 || i == 4;
                card.Heading.ForeColor = i == 4 ? dangerColor : textColor;
                card.Hint.ForeColor = mutedColor;
                card.Action.Theme = theme;
                card.Action.BackColor = i == 0 ? accentColor : surfaceColor;
                card.Action.ForeColor = i == 0 ? (primaryText ?? (light ? Color.White : Color.FromArgb(12, 23, 38))) : (i == 4 ? dangerColor : textColor);
            }
            statusBar.BackColor = surfaceColor;
            status.ForeColor = textColor;
            Arrange(this, EventArgs.Empty);
            Invalidate(true);
        }

        internal void SetStatus(string message, Color color)
        {
            status.Text = message ?? string.Empty;
            status.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : color;
            Arrange(this, EventArgs.Empty);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.PageDown || keyData == Keys.PageUp) {
                var direction = keyData == Keys.PageDown ? 1 : -1;
                var distance = Math.Max(U(40), viewport.ClientSize.Height - U(40));
                var maximum = Math.Max(0, content.Height - viewport.ClientSize.Height);
                var target = Math.Max(0, Math.Min(maximum, -viewport.AutoScrollPosition.Y + direction * distance));
                viewport.AutoScrollPosition = new Point(0, target);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private int U(int value) { return (int)Math.Round(value * Math.Max(96, DeviceDpi) / 96.0); }
        private int HeightFor(Label label, int width, int minimum)
        {
            return Math.Max(U(minimum), TextRenderer.MeasureText(label.Text, label.Font,
                new Size(Math.Max(1, width), 10000), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + U(4));
        }

        private int PlaceCard(TaskCard card, int x, int y, int width, int minimum)
        {
            var inset = U(22);
            var inside = Math.Max(U(80), width - inset * 2);
            var heading = HeightFor(card.Heading, inside, 28);
            var hint = HeightFor(card.Hint, inside, 44);
            var height = Math.Max(U(minimum), inset + heading + U(10) + hint + U(22) + U(40) + inset);
            card.Surface.SetBounds(x, y, width, height);
            card.Marker.SetBounds(0, 0, U(4), height);
            card.Heading.SetBounds(inset, inset, inside, heading);
            card.Hint.SetBounds(inset, inset + heading + U(10), inside, hint);
            card.Action.SetBounds(inset, height - inset - U(40), inside, U(40));
            return height;
        }

        private void Arrange(object sender, EventArgs args)
        {
            if (arranging || cards == null || statusBar == null || viewport.ClientSize.Width < 40) return;
            arranging = true;
            try {
                var scroll = -viewport.AutoScrollPosition.Y;
                var width = Math.Max(U(240), viewport.ClientSize.Width - U(16));
                var pad = width < U(600) ? U(18) : U(30);
                var inner = width - pad * 2;
                var gap = U(16);
                var y = U(24);
                title.SetBounds(pad, y, inner, HeightFor(title, inner, 42)); y += title.Height + U(6);
                subtitle.SetBounds(pad, y, inner, HeightFor(subtitle, inner, 24)); y += subtitle.Height + U(26);
                windowHeading.SetBounds(pad, y, inner, U(26)); y += U(38);
                if (inner >= U(620)) {
                    var primary = (inner - gap) * 3 / 5;
                    var a = PlaceCard(cards[0], pad, y, primary, 222);
                    var b = PlaceCard(cards[1], pad + primary + gap, y, inner - primary - gap, 222);
                    y += Math.Max(a, b) + U(26);
                } else {
                    y += PlaceCard(cards[0], pad, y, inner, 200) + gap;
                    y += PlaceCard(cards[1], pad, y, inner, 190) + U(26);
                }
                lobbyHeading.SetBounds(pad, y, inner, U(26)); y += U(38);
                if (inner >= U(720)) {
                    var w = (inner - gap * 2) / 3;
                    var h = 0;
                    for (var i = 2; i < 5; i++) h = Math.Max(h, PlaceCard(cards[i], pad + (i - 2) * (w + gap), y, w, 220));
                    y += h;
                } else {
                    for (var i = 2; i < 5; i++) y += PlaceCard(cards[i], pad, y, inner, 180) + gap;
                }
                content.Size = new Size(width, y + pad);
                viewport.AutoScrollMinSize = new Size(0, content.Height);
                viewport.AutoScrollPosition = new Point(0, scroll);
                var statusWidth = Math.Max(1, statusBar.ClientSize.Width - pad * 2);
                var statusHeight = HeightFor(status, statusWidth, 22);
                statusBar.Height = statusHeight + U(24);
                status.SetBounds(pad, U(12), statusWidth, statusHeight);
            } finally { arranging = false; }
        }

        internal void ValidateLayout()
        {
            Arrange(this, EventArgs.Empty);
            if (viewport.HorizontalScroll.Visible) throw new InvalidOperationException("Repair page horizontal overflow.");
            foreach (var card in cards) {
                if (card.Hint.Bottom > card.Action.Top || card.Action.Right > card.Surface.Width)
                    throw new InvalidOperationException("Repair action overlaps its description.");
                if (!card.Action.TabStop || string.IsNullOrEmpty(card.Action.AccessibleName))
                    throw new InvalidOperationException("Repair action is not keyboard accessible.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { viewport.SizeChanged -= Arrange; statusBar.SizeChanged -= Arrange; }
            base.Dispose(disposing);
            if (disposing) foreach (var font in fonts) font.Dispose();
        }
    }
}
