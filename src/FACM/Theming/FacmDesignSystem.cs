using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FACM.Theming
{
    /// <summary>
    /// Shared FACM visual primitives. ThemeCatalog is the palette source and FacmThemeRuntime
    /// supplies the process-wide active theme so every FACM-owned WinForms surface reads the same
    /// semantic colors without creating a second theme engine.
    ///
    /// Product direction: modern Windows desktop utility, not a generic glass/dashboard template.
    /// Keep one restrained accent, flatter surfaces, tighter radii and clear interaction hierarchy.
    /// </summary>
    internal static class FacmDesignSystem
    {
        private static ThemeDefinition Theme { get { return FacmThemeRuntime.Current; } }

        public static Color Canvas { get { return Theme.Background; } }
        public static Color CanvasRaised { get { return Theme.BackgroundSecondary; } }
        public static Color Surface { get { return Theme.Surface; } }
        public static Color SurfaceRaised { get { return Theme.SurfaceSecondary; } }
        public static Color SurfaceHover { get { return Blend(Theme.SurfaceSecondary, Theme.Accent, Theme.IsLight ? 0.04F : 0.07F); } }
        public static Color Border { get { return Theme.Border; } }
        public static Color BorderSoft { get { return Blend(Theme.Border, Theme.Background, Theme.IsLight ? 0.56F : 0.64F); } }
        public static Color Text { get { return Theme.TextPrimary; } }
        public static Color TextMuted { get { return Theme.TextMuted; } }
        public static Color Accent { get { return Theme.Accent; } }
        public static Color AccentSecondary { get { return Theme.AccentSecondary; } }
        public static Color Success { get { return Theme.Success; } }
        public static Color Warning { get { return Theme.Warning; } }
        public static Color Error { get { return Theme.IsLight ? Color.FromArgb(196, 53, 67) : Color.FromArgb(238, 92, 106); } }
        public static Color Disabled { get { return Blend(Theme.TextMuted, Theme.Background, 0.35F); } }

        // Clamp the legacy theme catalogue into a more disciplined product shape language.
        // Existing theme ids remain compatible, but the shared product chrome no longer turns
        // every card/control into a large rounded glass tile.
        public static int WindowRadius { get { return Math.Max(0, Math.Min(Theme.Radius, 12)); } }
        public static int CardRadius { get { return Math.Max(0, Math.Min(Theme.Radius, 8)); } }
        public static int ControlRadius { get { return Math.Max(0, Math.Min(Theme.ButtonRadius, 6)); } }

        public static Color Blend(Color source, Color target, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                source.A,
                source.R + (int)Math.Round((target.R - source.R) * amount),
                source.G + (int)Math.Round((target.G - source.G) * amount),
                source.B + (int)Math.Round((target.B - source.B) * amount));
        }

        public static void Round(Control control, int radius)
        {
            if (control == null || control.IsDisposed || control.Width <= 1 || control.Height <= 1)
                return;

            if (radius <= 0)
            {
                var previous = control.Region;
                control.Region = null;
                if (previous != null) previous.Dispose();
                return;
            }

            using (var path = RoundedRectangle(new Rectangle(0, 0, control.Width - 1, control.Height - 1), radius))
            {
                var previous = control.Region;
                control.Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 1 || bounds.Height <= 1 || radius <= 0)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ApplyLeagueSurface(Form form)
        {
            if (form == null || form.IsDisposed) return;
            form.BackColor = Canvas;
            form.ForeColor = Text;
            var fontName = Theme.FontName;
            if (form.Font == null || !string.Equals(form.Font.FontFamily.Name, fontName, StringComparison.OrdinalIgnoreCase))
                form.Font = new Font(fontName, 9F);
            ApplyRecursive(form);
        }

        public static void ApplyRecursive(Control root)
        {
            if (root == null || root.IsDisposed) return;
            Soften(root);
            root.ControlAdded -= HandleControlAdded;
            root.ControlAdded += HandleControlAdded;

            foreach (Control child in root.Controls)
                ApplyRecursive(child);
        }

        private static void HandleControlAdded(object sender, ControlEventArgs e)
        {
            ApplyRecursive(e.Control);
        }

        private static void HandleButtonResize(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button != null && !button.IsDisposed && !IsWindowChromeButton(button))
                Round(button, ResolveButtonRadius(button));
        }

        internal static int ResolveButtonRadius(Button button)
        {
            if (button == null) return ControlRadius;
            if (IsWindowChromeButton(button)) return 0;
            if (ControlRadius <= 0) return 0;
            return button.Height >= 42 ? Math.Max(4, ControlRadius) : Math.Max(3, ControlRadius - 1);
        }

        internal static bool IsWindowChromeButton(Control control)
        {
            return control != null && string.Equals(control.GetType().Name, "FacmChromeButton", StringComparison.Ordinal);
        }

        internal static Color ResolveOwnedBackground(Control control)
        {
            var parent = control == null ? null : control.Parent;
            while (parent != null)
            {
                var color = parent.BackColor;
                if (color != Color.Transparent && color.A > 0)
                    return color;
                parent = parent.Parent;
            }
            return Canvas;
        }

        private static void Soften(Control control)
        {
            var glass = control as FacmGlassPanel;
            if (glass != null)
            {
                glass.Radius = CardRadius;
                glass.BackColor = Surface;
                glass.Invalidate();
                return;
            }

            var nav = control as FacmNavButton;
            if (nav != null)
            {
                nav.BackColor = Surface;
                nav.ForeColor = TextMuted;
                nav.Font = new Font(Theme.FontName, 9F, FontStyle.Bold);
                nav.Invalidate();
                return;
            }

            var pill = control as FacmPillButton;
            if (pill != null)
            {
                pill.ForeColor = TextMuted;
                pill.Font = new Font(Theme.FontName, 8.6F, FontStyle.Bold);
                pill.Invalidate();
                return;
            }

            var button = control as Button;
            if (button != null)
            {
                // Window controls are owned by FacmWindowChrome and must not be restyled as
                // rounded business buttons. This also prevents the top-right glyph corruption
                // seen when global theming re-applies to an already owner-drawn chrome button.
                if (IsWindowChromeButton(button))
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 0;
                    button.BackColor = Color.Transparent;
                    button.ForeColor = TextMuted;
                    button.Resize -= HandleButtonResize;
                    Round(button, 0);
                    button.Invalidate();
                    return;
                }

                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = BorderSoft;
                button.FlatAppearance.MouseOverBackColor = SurfaceHover;
                button.FlatAppearance.MouseDownBackColor = Blend(SurfaceHover, Accent, 0.08F);
                button.BackColor = Blend(button.BackColor, SurfaceRaised, 0.10F);
                button.ForeColor = Text;
                button.Resize -= HandleButtonResize;
                button.Resize += HandleButtonResize;
                Round(button, ResolveButtonRadius(button));
                return;
            }

            var list = control as ListView;
            if (list != null)
            {
                list.BorderStyle = BorderStyle.None;
                list.BackColor = Blend(list.BackColor, CanvasRaised, 0.16F);
                list.ForeColor = Text;
                return;
            }

            var textBox = control as TextBoxBase;
            if (textBox != null)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = Blend(textBox.BackColor, Surface, 0.08F);
                textBox.ForeColor = Text;
                return;
            }

            var panel = control as Panel;
            if (panel != null && panel.BackColor != Color.Transparent && panel.BackColor.A > 0)
                panel.BackColor = Blend(panel.BackColor, CanvasRaised, 0.06F);
        }
    }

    /// <summary>
    /// FACM product surface card. The legacy class name is retained for compatibility, but the
    /// visual language is intentionally flatter and quieter than the former glass/highlight effect.
    /// </summary>
    internal class FacmGlassPanel : Panel
    {
        public FacmGlassPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            BackColor = FacmDesignSystem.Surface;
        }

        public int Radius { get; set; } = FacmDesignSystem.CardRadius;
        public bool AccentGlow { get; set; }
        public bool DrawBorder { get; set; } = true;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (var path = FacmDesignSystem.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (var fill = new SolidBrush(FacmDesignSystem.Surface))
            {
                e.Graphics.FillPath(fill, path);
            }

            // AccentGlow remains as a semantic opt-in, but is rendered as a restrained top rule
            // instead of decorative cyan/violet blobs.
            if (AccentGlow && Width > 8)
            {
                using (var accent = new SolidBrush(FacmDesignSystem.Accent))
                    e.Graphics.FillRectangle(accent, Math.Max(4, Radius), 0, Math.Max(1, Width - Math.Max(8, Radius * 2)), 2);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!DrawBorder || Width <= 1 || Height <= 1) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = FacmDesignSystem.RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (var pen = new Pen(FacmDesignSystem.BorderSoft, 1F))
                e.Graphics.DrawPath(pen, path);
        }
    }

    internal sealed class FacmNavButton : Button
    {
        private bool _hover;
        private bool _selected;

        public FacmNavButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = FacmDesignSystem.Surface;
            ForeColor = FacmDesignSystem.TextMuted;
            TextAlign = ContentAlignment.MiddleLeft;
            Padding = new Padding(14, 0, 8, 0);
            Cursor = Cursors.Hand;
            TabStop = true;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        }

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(FacmDesignSystem.ResolveOwnedBackground(this)))
                e.Graphics.FillRectangle(background, ClientRectangle);

            var bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                var radius = Math.Min(6, FacmDesignSystem.ControlRadius);
                using (var path = FacmDesignSystem.RoundedRectangle(bounds, radius))
                {
                    var fill = _selected
                        ? FacmDesignSystem.SurfaceRaised
                        : (_hover || Focused) ? FacmDesignSystem.SurfaceHover : FacmDesignSystem.Surface;
                    using (var brush = new SolidBrush(fill))
                        e.Graphics.FillPath(brush, path);

                    if (Focused && ShowFocusCues)
                    {
                        using (var focus = new Pen(FacmDesignSystem.Accent, 1F))
                            e.Graphics.DrawPath(focus, path);
                    }
                }

                if (_selected)
                {
                    using (var accent = new SolidBrush(FacmDesignSystem.Accent))
                        e.Graphics.FillRectangle(accent, 2, 8, 2, Math.Max(10, Height - 16));
                }
            }

            var textColor = _selected || _hover || Focused ? FacmDesignSystem.Text : FacmDesignSystem.TextMuted;
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                new Rectangle(14, 0, Math.Max(1, Width - 20), Height),
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class FacmPillButton : Button
    {
        private bool _hover;
        private bool _selected;

        public FacmPillButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            Font = new Font(FacmThemeRuntime.Current.FontName, 8.6F, FontStyle.Bold);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        }

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(FacmDesignSystem.ResolveOwnedBackground(this)))
                e.Graphics.FillRectangle(background, ClientRectangle);

            var bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                var radius = Math.Min(4, FacmDesignSystem.ControlRadius);
                using (var path = FacmDesignSystem.RoundedRectangle(bounds, radius))
                {
                    var fill = _selected
                        ? FacmDesignSystem.SurfaceRaised
                        : (_hover || Focused) ? FacmDesignSystem.SurfaceHover : Color.Transparent;
                    using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);

                    if (Focused && ShowFocusCues)
                    {
                        using (var focus = new Pen(FacmDesignSystem.Accent, 1F))
                            e.Graphics.DrawPath(focus, path);
                    }
                }

                if (_selected)
                {
                    using (var accent = new SolidBrush(FacmDesignSystem.Accent))
                        e.Graphics.FillRectangle(accent, 8, Math.Max(0, Height - 2), Math.Max(1, Width - 16), 2);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                _selected || _hover || Focused ? FacmDesignSystem.Text : FacmDesignSystem.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
