using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace FACM.Pets
{
    internal sealed class AnimalPetPickerForm : Form
    {
        private readonly ListBox _list;
        private readonly PictureBox _preview;
        private readonly Label _name;
        private readonly Label _description;
        private readonly Timer _previewTimer;
        private CancellationTokenSource _assetCancellation;
        private Bitmap _sheet;
        private double _animationSeconds;

        public AnimalPetPickerForm(string currentPetId)
        {
            Text = "FACM · 桌面宠物";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(790, 560);
            BackColor = Color.FromArgb(12, 17, 28);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = "桌面宠物",
                Location = new Point(26, 20),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = "选择喜欢的桌面宠物。部分桌宠首次使用时需要联网准备资源。",
                Location = new Point(28, 58),
                Size = new Size(730, 30),
                ForeColor = Color.FromArgb(160, 174, 198)
            };

            _list = new ListBox
            {
                Location = new Point(26, 96),
                Size = new Size(258, 392),
                BackColor = Color.FromArgb(8, 12, 21),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                ItemHeight = 34,
                DisplayMember = "Name",
                Font = new Font("Microsoft YaHei UI", 10F)
            };
            foreach (var pet in AnimalPetCatalog.All) _list.Items.Add(pet);

            var previewPanel = new Panel
            {
                Location = new Point(306, 96),
                Size = new Size(454, 314),
                BackColor = Color.FromArgb(8, 12, 21),
                BorderStyle = BorderStyle.FixedSingle
            };
            _preview = new PictureBox
            {
                Location = new Point(69, 20),
                Size = new Size(314, 260),
                BackColor = Color.FromArgb(8, 12, 21),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            previewPanel.Controls.Add(_preview);

            _name = new Label
            {
                Location = new Point(308, 426),
                Size = new Size(450, 30),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold)
            };
            _description = new Label
            {
                Location = new Point(308, 458),
                Size = new Size(450, 48),
                ForeColor = Color.FromArgb(189, 201, 220)
            };

            var close = new Button
            {
                Text = "关闭",
                Location = new Point(548, 510),
                Size = new Size(100, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(31, 41, 61),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel
            };
            close.FlatAppearance.BorderColor = Color.FromArgb(65, 82, 115);

            var apply = new Button
            {
                Text = "应用桌宠",
                Location = new Point(660, 510),
                Size = new Size(100, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 113, 255),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            apply.FlatAppearance.BorderColor = Color.FromArgb(112, 151, 255);
            apply.Click += delegate
            {
                var selected = _list.SelectedItem as AnimalPetDefinition;
                if (selected == null) return;
                SelectedPetId = selected.Id;
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_list);
            Controls.Add(previewPanel);
            Controls.Add(_name);
            Controls.Add(_description);
            Controls.Add(close);
            Controls.Add(apply);

            _list.SelectedIndexChanged += async delegate { await UpdateSelectionAsync(); };
            _list.DoubleClick += delegate { apply.PerformClick(); };

            var selectedIndex = 0;
            for (var index = 0; index < AnimalPetCatalog.All.Count; index++)
            {
                if (!string.Equals(AnimalPetCatalog.All[index].Id, currentPetId, StringComparison.OrdinalIgnoreCase)) continue;
                selectedIndex = index;
                break;
            }
            _list.SelectedIndex = selectedIndex;

            _previewTimer = new Timer { Interval = 33 };
            _previewTimer.Tick += delegate
            {
                _animationSeconds += 0.033;
                UpdatePreviewOnly();
            };
            _previewTimer.Start();

            CancelButton = close;
            FormClosed += delegate
            {
                _previewTimer.Stop();
                _previewTimer.Dispose();
                CancelAssetRequest();
                var image = _preview.Image;
                _preview.Image = null;
                if (image != null) image.Dispose();
                if (_sheet != null)
                {
                    _sheet.Dispose();
                    _sheet = null;
                }
            };
        }

        public string SelectedPetId { get; private set; }

        private async Task UpdateSelectionAsync()
        {
            var pet = _list.SelectedItem as AnimalPetDefinition;
            if (pet == null) return;
            _name.Text = pet.Name;
            _description.Text = pet.Description + "\r\n来源：" + pet.AssetAuthor + " · " + pet.AssetLicense + "；长按可拖动，托盘可复位。";
            _animationSeconds = 0;
            CancelAssetRequest();

            if (pet.Runtime == AnimalPetRuntime.VPetCore)
            {
                if (_sheet != null)
                {
                    _sheet.Dispose();
                    _sheet = null;
                }
                RenderVPetPreview(pet);
                return;
            }

            _assetCancellation = new CancellationTokenSource();
            var token = _assetCancellation.Token;
            var expectedId = pet.Id;
            Bitmap loaded = null;
            try
            {
                loaded = await SpritePetAssetService.LoadAsync(pet, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                loaded = null;
            }

            if (IsDisposed || token.IsCancellationRequested)
            {
                if (loaded != null) loaded.Dispose();
                return;
            }
            var current = _list.SelectedItem as AnimalPetDefinition;
            if (current == null || !string.Equals(current.Id, expectedId, StringComparison.OrdinalIgnoreCase))
            {
                if (loaded != null) loaded.Dispose();
                return;
            }

            var old = _sheet;
            _sheet = loaded;
            if (old != null) old.Dispose();
            _preview.Tag = null;
            UpdatePreviewOnly();
        }

        private void UpdatePreviewOnly()
        {
            var pet = _list.SelectedItem as AnimalPetDefinition;
            if (pet == null || IsDisposed) return;
            if (pet.Runtime == AnimalPetRuntime.VPetCore)
            {
                if (!string.Equals(_preview.Tag as string, pet.Id, StringComparison.Ordinal)) RenderVPetPreview(pet);
                return;
            }

            var frameCount = Math.Max(1, pet.FrameCount);
            var frame = (int)(_animationSeconds * Math.Max(1f, pet.FramesPerSecond)) % frameCount;
            var direction = pet.DirectionalRows ? 0 : pet.AnimationRow;
            using (var rendered = SpritePetWindow.RenderForSmokeTest(pet, _sheet, frame, direction, true))
            {
                var canvas = new Bitmap(_preview.Width, _preview.Height);
                using (var graphics = Graphics.FromImage(canvas))
                {
                    graphics.Clear(_preview.BackColor);
                    graphics.InterpolationMode = pet.PixelArt
                        ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
                        : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    var side = Math.Min(_preview.Width, _preview.Height) - 24;
                    var x = (_preview.Width - side) / 2;
                    var y = (_preview.Height - side) / 2;
                    graphics.DrawImage(rendered, new Rectangle(x, y, side, side));
                }
                ReplacePreview(canvas, null);
            }
        }

        private void RenderVPetPreview(AnimalPetDefinition pet)
        {
            var canvas = new Bitmap(_preview.Width, _preview.Height);
            using (var graphics = Graphics.FromImage(canvas))
            using (var titleFont = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold))
            using (var bodyFont = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular))
            using (var titleBrush = new SolidBrush(Color.FromArgb(232, 240, 255)))
            using (var bodyBrush = new SolidBrush(Color.FromArgb(162, 184, 219)))
            using (var linePen = new Pen(Color.FromArgb(82, 118, 224), 2F))
            {
                graphics.Clear(_preview.BackColor);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                graphics.DrawString("高精度桌宠", titleFont, titleBrush, new RectangleF(12, 38, _preview.Width - 24, 52), format);
                graphics.DrawLine(linePen, 76, 103, _preview.Width - 76, 103);
                graphics.DrawString(
                    "待机 · 移动 · 触摸 · 拖动\r\n\r\n应用后即可使用",
                    bodyFont,
                    bodyBrush,
                    new RectangleF(18, 112, _preview.Width - 36, 124),
                    format);
            }
            ReplacePreview(canvas, pet.Id);
        }

        private void ReplacePreview(Image image, string tag)
        {
            var old = _preview.Image;
            _preview.Image = image;
            _preview.Tag = tag;
            if (old != null) old.Dispose();
        }

        private void CancelAssetRequest()
        {
            if (_assetCancellation == null) return;
            _assetCancellation.Cancel();
            _assetCancellation.Dispose();
            _assetCancellation = null;
        }
    }
}
