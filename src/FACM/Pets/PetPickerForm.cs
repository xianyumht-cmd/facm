using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FACM.Pets
{
    internal sealed class PetPickerForm : Form
    {
        private static readonly HttpClient ImageClient = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        private readonly ListBox _list;
        private readonly PictureBox _preview;
        private readonly Label _selectedLabel;
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly Button _apply;
        private readonly Button _cancelOperation;
        private string _selectedPetId;
        private bool _busy;
        private CancellationTokenSource _operationCancellation;
        private CancellationTokenSource _thumbnailCancellation;

        public PetPickerForm(string currentPetId)
        {
            _selectedPetId = PetCatalog.Get(currentPetId).Id;

            Text = "FACM · 3D 桌面宠物";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(820, 650);
            BackColor = Color.FromArgb(12, 17, 28);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = "3D 桌面宠物",
                Location = new Point(24, 18),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = "选择喜欢的桌宠。首次使用会自动下载所需资源，完成后即可直接使用。",
                Location = new Point(26, 54),
                Size = new Size(760, 24),
                ForeColor = Color.FromArgb(155, 169, 196)
            };

            _list = new ListBox
            {
                Location = new Point(22, 88),
                Size = new Size(300, 450),
                BackColor = Color.FromArgb(8, 12, 21),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 10F),
                IntegralHeight = false,
                ItemHeight = 36,
                DisplayMember = "Name"
            };
            foreach (var pet in PetCatalog.All) _list.Items.Add(pet);

            var previewPanel = new Panel
            {
                Location = new Point(342, 88),
                Size = new Size(452, 350),
                BackColor = Color.FromArgb(8, 12, 21),
                BorderStyle = BorderStyle.FixedSingle
            };
            _preview = new PictureBox
            {
                Location = new Point(10, 10),
                Size = new Size(430, 328),
                BackColor = Color.FromArgb(8, 12, 21),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            previewPanel.Controls.Add(_preview);

            _selectedLabel = new Label
            {
                Location = new Point(344, 452),
                Size = new Size(448, 84),
                ForeColor = Color.FromArgb(205, 215, 236),
                Font = new Font("Microsoft YaHei UI", 9F)
            };

            _progress = new ProgressBar
            {
                Location = new Point(22, 554),
                Size = new Size(772, 10),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            _status = new Label
            {
                Text = "请选择一个桌宠。应用后可在桌面使用，点击桌宠打开 FACM 控制面板。",
                Location = new Point(22, 572),
                Size = new Size(772, 28),
                ForeColor = Color.FromArgb(115, 207, 174)
            };

            var engine = new Button
            {
                Text = "桌宠管理",
                Location = new Point(342, 604),
                Size = new Size(108, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(31, 41, 61),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            engine.FlatAppearance.BorderColor = Color.FromArgb(65, 82, 115);
            engine.Click += delegate
            {
                try
                {
                    DesktopHomunculusManager.OpenEngine();
                }
                catch (Exception exception)
                {
                    Services.AppLog.Info("Pet manager open failed: " + exception.Message);
                    MessageBox.Show("暂时无法打开桌宠管理，请稍后重试。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            var close = new Button
            {
                Text = "关闭",
                Location = new Point(458, 604),
                Size = new Size(96, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(31, 41, 61),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel
            };
            close.FlatAppearance.BorderColor = Color.FromArgb(65, 82, 115);

            _cancelOperation = new Button
            {
                Text = "取消下载",
                Location = new Point(562, 604),
                Size = new Size(106, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(66, 52, 63),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _cancelOperation.FlatAppearance.BorderColor = Color.FromArgb(120, 79, 88);
            _cancelOperation.Click += delegate
            {
                if (_operationCancellation != null && !_operationCancellation.IsCancellationRequested)
                    _operationCancellation.Cancel();
            };

            _apply = new Button
            {
                Text = "下载并应用",
                Location = new Point(676, 604),
                Size = new Size(118, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 113, 255),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            _apply.FlatAppearance.BorderColor = Color.FromArgb(112, 151, 255);
            _apply.Click += async delegate { await ApplySelectionAsync(); };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_list);
            Controls.Add(previewPanel);
            Controls.Add(_selectedLabel);
            Controls.Add(_progress);
            Controls.Add(_status);
            Controls.Add(engine);
            Controls.Add(close);
            Controls.Add(_cancelOperation);
            Controls.Add(_apply);

            var selectedIndex = 0;
            for (var index = 0; index < PetCatalog.All.Count; index++)
            {
                if (!string.Equals(PetCatalog.All[index].Id, _selectedPetId, StringComparison.OrdinalIgnoreCase)) continue;
                selectedIndex = index;
                break;
            }

            _list.SelectedIndexChanged += async delegate { await SelectPetAsync(); };
            _list.DoubleClick += async delegate { await ApplySelectionAsync(); };
            _list.SelectedIndex = selectedIndex;

            CancelButton = close;
            FormClosing += delegate
            {
                if (!_busy || _operationCancellation == null) return;
                _operationCancellation.Cancel();
            };
            FormClosed += delegate
            {
                if (_operationCancellation != null) _operationCancellation.Dispose();
                if (_thumbnailCancellation != null) _thumbnailCancellation.Dispose();
                var image = _preview.Image;
                _preview.Image = null;
                if (image != null) image.Dispose();
            };
        }

        public string SelectedPetId
        {
            get { return _selectedPetId; }
        }

        public string ActivatedPersonaId { get; private set; }

        private async Task SelectPetAsync()
        {
            var pet = _list.SelectedItem as PetDefinition;
            if (pet == null) return;
            _selectedPetId = pet.Id;
            _selectedLabel.Text = pet.Name + Environment.NewLine + pet.Description;

            if (_thumbnailCancellation != null)
            {
                _thumbnailCancellation.Cancel();
                _thumbnailCancellation.Dispose();
            }
            _thumbnailCancellation = new CancellationTokenSource();
            await LoadThumbnailAsync(pet, _thumbnailCancellation.Token);
        }

        private async Task LoadThumbnailAsync(PetDefinition pet, CancellationToken token)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, pet.ThumbnailUrl))
                using (var response = await ImageClient.SendAsync(request, token))
                {
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    token.ThrowIfCancellationRequested();
                    using (var stream = new MemoryStream(bytes))
                    using (var original = Image.FromStream(stream))
                    {
                        var copy = new Bitmap(original);
                        var old = _preview.Image;
                        _preview.Image = copy;
                        if (old != null) old.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                _status.Text = "预览加载失败，仍可继续应用。";
                _status.ForeColor = Color.FromArgb(230, 177, 103);
            }
        }

        private async Task ApplySelectionAsync()
        {
            if (_busy) return;
            var pet = _list.SelectedItem as PetDefinition;
            if (pet == null) return;

            _busy = true;
            _list.Enabled = false;
            _apply.Enabled = false;
            _cancelOperation.Enabled = true;
            _operationCancellation = new CancellationTokenSource();
            var progress = new Progress<PetSetupProgress>(value =>
            {
                _status.Text = string.IsNullOrWhiteSpace(value.Message) ? FriendlyProgress(value.Percent) : value.Message;
                _status.ForeColor = Color.FromArgb(112, 165, 255);
                _progress.Value = Math.Max(_progress.Minimum, Math.Min(_progress.Maximum, value.Percent));
            });

            try
            {
                if (NvidiaDesktopPetCompatibility.IsNvidiaDriverPresent())
                {
                    Environment.SetEnvironmentVariable(
                        "FACM_DESKTOP_PET_WGPU_BACKEND",
                        "vulkan",
                        EnvironmentVariableTarget.Process);
                    var compatibilityApplied = await NvidiaDesktopPetCompatibility.EnsurePreferNativeAsync(
                        DesktopHomunculusManager.FindInstalledEngine(),
                        progress,
                        _operationCancellation.Token);
                    if (!compatibilityApplied)
                    {
                        Services.AppLog.Info("NVIDIA native-present compatibility was not applied; desktop pet startup will still be attempted.");
                        _status.Text = "NVIDIA 透明显示兼容设置未能自动完成，正在继续尝试...";
                        _status.ForeColor = Color.FromArgb(230, 177, 103);
                    }
                }

                var result = await DesktopHomunculusManager.ActivateAsync(pet, progress, _operationCancellation.Token);
                if (!result.Success)
                {
                    Services.AppLog.Info("Pet activation failed: " + result.ErrorMessage);
                    _status.Text = "桌宠应用失败，默认悬浮球已保留。";
                    _status.ForeColor = Color.FromArgb(255, 145, 121);
                    return;
                }

                _selectedPetId = pet.Id;
                ActivatedPersonaId = result.PersonaId;
                _progress.Value = 100;
                _status.Text = "已应用 " + pet.Name + "。点击桌宠可打开 FACM 控制面板。";
                _status.ForeColor = Color.FromArgb(99, 219, 158);
                DialogResult = DialogResult.OK;
                Close();
            }
            finally
            {
                _busy = false;
                _list.Enabled = true;
                _apply.Enabled = true;
                _cancelOperation.Enabled = false;
                if (_operationCancellation != null)
                {
                    _operationCancellation.Dispose();
                    _operationCancellation = null;
                }
            }
        }

        private static string FriendlyProgress(int percent)
        {
            if (percent < 8) return "正在检查所需资源...";
            if (percent < 42) return "正在下载桌宠组件...";
            if (percent < 58) return "正在准备桌宠组件...";
            if (percent < 88) return "正在下载桌宠资源...";
            if (percent < 96) return "正在加载桌宠...";
            return "正在启动桌宠...";
        }
    }
}
