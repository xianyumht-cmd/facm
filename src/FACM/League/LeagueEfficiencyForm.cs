using System;
using System.Drawing;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueEfficiencyForm : Form
    {
        private readonly LeagueEfficiencyModule _module;
        private readonly UiTextCatalog _ui;
        private readonly TextBox _exitGame;
        private readonly TextBox _closeLobby;
        private readonly TextBox _credentials;
        private readonly Label _status;

        public LeagueEfficiencyForm(LeagueEfficiencyModule module, UiTextCatalog ui)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));

            Text = T(LeagueEfficiencyUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 470);
            MinimumSize = new Size(680, 440);
            BackColor = Color.FromArgb(17, 24, 39);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Microsoft YaHei UI", 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24),
                ColumnCount = 1,
                RowCount = 8,
                BackColor = BackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

            root.Controls.Add(new Label
            {
                Text = T(LeagueEfficiencyUiTextKeys.Title),
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
                ForeColor = ForeColor,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            root.Controls.Add(new Label
            {
                Text = T(LeagueEfficiencyUiTextKeys.Hint),
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(148, 163, 184),
                TextAlign = ContentAlignment.TopLeft
            }, 0, 1);
            root.Controls.Add(new Label
            {
                Text = T(LeagueEfficiencyUiTextKeys.HotkeySection),
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(226, 232, 240),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 2);

            _exitGame = AddHotkeyRow(root, 3,
                T(LeagueEfficiencyUiTextKeys.ExitGame),
                T(LeagueEfficiencyUiTextKeys.ExitGameHint),
                _module.ExitGameHotkey);
            _closeLobby = AddHotkeyRow(root, 4,
                T(LeagueEfficiencyUiTextKeys.CloseLobby),
                T(LeagueEfficiencyUiTextKeys.CloseLobbyHint),
                _module.CloseLobbyHotkey);
            _credentials = AddHotkeyRow(root, 5,
                T(LeagueEfficiencyUiTextKeys.Credentials),
                T(LeagueEfficiencyUiTextKeys.CredentialsHint),
                _module.CredentialHotkey);

            var privacy = new Label
            {
                Text = T(LeagueEfficiencyUiTextKeys.Privacy),
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(148, 163, 184),
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(0, 10, 0, 0)
            };
            root.Controls.Add(privacy, 0, 6);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = BackColor
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            _status = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(148, 163, 184),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var save = CreateButton(T(LeagueEfficiencyUiTextKeys.Save), 140);
            save.Click += delegate { SaveBindings(); };
            footer.Controls.Add(_status, 0, 0);
            footer.Controls.Add(save, 1, 0);
            root.Controls.Add(footer, 0, 7);

            Controls.Add(root);
        }

        private TextBox AddHotkeyRow(TableLayoutPanel parent, int row, string title, string hint, string value)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = Color.FromArgb(24, 33, 49),
                Padding = new Padding(12, 8, 12, 8)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            panel.Controls.Add(new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
                ForeColor = ForeColor,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            var box = new TextBox
            {
                Text = value ?? string.Empty,
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 3, 6, 3),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = ForeColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            panel.Controls.Add(box, 1, 0);

            var capture = CreateButton(T(LeagueEfficiencyUiTextKeys.Capture), 68);
            capture.Click += delegate
            {
                using (var dialog = new LeagueHotkeyCaptureDialog(_ui))
                {
                    dialog.TopMost = true;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        box.Text = dialog.Binding == null ? string.Empty : dialog.Binding.ToString();
                }
            };
            panel.Controls.Add(capture, 2, 0);

            var clear = CreateButton(T(LeagueEfficiencyUiTextKeys.Clear), 68);
            clear.Click += delegate { box.Text = string.Empty; };
            panel.Controls.Add(clear, 3, 0);

            var hintLabel = new Label
            {
                Text = hint,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(148, 163, 184),
                TextAlign = ContentAlignment.TopLeft,
                AutoEllipsis = true
            };
            panel.Controls.Add(hintLabel, 0, 1);
            panel.SetColumnSpan(hintLabel, 4);
            parent.Controls.Add(panel, 0, row);
            return box;
        }

        private Button CreateButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 30,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = ForeColor,
                Margin = new Padding(4, 2, 4, 2)
            };
        }

        private void SaveBindings()
        {
            string error;
            if (_module.TryUpdateBindings(_exitGame.Text, _closeLobby.Text, _credentials.Text, out error))
            {
                _exitGame.Text = _module.ExitGameHotkey;
                _closeLobby.Text = _module.CloseLobbyHotkey;
                _credentials.Text = _module.CredentialHotkey;
                _status.ForeColor = Color.FromArgb(134, 239, 172);
                _status.Text = T(LeagueEfficiencyUiTextKeys.Saved);
            }
            else
            {
                _status.ForeColor = Color.FromArgb(253, 186, 116);
                _status.Text = string.Format(T(LeagueEfficiencyUiTextKeys.SaveFailed), error ?? string.Empty);
            }
        }

        private string T(string key)
        {
            return LeagueEfficiencyText.Get(_ui, key);
        }
    }

    internal sealed class LeagueHotkeyCaptureDialog : Form
    {
        private readonly UiTextCatalog _ui;
        private readonly Label _prompt;

        public LeagueHotkeyCaptureDialog(UiTextCatalog ui)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            Text = LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.CaptureTitle);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(430, 150);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            BackColor = Color.FromArgb(17, 24, 39);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Microsoft YaHei UI", 9F);

            _prompt = new Label
            {
                Text = LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.CapturePrompt),
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = ForeColor
            };
            Controls.Add(_prompt);
            KeyDown += HandleKeyDown;
        }

        public LeagueHotkeyBinding Binding { get; private set; }

        private void HandleKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if ((e.KeyCode == Keys.Escape) && !e.Control && !e.Alt && !e.Shift)
            {
                Binding = LeagueHotkeyBinding.Disabled;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            var candidate = LeagueHotkeyBinding.FromKeyEvent(e.KeyData);
            string parsedError;
            LeagueHotkeyBinding parsed;
            if (!LeagueHotkeyBinding.TryParse(candidate.ToString(), out parsed, out parsedError) || !parsed.Enabled)
            {
                _prompt.ForeColor = Color.FromArgb(253, 186, 116);
                _prompt.Text = LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.CaptureUnsafe);
                return;
            }

            Binding = parsed;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
