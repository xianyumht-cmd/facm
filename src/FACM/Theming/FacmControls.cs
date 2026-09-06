using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FACM.Theming
{
    internal enum FacmButtonTone
    {
        Secondary,
        Primary,
        Danger
    }

    internal enum FacmStatusTone
    {
        Neutral,
        Accent,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Shared FACM action button. It keeps native Button focus/click/keyboard semantics while
    /// painting from FacmDesignSystem so normal product surfaces no longer carry private palettes.
    /// </summary>
    internal sealed class FacmActionButton : Button
    {
        private bool _hovered;
        private bool _pressed;
        private FacmButtonTone _tone = FacmButtonTone.Secondary;

        public FacmActionButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public FacmButtonTone Tone
        {
            get { return _tone; }
            set
            {
                if (_tone == value) return;
                _tone = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Enabled) _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            var fill = ResolveFill();
            var border = ResolveBorder();
            if (_hovered && Enabled) fill = FacmDesignSystem.Blend(fill, Color.White, FacmThemeRuntime.Current.IsLight ? 0.05F : 0.10F);
            if (_pressed && Enabled) fill = FacmDesignSystem.Blend(fill, Color.Black, FacmThemeRuntime.Current.IsLight ? 0.06F : 0.14F);
            if (!Enabled) fill = FacmDesignSystem.Blend(fill, FacmDesignSystem.Canvas, 0.48F);

            using (var path = FacmDesignSystem.RoundedRectangle(bounds, FacmDesignSystem.ControlRadius))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            var textColor = Enabled
                ? (_tone == FacmButtonTone.Secondary && FacmThemeRuntime.Current.IsLight ? FacmDesignSystem.Text : Color.White)
                : FacmDesignSystem.Disabled;
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            if (Focused && ShowFocusCues)
            {
                var focus = Rectangle.Inflate(bounds, -3, -3);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, textColor, Color.Transparent);
            }
        }

        private Color ResolveFill()
        {
            if (_tone == FacmButtonTone.Primary) return FacmDesignSystem.Accent;
            if (_tone == FacmButtonTone.Danger) return FacmDesignSystem.Error;
            return FacmDesignSystem.SurfaceRaised;
        }

        private Color ResolveBorder()
        {
            if (_tone == FacmButtonTone.Primary) return FacmDesignSystem.Blend(FacmDesignSystem.Accent, Color.White, 0.16F);
            if (_tone == FacmButtonTone.Danger) return FacmDesignSystem.Blend(FacmDesignSystem.Error, Color.White, 0.12F);
            return FacmDesignSystem.BorderSoft;
        }
    }

    /// <summary>
    /// Keyboard-accessible CheckBox rendered as the common FACM switch. The Checked property and
    /// CheckedChanged event remain standard WinForms contracts.
    /// </summary>
    internal sealed class FacmToggleSwitch : CheckBox
    {
        private bool _hovered;

        public FacmToggleSwitch()
        {
            AutoSize = false;
            Height = 32;
            Cursor = Cursors.Hand;
            TextAlign = ContentAlignment.MiddleLeft;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var switchWidth = 42;
            var switchHeight = 22;
            var switchLeft = Math.Max(2, Width - switchWidth - 2);
            var switchTop = Math.Max(1, (Height - switchHeight) / 2);
            var track = new Rectangle(switchLeft, switchTop, switchWidth, switchHeight);

            var trackColor = Checked ? FacmDesignSystem.Accent : FacmDesignSystem.SurfaceRaised;
            if (_hovered && Enabled) trackColor = FacmDesignSystem.Blend(trackColor, Color.White, FacmThemeRuntime.Current.IsLight ? 0.05F : 0.10F);
            if (!Enabled) trackColor = FacmDesignSystem.Blend(trackColor, FacmDesignSystem.Canvas, 0.42F);

            using (var path = FacmDesignSystem.RoundedRectangle(track, switchHeight / 2))
            using (var brush = new SolidBrush(trackColor))
            using (var pen = new Pen(Checked ? FacmDesignSystem.Accent : FacmDesignSystem.BorderSoft, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            var knobSize = 16;
            var knobLeft = Checked ? track.Right - knobSize - 3 : track.Left + 3;
            var knobTop = track.Top + (track.Height - knobSize) / 2;
            using (var knob = new SolidBrush(Enabled ? Color.White : FacmDesignSystem.Disabled))
                e.Graphics.FillEllipse(knob, knobLeft, knobTop, knobSize, knobSize);

            var textBounds = new Rectangle(0, 0, Math.Max(1, switchLeft - 10), Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textBounds,
                Enabled ? FacmDesignSystem.Text : FacmDesignSystem.Disabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -2), FacmDesignSystem.Text, Color.Transparent);
        }
    }

    /// <summary>Compact semantic state indicator shared by update, League and diagnostic surfaces.</summary>
    internal sealed class FacmStatusBadge : Label
    {
        private FacmStatusTone _tone;

        public FacmStatusBadge()
        {
            AutoSize = false;
            TextAlign = ContentAlignment.MiddleCenter;
            Font = new Font(FacmThemeRuntime.Current.FontName, 8F, FontStyle.Bold);
            BackColor = Color.Transparent;
        }

        public FacmStatusTone Tone
        {
            get { return _tone; }
            set
            {
                if (_tone == value) return;
                _tone = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var toneColor = ResolveToneColor();
            var fill = FacmDesignSystem.Blend(FacmDesignSystem.Surface, toneColor, FacmThemeRuntime.Current.IsLight ? 0.10F : 0.20F);
            using (var path = FacmDesignSystem.RoundedRectangle(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), Math.Max(6, FacmDesignSystem.ControlRadius)))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(FacmDesignSystem.Blend(FacmDesignSystem.BorderSoft, toneColor, 0.42F), 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                toneColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private Color ResolveToneColor()
        {
            if (_tone == FacmStatusTone.Accent) return FacmDesignSystem.Accent;
            if (_tone == FacmStatusTone.Success) return FacmDesignSystem.Success;
            if (_tone == FacmStatusTone.Warning) return FacmDesignSystem.Warning;
            if (_tone == FacmStatusTone.Error) return FacmDesignSystem.Error;
            return FacmDesignSystem.TextMuted;
        }
    }
}
