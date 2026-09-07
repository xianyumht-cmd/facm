using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Threading;
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
    /// A direct floating-entry click can additionally expose the already-owned League context;
    /// tray/external control-center opens remain the generic four-shortcut home.
    /// </summary>
    internal static class DesktopLauncherEnhancer
    {
        internal const int TileCount = 4;
        internal const int LauncherColumns = 4;
        private const int BaseWidth = 420;
        private const int BaseHeight = 680;
        private const int CompactBaseHeight = 236;
        private const int ContextCompactBaseHeight = 322;
        private const int TileBaseWidth = 82;
        private const int TileBaseHeight = 84;
        private const int TileGapX = 7;
        private const int TileGapY = 8;
        private const string LauncherName = "FACM.DesktopLauncher";
        private const string ContextName = "FACM.DesktopLauncher.Context";
        private const string BackdropName = "FACM.DesktopLauncher.Backdrop";
        private const ControlStyles DesktopTileStyles =
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
            ControlStyles.Selectable;

        private static int _contextualOpenArmed;

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

        public static void ArmContextualOpen()
        {
            Interlocked.Exchange(ref _contextualOpenArmed, 1);
        }

        public static void CancelContextualOpen()
        {
            Interlocked.Exchange(ref _contextualOpenArmed, 0);
        }

        public static bool Apply(CompactMenuForm menu)
        {
            if (menu == null || menu.IsDisposed) return false;
            if (menu.Controls.Find(LauncherName, true).Length > 0) return true;

            var contextual = Interlocked.Exchange(ref _contextualOpenArmed, 0) != 0;
            var theme = ThemeField == null ? null : ThemeField.GetValue(menu) as ThemeDefinition;
            if (theme == null) return false;
            var ui = UiTextCatalog.Load();
            var settings = SettingsField == null ? null : SettingsField.GetValue(menu) as AppSettings;

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

            var compactBaseHeight = contextual ? ContextCompactBaseHeight : CompactBaseHeight;
            var compactHeight = Math.Max(sy(contextual ? 296 : 210), sy(compactBaseHeight));
            menu.ClientSize = new Size(menu.ClientSize.Width, compactHeight);
            menu.BackColor = FacmDesignSystem.Canvas;
            FacmDesignSystem.Round(menu, FacmDesignSystem.WindowRadius);

            var backdrop = new Panel
            {
                Name = BackdropName,
                Dock = DockStyle.Fill,
                BackColor = FacmDesignSystem.Canvas,
                TabStop = false
            };
            menu.Controls.Add(backdrop);
            backdrop.SendToBack();

            if (header != null)
            {
                header.Visible = true;
                header.Width = menu.ClientSize.Width;
                NormalizeHeader(header);
                header.BringToFront();
            }

            var launcherTop = contextual ? 184 : 82;
            if (contextual)
            {
                var contextCard = new LauncherContextCard(theme, settings, LeagueShellContextState.Current)
                {
                    Name = ContextName,
                    Location = new Point(sx(16), sy(78)),
                    Size = new Size(Math.Max(120, menu.ClientSize.Width - sx(32)), sy(96)),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };
                menu.Controls.Add(contextCard);
                contextCard.BringToFront();
            }

            var launcher = new LauncherFlowPanel
            {
                Name = LauncherName,
                Location = new Point(sx(16), sy(launcherTop)),
                Size = new Size(Math.Max(120, menu.ClientSize.Width - sx(32)), Math.Max(sy(98), compactHeight - sy(launcherTop + 16))),
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
                (Action)delegate { OpenLeague(menu, contextual); });
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
            if (ContextCompactBaseHeight <= CompactBaseHeight)
                throw new InvalidOperationException("Contextual launcher must reserve room for the state card without shrinking the four shortcuts.");
            if (ThemeField == null || OwnerField == null || SettingsField == null || CleanupField == null ||
                PersonalizationMethod == null || MoreMethod == null)
                throw new InvalidOperationException("Desktop launcher lost access to its bounded control-center actions.");
            if ((DesktopTileStyles & ControlStyles.SupportsTransparentBackColor) == 0)
                throw new InvalidOperationException("Desktop launcher tiles must support transparent backgrounds before assigning Color.Transparent.");
            if ((4 * TileBaseWidth) + (3 * TileGapX) > BaseWidth - 32)
                throw new InvalidOperationException("Default control-center width can no longer hold four natural desktop shortcuts.");
            if (FacmDesignSystem.WindowRadius > 12 || FacmDesignSystem.ControlRadius > 6)
                throw new InvalidOperationException("Desktop launcher escaped the shared compact geometry contract.");

            ArmContextualOpen();
            CancelContextualOpen();
            LeagueShellContextRouter.ValidateForSmokeTest();
        }

        private static void NormalizeHeader(Control header)
        {
            if (header == null || header.IsDisposed) return;
            header.BackColor = FacmDesignSystem.Canvas;

            foreach (Control child in header.Controls)
            {
                var label = child as Label;
                if (label != null)
                {
                    label.BackColor = Color.Transparent;
                    if (string.Equals(label.Text, "F", StringComparison.Ordinal))
                    {
                        label.BackColor = FacmDesignSystem.Accent;
                        label.ForeColor = Color.White;
                        FacmDesignSystem.Round(label, Math.Max(4, FacmDesignSystem.ControlRadius + 1));
                    }
                    else if (string.Equals(label.Text, "×", StringComparison.Ordinal))
                    {
                        label.ForeColor = FacmDesignSystem.TextMuted;
                    }
                    else
                    {
                        label.ForeColor = label.Font != null && label.Font.Size >= 12F
                            ? FacmDesignSystem.Text
                            : FacmDesignSystem.TextMuted;
                    }
                    continue;
                }

                // The old administrator/status badge is a legacy ThemedButton. The launcher is a
                // navigation surface, so this secondary status does not belong in its compact header.
                if (string.Equals(child.GetType().Name, "ThemedButton", StringComparison.Ordinal))
                    child.Visible = false;
            }
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

        private static void OpenLeague(CompactMenuForm menu, bool contextual)
        {
            if (menu == null || menu.IsDisposed) return;
            var owner = OwnerField == null ? null : OwnerField.GetValue(menu) as MainForm;
            if (!contextual || owner == null)
            {
                LeagueHubUiBridge.RequestOpen();
                return;
            }

            var action = LeagueShellContextRouter.Resolve(LeagueShellContextState.Current);
            if (action == LeagueShellContextAction.Live)
            {
                LeagueLiveUiBridge.RequestOpen(owner);
                return;
            }
            if (action == LeagueShellContextAction.Efficiency)
            {
                LeagueEfficiencyUiBridge.RequestOpen(owner);
                return;
            }
            if (action == LeagueShellContextAction.Dashboard)
            {
                LeagueDashboardUiBridge.RequestOpen(owner);
                return;
            }

            LeagueHubUiBridge.RequestOpen();
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
                BackColor = FacmDesignSystem.Canvas;
            }
        }

        private sealed class LauncherContextCard : Control
        {
            private readonly ThemeDefinition _theme;
            private readonly string _leagueStatus;
            private readonly string _automationStatus;
            private readonly string _contextHint;
            private readonly string _directoryHint;

            public LauncherContextCard(ThemeDefinition theme, AppSettings settings, LeagueDashboardPhaseState state)
            {
                _theme = theme ?? throw new ArgumentNullException(nameof(theme));
                _leagueStatus = DesktopLauncherContextUiText.LeagueStatus(state);
                _automationStatus = DesktopLauncherContextUiText.AutomationStatus(settings);
                _contextHint = DesktopLauncherContextUiText.ContextHint(state);
                _directoryHint = settings == null || string.IsNullOrWhiteSpace(settings.GamePath)
                    ? DesktopLauncherContextUiText.DirectoryMissing
                    : string.Empty;
                SetStyle(
                    ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
                    true);
                BackColor = FacmDesignSystem.Canvas;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
                using (var path = FacmDesignSystem.RoundedRectangle(bounds, FacmDesignSystem.CardRadius))
                using (var fill = new SolidBrush(FacmDesignSystem.Surface))
                using (var border = new Pen(FacmDesignSystem.BorderSoft, 1F))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }

                var left = 12;
                var width = Math.Max(1, Width - 24);
                using (var titleFont = new Font(_theme.FontName, 9.2F, FontStyle.Bold))
                using (var detailFont = new Font(_theme.FontName, 7.8F, FontStyle.Bold))
                using (var hintFont = new Font(_theme.FontName, 7.5F, FontStyle.Regular))
                {
                    TextRenderer.DrawText(e.Graphics, _leagueStatus, titleFont,
                        new Rectangle(left, 9, width, 20), FacmDesignSystem.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(e.Graphics, _automationStatus, detailFont,
                        new Rectangle(left, 31, width, 18), FacmDesignSystem.TextMuted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(e.Graphics, _contextHint, hintFont,
                        new Rectangle(left, 51, width, 18), FacmDesignSystem.Accent,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    if (!string.IsNullOrWhiteSpace(_directoryHint))
                    {
                        TextRenderer.DrawText(e.Graphics, _directoryHint, hintFont,
                            new Rectangle(left, 71, width, 17), FacmDesignSystem.Warning,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    }
                }
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

            protected override void OnGotFocus(EventArgs e)
            {
                base.OnGotFocus(e);
                Invalidate();
            }

            protected override void OnLostFocus(EventArgs e)
            {
                base.OnLostFocus(e);
                Invalidate();
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
                    var hoverFill = _pressed
                        ? FacmDesignSystem.Blend(FacmDesignSystem.SurfaceHover, FacmDesignSystem.Accent, 0.10F)
                        : FacmDesignSystem.SurfaceHover;
                    using (var hoverPath = FacmDesignSystem.RoundedRectangle(full, Math.Max(5, FacmDesignSystem.ControlRadius + 2)))
                    using (var hoverBrush = new SolidBrush(hoverFill))
                        e.Graphics.FillPath(hoverBrush, hoverPath);

                    if (Focused && ShowFocusCues)
                    {
                        using (var focusPath = FacmDesignSystem.RoundedRectangle(full, Math.Max(5, FacmDesignSystem.ControlRadius + 2)))
                        using (var focusPen = new Pen(FacmDesignSystem.Accent, 1F))
                            e.Graphics.DrawPath(focusPen, focusPath);
                    }
                }

                var iconSize = Math.Max(32, Math.Min(40, Height / 2));
                var icon = new Rectangle((Width - iconSize) / 2, 4, iconSize, iconSize);
                using (var iconPath = FacmDesignSystem.RoundedRectangle(icon, Math.Max(5, Math.Min(8, FacmDesignSystem.ControlRadius + 2))))
                using (var iconBrush = new SolidBrush(FacmDesignSystem.Accent))
                using (var iconPen = new Pen(FacmDesignSystem.Blend(FacmDesignSystem.Accent, FacmDesignSystem.BorderSoft, 0.35F), 1F))
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
                        FacmDesignSystem.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }
        }
    }
}
