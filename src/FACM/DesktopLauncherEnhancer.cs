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
    /// Replaces the card/table-looking feature rows with a sparse desktop-launcher surface while
    /// leaving the header, directory, cleanup card and footer description area untouched.
    /// </summary>
    internal static class DesktopLauncherEnhancer
    {
        internal const int TileCount = 5;
        private const int BaseWidth = 420;
        private const int BaseHeight = 680;
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
                Location = new Point(sx(16), sy(258)),
                Size = new Size(sx(388), sy(292)),
                BackColor = Color.Transparent
            };
            var caption = new Label
            {
                Text = ui.Get(UiTextKeys.ShellFeatureCenter),
                Location = new Point(sx(8), sy(3)),
                Size = new Size(sx(180), sy(24)),
                ForeColor = theme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(theme.FontName, Math.Max(7F, 8.5F * Math.Min(scaleX, scaleY)), FontStyle.Bold)
            };
            launcher.Controls.Add(caption);

            var repairTitle = ui.Get(UiTextKeys.ShellRepairTools);
            var leagueTitle = ui.Get(UiTextKeys.ShellLeague);
            var presenceTitle = LeaguePresenceText.Get(ui, LeaguePresenceUiTextKeys.Menu);
            var personalizeTitle = ui.Get(UiTextKeys.ShellPersonalization);
            var moreTitle = ui.Get(UiTextKeys.ShellMoreSettings);

            AddTile(launcher, theme, sx, sy, "⚙", repairTitle, ui.Get(UiTextKeys.ShellRepairHint), 3, 32,
                tile => InvokeLegacy(menu, RepairMethod, tile), footerHint, ui);
            AddTile(launcher, theme, sx, sy, "L", leagueTitle, ui.Get(UiTextKeys.ShellLeagueHint), 129, 32,
                tile => LeagueHubUiBridge.RequestOpen(), footerHint, ui);
            AddTile(launcher, theme, sx, sy, "●", presenceTitle, LeaguePresenceText.Get(ui, LeaguePresenceUiTextKeys.Hint), 255, 32,
                tile => LeaguePresenceUiBridge.RequestOpen(theme), footerHint, ui);
            AddTile(launcher, theme, sx, sy, "✦", personalizeTitle, ui.Get(UiTextKeys.ShellPersonalizationHint), 66, 139,
                tile => InvokeLegacy(menu, PersonalizationMethod, tile), footerHint, ui);
            AddTile(launcher, theme, sx, sy, "⋯", moreTitle, ui.Get(UiTextKeys.ShellSimpleHint), 192, 139,
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
            if (ThemeField == null || RepairMethod == null || PersonalizationMethod == null || MoreMethod == null)
                throw new InvalidOperationException("Desktop launcher lost access to the existing bounded control-center actions.");
            if ((DesktopTileStyles & ControlStyles.SupportsTransparentBackColor) == 0)
                throw new InvalidOperationException("Desktop launcher tiles must support transparent backgrounds before assigning Color.Transparent.");
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
            Control parent,
            ThemeDefinition theme,
            Func<int, int> sx,
            Func<int, int> sy,
            string glyph,
            string title,
            string hint,
            int x,
            int y,
            Action<Control> click,
            Label footerHint,
            UiTextCatalog ui)
        {
            var tile = new DesktopTile(theme, glyph, title)
            {
                Location = new Point(sx(x), sy(y)),
                Size = new Size(sx(112), sy(92)),
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
                // WinForms throws ArgumentException if the assignment happens first.
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
                    using (var hoverPath = RoundedPath(full, Math.Max(8, _theme.ButtonRadius)))
                    using (var hoverBrush = new SolidBrush(Color.FromArgb(
                        _pressed ? 92 : 54,
                        _theme.IsLight ? _theme.SurfaceSecondary : _theme.TextPrimary)))
                    {
                        e.Graphics.FillPath(hoverBrush, hoverPath);
                    }
                }

                var iconSize = Math.Max(32, Math.Min(42, Height / 2));
                var icon = new Rectangle((Width - iconSize) / 2, 7, iconSize, iconSize);
                using (var iconPath = RoundedPath(icon, Math.Max(8, _theme.ButtonRadius)))
                using (var iconBrush = new LinearGradientBrush(icon, _theme.Accent, _theme.AccentSecondary, 35F))
                {
                    e.Graphics.FillPath(iconBrush, iconPath);
                }

                using (var glyphFont = new Font(
                    string.Equals(_glyph, "L", StringComparison.Ordinal) ? "Segoe UI" : "Segoe UI Symbol",
                    string.Equals(_glyph, "L", StringComparison.Ordinal) ? 16F : 15F,
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

                var titleBounds = new Rectangle(4, icon.Bottom + 8, Math.Max(1, Width - 8), Math.Max(1, Height - icon.Bottom - 10));
                using (var titleFont = new Font(_theme.FontName, 8.6F, FontStyle.Bold))
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
