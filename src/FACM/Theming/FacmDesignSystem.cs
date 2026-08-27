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
    /// </summary>
    internal static class FacmDesignSystem
    {
        private static ThemeDefinition Theme { get { return FacmThemeRuntime.Current; } }

        public static Color Canvas { get { return Theme.Background; } }
        public static Color CanvasRaised { get { return Theme.BackgroundSecondary; } }
        public static Color Surface { get { return Theme.Surface; } }
        public static Color SurfaceRaised { get { return Theme.SurfaceSecondary; } }
        public static Color SurfaceHover { get { return Blend(Theme.SurfaceSecondary, Theme.Accent, Theme.IsLight ? 0.08F : 0.16F); } }
        public static Color Border { get { return Theme.Border; } }
        public static Color BorderSoft { get { return Blend(Theme.Border, Theme.Background, Theme.IsLight ? 0.40F : 0.48F); } }
        public static Color Text { get { return Theme.TextPrimary; } }
        public static Color TextMuted { get { return Theme.TextMuted; } }
        public static Color Accent { get { return Theme.Accent; } }
        public static Color AccentSecondary { get { return Theme.AccentSecondary; } }
        public static Color Success { get { return Theme.Success; } }
        public static Color Warning { get { return Theme.Warning; } }
        public static Color Error { get { return Theme.IsLight ? Color.FromArgb(196, 53, 67) : Color.FromArgb(238, 92, 106); } }
        public static Color Disabled { get { return Blend(Theme.TextMuted, Theme.Background, 0.35F); } }

        public static int WindowRadius { get { return Math.Max(0, Theme.Radius); } }
        public static int CardRadius { get { return Math.Max(0, Math.Min(Theme.Radius, 14)); } }
        public static int ControlRadius { get { return Math.Max(0, Math.Min(Theme.ButtonRadius, 10)); } }

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
            if (control == null || control.IsDisposed || control.Width <= 1 || control.Height <= 1 || radius <= 0)
                return;

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
            if (button != null && !button.IsDisposed)
                Round(button, button.Height >= 42 ? Math.Max(6, ControlRadius) : Math.Max(5, ControlRadius - 1));
        }

        private static void Soften(Control control)
        {
            if (control is FacmGlassPanel || control is FacmNavButton || control is FacmPillButton)
                return;

            var button = control as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = SurfaceHover;
                button.FlatAppearance.MouseDownBackColor = Blend(SurfaceHover, Accent, 0.12F);
                button.BackColor = Blend(button.BackColor, SurfaceRaised, 0.18F);
                button.ForeColor = Text;
                button.Resize -= HandleButtonResize;
                button.Resize += HandleButtonResize;
                Round(button, button.Height >= 42 ? Math.Max(6, ControlRadius) : Math.Max(5, ControlRadius - 1));
                return;
            }

            var list = control as ListView;
            if (list != null)
            {
                list.BorderStyle = BorderStyle.None;
                list.BackColor = Blend(list.BackColor, CanvasRaised, 0.22F);
                list.ForeColor = Text;
                return;
            }

            var textBox = control as TextBoxBase;
            if (textBox != null)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = Blend(textBox.BackColor, Surface, 0.12F);
                textBox.ForeColor = Text;
                return;
            }

            var panel = control as Panel;
            if (panel != null && panel.BackColor != Color.Transparent && panel.BackColor.A > 0)
                panel.BackColor = Blend(panel.BackColor, CanvasRaised, 0.10F);
        }
    }

    /// <summary>
    /// Opaque glass-like card: layered gradients, faint highlight and a soft 1px border.
    /// It intentionally does not capture/blur the desktop behind the window.
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
            using (var gradient = new LinearGradientBrush(
                ClientRectangle,
                FacmDesignSystem.SurfaceRaised,
                FacmDesignSystem.Surface,
                118F))
            {
                e.Graphics.FillPath(gradient, path);

                using (var highlight = new SolidBrush(Color.FromArgb(18, Color.White)))
                    e.Graphics.FillEllipse(highlight, -Width / 5, -Height / 2, Width, Height);

                if (AccentGlow)
                {
                    using (var cyan = new SolidBrush(Color.FromArgb(22, FacmDesignSystem.Accent)))
                        e.Graphics.FillEllipse(cyan, Width - Math.Max(140, Width / 2), -Height / 2, Math.Max(180, Width / 2), Height + 30);
                    using (var violet = new SolidBrush(Color.FromArgb(14, FacmDesignSystem.AccentSecondary)))
                        e.Graphics.FillEllipse(violet, Width - 100, -30, 150, Math.Max(100, Height));
                }
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
            BackColor = Color.Transparent;
            ForeColor = FacmDesignSystem.TextMuted;
            TextAlign = ContentAlignment.MiddleLeft;
            Padding = new Padding(14, 0, 8, 0);
            Cursor = Cursors.Hand;
            TabStop = false;
            Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = FacmDesignSystem.RoundedRectangle(bounds, Math.Max(6, FacmDesignSystem.ControlRadius)))
            {
                var fill = _selected
                    ? FacmDesignSystem.Blend(FacmDesignSystem.SurfaceRaised, FacmDesignSystem.Accent, 0.24F)
                    : _hover ? FacmDesignSystem.SurfaceHover : Color.FromArgb(8, FacmDesignSystem.Surface);
                using (var brush = new SolidBrush(fill))
                    e.Graphics.FillPath(brush, path);

                if (_selected)
                {
                    using (var accent = new SolidBrush(FacmDesignSystem.Accent))
                        e.Graphics.FillRoundedRectangle(accent, new Rectangle(3, 10, 3, Math.Max(12, Height - 20)), 2);
                }
            }

            var textColor = _selected ? FacmDesignSystem.Text : (_hover ? FacmDesignSystem.Text : FacmDesignSystem.TextMuted);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                new Rectangle(15, 0, Math.Max(1, Width - 20), Height),
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
            TabStop = false;
            Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Bold);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = FacmDesignSystem.RoundedRectangle(bounds, Math.Max(6, Height / 2)))
            {
                var fill = _selected
                    ? FacmDesignSystem.Blend(FacmDesignSystem.SurfaceRaised, FacmDesignSystem.Accent, 0.24F)
                    : _hover ? FacmDesignSystem.SurfaceHover : FacmDesignSystem.Surface;
                var border = _selected ? FacmDesignSystem.Accent : FacmDesignSystem.Border;
                using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                using (var pen = new Pen(border, 1F)) e.Graphics.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                _selected ? FacmDesignSystem.Text : FacmDesignSystem.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
