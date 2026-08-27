using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;
using FACM.Theming;

namespace FACM
{
    /// <summary>
    /// Turns CompactMenuForm into a launcher surface: the control center answers only “what do I
    /// want to open?”. Business state, directory selection and repair guidance live inside their
    /// product pages. Shortcuts flow left-to-right like desktop icons and wrap only when needed.
    /// </summary>
    internal static class DesktopLauncherEnhancer
    {
        internal const int TileCount = 4;
        internal const int LauncherColumns = 4;
        private const int BaseWidth = 420;
        private const int BaseHeight = 680;
        private const int CompactBaseHeight = 236;
        private const int TileBaseWidth = 82;
        private const int TileBaseHeight = 84;
        private const int TileGapX = 7;
        private const int TileGapY = 8;
        private const string LauncherName = "FACM.DesktopLauncher";
        private const ControlStyles DesktopTileStyles =
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
            ControlStyles.Selectable;

        private static readonly FieldInfo ThemeField = typeof(CompactMenuForm).GetField(
            "_theme", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo OwnerField = typeof(CompactMenuForm).GetField(
            "_ownerBall", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SettingsField = typeof(CompactMenuForm).GetField(
            "_settings", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CleanupField = typeof(CompactMenuForm).GetField(
            "_cleanup", BindingFlags.Instance | BindingFlags.NonPublic);
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

            var scaleX = menu.ClientSize.Width / (float)BaseWidth;
            var scaleY = menu.ClientSize.Height / (float)BaseHeight;
            Func<int, int> sx = value => Math.Max(1, (int)Math.Round(value * scaleX));
            Func<int, int> sy = value => Math.Max(1, (int)Math.Round(value * scaleY));

            var header = menu.Controls.Cast<Control>()
                .Where(control => control.Top <= sy(2) && control.Height <= sy(86))
                .OrderByDescending(control => control.Width)
                .FirstOrDefault();

            foreach (Control control in menu.Controls)
            {
                if (!ReferenceEquals(control, header)) control.Visible = false;
            }

            var compactHeight = Math.Max(sy(210), sy(CompactBaseHeight));
            menu.ClientSize = new Size(menu.ClientSize.Width, compactHeight);
            if (header != null)
            {
                header.Visible = true;
                header.Width = menu.ClientSize.Width;
            }

            var launcher = new LauncherFlowPanel
            {
                Name = LauncherName,
                Location = new Point(sx(16), sy(82)),
                Size = new Size(Math.Max(120, menu.ClientSize.Width - sx(32)), Math.Max(sy(98), compactHeight - sy(98))),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };

            AddTile(launcher, theme, sx, sy, "◈", CleanupRepairUiText.LauncherTitle,
                (Action)delegate { OpenCleanupRepair(menu); });
            AddTile(launcher, theme, sx, sy, "L", LeagueHubText.Get(ui, LeagueHubUiTextKeys.Title),
                (Action)delegate { LeagueHubUiBridge.RequestOpen(); });
            AddTile(launcher, theme, sx, sy, "✦", ui.Get(UiTextKeys.ShellPersonalization),
                tile => InvokeLegacy(menu, PersonalizationMethod, tile));
            AddTile(launcher, theme, sx, sy, "⋯", ui.Get(UiTextKeys.ShellMoreSettings),
                tile => InvokeLegacy(menu, MoreMethod, tile));

            menu.Controls.Add(launcher);
            launcher.BringToFront();
            return true;
        }

        internal static void ValidateDefinitionForSmokeTest()
        {
            if (TileCount != 4) throw new InvalidOperationException("Control-center launcher must expose exactly four primary desktop shortcuts.");
            if (LauncherColumns != 4) throw new InvalidOperationException("Control-center launcher must prefer four left-to-right desktop shortcuts before wrapping.");
            if (ThemeField == null || OwnerField == null || SettingsField == null || CleanupField == null ||
                PersonalizationMethod == null || MoreMethod == null)
                throw new InvalidOperationException("Desktop launcher lost access to its bounded control-center actions.");
            if ((DesktopTileStyles & ControlStyles.SupportsTransparentBackColor) == 0)
                throw new InvalidOperationException("Desktop launcher tiles must support transparent backgrounds before assigning Color.Transparent.");
            if ((4 * TileBaseWidth) + (3 * TileGapX) > BaseWidth - 32)
                throw new InvalidOperationException("Default control-center width can no longer hold four natural desktop shortcuts.");
        }

        private static void OpenCleanupRepair(CompactMenuForm menu)
        {
            if (menu == null || menu.IsDisposed) return;
            try
            {
                var owner = OwnerField.GetValue(menu) as MainForm;
                var settings = SettingsField.GetValue(menu) as AppSettings;
                var cleanup = CleanupField.GetValue(menu) as CleanupModule;
                if (owner == null || settings == null || cleanup == null) return;

                using (var form = new CleanupRepairForm(owner, settings, cleanup))
                {
                    form.TopMost = true;
                    menu.Close();
                    form.ShowDialog();
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Cleanup/repair launcher action failed", exception);
            }
        }

        private static void AddTile(
            FlowLayoutPanel parent,
            ThemeDefinition theme,
            Func<int, int> sx,
            Func<int, int> sy,
            string glyph,
            string title,
            Action click)
        {
            AddTile(parent, theme, sx, sy, glyph, title, delegate(Control control) { if (click != null) click(); });
        }

        private static void AddTile(
            FlowLayoutPanel parent,
            ThemeDefinition theme,
            Func<int, int> sx,
            Func<int, int> sy,
            string glyph,
            string title,
            Action<Control> click)
        {
            var tile = new DesktopTile(theme, glyph, title)
            {
                Size = new Size(sx(TileBaseWidth), sy(TileBaseHeight)),
                Margin = new Padding(0, 0, sx(TileGapX), sy(TileGapY)),
                AccessibleName = title
            };
            tile.Click += delegate { if (click != null) click(tile); };
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
                    using (var hoverPath = RoundedPath(full, Math.Max(6, _theme.ButtonRadius + 4)))
                    using (var hoverBrush = new SolidBrush(Color.FromArgb(_pressed ? 46 : 24, _theme.TextPrimary)))
                        e.Graphics.FillPath(hoverBrush, hoverPath);

                    if (Focused)
                    {
                        using (var focusPath = RoundedPath(full, Math.Max(6, _theme.ButtonRadius + 4)))
                        using (var focusPen = new Pen(Color.FromArgb(150, _theme.AccentSecondary), 1F))
                            e.Graphics.DrawPath(focusPen, focusPath);
                    }
                }

                var iconSize = Math.Max(32, Math.Min(40, Height / 2));
                var icon = new Rectangle((Width - iconSize) / 2, 4, iconSize, iconSize);
                using (var iconPath = RoundedPath(icon, Math.Max(7, Math.Min(10, _theme.ButtonRadius + 4))))
                using (var iconBrush = new SolidBrush(_theme.Accent))
                using (var iconPen = new Pen(Color.FromArgb(135, _theme.AccentSecondary), 1F))
                {
                    e.Graphics.FillPath(iconBrush, iconPath);
                    e.Graphics.DrawPath(iconPen, iconPath);
                }

                using (var glyphFont = new Font(
                    string.Equals(_glyph, "L", StringComparison.Ordinal) ? "Segoe UI" : "Segoe UI Symbol",
                    string.Equals(_glyph, "L", StringComparison.Ordinal) ? 14F : 13.5F,
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
                using (var titleFont = new Font(_theme.FontName, 8.1F, FontStyle.Bold))
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
