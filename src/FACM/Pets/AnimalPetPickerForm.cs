using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using Timer = System.Windows.Forms.Timer;

namespace FACM.Pets
{
    internal sealed class AnimalPetPickerForm : Form
    {
        private readonly string _currentPetId;
        private readonly ListBox _list;
        private readonly PictureBox _preview;
        private readonly Label _name;
        private readonly Label _runtimeBadge;
        private readonly Label _behavior;
        private readonly Label _description;
        private readonly Label _interaction;
        private readonly Label _currentStatus;
        private readonly Button _apply;
        private readonly Font _listNameFont;
        private readonly Font _listMetaFont;
        private readonly Font _listCurrentFont;
        private readonly Timer _previewTimer;
        private CancellationTokenSource _assetCancellation;
        private Bitmap _sheet;
        private double _animationSeconds;

        public AnimalPetPickerForm(string currentPetId)
        {
            _currentPetId = currentPetId ?? string.Empty;
            _listNameFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            _listMetaFont = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular);
            _listCurrentFont = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);

            Text = T(UiTextKeys.PetPickerWindowTitle);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(820, 590);
            BackColor = Color.FromArgb(12, 17, 28);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = T(UiTextKeys.PetPickerTitle),
                Location = new Point(26, 20),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = T(UiTextKeys.PetPickerHint),
                Location = new Point(28, 60),
                Size = new Size(660, 26),
                ForeColor = Color.FromArgb(160, 174, 198)
            };
            _currentStatus = new Label
            {
                Location = new Point(610, 22),
                Size = new Size(178, 28),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(151, 173, 215),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };

            _list = new ListBox
            {
                Location = new Point(26, 100),
                Size = new Size(284, 410),
                BackColor = Color.FromArgb(8, 12, 21),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                ItemHeight = 62,
                DrawMode = DrawMode.OwnerDrawFixed,
                Font = new Font("Microsoft YaHei UI", 10F)
            };
            foreach (var pet in AnimalPetCatalog.Visible) _list.Items.Add(pet);
            _list.DrawItem += DrawPetItem;

            var previewPanel = new Panel
            {
                Location = new Point(330, 100),
                Size = new Size(458, 292),
                BackColor = Color.FromArgb(8, 12, 21),
                BorderStyle = BorderStyle.FixedSingle
            };
            _preview = new PictureBox
            {
                Location = new Point(72, 18),
                Size = new Size(314, 252),
                BackColor = Color.FromArgb(8, 12, 21),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            previewPanel.Controls.Add(_preview);

            _name = new Label
            {
                Location = new Point(332, 407),
                Size = new Size(300, 30),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold)
            };
            _runtimeBadge = new Label
            {
                Location = new Point(640, 408),
                Size = new Size(146, 26),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(204, 218, 244),
                BackColor = Color.FromArgb(28, 39, 61),
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            };
            _behavior = new Label
            {
                Location = new Point(332, 444),
                Size = new Size(454, 24),
                ForeColor = Color.FromArgb(118, 169, 255),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            _description = new Label
            {
                Location = new Point(332, 474),
                Size = new Size(454, 42),
                ForeColor = Color.FromArgb(189, 201, 220)
            };
            _interaction = new Label
            {
                Location = new Point(332, 518),
                Size = new Size(454, 28),
                ForeColor = Color.FromArgb(132, 151, 184),
                Font = new Font("Microsoft YaHei UI", 8.3F)
            };

            var close = new Button
            {
                Text = T(UiTextKeys.Close),
                Location = new Point(574, 550),
                Size = new Size(100, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(31, 41, 61),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel
            };
            close.FlatAppearance.BorderColor = Color.FromArgb(65, 82, 115);

            _apply = new Button
            {
                Text = T(UiTextKeys.ApplyPet),
                Location = new Point(686, 550),
                Size = new Size(102, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 113, 255),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            _apply.FlatAppearance.BorderColor = Color.FromArgb(112, 151, 255);
            _apply.Click += delegate
            {
                var selected = _list.SelectedItem as AnimalPetDefinition;
                if (selected == null || IsCurrentPet(selected)) return;
                SelectedPetId = selected.Id;
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_currentStatus);
            Controls.Add(_list);
            Controls.Add(previewPanel);
            Controls.Add(_name);
            Controls.Add(_runtimeBadge);
            Controls.Add(_behavior);
            Controls.Add(_description);
            Controls.Add(_interaction);
            Controls.Add(close);
            Controls.Add(_apply);

            _list.SelectedIndexChanged += async delegate { await UpdateSelectionAsync(); };
            _list.DoubleClick += delegate { if (_apply.Enabled) _apply.PerformClick(); };

            var selectedIndex = 0;
            var currentName = string.Empty;
            for (var index = 0; index < AnimalPetCatalog.Visible.Count; index++)
            {
                var candidate = AnimalPetCatalog.Visible[index];
                if (!string.Equals(candidate.Id, _currentPetId, StringComparison.OrdinalIgnoreCase)) continue;
                selectedIndex = index;
                currentName = GetPetName(candidate);
                break;
            }
            _currentStatus.Text = string.IsNullOrWhiteSpace(currentName) ? string.Empty : T(UiTextKeys.PetCurrentPrefix) + currentName;
            if (_list.Items.Count > 0) _list.SelectedIndex = selectedIndex;

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
                _listNameFont.Dispose();
                _listMetaFont.Dispose();
                _listCurrentFont.Dispose();
            };
        }

        public string SelectedPetId { get; private set; }

        internal static string SummaryForSmokeTest(AnimalPetDefinition pet)
        {
            return GetPetSummary(pet);
        }

        internal static string BehaviorForSmokeTest(AnimalPetDefinition pet)
        {
            return GetBehaviorLine(pet);
        }

        internal static string RuntimeBadgeForSmokeTest(AnimalPetDefinition pet)
        {
            return GetRuntimeBadge(pet);
        }

        internal static string NameForSmokeTest(AnimalPetDefinition pet)
        {
            return GetPetName(pet);
        }

        private void DrawPetItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count) return;
            var pet = _list.Items[e.Index] as AnimalPetDefinition;
            if (pet == null) return;

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var background = selected ? Color.FromArgb(24, 39, 67) : Color.FromArgb(8, 12, 21);
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            if (selected)
            {
                using (var accent = new SolidBrush(Color.FromArgb(88, 136, 255)))
                    e.Graphics.FillRectangle(accent, new Rectangle(e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height));
            }

            var left = e.Bounds.Left + 14;
            var nameRect = new Rectangle(left, e.Bounds.Top + 8, e.Bounds.Width - 28, 23);
            var metaRect = new Rectangle(left, e.Bounds.Top + 33, e.Bounds.Width - 28, 20);
            var current = IsCurrentPet(pet);
            if (current) nameRect.Width -= 54;

            TextRenderer.DrawText(
                e.Graphics,
                GetPetName(pet),
                _listNameFont,
                nameRect,
                selected ? Color.White : Color.FromArgb(224, 232, 247),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(
                e.Graphics,
                GetPetSummary(pet),
                _listMetaFont,
                metaRect,
                selected ? Color.FromArgb(159, 190, 245) : Color.FromArgb(124, 143, 174),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (current)
            {
                var badge = new Rectangle(e.Bounds.Right - 52, e.Bounds.Top + 9, 38, 20);
                using (var badgeBrush = new SolidBrush(Color.FromArgb(39, 75, 68)))
                    e.Graphics.FillRectangle(badgeBrush, badge);
                TextRenderer.DrawText(
                    e.Graphics,
                    T(UiTextKeys.PetCurrentBadge),
                    _listCurrentFont,
                    badge,
                    Color.FromArgb(154, 224, 199),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            e.DrawFocusRectangle();
        }

        private async Task UpdateSelectionAsync()
        {
            var pet = _list.SelectedItem as AnimalPetDefinition;
            if (pet == null) return;
            _name.Text = GetPetName(pet);
            _runtimeBadge.Text = GetRuntimeBadge(pet);
            _runtimeBadge.BackColor = pet.Runtime == AnimalPetRuntime.VPetCore
                ? Color.FromArgb(55, 43, 76)
                : Color.FromArgb(27, 55, 68);
            _behavior.Text = GetBehaviorLine(pet);
            _description.Text = GetUserDescription(pet);
            _interaction.Text = pet.Runtime == AnimalPetRuntime.VPetCore
                ? T(UiTextKeys.PetInteractionVPet)
                : T(UiTextKeys.PetInteractionFlying);
            var current = IsCurrentPet(pet);
            _apply.Text = current ? T(UiTextKeys.PetCurrentUse) : T(UiTextKeys.ApplyPet);
            _apply.Enabled = !current;
            _apply.BackColor = current ? Color.FromArgb(42, 53, 70) : Color.FromArgb(70, 113, 255);
            _apply.ForeColor = current ? Color.FromArgb(139, 153, 178) : Color.White;
            _list.Invalidate();

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
            var selected = _list.SelectedItem as AnimalPetDefinition;
            if (selected == null || !string.Equals(selected.Id, expectedId, StringComparison.OrdinalIgnoreCase))
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
            Bitmap rendered;
            if (FlyingPetProfiles.IsManaged(pet))
            {
                // Preview only: expose the smooth body heading without pretending to replay the randomized desktop path.
                var heading = (float)Math.Sin(_animationSeconds * 0.75) * 18f;
                rendered = SpritePetWindow.RenderFlyingForSmokeTest(pet, _sheet, frame, heading);
            }
            else
            {
                rendered = SpritePetWindow.RenderForSmokeTest(pet, _sheet, frame, direction, true);
            }

            using (rendered)
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
                graphics.DrawString(T(UiTextKeys.VPetPreviewTitle), titleFont, titleBrush, new RectangleF(12, 34, _preview.Width - 24, 52), format);
                graphics.DrawLine(linePen, 76, 98, _preview.Width - 76, 98);
                graphics.DrawString(
                    T(UiTextKeys.VPetPreviewDescription),
                    bodyFont,
                    bodyBrush,
                    new RectangleF(18, 106, _preview.Width - 36, 126),
                    format);
            }
            ReplacePreview(canvas, pet.Id);
        }

        private bool IsCurrentPet(AnimalPetDefinition pet)
        {
            return pet != null && string.Equals(pet.Id, _currentPetId, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRuntimeBadge(AnimalPetDefinition pet)
        {
            if (pet == null) return string.Empty;
            return T(pet.Runtime == AnimalPetRuntime.VPetCore ? UiTextKeys.PetRuntimeVPet : UiTextKeys.PetRuntimeFlying);
        }

        private static string GetPetName(AnimalPetDefinition pet)
        {
            if (pet == null) return string.Empty;
            switch ((pet.Id ?? string.Empty).ToLowerInvariant())
            {
                case "greenfly": return T(UiTextKeys.PetNameGreenFly);
                case "bee": return T(UiTextKeys.PetNameBee);
                case "real-bee": return T(UiTextKeys.PetNameRealBee);
                case "dragonfly": return T(UiTextKeys.PetNameDragonfly);
                case "butterfly": return T(UiTextKeys.PetNameButterfly);
                case "moth": return T(UiTextKeys.PetNameMoth);
                case "vpet": return T(UiTextKeys.PetNameVPet);
                default: return UiTextRuntime.Translate(pet.Name ?? string.Empty);
            }
        }

        private static string GetPetSummary(AnimalPetDefinition pet)
        {
            if (pet == null) return string.Empty;
            switch ((pet.Id ?? string.Empty).ToLowerInvariant())
            {
                case "greenfly": return T(UiTextKeys.PetSummaryGreenFly);
                case "bee": return T(UiTextKeys.PetSummaryBee);
                case "real-bee": return T(UiTextKeys.PetSummaryRealBee);
                case "dragonfly": return T(UiTextKeys.PetSummaryDragonfly);
                case "butterfly": return T(UiTextKeys.PetSummaryButterfly);
                case "moth": return T(UiTextKeys.PetSummaryMoth);
                case "vpet": return T(UiTextKeys.PetSummaryVPet);
                default: return T(pet.Runtime == AnimalPetRuntime.VPetCore ? UiTextKeys.PetSummaryDefaultVPet : UiTextKeys.PetSummaryDefaultFlying);
            }
        }

        private static string GetBehaviorLine(AnimalPetDefinition pet)
        {
            if (pet == null) return string.Empty;
            switch ((pet.Id ?? string.Empty).ToLowerInvariant())
            {
                case "greenfly": return T(UiTextKeys.PetBehaviorGreenFly);
                case "bee": return T(UiTextKeys.PetBehaviorBee);
                case "real-bee": return T(UiTextKeys.PetBehaviorRealBee);
                case "dragonfly": return T(UiTextKeys.PetBehaviorDragonfly);
                case "butterfly": return T(UiTextKeys.PetBehaviorButterfly);
                case "moth": return T(UiTextKeys.PetBehaviorMoth);
                case "vpet": return T(UiTextKeys.PetBehaviorVPet);
                default: return string.Empty;
            }
        }

        private static string GetUserDescription(AnimalPetDefinition pet)
        {
            if (pet == null) return string.Empty;
            switch ((pet.Id ?? string.Empty).ToLowerInvariant())
            {
                case "greenfly": return T(UiTextKeys.PetDescriptionGreenFly);
                case "bee": return T(UiTextKeys.PetDescriptionBee);
                case "real-bee": return T(UiTextKeys.PetDescriptionRealBee);
                case "dragonfly": return T(UiTextKeys.PetDescriptionDragonfly);
                case "butterfly": return T(UiTextKeys.PetDescriptionButterfly);
                case "moth": return T(UiTextKeys.PetDescriptionMoth);
                case "vpet": return T(UiTextKeys.PetDescriptionVPet);
                default: return UiTextRuntime.Translate(pet.Description ?? string.Empty);
            }
        }

        private static string T(string key)
        {
            return UiTextRuntime.Text(key);
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
