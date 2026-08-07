using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FACM.Pets
{
    internal sealed class PetPickerForm : Form
    {
        private readonly FlowLayoutPanel _list;
        private readonly Label _selectedLabel;
        private readonly PreviewPanel _preview;
        private string _selectedPetId;

        public PetPickerForm(string currentPetId)
        {
            _selectedPetId = PetCatalog.Get(currentPetId).Id;

            Text = "FACM 桌面宠物";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(720, 590);
            BackColor = Color.FromArgb(12, 17, 28);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = "选择桌面宠物",
                Location = new Point(24, 18),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = "桌宠样式与控制面板主题完全独立。选择后立即替换悬浮球外观。",
                Location = new Point(26, 54),
                Size = new Size(650, 24),
                ForeColor = Color.FromArgb(155, 169, 196)
            };

            _list = new FlowLayoutPanel
            {
                Location = new Point(22, 88),
                Size = new Size(450, 420),
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(3),
                BackColor = Color.FromArgb(8, 12, 21)
            };

            foreach (var pet in PetCatalog.All)
            {
                var choice = new PetChoiceButton(pet)
                {
                    Selected = string.Equals(pet.Id, _selectedPetId, StringComparison.OrdinalIgnoreCase)
                };
                choice.Click += SelectPet;
                choice.DoubleClick += delegate
                {
                    SelectPet(choice, EventArgs.Empty);
                    ApplySelection();
                };
                _list.Controls.Add(choice);
            }

            _preview = new PreviewPanel
            {
                Location = new Point(492, 88),
                Size = new Size(206, 250),
                Pet = PetCatalog.Get(_selectedPetId),
                BackColor = Color.FromArgb(8, 12, 21)
            };

            _selectedLabel = new Label
            {
                Location = new Point(492, 354),
                Size = new Size(206, 90),
                ForeColor = Color.FromArgb(205, 215, 236),
                Font = new Font("Microsoft YaHei UI", 9F),
                Text = BuildSelectedText(PetCatalog.Get(_selectedPetId))
            };

            var cancel = new Button
            {
                Text = "取消",
                Location = new Point(500, 520),
                Size = new Size(88, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(31, 41, 61),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel,
                TabStop = false
            };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(65, 82, 115);

            var apply = new Button
            {
                Text = "应用桌宠",
                Location = new Point(598, 520),
                Size = new Size(100, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 113, 255),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            apply.FlatAppearance.BorderColor = Color.FromArgb(112, 151, 255);
            apply.Click += delegate { ApplySelection(); };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_list);
            Controls.Add(_preview);
            Controls.Add(_selectedLabel);
            Controls.Add(cancel);
            Controls.Add(apply);

            AcceptButton = apply;
            CancelButton = cancel;
        }

        public string SelectedPetId
        {
            get { return _selectedPetId; }
        }

        private void SelectPet(object sender, EventArgs e)
        {
            var clicked = sender as PetChoiceButton;
            if (clicked == null) return;

            _selectedPetId = clicked.Pet.Id;
            foreach (Control control in _list.Controls)
            {
                var choice = control as PetChoiceButton;
                if (choice == null) continue;
                choice.Selected = ReferenceEquals(choice, clicked);
                choice.Invalidate();
            }

            _preview.Pet = clicked.Pet;
            _preview.Invalidate();
            _selectedLabel.Text = BuildSelectedText(clicked.Pet);
        }

        private void ApplySelection()
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string BuildSelectedText(PetDefinition pet)
        {
            return pet.Name + Environment.NewLine +
                   pet.Description + Environment.NewLine + Environment.NewLine +
                   "尺寸：" + pet.Size.Width + " × " + pet.Size.Height +
                   Environment.NewLine + "可拖动停放在桌面任意可见位置";
        }

        private sealed class PreviewPanel : Panel
        {
            private readonly Timer _timer;
            private float _phase;

            public PreviewPanel()
            {
                DoubleBuffered = true;
                _timer = new Timer { Interval = 40 };
                _timer.Tick += delegate
                {
                    _phase += 0.08F;
                    Invalidate();
                };
                _timer.Start();
                Disposed += delegate { _timer.Dispose(); };
            }

            public PetDefinition Pet { get; set; }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Pet == null) return;

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var state = e.Graphics.Save();
                var x = (Width - Pet.Size.Width) / 2F;
                var y = (Height - Pet.Size.Height) / 2F - 10F;
                e.Graphics.TranslateTransform(x, y);
                e.Graphics.SetClip(new Rectangle(0, 0, Pet.Size.Width, Pet.Size.Height));
                PetRenderer.Draw(e.Graphics, Pet, _phase, 0F, true);
                e.Graphics.Restore(state);

                TextRenderer.DrawText(
                    e.Graphics,
                    Pet.Name,
                    new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                    new Rectangle(8, Height - 34, Width - 16, 24),
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private sealed class PetChoiceButton : Control
        {
            private bool _hovered;

            public PetChoiceButton(PetDefinition pet)
            {
                Pet = pet;
                Size = new Size(208, 92);
                Margin = new Padding(5);
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
                TabStop = true;
                SetStyle(ControlStyles.Selectable, true);
                MouseEnter += delegate { _hovered = true; Invalidate(); };
                MouseLeave += delegate { _hovered = false; Invalidate(); };
            }

            public PetDefinition Pet { get; private set; }
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
                using (var path = Rounded(bounds, 13))
                using (var fill = new LinearGradientBrush(bounds, Color.FromArgb(26, 36, 56), Color.FromArgb(17, 24, 40), 30F))
                using (var border = new Pen(Selected ? Pet.Primary : (_hovered ? Pet.Secondary : Color.FromArgb(55, 70, 98)), Selected ? 2.2F : 1.2F))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }

                var state = e.Graphics.Save();
                e.Graphics.TranslateTransform(10F, 9F);
                var scale = Math.Min(62F / Pet.Size.Width, 62F / Pet.Size.Height);
                e.Graphics.ScaleTransform(scale, scale);
                PetRenderer.Draw(e.Graphics, Pet, 0.7F, _hovered ? 1F : 0F, true);
                e.Graphics.Restore(state);

                TextRenderer.DrawText(
                    e.Graphics,
                    Pet.Name,
                    new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                    new Rectangle(82, 17, 112, 25),
                    Color.White,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(
                    e.Graphics,
                    Pet.Description,
                    new Font("Microsoft YaHei UI", 7.8F),
                    new Rectangle(82, 43, 112, 34),
                    Color.FromArgb(160, 177, 208),
                    TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            }

            private static GraphicsPath Rounded(Rectangle bounds, int radius)
            {
                var path = new GraphicsPath();
                var diameter = radius * 2;
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
