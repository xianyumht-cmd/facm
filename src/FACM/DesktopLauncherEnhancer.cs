using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FACM.League;
using FACM.Services;
using FACM.Theming;

namespace FACM
{
    /// <summary>
    /// Replaces the card/table-looking feature rows with a compact desktop-style launcher while
    /// leaving the header, directory, cleanup card and footer description area untouched.
    /// The launcher deliberately grows from the top-left instead of stretching to fill the menu,
    /// so future shortcuts can occupy the next natural slot without rebalancing existing entries.
    /// </summary>
    internal static class DesktopLauncherEnhancer
    {
        internal const int TileCount = 5;
        internal const int LauncherColumns = 3;
        private const int BaseWidth = 420;
        private const int BaseHeight = 680;
        private const int LauncherBaseWidth = 300;
        private const int LauncherBaseHeight = 210;
        private const int FlowBaseWidth = 286;
        private const int FlowBaseHeight = 178;
        private const int TileBaseWidth = 80;
        private const int TileBaseHeight = 78;
        private const int TileGapX = 12;
        private const int TileGapY = 10;
        private const string LauncherName = "FACM.DesktopLauncher";
        private const ControlStyles DesktopTileStyles =
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
            ControlStyles.Selectable;

        private static readonly FieldInfo ThemeField = typeof(CompactMenuForm).GetField(
            "_theme", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo RepairMethod = typeof(CompactMenuForm).GetMethod(
            "OpenRepairMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo PersonalizationMethod = typeof(CompactMenuForm).GetMethod(
            "OpenPersonalizationMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo MoreMethod = typeof(CompactMenuForm).GetMethod(
            "OpenMoreMenu", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool Apply(CompactMenuForm menu)
        {
            if (menu == null || menu.IsDisposed) return false;
            if (menu.Controls.Find(LauncherName, true).Length > 0) return true;

            var theme = ThemeField == null ? null : ThemeField.GetValue(menu) as ThemeDefinition;
            if (theme == null) return false;
            var ui = UiTextCatalog.Load();
            var footerHint = Descendants(menu).OfType<Label>()
                .FirstOrDefault(label => string.Equals(label.Text, ui.Get(UiTextKeys.ShellSimpleHint), StringComparison.Ordinal));
            if (footerHint == null) return false;

            var scaleX = menu.ClientSize.Width / (float)BaseWidth;
            var scaleY = menu.ClientSize.Height / (float)BaseHeight;
            Func<int, int> sx = value => Math.Max(1, (int)Math.Round(value * scaleX));
            Func<int, int> sy = value => Math.Max(1, (int)Math.Round(value * scaleY));

            var launcher = new Panel
            {
                Name = LauncherName,
                Location = new Point(sx(18), sy(258)),
                Size = new Size(sx(LauncherBaseWidth), sy(LauncherBaseHeight)),
                BackColor = Color.Transparent
            };
            var caption = new Label
            {
                Text = ui.Get(UiTextKeys.ShellFeatureCenter),
                Location = new Point(sx(2), sy(2)),
                Size = new Size(sx(132), sy(22)),
                ForeColor = theme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(theme.FontName, Math.Max(7F, 8.2F * Math.Min(scaleX, scaleY)), FontStyle.Bold)
            };
            launcher.Controls.Add(caption);

            var flow = new LauncherFlowPanel
            {
                Location = new Point(0, sy(28)),
                Size = new Size(sx(FlowBaseWidth), sy(FlowBaseHeight)),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            launcher.Controls.Add(flow);

            var repairTitle = ui.Get(UiTextKeys.ShellRepairTools);
            var leagueTitle = ui.Get(UiTextKeys.ShellLeague);
            var presenceTitle = LeaguePresenceText.Get(ui, LeaguePresenceUiTextKeys.Menu);
            var personalizeTitle = ui.Get(UiTextKeys.ShellPersonalization);
            var moreTitle = ui.Get(UiTextKeys.ShellMoreSettings);

            AddTile(flow, theme, sx, sy, "⚙", repairTitle, ui.Get(UiTextKeys.ShellRepairHint),
                tile => InvokeLegacy(menu, RepairMethod, tile), footerHint, ui);
            AddTile(flow, theme, sx, sy, "L", leagueTitle, ui.Get(UiTextKeys.ShellLeagueHint),
                tile => LeagueHubUiBridge.RequestOpen(), footerHint, ui);
            AddTile(flow, theme, sx, sy, "●", presenceTitle, LeaguePresenceText.Get(ui, LeaguePresenceUiTextKeys.Hint),
                tile => LeaguePresenceUiBridge.RequestOpen(theme), footerHint, ui);
            AddTile(flow, theme, sx, sy, "✦", personalizeTitle, ui.Get(UiTextKeys.ShellPersonalizationHint),
                tile => InvokeLegacy(menu, PersonalizationMethod, tile), footerHint, ui);
            AddTile(flow, theme, sx, sy, "⋯", moreTitle, ui.Get(UiTextKeys.ShellSimpleHint),
                tile => InvokeLegacy(menu, MoreMethod, tile), footerHint, ui);

            // Only hide the legacy controls after the complete replacement surface has been built.
            // If tile construction ever fails, the old controls stay visible instead of leaving a blank panel.
            HideLegacyFeatureRows(menu, ui);
            menu.Controls.Add(launcher);
            launcher.BringToFront();
            return true;
        }

        internal static void ValidateDefinitionForSmokeTest()
        {
            if (TileCount != 5) throw new InvalidOperationException("Control-center launcher must expose exactly five primary desktop shortcuts.");
            if (LauncherColumns != 3) throw new InvalidOperationException("Control-center launcher must keep the three-column growth contract.");
            if (ThemeField == null || RepairMethod == null || PersonalizationMethod == null || MoreMethod == null)
                throw new InvalidOperationException("Desktop launcher lost access to the existing bounded control-center actions.");
            if ((DesktopTileStyles & ControlStyles.SupportsTransparentBackColor) == 0)
                throw new InvalidOperationException("Desktop launcher tiles must support transparent backgrounds before assigning Color.Transparent.");
            if (FlowBaseWidth < (LauncherColumns * TileBaseWidth) + ((LauncherColumns - 1) * TileGapX))
                throw new InvalidOperationException("Compact launcher flow width can no longer hold three natural columns.");
        }

        private static void HideLegacyFeatureRows(CompactMenuForm menu, UiTextCatalog ui)
        {
            var featureCaption = Descendants(menu).OfType<Label>()
                .FirstOrDefault(label => string.Equals(label.Text, ui.Get(UiTextKeys.ShellFeatureCenter), StringComparison.Ordinal));
            var featureCard = featureCaption == null ? null : featureCaption.Parent;
            if (featureCard != null) featureCard.Visible = false;

            var moreText = ui.Get(UiTextKeys.ShellMoreSettings) + "  " + ui.Get(UiTextKeys.ShellArrow);
            var legacyMore = menu.Controls.Cast<Control>()
                .FirstOrDefault(control => string.Equals(control.Text, moreText, StringComparison.Ordinal));
            if (legacyMore != null) legacyMore.Visible = false;
        }

        private static void AddTile(
            FlowLayoutPanel parent,
            ThemeDefinition theme,
            Func<int, int> sx,
            Func<int, int> sy,
            string glyph,
            string title,
            string hint,
            Action<Control> click,
            Label footerHint,
            UiTextCatalog ui)
        {
            var tile = new DesktopTile(theme, glyph, title)
            {
                Size = new Size(sx(TileBaseWidth), sy(TileBaseHeight)),
                Margin = new Padding(0, 0, sx(TileGapX), sy(TileGapY)),
                AccessibleName = title,
                AccessibleDescription = hint
            };
            tile.Click += delegate { if (click != null) click(tile); };
            tile.MouseEnter += delegate
            {
                if (!footerHint.IsDisposed) footerHint.Text = title + " · " + hint;
            };
            tile.MouseLeave += delegate
            {
                if (!footerHint.IsDisposed) footerHint.Text = ui.Get(UiTextKeys.ShellSimpleHint);
            };
            parent.Controls.Add(tile);
        }

        private static void InvokeLegacy(CompactMenuForm menu, MethodInfo method, Control anchor)
        {
            if (menu == null || menu.IsDisposed || method == null) return;
            try { method.Invoke(menu, new object[] { anchor, EventArgs.Empty }); }
            catch (TargetInvocationException exception)
            {
                AppLog.Error("Desktop launcher action failed", exception.InnerException ?? exception);
            }
            catch (Exception exception)
            {
                AppLog.Error("Desktop launcher action failed", exception);
            }
        }

        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        {
            if (root == null) yield break;
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        private sealed class LauncherFlowPanel : FlowLayoutPanel
        {
            public LauncherFlowPanel()
            {
                SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
                BackColor = Color.Transparent;
            }
        }

        private sealed class DesktopTile : Control
        {
            private readonly ThemeDefinition _theme;
            private readonly string _glyph;
            private bool _hovered;
            private bool _pressed;

            public DesktopTile(ThemeDefinition theme, string glyph, string title)
            {
                _theme = theme;
                _glyph = glyph ?? string.Empty;
                Text = title ?? string.Empty;
                Cursor = Cursors.Hand;
                TabStop = true;

                // SupportsTransparentBackColor must be enabled before assigning Color.Transparent.
                SetStyle(DesktopTileStyles, true);
                BackColor = Color.Transparent;

                MouseEnter += delegate { _hovered = true; Invalidate(); };
                MouseLeave += delegate { _hovered = false; _pressed = false; Invalidate(); };
                MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button != MouseButtons.Left) return;
                    _pressed = true;
                    Invalidate();
                };
                MouseUp += delegate { _pressed = false; Invalidate(); };
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                {
                    OnClick(EventArgs.Empty);
                    e.Handled = true;
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                var full = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
                if (_hovered || _pressed || Focused)
                {
                    using (var hoverPath = RoundedPath(full, 10))
                    using (var hoverBrush = new SolidBrush(Color.FromArgb(
                        _pressed ? 46 : 24,
                        _theme.TextPrimary)))
                    {
                        e.Graphics.FillPath(hoverBrush, hoverPath);
                    }

                    if (Focused)
                    {
                        using (var focusPath = RoundedPath(full, 10))
                        using (var focusPen = new Pen(Color.FromArgb(150, _theme.AccentSecondary), 1F))
                        {
                            e.Graphics.DrawPath(focusPen, focusPath);
                        }
                    }
                }

                var iconSize = Math.Max(32, Math.Min(38, Height / 2));
                var icon = new Rectangle((Width - iconSize) / 2, 5, iconSize, iconSize);
                using (var iconPath = RoundedPath(icon, Math.Max(7, Math.Min(10, _theme.ButtonRadius + 4))))
                using (var iconBrush = new SolidBrush(_theme.Accent))
                using (var iconPen = new Pen(Color.FromArgb(150, _theme.AccentSecondary), 1F))
                {
                    e.Graphics.FillPath(iconBrush, iconPath);
                    e.Graphics.DrawPath(iconPen, iconPath);
                }

                using (var glyphFont = new Font(
                    string.Equals(_glyph, "L", StringComparison.Ordinal) ? "Segoe UI" : "Segoe UI Symbol",
                    string.Equals(_glyph, "L", StringComparison.Ordinal) ? 14.5F : 14F,
                    FontStyle.Bold))
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        _glyph,
                        glyphFont,
                        icon,
                        Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }

                var titleBounds = new Rectangle(2, icon.Bottom + 7, Math.Max(1, Width - 4), Math.Max(1, Height - icon.Bottom - 8));
                using (var titleFont = new Font(_theme.FontName, 8.3F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        Text,
                        titleFont,
                        titleBounds,
                        _theme.TextPrimary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }

            private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
            {
                var path = new GraphicsPath();
                var diameter = Math.Max(2, Math.Min(Math.Min(bounds.Width, bounds.Height), radius * 2));
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                return path;
            }
        }
    }
}
