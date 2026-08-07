using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace FACM.Pets
{
    internal sealed class PetPickerForm : Form
    {
        private readonly ListBox _list;
        private readonly Label _selectedLabel;
        private readonly Pet3DScene _scene;
        private readonly ElementHost _previewHost;
        private string _selectedPetId;
        private bool _initialized;

        public PetPickerForm(string currentPetId)
        {
            _selectedPetId = PetCatalog.Get(currentPetId).Id;

            Text = "FACM 3D 桌面宠物";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(760, 590);
            BackColor = Color.FromArgb(12, 17, 28);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);

            var title = new Label
            {
                Text = "选择 3D 桌面宠物",
                Location = new Point(24, 18),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold)
            };
            var hint = new Label
            {
                Text = "当前内置预览即将替换为开源 VRM 桌宠引擎；此窗口仅用于兼容旧设置。",
                Location = new Point(26, 54),
                Size = new Size(700, 24),
                ForeColor = Color.FromArgb(155, 169, 196)
            };

            _list = new ListBox
            {
                Location = new Point(22, 88),
                Size = new Size(278, 420),
                BackColor = Color.FromArgb(8, 12, 21),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 10F),
                IntegralHeight = false,
                ItemHeight = 34,
                DisplayMember = "Name"
            };
            foreach (var pet in PetCatalog.All) _list.Items.Add(pet);

            var current = PetCatalog.Get(_selectedPetId);
            _scene = new Pet3DScene(current);
            _previewHost = new ElementHost
            {
                Location = new Point(4, 4),
                Size = new Size(418, 330),
                BackColor = Color.FromArgb(8, 12, 21),
                Child = _scene
            };

            var previewBorder = new Panel
            {
                Location = new Point(316, 84),
                Size = new Size(426, 338),
                BackColor = Color.FromArgb(48, 62, 91)
            };
            previewBorder.Controls.Add(_previewHost);

            _selectedLabel = new Label
            {
                Location = new Point(320, 438),
                Size = new Size(418, 70),
                ForeColor = Color.FromArgb(205, 215, 236),
                Font = new Font("Microsoft YaHei UI", 9F),
                Text = BuildSelectedText(current)
            };

            var technology = new Label
            {
                Text = "兼容预览：WPF Viewport3D；正式桌宠将使用开源 VRM 引擎运行时",
                Location = new Point(320, 510),
                Size = new Size(418, 24),
                ForeColor = Color.FromArgb(118, 142, 183),
                Font = new Font("Microsoft YaHei UI", 8F)
            };

            var cancel = new Button
            {
                Text = "取消",
                Location = new Point(536, 540),
                Size = new Size(92, 38),
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
                Text = "应用",
                Location = new Point(638, 540),
                Size = new Size(100, 38),
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
            Controls.Add(previewBorder);
            Controls.Add(_selectedLabel);
            Controls.Add(technology);
            Controls.Add(cancel);
            Controls.Add(apply);

            var selectedIndex = 0;
            for (var index = 0; index < PetCatalog.All.Count; index++)
            {
                if (!string.Equals(PetCatalog.All[index].Id, _selectedPetId, StringComparison.OrdinalIgnoreCase)) continue;
                selectedIndex = index;
                break;
            }

            _list.SelectedIndexChanged += SelectPet;
            _list.DoubleClick += delegate { ApplySelection(); };
            _initialized = true;
            _list.SelectedIndex = selectedIndex;

            AcceptButton = apply;
            CancelButton = cancel;
            FormClosed += delegate
            {
                _initialized = false;
                _previewHost.Child = null;
                _scene.Dispose();
            };
        }

        public string SelectedPetId
        {
            get { return _selectedPetId; }
        }

        private void SelectPet(object sender, EventArgs e)
        {
            if (!_initialized || _scene == null || _selectedLabel == null) return;
            var pet = _list.SelectedItem as PetDefinition;
            if (pet == null) return;
            _selectedPetId = pet.Id;
            _scene.SetPet(pet);
            _selectedLabel.Text = BuildSelectedText(pet);
        }

        private void ApplySelection()
        {
            var pet = _list.SelectedItem as PetDefinition;
            if (pet == null) return;
            _selectedPetId = pet.Id;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string BuildSelectedText(PetDefinition pet)
        {
            return pet.Name + "  ·  " + pet.Description + Environment.NewLine +
                   "窗口尺寸：" + pet.Size.Width + " × " + pet.Size.Height +
                   "  ·  可自由拖动并保存准确桌面坐标";
        }
    }
}
