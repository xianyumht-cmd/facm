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

            Text = "FACM 主题";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(680, 590);
            BackColor = Color.FromArgb(12, 17, 28);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = "选择控制面板主题",
                Location = new Point(24, 18),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = "选择喜欢的界面风格，应用后立即生效。",
                Location = new Point(26, 54),
                Size = new Size(620, 24),
                ForeColor = Color.FromArgb(155, 169, 196),
                Font = new Font("Microsoft YaHei UI", 9F)
            };

            _list = new FlowLayoutPanel
            {
                Location = new Point(22, 88),
                Size = new Size(636, 418),
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(3),
                BackColor = Color.FromArgb(8, 12, 21)
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
                ForeColor = Color.FromArgb(195, 208, 232),
                Font = new Font("Microsoft YaHei UI", 8.6F)
            };

            var cancel = new Button
            {
                Text = "取消",
                Location = new Point(480, 523),
                Size = new Size(78, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(31, 41, 61),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel,
                TabStop = false
            };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(65, 82, 115);

            _applyButton = new Button
            {
                Text = "应用主题",
                Location = new Point(566, 523),
                Size = new Size(92, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 113, 255),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            _applyButton.FlatAppearance.BorderColor = Color.FromArgb(112, 151, 255);
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
            _applyButton.BackColor = clicked.Theme.Accent;
            _applyButton.FlatAppearance.BorderColor = clicked.Theme.AccentSecondary;
        }

        private void ApplySelection()
        {
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

                var primary = Theme.IsLight ? Theme.TextPrimary : Color.White;
                TextRenderer.DrawText(
                    e.Graphics,
                    Theme.Name,
                    new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                    new Rectangle(76, 14, 204, 25),
                    primary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(
                    e.Graphics,
                    Theme.Description,
                    new Font("Microsoft YaHei UI", 8F),
                    new Rectangle(76, 39, 204, 24),
                    Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
