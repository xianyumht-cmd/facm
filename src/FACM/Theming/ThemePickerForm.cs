using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FACM.Theming
{
    internal sealed class ThemePickerForm : Form
    {
        private readonly FlowLayoutPanel _list;
        private readonly Label _selectedLabel;
        private readonly Button _applyButton;
        private string _selectedThemeId;

        public ThemePickerForm(string currentThemeId)
        {
            _selectedThemeId = ThemeCatalog.Get(currentThemeId).Id;
            var current = ThemeCatalog.Get(_selectedThemeId);

            Text = ThemePickerUiText.WindowTitle;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(680, 590);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            var title = new Label
            {
                Text = ThemePickerUiText.Title,
                Location = new Point(24, 18),
                AutoSize = true,
                ForeColor = FacmDesignSystem.Text,
                Font = new Font(FacmThemeRuntime.Current.FontName, 15F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = ThemePickerUiText.Hint,
                Location = new Point(26, 54),
                Size = new Size(620, 24),
                ForeColor = FacmDesignSystem.TextMuted,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
            };

            _list = new FlowLayoutPanel
            {
                Location = new Point(22, 88),
                Size = new Size(636, 418),
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(3),
                BackColor = FacmDesignSystem.Surface
            };

            foreach (var theme in ThemeCatalog.All)
            {
                var choice = new ThemeChoiceButton(theme)
                {
                    Selected = string.Equals(theme.Id, _selectedThemeId, StringComparison.OrdinalIgnoreCase)
                };
                choice.Click += SelectTheme;
                choice.DoubleClick += delegate
                {
                    SelectTheme(choice, EventArgs.Empty);
                    ApplySelection();
                };
                _list.Controls.Add(choice);
            }

            _selectedLabel = new Label
            {
                Text = BuildSelectedText(current),
                Location = new Point(24, 520),
                Size = new Size(420, 44),
                ForeColor = FacmDesignSystem.TextMuted,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F)
            };

            var cancel = new FacmActionButton
            {
                Text = "取消",
                Location = new Point(480, 523),
                Size = new Size(78, 38),
                Tone = FacmButtonTone.Secondary,
                DialogResult = DialogResult.Cancel,
                TabStop = true
            };

            _applyButton = new FacmActionButton
            {
                Text = "应用主题",
                Location = new Point(566, 523),
                Size = new Size(92, 38),
                Tone = FacmButtonTone.Primary,
                TabStop = true
            };
            _applyButton.Click += delegate { ApplySelection(); };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_list);
            Controls.Add(_selectedLabel);
            Controls.Add(cancel);
            Controls.Add(_applyButton);

            AcceptButton = _applyButton;
            CancelButton = cancel;
        }

        public string SelectedThemeId
        {
            get { return _selectedThemeId; }
        }

        private void SelectTheme(object sender, EventArgs e)
        {
            var clicked = sender as ThemeChoiceButton;
            if (clicked == null) return;

            _selectedThemeId = clicked.Theme.Id;
            foreach (Control control in _list.Controls)
            {
                var item = control as ThemeChoiceButton;
                if (item == null) continue;
                item.Selected = ReferenceEquals(item, clicked);
                item.Invalidate();
            }
            _selectedLabel.Text = BuildSelectedText(clicked.Theme);
            _applyButton.Invalidate();
        }

        private void ApplySelection()
        {
            FacmThemeRuntime.SetCurrent(_selectedThemeId);
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string BuildSelectedText(ThemeDefinition theme)
        {
            return theme.Name + "  ·  " + theme.Description;
        }

        private sealed class ThemeChoiceButton : Control
        {
            private bool _hovered;

            public ThemeChoiceButton(ThemeDefinition theme)
            {
                Theme = theme;
                Size = new Size(300, 76);
                Margin = new Padding(5);
                Cursor = Cursors.Hand;
                AccessibleName = theme.Name;
                AccessibleDescription = theme.Description;
                DoubleBuffered = true;
                TabStop = true;
                SetStyle(ControlStyles.Selectable, true);
                MouseEnter += delegate { _hovered = true; Invalidate(); };
                MouseLeave += delegate { _hovered = false; Invalidate(); };
            }

            public ThemeDefinition Theme { get; private set; }
            public bool Selected { get; set; }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Focus();
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
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
                var radius = Theme.UsesAngularCorners ? 3 : 13;
                using (var path = CreatePath(bounds, radius, Theme.UsesAngularCorners))
                using (var background = new LinearGradientBrush(bounds, Theme.Surface, Theme.SurfaceSecondary, 12F))
                using (var border = new Pen(Selected ? Theme.AccentSecondary : (_hovered ? Theme.Accent : Theme.Border), Selected ? 2.2F : 1.2F))
                {
                    e.Graphics.FillPath(background, path);
                    e.Graphics.DrawPath(border, path);
                }

                var swatch = new Rectangle(14, 14, 48, 48);
                using (var swatchPath = CreatePath(swatch, Theme.UsesAngularCorners ? 2 : 10, Theme.UsesAngularCorners))
                using (var swatchBrush = new LinearGradientBrush(swatch, Theme.Accent, Theme.AccentSecondary, 35F))
                {
                    e.Graphics.FillPath(swatchBrush, swatchPath);
                }

                using (var nameFont = new Font(FacmThemeRuntime.Current.FontName, 10F, FontStyle.Bold))
                using (var descriptionFont = new Font(FacmThemeRuntime.Current.FontName, 9F))
                {
                    TextRenderer.DrawText(e.Graphics, Theme.Name, nameFont,
                        new Rectangle(76, 13, 204, 27),
                        FacmDesignSystem.ReadableTextOn(Theme.Surface, Theme.TextPrimary),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(e.Graphics, Theme.Description, descriptionFont,
                        new Rectangle(76, 40, 204, 24),
                        FacmDesignSystem.ReadableTextOn(Theme.SurfaceSecondary, Theme.TextMuted),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4),
                        FacmDesignSystem.Text, Theme.Surface);
            }

            private static GraphicsPath CreatePath(Rectangle bounds, int radius, bool angular)
            {
                var path = new GraphicsPath();
                if (angular)
                {
                    var cut = Math.Max(3, radius + 5);
                    path.AddPolygon(new[]
                    {
                        new Point(bounds.Left + cut, bounds.Top),
                        new Point(bounds.Right - cut, bounds.Top),
                        new Point(bounds.Right, bounds.Top + cut),
                        new Point(bounds.Right, bounds.Bottom - cut),
                        new Point(bounds.Right - cut, bounds.Bottom),
                        new Point(bounds.Left + cut, bounds.Bottom),
                        new Point(bounds.Left, bounds.Bottom - cut),
                        new Point(bounds.Left, bounds.Top + cut)
                    });
                    path.CloseFigure();
                    return path;
                }

                var diameter = Math.Max(2, radius * 2);
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
