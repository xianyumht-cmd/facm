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
                Text = "右侧是实时 3D 场景预览：透视相机、灯光、材质、网格和动画均实际运行。",
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
            _list.SelectedIndexChanged += SelectPet;
            _list.DoubleClick += delegate { ApplySelection(); };

            var selectedIndex = 0;
            for (var index = 0; index < PetCatalog.All.Count; index++)
            {
                if (!string.Equals(PetCatalog.All[index].Id, _selectedPetId, StringComparison.OrdinalIgnoreCase)) continue;
                selectedIndex = index;
                break;
            }
            _list.SelectedIndex = selectedIndex;

            var current = PetCatalog.Get(_selectedPetId);
            _scene = new Pet3DScene(current);
            _previewHost = new ElementHost
            {
                Location = new Point(320, 88),
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
            _previewHost.Location = new Point(4, 4);

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
                Text = "渲染：WPF Viewport3D · MeshGeometry3D · PerspectiveCamera · 动态灯光材质",
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
                Text = "应用 3D 桌宠",
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

            AcceptButton = apply;
            CancelButton = cancel;
            FormClosed += delegate
            {
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
